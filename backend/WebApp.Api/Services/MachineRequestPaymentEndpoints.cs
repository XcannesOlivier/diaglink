using Microsoft.AspNetCore.Mvc;

namespace WebApp.Api.Services;

public record CreateMachineRequestPayment(int TotalPages, string? Email);

public static class MachineRequestPaymentEndpoints
{
    public static void MapMachineRequestPayments(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/public/machine-request-payments", CreateAsync).AllowAnonymous();
        app.MapGet("/api/public/machine-request-payments/{paymentRequestId:guid}", ReadAsync).AllowAnonymous();
        app.MapPost("/api/stripe/webhooks/machine-request-payments", StripeMachineRequestPaymentWebhook.HandleHttpAsync).AllowAnonymous();
        var company = app.MapGroup("/api/company/machine-request-payments").RequireAuthorization("CompanyAdminOnly");
        company.MapPost("", CreateAdditionalAsync)
            .Accepts<IFormFileCollection>("multipart/form-data")
            .WithMetadata(new RequestSizeLimitAttribute(MachineRequestUploadLimits.MaxRequestBodyBytes))
            .WithMetadata(new RequestFormLimitsAttribute { MultipartBodyLengthLimit = MachineRequestUploadLimits.MaxRequestBodyBytes });
        company.MapGet("/{paymentRequestId:guid}", ReadAdditionalAsync);
        var admin = app.MapGroup("/api/admin/machine-request-payments").RequireAuthorization("SuperAdminOnly");
        admin.MapPost("/{paymentRequestId:guid}/capture", CaptureAsync);
        admin.MapPost("/{paymentRequestId:guid}/cancel", CancelAsync);
    }

