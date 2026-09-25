using System.Net.Mail;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using WebApp.Api.Models;

namespace WebApp.Api.Services;

public static partial class MachineRequestPublicEndpoints
{
    public const string Route = "/api/public/machine-requests";

    public static void MapPublicMachineRequests(this WebApplication app)
    {
        app.MapPost(Route, CreateAsync)
            .AllowAnonymous()
            .Accepts<IFormFileCollection>("multipart/form-data")
            .WithMetadata(new RequestSizeLimitAttribute(MachineRequestUploadLimits.MaxRequestBodyBytes))
            .WithMetadata(new RequestFormLimitsAttribute { MultipartBodyLengthLimit = MachineRequestUploadLimits.MaxRequestBodyBytes });
    }

    public static async Task<IResult> CreateAsync(
        HttpContext httpContext,
        [FromServices] MachineRequestStorageService storageService,
        MachineRequestPaymentService paymentService,
        IPdfPageCounter pageCounter,
        CancellationToken cancellationToken,
        [FromServices] RequestReceivedNotificationService? notifications = null)
    {
        if (!httpContext.Request.HasFormContentType)
            return ValidationError("request", "Le contenu multipart/form-data est requis.");

        IFormCollection form;
        try { form = await httpContext.Request.ReadFormAsync(cancellationToken); }
        catch (BadHttpRequestException exception) when (exception.StatusCode == StatusCodes.Status413PayloadTooLarge)
        { return Results.Problem(statusCode: StatusCodes.Status413PayloadTooLarge, title: "La requête dépasse la taille autorisée."); }
        catch (InvalidDataException)
        { return Results.Problem(statusCode: StatusCodes.Status413PayloadTooLarge, title: "La requête multipart est invalide ou trop volumineuse."); }

        if (!Guid.TryParse(form["paymentRequestId"].FirstOrDefault(), out var paymentRequestId))
            return ValidationError("paymentRequestId", "La référence de paiement est invalide.");

        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["firstName"] = Trim(form["firstName"].FirstOrDefault()),
            ["lastName"] = Trim(form["lastName"].FirstOrDefault()),
            ["company"] = Trim(form["company"].FirstOrDefault()),
            ["email"] = Trim(form["email"].FirstOrDefault()),
            ["phone"] = Trim(form["phone"].FirstOrDefault()),
            ["machineName"] = Trim(form["machineName"].FirstOrDefault()),
            ["manufacturer"] = Trim(form["manufacturer"].FirstOrDefault()),
            ["model"] = Trim(form["model"].FirstOrDefault()),
            ["serialNumber"] = NullIfEmpty(Trim(form["serialNumber"].FirstOrDefault())),
            ["description"] = NullIfEmpty(Trim(form["description"].FirstOrDefault())),
        };

        foreach (var field in new[] { "firstName", "lastName", "company", "email", "phone", "machineName", "manufacturer", "model" })
        {
            if (string.IsNullOrWhiteSpace(values[field])) return ValidationError(field, "Ce champ est obligatoire.");
            if (values[field]!.Length > MachineRequestUploadLimits.MaxShortTextLength) return ValidationError(field, "Ce champ est trop long.");
        }
        if (values["serialNumber"]?.Length > MachineRequestUploadLimits.MaxShortTextLength) return ValidationError("serialNumber", "Ce champ est trop long.");
        if (values["description"]?.Length > MachineRequestUploadLimits.MaxDescriptionLength) return ValidationError("description", "Ce champ est trop long.");
        if (!MailAddress.TryCreate(values["email"], out _)) return ValidationError("email", "L’adresse e-mail est invalide.");
        if (!IsReasonablePhone(values["phone"]!)) return ValidationError("phone", "Le numéro de téléphone est invalide.");

        var files = form.Files.GetFiles("documents");
        if (files.Count == 0) return ValidationError("documents", "Au moins un document PDF est requis.");
        if (files.Count > MachineRequestUploadLimits.MaxDocumentCount) return ValidationError("documents", $"Le nombre maximal de PDF est {MachineRequestUploadLimits.MaxDocumentCount}.");
        if (files.Sum(file => file.Length) > MachineRequestUploadLimits.MaxCombinedDocumentBytes) return ValidationError("documents", "La taille totale des PDF dépasse la limite autorisée.");

        MachineRequestPayment payment;
        try
        {
            var verification = await paymentService.VerifyForMachineRequestAsync(paymentRequestId, cancellationToken);
            if (verification.Status == "not_found") return Results.NotFound(new { error = "La référence de paiement est introuvable." });
            if (verification.Status != "authorized" || verification.Payment is null)
                return Results.Conflict(new { error = "Le paiement n'est pas autorisé ou n'est plus intégralement capturable." });
            payment = verification.Payment;
        }
        catch (Stripe.StripeException)
        { return Results.Json(new { error = "La vérification Stripe est temporairement indisponible." }, statusCode: StatusCodes.Status503ServiceUnavailable); }
        catch (InvalidOperationException)
        { return Results.Conflict(new { error = "L'autorisation Stripe ne correspond plus au paiement attendu." }); }

