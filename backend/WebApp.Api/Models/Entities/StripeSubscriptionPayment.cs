namespace WebApp.Api.Models.Entities;

public class StripeSubscriptionPayment
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string StripeSubscriptionId { get; set; } = "";
    public string StripeInvoiceId { get; set; } = "";
    public string ExternalEventId { get; set; } = "";
    public string BillingReason { get; set; } = "";
    public DateTime PeriodStartUtc { get; set; }
    public DateTime PeriodEndUtc { get; set; }
    public DateTime PaymentConfirmedAtUtc { get; set; }
    public long AmountPaidCents { get; set; }
    public string PaymentReference { get; set; } = "";
    public string MachineIdsJson { get; set; } = "[]";
    public DateTime? CompletedAtUtc { get; set; }
}
