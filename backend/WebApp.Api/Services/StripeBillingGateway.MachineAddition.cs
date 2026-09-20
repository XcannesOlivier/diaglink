using Stripe;
using WebApp.Api.Models.Entities;

namespace WebApp.Api.Services;

public sealed partial class StripeBillingGateway : IStripeMachineAdditionGateway
{
    private static RequestOptions AdditionKey(StripeMachineAddition op, string step) =>
        new() { IdempotencyKey = $"diaglink:machine-add:{op.Id:N}:{step}" };

    private void ValidateAddition(StripeMachineAddition op)
    {
        options.Validate();
        if (options.PriceId != op.StripePriceId || DateTime.UtcNow - op.CreatedAtUtc >= TimeSpan.FromHours(23)
            || DateTime.UtcNow >= op.CycleEndUtc)
            throw new InvalidOperationException("Addition expired or price changed; reconciliation required.");
    }

    public async Task SetQuantityAsync(StripeMachineAddition op, CancellationToken ct)
    {
        ValidateAddition(op);
        var current = await GetSubscriptionAsync(op.StripeSubscriptionId, op.StripeCustomerId, op.CompanyId, ct);
        if (current.Status != "active" || current.ItemId != op.StripeSubscriptionItemId
            || current.PeriodStartUtc != op.CycleStartUtc || current.PeriodEndUtc != op.CycleEndUtc
            || (current.Quantity != op.OriginalQuantity && current.Quantity != op.TargetQuantity))
            throw new InvalidOperationException("Subscription changed outside this addition; reconciliation required.");
        if (current.Quantity == op.TargetQuantity) return;
        ValidateAddition(op);
        // Absolute quantity, no billing_cycle_anchor and no price change.
        var item = await subscriptionItems.UpdateAsync(op.StripeSubscriptionItemId,
            new SubscriptionItemUpdateOptions { Quantity = op.TargetQuantity, ProrationBehavior = "none" }, AdditionKey(op, "quantity"), ct);
        if (item.Quantity != op.TargetQuantity || item.CurrentPeriodStart != op.CycleStartUtc || item.CurrentPeriodEnd != op.CycleEndUtc)
            throw new InvalidOperationException("Unexpected subscription quantity or period after update.");
    }

    public async Task<string> CreateInvoiceAsync(StripeMachineAddition op, CancellationToken ct)
    {
        ValidateAddition(op);
        var invoice = await invoices.CreateAsync(new InvoiceCreateOptions
        {
            Customer = op.StripeCustomerId, Currency = "eur", AutoAdvance = false,
            CollectionMethod = "charge_automatically", PendingInvoiceItemsBehavior = "exclude",
            Metadata = new() { ["diaglink_addition_id"] = op.Id.ToString(), ["diaglink_machine_id"] = op.MachineId.ToString(),
                ["diaglink_subscription_id"] = op.StripeSubscriptionId }
        }, AdditionKey(op, "invoice"), ct);
        CheckInvoiceOwner(invoice, op);
        return invoice.Id;
    }

    public async Task AddInvoiceLinesAsync(StripeMachineAddition op, CancellationToken ct)
    {
        if (op.StripeInvoiceId == null) throw new InvalidOperationException("Draft invoice required.");
        foreach (var line in new[] { (Key: "ai", Amount: op.AiAmountCents, Description: "DiagLink - budget IA complet"),
            (Key: "service", Amount: op.ServiceAmountCents, Description: "DiagLink - service au prorata") })
        {
            ValidateAddition(op);
            await invoiceItems.CreateAsync(new InvoiceItemCreateOptions
            {
                Customer = op.StripeCustomerId, Invoice = op.StripeInvoiceId, Amount = line.Amount,
                Currency = "eur", TaxBehavior = "exclusive", Discountable = false, Description = line.Description,
                Period = new InvoiceItemPeriodOptions { Start = op.ActivatedAtUtc, End = op.CycleEndUtc },
                Metadata = new() { ["diaglink_addition_id"] = op.Id.ToString(), ["diaglink_component"] = line.Key }
            }, AdditionKey(op, line.Key), ct);
        }
    }