        if (payment.MachineRequestId is not null)
        {
            var existing = await storageService.GetAsync(payment.MachineRequestId, cancellationToken);
            if (existing is not null && existing.Payment?.PaymentRequestId == paymentRequestId)
            {
                if (notifications is not null) await notifications.TryEnqueueAsync(paymentRequestId, CancellationToken.None);
                return Results.Ok(Response(existing));
            }
        }

        var uploads = new List<MachineRequestDocumentUpload>(files.Count);
        try
        {
            foreach (var file in files)
            {
                if (file.Length <= 0) return ValidationError("documents", $"Le fichier {Path.GetFileName(file.FileName)} est vide.");
                if (file.Length > MachineRequestUploadLimits.MaxDocumentBytes) return ValidationError("documents", $"Le fichier {Path.GetFileName(file.FileName)} dépasse la taille autorisée.");
                if (!file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) return ValidationError("documents", $"Le fichier {Path.GetFileName(file.FileName)} n’est pas un PDF.");

                int pageCount;
                await using (var inspectionStream = file.OpenReadStream())
                {
                    try { pageCount = await pageCounter.CountPagesAsync(inspectionStream, cancellationToken); }
                    catch (InvalidDataException) { return ValidationError("documents", $"Le fichier {Path.GetFileName(file.FileName)} est un PDF invalide ou illisible."); }
                }
                uploads.Add(new MachineRequestDocumentUpload(Path.GetFileName(file.FileName), "application/pdf",
                    file.Length, pageCount, file.OpenReadStream()));
            }

            var receivedPages = uploads.Sum(upload => upload.PageCount);
            var includesMaximumSubscription = MachineRequestPreparationPricing.IncludesMaximumFirstSubscription(
                payment.TotalPages, payment.AmountCents);
            var recalculatedAmountCents = MachineRequestPreparationPricing.CalculatePreparationCents(receivedPages)
                + (includesMaximumSubscription ? MachineRequestPreparationPricing.MaximumFirstSubscriptionCents : 0);
            var paymentReference = new MachineRequestPaymentReference(payment.PaymentRequestId, payment.TotalPages,
                payment.AmountCents, payment.Currency, receivedPages, recalculatedAmountCents,
                receivedPages - payment.TotalPages, recalculatedAmountCents - payment.AmountCents,
                recalculatedAmountCents > payment.AmountCents);

            payment = await paymentService.ReserveMachineRequestAsync(payment, cancellationToken);
            var requestId = payment.MachineRequestId
                ?? throw new InvalidOperationException("La réservation de la demande a échoué.");
            var existing = await storageService.GetAsync(requestId, cancellationToken);
            if (existing is not null)
            {
                if (existing.Payment?.PaymentRequestId != paymentRequestId)
                    throw new InvalidOperationException("La demande existante ne correspond pas au paiement.");
                if (notifications is not null) await notifications.TryEnqueueAsync(paymentRequestId, CancellationToken.None);
                return Results.Ok(Response(existing));
            }

            var draft = new MachineRequestDraft(
                new MachineRequestClient(values["firstName"]!, values["lastName"]!, values["company"]!, values["email"]!, values["phone"]!),
                new MachineRequestMachine(values["machineName"]!, values["manufacturer"]!, values["model"]!, values["serialNumber"], values["description"]));
            MachineRequestRecord record;
            try { record = await storageService.CreateAsync(draft, uploads, requestId, paymentReference, cancellationToken); }
            catch
            {
                var reconciled = await storageService.GetAsync(requestId, CancellationToken.None);
                if (reconciled is not null && reconciled.Payment?.PaymentRequestId == paymentRequestId)
                {
                    if (notifications is not null) await notifications.TryEnqueueAsync(paymentRequestId, CancellationToken.None);
                    return Results.Ok(Response(reconciled));
                }
                throw;
            }
            if (notifications is not null) await notifications.TryEnqueueAsync(paymentRequestId, CancellationToken.None);
            return Results.Json(Response(record), statusCode: StatusCodes.Status201Created);
        }
        catch (InvalidOperationException exception)
        { return Results.Conflict(new { error = exception.Message }); }
        finally
        { foreach (var upload in uploads) await upload.Content.DisposeAsync(); }
    }

    private static MachineRequestCreatedResponse Response(MachineRequestRecord record) => new(
        record.RequestId, record.Status, record.CreatedAt, record.Documents.Count, record.Pricing.TotalPages,
        record.Pricing.AdditionalPages, record.Pricing.PreparationTotal,
        record.Payment?.AuthorizationInsufficient ?? false);

    private static IResult ValidationError(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });
    private static string? Trim(string? value) => value?.Trim();
    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;
    private static bool IsReasonablePhone(string phone) => PhonePattern().IsMatch(phone) && phone.Count(char.IsDigit) >= 6;

    [GeneratedRegex(@"^\+?[\d\s().-]{7,25}$", RegexOptions.CultureInvariant)]
    private static partial Regex PhonePattern();
}

public sealed record MachineRequestCreatedResponse(
    string RequestId,
    string Status,
    DateTimeOffset CreatedAt,
    int DocumentCount,
    int TotalPages,
    int AdditionalPages,
    decimal PreparationTotal,
    bool AuthorizationInsufficient);
