using WebApp.Api.Models.Entities;
namespace WebApp.Api.Services;

public sealed class SubscriptionReconciliationException(string message) : InvalidOperationException(message);

public record SubscriptionInvoice(string Id, string SubscriptionId, string Status, string BillingReason,
    DateTime StartUtc, DateTime EndUtc, long Quantity, long AmountPaidCents,
    DateTime? PaidAtUtc, string? PaymentReference, string? PaymentUrl, string SubscriptionStatus)
{
    public string? VerifiedTestClockId { get; init; }
    public DateTime? VerifiedTestClockUtc { get; init; }
    public bool Confirmed => Status == "paid" && PaidAtUtc != null && !string.IsNullOrEmpty(PaymentReference);
}

public interface IStripeSubscriptionPaymentGateway
{
    Task<SubscriptionInvoice> ReadForReconciliationAsync(BillingAccount account, SubscriptionReconciliationRequest request, CancellationToken ct) => throw new SubscriptionReconciliationException("Explicit reconciliation is not supported.");
    Task<SubscriptionInvoice> ReadAsync(BillingAccount account, string? invoiceId, CancellationToken ct);
}

public record SubscriptionReconciliationRequest(Guid CompanyId, Guid MachineId, string SubscriptionId, string InvoiceId, string InvoiceNumber, string EventId, DateTime StartUtc, DateTime EndUtc, long AmountPaidCents);

