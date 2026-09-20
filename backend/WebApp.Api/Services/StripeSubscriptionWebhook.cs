using Stripe;
namespace WebApp.Api.Services;

// One signed destination can receive both invoice types; the existing addition handler is preserved.
public sealed class StripeSubscriptionWebhook(StripeBillingOptions settings, StripeSubscriptionPaymentService payments,
    StripeMachineAdditionWebhook additions, ILogger<StripeSubscriptionWebhook> logger, StripeLifecycleService? lifecycle = null)
{
    public static async Task<IResult> HandleHttpAsync(HttpRequest request, StripeSubscriptionWebhook handler, CancellationToken ct)
    {
        using var reader = new StreamReader(request.Body);
        var result = await handler.HandleAsync(await reader.ReadToEndAsync(ct), request.Headers["Stripe-Signature"].ToString(), ct);
        return Results.Json(new { result.Status }, statusCode: result.HttpStatus);
    }
    public async Task<StripeAdditionWebhookResult> HandleAsync(string body, string signature, CancellationToken ct = default)
    {
        if (!StripeAdminEndpoints.TestActionsEnabled(settings) || !settings.WebhookSecret.StartsWith("whsec_", StringComparison.Ordinal))
            return new("WebhookDisabled", 503);
        Event evt;
        try { evt = EventUtility.ConstructEvent(body, signature, settings.WebhookSecret); }
        catch (Exception ex) when (ex is StripeException or System.Text.Json.JsonException or ArgumentException)
        { return new("InvalidSignatureOrPayload", 400); }
        if (evt.Type is "invoice.payment_failed" or "invoice.finalization_failed" or "customer.subscription.updated" or "customer.subscription.deleted" or "invoice.upcoming")
        {
            if (evt.Livemode || !string.IsNullOrEmpty(evt.Account) || string.IsNullOrEmpty(evt.Id)
                || !evt.Id.StartsWith("evt_", StringComparison.Ordinal) || evt.Id.Length > 200) return new("InvalidEvent",400);
            var linkedId = evt.Data?.Object switch
            {
                Subscription s when evt.Type.StartsWith("customer.subscription.",StringComparison.Ordinal) => s.Id,
                Invoice i when i.Metadata?.ContainsKey("diaglink_addition_id") != true => i.Parent?.SubscriptionDetails?.SubscriptionId,
                _ => null
            };
            if (linkedId == null) return new("Ignored");
            if (lifecycle == null) return new("RetryRequired",503);
            try
            {
                var status = await lifecycle.ProcessAsync(linkedId,evt.Id,evt.Type,ct);
                logger.LogInformation("Stripe lifecycle {EventId}: {Status}",evt.Id,status);
                return new(status,status is "RetryRequired" or "SubscriptionNotLinked" ? 503 : 200);
            }
            catch (SubscriptionReconciliationException) { return new("ReconciliationRequired",409); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            { logger.LogError("Stripe lifecycle {EventId} failed ({ErrorType})",evt.Id,ex.GetType().Name);return new("RetryRequired",503); }
        }
        if (evt.Type != "invoice.payment_succeeded") return new("Ignored");
        if (evt.Livemode || !string.IsNullOrEmpty(evt.Account) || string.IsNullOrEmpty(evt.Id)
            || !evt.Id.StartsWith("evt_", StringComparison.Ordinal) || evt.Id.Length > 200 || evt.Data?.Object is not Invoice invoice
            || string.IsNullOrEmpty(invoice.Id) || invoice.Id.Length > 200)
            return new("InvalidEvent", 400);
        var subscriptionId = invoice.Parent?.SubscriptionDetails?.SubscriptionId;
        if (invoice.Metadata?.ContainsKey("diaglink_addition_id") == true || string.IsNullOrEmpty(subscriptionId))
            return await additions.HandleAsync(body, signature, ct);
        if (invoice.BillingReason is not ("subscription_create" or "subscription_cycle")) return new("Ignored");
        try
        {
            if (lifecycle != null)
            {
                var sync = await lifecycle.ProcessAsync(subscriptionId,evt.Id,evt.Type,ct);
                if(sync is "RetryRequired" or "SubscriptionNotLinked") return new(sync,503);
            }
            var status = await payments.ProcessAsync(invoice.Id, evt.Id, subscriptionId, ct);
            logger.LogInformation("Subscription invoice {InvoiceId} event {EventId}: {Status}", invoice.Id, evt.Id, status);
            return new(status, status is "AwaitingPayment" or "SubscriptionNotLinked" ? 503 : 200);
        }
        catch (SubscriptionReconciliationException)
        {
            logger.LogWarning("Subscription invoice {InvoiceId} requires reconciliation", invoice.Id);
            return new("ReconciliationRequired");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError("Subscription event {EventId} failed ({ErrorType}); retry required", evt.Id, ex.GetType().Name);
            return new("RetryRequired", 503);
        }
    }
}
