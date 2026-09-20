using Stripe;
using Stripe.Checkout;
namespace WebApp.Api.Services;

public sealed class StripeWalletTopUpWebhook(StripeBillingOptions settings, CompanyWalletTopUpService service,
    ILogger<StripeWalletTopUpWebhook> logger)
{
    public static async Task<IResult> HandleHttpAsync(HttpRequest request, StripeWalletTopUpWebhook handler, CancellationToken ct)
    {
        using var reader = new StreamReader(request.Body, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: false);
        var result = await handler.HandleAsync(await reader.ReadToEndAsync(ct), request.Headers["Stripe-Signature"].ToString(), ct);
        return Results.Json(new { result.Status }, statusCode: result.HttpStatus);
    }
    public async Task<StripeAdditionWebhookResult> HandleAsync(string body, string signature, CancellationToken ct = default)
    {
        StripeAdditionWebhookResult Reply(string? id, string status, int http = 200)
        { logger.LogInformation("Stripe wallet top-up event {EventId}: {Status}", id, status); return new(status, http); }
        if (!StripeAdminEndpoints.TestActionsEnabled(settings) || !settings.TopUpWebhookSecret.StartsWith("whsec_", StringComparison.Ordinal))
            return Reply(null, "WebhookNotConfigured", 503);
        Event evt;
        try { evt = EventUtility.ConstructEvent(body, signature, settings.TopUpWebhookSecret); }
        catch (Exception ex) when (ex is StripeException or ArgumentException or System.Text.Json.JsonException)
        { return Reply(null, "InvalidSignatureOrPayload", 400); }
        if (evt.Type is not ("checkout.session.completed" or "checkout.session.async_payment_succeeded")) return Reply(evt.Id, "Ignored");
        if (evt.Livemode || !string.IsNullOrEmpty(evt.Account) || evt.Data?.Object is not Session session || session.Livemode
            || string.IsNullOrEmpty(session.Id) || string.IsNullOrEmpty(evt.Id)) return Reply(evt.Id, "InvalidEvent", 400);
        if (!Guid.TryParse(session.Metadata?.GetValueOrDefault("diaglink_topup_id"), out var id)) return Reply(evt.Id, "UnknownOperation");
        try
        {
            var result = await service.ConfirmAsync(id, session.Id, evt.Id, ct);
            return Reply(evt.Id, result?.Status ?? "UnknownOperation", result?.Status == "AwaitingPayment" ? 503 : 200);
        }
        catch (ArgumentException) { return Reply(evt.Id, "InvalidEvent", 400); }
        catch (InvalidOperationException) { return Reply(evt.Id, "ReconciliationRequired"); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError("Stripe wallet top-up {EventId} requires retry ({ErrorType})", evt.Id, ex.GetType().Name);
            return new("RetryRequired", 503);
        }
    }
}
