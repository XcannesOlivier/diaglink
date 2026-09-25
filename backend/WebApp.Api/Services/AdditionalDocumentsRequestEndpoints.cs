using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc;
using WebApp.Api.Models;

namespace WebApp.Api.Services;

public static class AdditionalDocumentsRequestEndpoints
{
    public const string Route = "/api/company/machines/{machineId:guid}/document-requests";

    public static void MapAdditionalDocumentsRequests(this IEndpointRouteBuilder app)
    {
        app.MapPost(Route, CreateAsync)
            .RequireAuthorization("CompanyAdminOnly")
            .Accepts<IFormFileCollection>("multipart/form-data")
            .WithMetadata(new RequestSizeLimitAttribute(MachineRequestUploadLimits.MaxRequestBodyBytes))
            .WithMetadata(new RequestFormLimitsAttribute { MultipartBodyLengthLimit = MachineRequestUploadLimits.MaxRequestBodyBytes });
        var payments = app.MapGroup("/api/company/document-requests").RequireAuthorization("CompanyAdminOnly");
        payments.MapPost("/{requestId:guid}/payment", StartPaymentAsync);
        payments.MapGet("/{requestId:guid}/payment", ReadPaymentAsync);
        payments.MapPost("/{requestId:guid}/cancel", CancelAsync);
    }

    public static async Task<IResult> StartPaymentAsync(Guid requestId, HttpContext httpContext,
        [FromServices] AdditionalDocumentsContextResolver resolver,
        [FromServices] MachineRequestPaymentService paymentService,
        [FromServices] MachineRequestStorageService storage,
        CancellationToken ct)
    {
        try
        {
            var prepared = await PreparePaymentAsync(requestId, httpContext, resolver, paymentService, storage, ct);
            if (prepared.Error is not null) return prepared.Error;
            return Results.Ok(await paymentService.StartAdditionalDocumentsAsync(requestId, prepared.Context!, ct));
        }
        catch (UnauthorizedAccessException) { return Results.Forbid(); }
        catch (KeyNotFoundException) { return Results.NotFound(); }
        catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
        catch (Stripe.StripeException) { return Results.Json(new { error = "Stripe indisponible." }, statusCode: 502); }
    }

    public static async Task<IResult> ReadPaymentAsync(Guid requestId, HttpContext httpContext,
        [FromServices] AdditionalDocumentsContextResolver resolver,
        [FromServices] MachineRequestPaymentService paymentService,
        [FromServices] MachineRequestStorageService storage,
        CancellationToken ct)
    {
        try
        {
            var prepared = await PreparePaymentAsync(requestId, httpContext, resolver, paymentService, storage, ct);
            if (prepared.Error is not null) return prepared.Error;
            var result = await paymentService.ReadAdditionalDocumentsAsync(requestId, prepared.Context!, ct);
            return result is null ? Results.NotFound() : Results.Ok(new
            { result.PaymentRequestId, result.Status, result.Amount, result.Currency });
        }
        catch (UnauthorizedAccessException) { return Results.Forbid(); }
        catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
        catch (Stripe.StripeException) { return Results.Json(new { error = "Stripe indisponible." }, statusCode: 502); }
    }

    public static async Task<IResult> CancelAsync(Guid requestId, HttpContext httpContext,
        [FromServices] AdditionalDocumentsContextResolver resolver,
        [FromServices] MachineRequestPaymentService paymentService,
        [FromServices] AdditionalDocumentsRequestDecisionService decisions,
        CancellationToken ct)
    {
        try
        {
            var payment = await paymentService.ReadPaymentAsync(requestId, ct);
            if (payment is null) return Results.NotFound();
            if (payment.RequestKind != MachineRequestKind.AdditionalDocuments
                || payment.TargetMachineId is null || string.IsNullOrWhiteSpace(payment.MachineRequestId))
                return Results.Forbid();
            var resolution = await resolver.ResolveAsync(httpContext.User, payment.TargetMachineId.Value, ct);
            if (!resolution.Success || resolution.Context is null)
                return resolution.Error == AdditionalDocumentsContextError.Forbidden
                    ? Results.Forbid() : Results.Conflict(new { error = resolution.ErrorMessage });
            if (payment.CompanyId != resolution.Context.CompanyId
                || payment.RequestedByUserId != resolution.Context.UserId)
                return Results.Forbid();
            var result = await decisions.AbandonAsync(payment.MachineRequestId, resolution.Context, ct);
            return result is null ? Results.NotFound() : Results.Ok(new
            { result.Payment.PaymentRequestId, PaymentStatus = result.Payment.Status, RequestStatus = result.Request.Status });
        }
        catch (UnauthorizedAccessException) { return Results.Forbid(); }
        catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
        catch (Stripe.StripeException) { return Results.Json(new { error = "Stripe indisponible." }, statusCode: 502); }
    }

