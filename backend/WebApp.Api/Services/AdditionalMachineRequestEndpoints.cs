using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography;
using WebApp.Api.Models;

namespace WebApp.Api.Services;

public static class AdditionalMachineRequestEndpoints
{
    public const string Route = "/api/company/machine-requests";

    public static void MapAdditionalMachineRequests(this IEndpointRouteBuilder app) =>
        app.MapPost(Route, CreateAsync)
            .RequireAuthorization("CompanyAdminOnly")
            .Accepts<IFormFileCollection>("multipart/form-data")
            .WithMetadata(new RequestSizeLimitAttribute(MachineRequestUploadLimits.MaxRequestBodyBytes))
            .WithMetadata(new RequestFormLimitsAttribute { MultipartBodyLengthLimit = MachineRequestUploadLimits.MaxRequestBodyBytes });

    public static async Task<IResult> CreateAsync(HttpContext httpContext,
        [FromServices] AdditionalMachinePaymentContextResolver contextResolver,
        [FromServices] MachineRequestPaymentService paymentService,
        [FromServices] MachineRequestStorageService storageService,
        [FromServices] IPdfPageCounter pageCounter,
        CancellationToken ct,
        [FromServices] RequestReceivedNotificationService? notifications = null)
    {
        var resolution = await contextResolver.ResolveAsync(httpContext.User, ct);
        if (!resolution.Success || resolution.Context is null)
            return resolution.Error == AdditionalMachinePaymentContextError.Forbidden
                ? Results.Forbid()
                : Results.Conflict(new { error = resolution.ErrorMessage });
        if (!httpContext.Request.HasFormContentType)
            return Validation("request", "Le contenu multipart/form-data est requis.");

        IFormCollection form;
        try { form = await httpContext.Request.ReadFormAsync(ct); }
        catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
        { return Results.StatusCode(StatusCodes.Status413PayloadTooLarge); }
        catch (InvalidDataException) { return Validation("request", "La requête multipart est invalide."); }

        if (!Guid.TryParse(form["paymentRequestId"].FirstOrDefault(), out var paymentRequestId))
            return Validation("paymentRequestId", "La référence de paiement est invalide.");
        var machine = new MachineRequestMachine(
            Trim(form["machineName"].FirstOrDefault()), Trim(form["manufacturer"].FirstOrDefault()),
            Trim(form["model"].FirstOrDefault()), Optional(form["serialNumber"].FirstOrDefault()),
            Optional(form["description"].FirstOrDefault()));
        foreach (var value in new[] { machine.MachineName, machine.Manufacturer, machine.Model })
            if (string.IsNullOrWhiteSpace(value)) return Validation("machine", "Le nom, le fabricant et le modèle sont obligatoires.");
        if (new[] { machine.MachineName, machine.Manufacturer, machine.Model, machine.SerialNumber }.Any(value => value?.Length > MachineRequestUploadLimits.MaxShortTextLength))
            return Validation("machine", "Une information machine est trop longue.");
        if (machine.Description?.Length > MachineRequestUploadLimits.MaxDescriptionLength)
            return Validation("description", "La description est trop longue.");

        var files = form.Files.GetFiles("documents");
        if (files.Count == 0 || files.Count > MachineRequestUploadLimits.MaxDocumentCount)
            return Validation("documents", "Le nombre de PDF est invalide.");
        if (files.Sum(file => file.Length) > MachineRequestUploadLimits.MaxCombinedDocumentBytes)
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

        MachineRequestPayment payment;
        try
        {
            var verified = await paymentService.VerifyAdditionalForMachineRequestAsync(paymentRequestId, resolution.Context, ct);
            if (verified.Status == "not_found") return Results.NotFound(new { error = "La référence de paiement est introuvable." });
            if (verified.Status != "authorized" || verified.Payment is null)
                return Results.Conflict(new { error = "Le paiement n'est pas autorisé ou n'est plus intégralement capturable." });
            payment = verified.Payment;
        }
        catch (UnauthorizedAccessException) { return Results.Forbid(); }
        catch (Stripe.StripeException) { return Results.Json(new { error = "La vérification Stripe est temporairement indisponible." }, statusCode: 503); }
        catch (InvalidOperationException) { return Results.Conflict(new { error = "L'autorisation Stripe ne correspond plus au paiement attendu." }); }

        var uploads = new List<MachineRequestDocumentUpload>(files.Count);
        try
        {
            foreach (var file in files)
            {
                if (file.Length <= 0 || file.Length > MachineRequestUploadLimits.MaxDocumentBytes
                    || !file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                    return Validation("documents", $"Le fichier {Path.GetFileName(file.FileName)} est invalide.");
                int pages;
                await using (var inspection = file.OpenReadStream())
                {
                    try { pages = await pageCounter.CountPagesAsync(inspection, ct); }
                    catch (InvalidDataException) { return Validation("documents", $"Le fichier {Path.GetFileName(file.FileName)} est illisible."); }
                }
                uploads.Add(new(Path.GetFileName(file.FileName), "application/pdf", file.Length, pages, file.OpenReadStream()));
            }

            var receivedPages = uploads.Sum(upload => upload.PageCount);
            var recalculated = MachineRequestPreparationPricing.CalculateMaximumAuthorizationCents(receivedPages);
            if (receivedPages != payment.TotalPages || recalculated != payment.AmountCents)
                return Results.Conflict(new { error = "Les PDF ne correspondent pas à l'autorisation de paiement." });

            payment = await paymentService.ReserveMachineRequestAsync(payment, ct);
            var requestId = payment.MachineRequestId ?? throw new InvalidOperationException("La réservation de la demande a échoué.");
            var draft = new MachineRequestDraft(new MachineRequestClient(
                resolution.Context.UserFirstName ?? string.Empty,
                resolution.Context.UserLastName ?? string.Empty,
                resolution.Context.CompanyName,
                resolution.Context.UserEmail,
                resolution.Context.UserPhone ?? string.Empty), machine);
            var existing = await storageService.GetAsync(requestId, ct);
            if (existing is not null)
            {
                if (!await MatchesAsync(storageService, existing, draft, uploads, paymentRequestId, resolution.Context, ct))
                    return Results.Conflict(new { error = "La demande existante ne correspond pas à ce nouvel envoi." });
                if (notifications is not null) await notifications.TryEnqueueAsync(paymentRequestId, CancellationToken.None);
                return Results.Ok(Response(existing));
            }

            var reference = new MachineRequestPaymentReference(payment.PaymentRequestId, payment.TotalPages,
                payment.AmountCents, payment.Currency, receivedPages, recalculated, 0, 0, false);
            try
            {
                var record = await storageService.CreateAdditionalAsync(draft, uploads, requestId, reference,
                    resolution.Context.CompanyId, resolution.Context.UserId, ct);
                if (notifications is not null) await notifications.TryEnqueueAsync(paymentRequestId, CancellationToken.None);
                return Results.Json(Response(record), statusCode: StatusCodes.Status201Created);
            }
            catch
            {
                var reconciled = await storageService.GetAsync(requestId, CancellationToken.None);
                if (reconciled is not null && await MatchesAsync(storageService, reconciled, draft, uploads,
                    paymentRequestId, resolution.Context, CancellationToken.None))
                {
                    if (notifications is not null) await notifications.TryEnqueueAsync(paymentRequestId, CancellationToken.None);
                    return Results.Ok(Response(reconciled));
                }
                throw;
            }
        }
        catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
        finally { foreach (var upload in uploads) await upload.Content.DisposeAsync(); }
    }

    private static async Task<bool> MatchesAsync(MachineRequestStorageService storageService,
        MachineRequestRecord record, MachineRequestDraft draft, IReadOnlyCollection<MachineRequestDocumentUpload> uploads,
        Guid paymentRequestId, AdditionalMachinePaymentContext context, CancellationToken ct)
    {
        if (record.RequestKind != MachineRequestKind.AdditionalMachine || record.CompanyId != context.CompanyId
            || record.RequestedByUserId != context.UserId || record.Payment?.PaymentRequestId != paymentRequestId
            || record.Client != draft.Client || record.Machine != draft.Machine) return false;
        var submitted = uploads.ToArray();
        if (!record.Documents.Select(document => (document.OriginalName, document.Size, document.PageCount))
            .SequenceEqual(submitted.Select(upload => (upload.OriginalName, upload.Size, upload.PageCount)))) return false;
        for (var index = 0; index < submitted.Length; index++)
        {
            await using var stored = (await storageService.OpenDocumentAsync(record.RequestId,
                record.Documents[index].BlobName, ct))?.Content;
            if (stored is null) return false;
            var upload = submitted[index].Content;
            if (upload.CanSeek) upload.Position = 0;
            var storedHash = await SHA256.HashDataAsync(stored, ct);
            var uploadHash = await SHA256.HashDataAsync(upload, ct);
            if (upload.CanSeek) upload.Position = 0;
            if (!storedHash.SequenceEqual(uploadHash)) return false;
        }
        return true;
    }

    private static MachineRequestCreatedResponse Response(MachineRequestRecord record) => new(record.RequestId,
        record.Status, record.CreatedAt, record.Documents.Count, record.Pricing.TotalPages,
        record.Pricing.AdditionalPages, record.Pricing.PreparationTotal, false);
    private static IResult Validation(string field, string message) => Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });
    private static string Trim(string? value) => value?.Trim() ?? string.Empty;
    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
