using Stripe;
using Stripe.Checkout;

namespace WebApp.Api.Services;

public sealed record MachineRequestWebhookResult(string Status, int HttpStatus);

public sealed class StripeMachineRequestPaymentWebhook(StripeBillingOptions settings, MachineRequestPaymentService service,
    ILogger<StripeMachineRequestPaymentWebhook> logger)
{
    public static async Task<IResult> HandleHttpAsync(HttpRequest request, StripeMachineRequestPaymentWebhook handler, CancellationToken ct)
    {
        using var reader = new StreamReader(request.Body, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: false);
        var result = await handler.HandleAsync(await reader.ReadToEndAsync(ct), request.Headers["Stripe-Signature"].ToString(), ct);
        return Results.Json(new { result.Status }, statusCode: result.HttpStatus);
    }

    public async Task<MachineRequestWebhookResult> HandleAsync(string body, string signature, CancellationToken ct = default)
    {
        MachineRequestWebhookResult Reply(string? eventId, string status, int http = 200)
        {
            logger.LogInformation("Stripe machine request payment event {EventId}: {Status}", eventId, status);
            return new(status, http);
        }
        if (!settings.Enabled || !(settings.SecretKey.StartsWith("sk_test_", StringComparison.Ordinal)
            || settings.SecretKey.StartsWith("rk_test_", StringComparison.Ordinal))
            || !settings.MachineRequestWebhookSecret.StartsWith("whsec_", StringComparison.Ordinal))
            return Reply(null, "webhook_not_configured", 503);
        Event evt;
        try { evt = EventUtility.ConstructEvent(body, signature, settings.MachineRequestWebhookSecret); }
        catch (Exception ex) when (ex is StripeException or ArgumentException or System.Text.Json.JsonException)
        { return Reply(null, "invalid_signature_or_payload", 400); }
        if (evt.Type is not ("checkout.session.completed" or "checkout.session.async_payment_succeeded"))
            return Reply(evt.Id, "ignored");
        if (evt.Livemode || !string.IsNullOrEmpty(evt.Account) || evt.Data?.Object is not Session session
            || session.Livemode || string.IsNullOrEmpty(evt.Id) || string.IsNullOrEmpty(session.Id))
            return Reply(evt.Id, "invalid_event", 400);
        var paymentType = session.Metadata?.GetValueOrDefault("diaglink_payment_type");
        if (paymentType is not ("machine_request_preparation" or "machine_request_additional_documents")
            || !Guid.TryParse(session.Metadata?.GetValueOrDefault("diaglink_payment_request_id"), out var id))
            return Reply(evt.Id, "unknown_payment");
        try
        {
            var status = await service.ConfirmAuthorizationAsync(id, session.Id, evt.Id, ct);
            return Reply(evt.Id, status, status == "pending" ? 503 : status == "invalid" ? 400 : 200);
        }
        catch (InvalidOperationException) { return Reply(evt.Id, "verification_failed"); }
        catch (StripeException ex)
        {
            logger.LogError("Stripe payment verification requires retry ({ErrorType})", ex.GetType().Name);
            return Reply(evt.Id, "retry_required", 503);
        }
    }
}