    private static async Task<(AdditionalDocumentsContext? Context, IResult? Error)> PreparePaymentAsync(
        Guid requestId, HttpContext httpContext, AdditionalDocumentsContextResolver resolver,
        MachineRequestPaymentService paymentService, MachineRequestStorageService storage, CancellationToken ct)
    {
        var payment = await paymentService.ReadPaymentAsync(requestId, ct);
        if (payment is null) return (null, Results.NotFound());
        if (payment.RequestKind != MachineRequestKind.AdditionalDocuments || payment.TargetMachineId is null)
            return (null, Results.Forbid());
        var resolution = await resolver.ResolveAsync(httpContext.User, payment.TargetMachineId.Value, ct);
        if (!resolution.Success || resolution.Context is null)
            return (null, resolution.Error == AdditionalDocumentsContextError.Forbidden
                ? Results.Forbid() : Results.Conflict(new { error = resolution.ErrorMessage }));
        if (payment.CompanyId != resolution.Context.CompanyId || payment.RequestedByUserId != resolution.Context.UserId)
            return (null, Results.Forbid());
        if (payment.AmountCents <= 0 || payment.Currency != "EUR" || string.IsNullOrWhiteSpace(payment.MachineRequestId))
            return (null, Results.Conflict(new { error = "La tentative documentaire durable est incohérente." }));
        var record = await storage.GetAsync(payment.MachineRequestId, ct);
        if (record?.RequestKind != MachineRequestKind.AdditionalDocuments
            || record.Payment?.PaymentRequestId != payment.PaymentRequestId
            || record.CompanyId != payment.CompanyId || record.RequestedByUserId != payment.RequestedByUserId
            || record.TargetMachineId != payment.TargetMachineId || record.Pricing.TotalPages != payment.TotalPages
            || record.Payment.AuthorizedAmountCents != payment.AmountCents
            || AdditionalDocumentsPricing.CalculateAmountCents(record.Pricing.TotalPages) != payment.AmountCents)
            return (null, Results.Conflict(new { error = "Le request.json documentaire ne correspond pas à la tentative SQL." }));
        return (resolution.Context, null);
    }