    public static async Task<IResult> CreateAdditionalAsync(HttpContext httpContext,
        [FromServices] AdditionalMachinePaymentContextResolver contextResolver,
        [FromServices] MachineRequestPaymentService service,
        [FromServices] IPdfPageCounter pageCounter,
        CancellationToken ct)
    {
        var contextResult = await contextResolver.ResolveAsync(httpContext.User, ct);
        if (!contextResult.Success || contextResult.Context is null)
            return ContextError(contextResult);
        if (!httpContext.Request.HasFormContentType)
            return Results.BadRequest(new { error = "Le contenu multipart/form-data est requis." });
        if (!Guid.TryParse(httpContext.Request.Headers["Idempotency-Key"].FirstOrDefault(), out var paymentRequestId))
            return Results.BadRequest(new { error = "Un Idempotency-Key UUID stable est requis." });

        IFormCollection form;
        try { form = await httpContext.Request.ReadFormAsync(ct); }
        catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
        { return Results.StatusCode(StatusCodes.Status413PayloadTooLarge); }
        catch (InvalidDataException) { return Results.BadRequest(new { error = "La requête multipart est invalide." }); }

        var files = form.Files.GetFiles("documents");
        if (files.Count == 0 || files.Count > MachineRequestUploadLimits.MaxDocumentCount)
            return Results.BadRequest(new { error = "Le nombre de PDF est invalide." });
        if (files.Sum(file => file.Length) > MachineRequestUploadLimits.MaxCombinedDocumentBytes)
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

        var totalPages = 0;
        foreach (var file in files)
        {
            if (file.Length <= 0 || file.Length > MachineRequestUploadLimits.MaxDocumentBytes
                || !file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                return Results.BadRequest(new { error = $"Le fichier {Path.GetFileName(file.FileName)} est invalide." });
            try
            {
                await using var stream = file.OpenReadStream();
                totalPages = checked(totalPages + await pageCounter.CountPagesAsync(stream, ct));
            }
            catch (Exception ex) when (ex is InvalidDataException or OverflowException)
            { return Results.BadRequest(new { error = $"Le fichier {Path.GetFileName(file.FileName)} est illisible." }); }
        }

        try
        {
            return Results.Ok(await service.CreateAdditionalAsync(paymentRequestId, totalPages,
                contextResult.Context, ct));
        }
        catch (UnauthorizedAccessException) { return Results.Forbid(); }
        catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
        catch (Stripe.StripeException) { return Results.Json(new { error = "Stripe indisponible." }, statusCode: 502); }
    }

    public static async Task<IResult> ReadAdditionalAsync(Guid paymentRequestId, HttpContext httpContext,
        [FromServices] AdditionalMachinePaymentContextResolver contextResolver,
        [FromServices] MachineRequestPaymentService service,
        CancellationToken ct)
    {
        var contextResult = await contextResolver.ResolveAsync(httpContext.User, ct);
        if (!contextResult.Success || contextResult.Context is null)
            return ContextError(contextResult);
        try
        {
            var result = await service.ReadAdditionalAsync(paymentRequestId, contextResult.Context, ct);
            return result is null ? Results.NotFound() : Results.Ok(new
            {
                result.PaymentRequestId, result.Status, result.Amount, result.Currency
            });
        }
        catch (UnauthorizedAccessException) { return Results.Forbid(); }
    }

    private static IResult ContextError(AdditionalMachinePaymentContextResolution resolution) =>
        resolution.Error == AdditionalMachinePaymentContextError.Forbidden
            ? Results.Forbid()
            : Results.Conflict(new { error = resolution.ErrorMessage });

    public static async Task<IResult> CreateAsync(CreateMachineRequestPayment request, HttpContext httpContext,
        MachineRequestPaymentService service, CancellationToken ct)
    {
        if (!Guid.TryParse(httpContext.Request.Headers["Idempotency-Key"].FirstOrDefault(), out var paymentRequestId))
            return Results.BadRequest(new { error = "Un Idempotency-Key UUID stable est requis." });
        try { return Results.Ok(await service.CreateAsync(paymentRequestId, request.TotalPages, request.Email, ct)); }
        catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
        catch (Stripe.StripeException) { return Results.Json(new { error = "Stripe indisponible." }, statusCode: 502); }
    }

    public static async Task<IResult> ReadAsync(Guid paymentRequestId, MachineRequestPaymentService service, CancellationToken ct)
    {
        var result = await service.ReadAsync(paymentRequestId, ct);
        return result is null ? Results.NotFound() : Results.Ok(new
        {
            result.PaymentRequestId,
            result.Status,
            result.Amount,
            result.Currency
        });
    }

    public static async Task<IResult> CaptureAsync(Guid paymentRequestId,
        [FromServices] MachineRequestPaymentService service,
        [FromServices] MachineRequestStorageService storageService,
        [FromServices] ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        try
        {
            var payment = await service.ReadPaymentAsync(paymentRequestId, ct);
            if (payment is null) return Results.NotFound();
            long? preparationAmountCents = null;
            if (MachineRequestPreparationPricing.IncludesMaximumFirstSubscription(payment.TotalPages, payment.AmountCents))
            {
                if (string.IsNullOrEmpty(payment.MachineRequestId))
                    return Results.Conflict(new { error = "La demande machine liée est introuvable." });
                var request = await storageService.GetAsync(payment.MachineRequestId, ct);
                if (request?.Payment?.PaymentRequestId != paymentRequestId)
                    return Results.Conflict(new { error = "La demande machine liée est incohérente." });
                preparationAmountCents = MachineRequestPreparationPricing.ToCents(request.Pricing.PreparationTotal);
                if (request.Payment.AuthorizedAmountCents != payment.AmountCents
                    || request.Payment.RecalculatedAmountCents != checked(preparationAmountCents.Value
                        + MachineRequestPreparationPricing.MaximumFirstSubscriptionCents))
                    return Results.Conflict(new { error = "Le coût documentaire serveur ne correspond pas à l'autorisation." });
            }
            if (string.IsNullOrWhiteSpace(payment.MachineRequestId))
                return Results.Conflict(new { error = "La demande machine liée est introuvable." });
            var decisionRequest = await storageService.GetAsync(payment.MachineRequestId, ct);
            if (decisionRequest?.Payment?.PaymentRequestId != paymentRequestId)
                return Results.Conflict(new { error = "La demande machine liée est incohérente." });
            var result = await service.CaptureFromAdminDecisionAsync(
                paymentRequestId, preparationAmountCents, decisionRequest, ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }
        catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
        catch (Stripe.StripeException ex)
        {
            loggerFactory.CreateLogger("WebApp.Api.Services.MachineRequestPaymentEndpoints").LogError(ex,
                "Stripe machine request capture failed for PaymentRequestId {PaymentRequestId}. " +
                "StripeExceptionType={StripeExceptionType} StripeMessage={StripeMessage} " +
                "StripeHttpStatus={StripeHttpStatus} StripeErrorCode={StripeErrorCode} " +
                "StripeErrorType={StripeErrorType} StripeErrorMessage={StripeErrorMessage} " +
                "StripeRequestId={StripeRequestId}",
                paymentRequestId, ex.GetType().FullName, ex.Message, ex.HttpStatusCode,
                ex.StripeError?.Code, ex.StripeError?.Type, ex.StripeError?.Message, ex.StripeResponse?.RequestId);
            return Results.Json(new { error = "Stripe indisponible." }, statusCode: 502);
        }
    }

    public static async Task<IResult> CancelAsync(Guid paymentRequestId,
        MachineRequestPaymentService service, MachineRequestStorageService storageService, CancellationToken ct)
    {
        try
        {
            var payment = await service.ReadPaymentAsync(paymentRequestId, ct);
            if (payment is null) return Results.NotFound();
            if (string.IsNullOrWhiteSpace(payment.MachineRequestId))
                return Results.Conflict(new { error = "La demande machine liée est introuvable." });
            var request = await storageService.GetAsync(payment.MachineRequestId, ct);
            if (request?.Payment?.PaymentRequestId != paymentRequestId)
                return Results.Conflict(new { error = "La demande machine liée est incohérente." });
            var result = await service.CancelFromAdminDecisionAsync(paymentRequestId, request, ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }
        catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
        catch (Stripe.StripeException) { return Results.Json(new { error = "Stripe indisponible." }, statusCode: 502); }
    }

    private static async Task<IResult> ChangeAuthorizationAsync(Guid paymentRequestId,
        Func<Guid, CancellationToken, Task<MachineRequestPaymentResult?>> operation, CancellationToken ct)
    {
        try
        {
            var result = await operation(paymentRequestId, ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }
        catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
        catch (Stripe.StripeException) { return Results.Json(new { error = "Stripe indisponible." }, statusCode: 502); }
    }
}
