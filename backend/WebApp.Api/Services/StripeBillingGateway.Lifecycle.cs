using Stripe;
using WebApp.Api.Models.Entities;
namespace WebApp.Api.Services;
public sealed partial class StripeBillingGateway : IStripeLifecycleGateway
{
    public async Task<StripeLifecycleSnapshot> ReadAsync(BillingAccount account, CancellationToken ct)
    {
        if (!StripeAdminEndpoints.TestActionsEnabled(options)) throw new InvalidOperationException("Stripe test required");
        var sub = await subscriptions.GetAsync(account.StripeSubscriptionId, cancellationToken:ct);
        if (sub.Livemode || sub.CustomerId != account.StripeCustomerId || sub.Metadata?.GetValueOrDefault("diaglink_company_id") != account.CompanyId.ToString()
            || sub.Items?.Data.Count != 1 || sub.Items.HasMore) throw new SubscriptionReconciliationException("Subscription ownership mismatch");
        var item = sub.Items.Data[0];
        if (item.Price?.Id != options.PriceId || item.Quantity < 0 || item.CurrentPeriodEnd <= item.CurrentPeriodStart) throw new SubscriptionReconciliationException("Subscription item mismatch");
        Invoice? invoice = sub.LatestInvoiceId == null ? null : await invoices.GetAsync(sub.LatestInvoiceId, cancellationToken:ct);
        if (invoice != null && (invoice.Livemode || invoice.CustomerId != account.StripeCustomerId
            || invoice.Parent?.SubscriptionDetails?.SubscriptionId != sub.Id || invoice.Currency != "eur")) throw new SubscriptionReconciliationException("Invoice ownership mismatch");
        return new(sub.Status,DateTime.SpecifyKind(item.CurrentPeriodStart,DateTimeKind.Utc),DateTime.SpecifyKind(item.CurrentPeriodEnd,DateTimeKind.Utc),
            sub.CancelAtPeriodEnd,invoice?.Id,invoice?.Status,invoice?.AmountRemaining,item.Id,item.Quantity);
    }
    public async Task SetQuantityAsync(BillingAccount account, StripeLifecycleSnapshot snapshot, int quantity, string key, CancellationToken ct)
    {
        if (quantity < 0 || !StripeAdminEndpoints.TestActionsEnabled(options)) throw new InvalidOperationException("Invalid quantity or mode");
        if (snapshot.Quantity == quantity || snapshot.Status == "canceled") return;
        var item = await subscriptionItems.UpdateAsync(snapshot.ItemId,new SubscriptionItemUpdateOptions {Quantity=quantity,ProrationBehavior="none"},
            new RequestOptions {IdempotencyKey=$"diaglink:lifecycle:{key}:quantity:{quantity}"},ct);
        if (item.Quantity != quantity || item.CurrentPeriodStart != snapshot.Start || item.CurrentPeriodEnd != snapshot.End)
            throw new SubscriptionReconciliationException("Cycle changed during quantity update");
    }
}