    public static async Task<IResult> CreateAsync(
        Guid machineId,
        HttpContext httpContext,
        [FromServices] AdditionalDocumentsContextResolver contextResolver,
        [FromServices] MachineRequestPaymentStore paymentStore,
        [FromServices] MachineRequestStorageService storageService,
        [FromServices] IPdfPageCounter pageCounter,
        CancellationToken ct)
    {
        var resolution = await contextResolver.ResolveAsync(httpContext.User, machineId, ct);
        if (!resolution.Success || resolution.Context is null)
            return resolution.Error == AdditionalDocumentsContextError.Forbidden
                ? Results.Forbid()
                : Results.Conflict(new { error = resolution.ErrorMessage });
        if (!Guid.TryParse(httpContext.Request.Headers["Idempotency-Key"].FirstOrDefault(), out var attemptId))
            return Validation("Idempotency-Key", "Une clé d'idempotence UUID valide est requise.");
        if (!httpContext.Request.HasFormContentType)
            return Validation("request", "Le contenu multipart/form-data est requis.");

        IFormCollection form;
        try { form = await httpContext.Request.ReadFormAsync(ct); }
        catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
        { return Results.StatusCode(StatusCodes.Status413PayloadTooLarge); }
        catch (InvalidDataException) { return Validation("request", "La requête multipart est invalide."); }

        var files = form.Files.GetFiles("documents");
        if (files.Count == 0 || files.Count > MachineRequestUploadLimits.MaxDocumentCount)
            return Validation("documents", "Le nombre de PDF est invalide.");
        if (files.Sum(file => file.Length) > MachineRequestUploadLimits.MaxCombinedDocumentBytes)
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

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

            var totalPages = uploads.Sum(upload => upload.PageCount);
            var amountCents = AdditionalDocumentsPricing.CalculateAmountCents(totalPages);
            var requestId = attemptId.ToString("N");
            var now = DateTime.UtcNow;
            var proposed = new MachineRequestPayment(attemptId, totalPages, amountCents, "EUR",
                resolution.Context.UserEmail, null, null, "pending", now,
                MachineRequestId: requestId, RequestLinkedAtUtc: now,
                CompanyId: resolution.Context.CompanyId,
                RequestKind: MachineRequestKind.AdditionalDocuments,
                RequestedByUserId: resolution.Context.UserId,
                TargetMachineId: resolution.Context.MachineId);
            var payment = await paymentStore.AddOrGetAsync(proposed, ct);
            if (!MatchesPayment(payment, proposed))
                return Results.Conflict(new { error = "La clé d'idempotence est déjà associée à une autre tentative." });

            var draft = new MachineRequestDraft(
                new MachineRequestClient(resolution.Context.UserFirstName ?? string.Empty,
                    resolution.Context.UserLastName ?? string.Empty, resolution.Context.CompanyName,
                    resolution.Context.UserEmail, resolution.Context.UserPhone ?? string.Empty),
                new MachineRequestMachine(resolution.Context.MachineName, string.Empty, string.Empty, null,
                    "Ajout de documents à une machine existante"));
            var existing = await storageService.GetAsync(requestId, ct);
            if (existing is not null)
            {
                if (!await MatchesRecordAsync(storageService, existing, draft, uploads, payment, ct))
                    return Results.Conflict(new { error = "La clé d'idempotence est déjà associée à des documents différents." });
                return Results.Ok(Response(existing));
            }

            var reference = new MachineRequestPaymentReference(attemptId, totalPages, amountCents, "EUR",
                totalPages, amountCents, 0, 0, false);
            try
            {
                var record = await storageService.CreateAdditionalDocumentsAsync(draft, uploads, requestId,
                    reference, resolution.Context.CompanyId, resolution.Context.UserId,
                    resolution.Context.MachineId, ct);
                return Results.Json(Response(record), statusCode: StatusCodes.Status201Created);
            }
            catch
            {
                var reconciled = await storageService.GetAsync(requestId, CancellationToken.None);
                if (reconciled is not null && await MatchesRecordAsync(storageService, reconciled, draft,
                    uploads, payment, CancellationToken.None))
                    return Results.Ok(Response(reconciled));
                throw;
            }
        }
        catch (OverflowException) { return Validation("documents", "Le nombre total de pages est invalide."); }
        finally { foreach (var upload in uploads) await upload.Content.DisposeAsync(); }
    }

    private static bool MatchesPayment(MachineRequestPayment actual, MachineRequestPayment expected) =>
        actual.PaymentRequestId == expected.PaymentRequestId
        && actual.RequestKind == MachineRequestKind.AdditionalDocuments
        && actual.CompanyId == expected.CompanyId
        && actual.RequestedByUserId == expected.RequestedByUserId
        && actual.TargetMachineId == expected.TargetMachineId
        && actual.TotalPages == expected.TotalPages
        && actual.AmountCents == expected.AmountCents
        && actual.Currency == "EUR"
        && actual.Status == "pending"
        && actual.MachineRequestId == expected.MachineRequestId
        && actual.StripeSessionId is null
        && actual.StripePaymentIntentId is null
        && actual.AuthorizationEventId is null;

    private static async Task<bool> MatchesRecordAsync(MachineRequestStorageService storage,
        MachineRequestRecord record, MachineRequestDraft draft,
        IReadOnlyCollection<MachineRequestDocumentUpload> uploads, MachineRequestPayment payment, CancellationToken ct)
    {
        if (record.RequestKind != MachineRequestKind.AdditionalDocuments || record.CompanyId != payment.CompanyId
            || record.RequestedByUserId != payment.RequestedByUserId || record.TargetMachineId != payment.TargetMachineId
            || record.Payment?.PaymentRequestId != payment.PaymentRequestId || record.Client != draft.Client
            || record.Machine != draft.Machine || record.Pricing.TotalPages != payment.TotalPages
            || record.Payment.AuthorizedAmountCents != payment.AmountCents) return false;
        var submitted = uploads.ToArray();
        if (!record.Documents.Select(document => (document.OriginalName, document.Size, document.PageCount))
            .SequenceEqual(submitted.Select(upload => (upload.OriginalName, upload.Size, upload.PageCount)))) return false;
        for (var index = 0; index < submitted.Length; index++)
        {
            await using var stored = (await storage.OpenDocumentAsync(record.RequestId,
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

    private static AdditionalDocumentsStagedResponse Response(MachineRequestRecord record) => new(
        record.RequestId, record.Payment!.PaymentRequestId, record.Status, record.TargetMachineId!.Value,
        record.Documents.Count, record.Pricing.TotalPages, record.Payment.AuthorizedAmountCents, record.Payment.Currency);

    private static IResult Validation(string field, string message) => Results.ValidationProblem(
        new Dictionary<string, string[]> { [field] = [message] });
}

public sealed record AdditionalDocumentsStagedResponse(string RequestId, Guid PaymentRequestId, string Status,
    Guid TargetMachineId, int DocumentCount, int TotalPages, long AmountCents, string Currency);
