using Stripe;
using WebApp.Api.Models.Entities;
namespace WebApp.Api.Services;

public sealed partial class StripeBillingGateway : IStripeSubscriptionPaymentGateway
{
    public Task<SubscriptionInvoice> ReadAsync(BillingAccount account, string? invoiceId, CancellationToken ct) => ReadPaymentAsync(account, invoiceId, null, ct);
    public Task<SubscriptionInvoice> ReadForReconciliationAsync(BillingAccount account, SubscriptionReconciliationRequest request, CancellationToken ct) => ReadPaymentAsync(account, request.InvoiceId, request, ct);
    private async Task<SubscriptionInvoice> ReadPaymentAsync(BillingAccount account, string? invoiceId, SubscriptionReconciliationRequest? reconciliation, CancellationToken ct)
    {
        if (!StripeAdminEndpoints.TestActionsEnabled(options)) throw new SubscriptionReconciliationException("Stripe test required.");
        await ValidatePriceAsync(ct);
        var subscription = await subscriptions.GetAsync(account.StripeSubscriptionId, cancellationToken: ct);
        var snapshot = Snapshot(subscription, account.StripeCustomerId!, account.CompanyId);
        var invoice = await invoices.GetAsync(invoiceId ?? subscription.LatestInvoiceId, cancellationToken: ct);
        if (subscription.Livemode || invoice.Livemode || invoice.CustomerId != account.StripeCustomerId
            || invoice.Parent?.SubscriptionDetails?.SubscriptionId != account.StripeSubscriptionId
            || invoice.Metadata?.ContainsKey("diaglink_addition_id") == true
            || invoice.Currency != "eur" || invoice.BillingReason is not ("subscription_create" or "subscription_cycle")
            || invoice.Lines?.HasMore != false || invoice.Lines.Data.Count != 1)
            throw new SubscriptionReconciliationException("Subscription invoice mismatch.");
        DateTime? clockUtc = null;
        if (subscription.TestClockId != null || invoice.TestClockId != null)
        {
            if (subscription.Livemode || invoice.Livemode || string.IsNullOrEmpty(subscription.TestClockId)
                || subscription.TestClockId != invoice.TestClockId)
                throw new SubscriptionReconciliationException("Inconsistent test clock association.");
            var clock = await testClocks.GetAsync(subscription.TestClockId, cancellationToken: ct);
            if (clock.Id != subscription.TestClockId || clock.Livemode)
                throw new SubscriptionReconciliationException("Inconsistent test clock.");
            // Clock advancement is transient: let the webhook return 503 so Stripe retries.
            if (clock.Status != "ready") throw new InvalidOperationException("Test clock is not ready.");
            clockUtc = DateTime.SpecifyKind(clock.FrozenTime, DateTimeKind.Utc);
            if (clockUtc < snapshot.PeriodStartUtc || clockUtc >= snapshot.PeriodEndUtc)
                throw new SubscriptionReconciliationException("Test clock is outside the subscription cycle.");
        }
        var validationNow = clockUtc ?? DateTime.UtcNow;
        var line = invoice.Lines.Data[0];
        if (reconciliation != null)
        {
            var evt = await events.GetAsync(reconciliation.EventId, cancellationToken: ct);
            if (clockUtc == null || account.CompanyId != reconciliation.CompanyId
                || subscription.Id != reconciliation.SubscriptionId || invoice.Id != reconciliation.InvoiceId
                || invoice.Number != reconciliation.InvoiceNumber || invoice.BillingReason != "subscription_cycle"
                || line.Quantity != 1 || invoice.AmountPaid != reconciliation.AmountPaidCents
                || reconciliation.AmountPaidCents != 2990 || line.Period?.Start != reconciliation.StartUtc
                || line.Period.End != reconciliation.EndUtc || line.Period.End > snapshot.PeriodStartUtc
                || evt.Livemode || !string.IsNullOrEmpty(evt.Account) || evt.Id != reconciliation.EventId
                || evt.Type != "invoice.payment_succeeded" || evt.Data.Object is not Invoice eventInvoice
                || eventInvoice.Id != invoice.Id || eventInvoice.Status != "paid")
                throw new SubscriptionReconciliationException("Historical reconciliation expectations mismatch.");
        }
        var expected = checked(line.Quantity.GetValueOrDefault() * 2990);
        if (line.Pricing?.PriceDetails?.PriceId != options.PriceId || line.Quantity is null or < 0
            || line.Parent?.SubscriptionItemDetails?.Proration != false
            || line.Parent.SubscriptionItemDetails.Subscription != account.StripeSubscriptionId
            || line.Parent.SubscriptionItemDetails.SubscriptionItem != snapshot.ItemId
            || line.Period == null || line.Period.End <= line.Period.Start
            || line.Amount != expected || invoice.Subtotal != expected || invoice.TotalExcludingTax != expected
            || invoice.Total < expected || invoice.AmountDue != invoice.Total)
            throw new SubscriptionReconciliationException("Unexpected subscription invoice amount or cycle.");
        string? reference = null;
        DateTime? paidAt = null;
        if (invoice.Status == "paid" && expected > 0)
        {
            if (reconciliation == null && (snapshot.Status != "active" || snapshot.PeriodStartUtc != line.Period.Start || snapshot.PeriodEndUtc != line.Period.End))
                throw new SubscriptionReconciliationException("Paid cycle is not the current active subscription cycle.");
            if (invoice.AmountRemaining != 0 || invoice.AmountPaid != invoice.Total || invoice.StatusTransitions?.PaidAt == null)
                throw new SubscriptionReconciliationException("Invoice not fully paid.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            long paid = 0;
            await foreach (var payment in invoicePayments.ListAutoPagingAsync(new InvoicePaymentListOptions
            { Invoice = invoice.Id, Status = "paid", Limit = 100, Expand = ["data.payment.payment_intent"] }, cancellationToken: ct))
            {
                if (payment.InvoiceId != invoice.Id || payment.Status != "paid" || payment.Currency != "eur"
                    || payment.AmountPaid is not > 0 || payment.Payment?.Type != "payment_intent"
                    || payment.Payment.PaymentIntent is not { Status: "succeeded", Livemode: false } intent
                    || intent.CustomerId != account.StripeCustomerId || intent.Currency != "eur"
                    || intent.AmountReceived < payment.AmountPaid || string.IsNullOrEmpty(payment.Id))
                    throw new SubscriptionReconciliationException("Unverified subscription payment.");
                if (ids.Add(payment.Id)) paid = checked(paid + payment.AmountPaid.Value);
            }
            reference = System.Text.Json.JsonSerializer.Serialize(ids.Order(StringComparer.Ordinal));
            if (paid != invoice.Total || ids.Count == 0 || reference.Length > 2000)
                throw new SubscriptionReconciliationException("Settled payment required.");
            paidAt = DateTime.SpecifyKind(invoice.StatusTransitions.PaidAt.Value, DateTimeKind.Utc);
            if (paidAt > validationNow || paidAt < line.Period.Start) throw new SubscriptionReconciliationException("Invalid payment timestamp.");
        }
        var url = Uri.TryCreate(invoice.HostedInvoiceUrl, UriKind.Absolute, out var uri)
            && uri.Scheme == "https" && uri.Host == "invoice.stripe.com" && string.IsNullOrEmpty(uri.UserInfo)
            && invoice.Status == "open" ? uri.AbsoluteUri : null;
        return new(invoice.Id, snapshot.Id, invoice.Status, invoice.BillingReason,
            DateTime.SpecifyKind(line.Period.Start, DateTimeKind.Utc), DateTime.SpecifyKind(line.Period.End, DateTimeKind.Utc),
            line.Quantity!.Value, invoice.AmountPaid, paidAt, reference, url, snapshot.Status)
        { VerifiedTestClockId = subscription.TestClockId, VerifiedTestClockUtc = clockUtc };
    }
}