    public async Task FinalizeInvoiceAsync(StripeMachineAddition op, CancellationToken ct)
    {
        ValidateAddition(op);
        var invoice = await invoices.GetAsync(op.StripeInvoiceId, cancellationToken: ct);
        CheckInvoiceOwner(invoice, op);
        if (invoice.Subtotal != op.AiAmountCents + op.ServiceAmountCents || invoice.Lines.HasMore || invoice.Lines.Data.Count != 2)
            throw new InvalidOperationException("Unexpected invoice lines or subtotal; reconciliation required.");
        if (invoice.Status == "draft")
        {
            ValidateAddition(op);
            invoice = await invoices.FinalizeInvoiceAsync(invoice.Id, new InvoiceFinalizeOptions { AutoAdvance = false }, AdditionKey(op, "finalize"), ct);
        }
        if (invoice.Status is not ("open" or "paid"))
            throw new InvalidOperationException("Invoice not finalized successfully.");
    }

    private static void CheckInvoiceOwner(Invoice invoice, StripeMachineAddition op)
    {
        if (invoice.CustomerId != op.StripeCustomerId || invoice.Currency != "eur"
            || invoice.Metadata?.GetValueOrDefault("diaglink_addition_id") != op.Id.ToString())
            throw new InvalidOperationException("Invoice ownership mismatch.");
    }

    public async Task<StripeAdditionInvoiceState> GetInvoicePaymentAsync(StripeMachineAddition op, CancellationToken ct)
    {
        // No 23h/current-cycle guard: payment confirmation can legitimately arrive much later.
        options.Validate();
        if (string.IsNullOrEmpty(op.StripeInvoiceId)) throw new InvalidOperationException("Invoice required.");
        var invoice = await invoices.GetAsync(op.StripeInvoiceId, cancellationToken: ct);
        CheckInvoiceOwner(invoice, op);
        if (invoice.Id != op.StripeInvoiceId) throw new InvalidOperationException("Invoice ID mismatch.");
        if (invoice.Status == "draft") return new(invoice.Status);
        var expected = op.AiAmountCents + op.ServiceAmountCents;
        if (invoice.Subtotal != expected || invoice.Lines?.HasMore != false || invoice.Lines.Data.Count != 2
            || invoice.Status is "void" or "uncollectible")
            return new(invoice.Status, ReconciliationRequired: true);
        if (invoice.Status != "paid") return new(invoice.Status);
        if (invoice.TotalExcludingTax != expected || invoice.Total < expected || invoice.AmountRemaining != 0
            || invoice.AmountPaid < invoice.Total || invoice.StatusTransitions?.PaidAt is not { } paidAt)
            return new(invoice.Status, ReconciliationRequired: true);

        // Require Stripe-settled payments covering the entire invoice (including any tax).
        // Out-of-band/manual payment records and credits alone are not accepted as proof.
        var references = new HashSet<string>(StringComparer.Ordinal);
        long paid = 0;
        await foreach (var payment in invoicePayments.ListAutoPagingAsync(new InvoicePaymentListOptions
        {
            Invoice = invoice.Id, Status = "paid", Limit = 100,
            Expand = ["data.payment.payment_intent"]
        }, cancellationToken: ct))
        {
            if (payment.InvoiceId != invoice.Id || payment.Currency != "eur" || payment.Status != "paid"
                || payment.AmountPaid is not > 0 || payment.Payment?.Type != "payment_intent"
                || payment.Payment.PaymentIntent is not { Status: "succeeded" } intent
                || intent.Currency != "eur" || intent.CustomerId != op.StripeCustomerId
                || intent.AmountReceived < payment.AmountPaid || string.IsNullOrEmpty(payment.Id))
                return new(invoice.Status, ReconciliationRequired: true);
            if (references.Add(payment.Id)) paid = checked(paid + payment.AmountPaid.Value);
        }
        var reference = System.Text.Json.JsonSerializer.Serialize(references.Order(StringComparer.Ordinal));
        if (paid < invoice.Total || references.Count == 0 || reference.Length > 2000)
            return new(invoice.Status, ReconciliationRequired: true);
        return new(invoice.Status, new(reference, DateTime.SpecifyKind(paidAt, DateTimeKind.Utc)));
    }
}
