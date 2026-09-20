namespace WebApp.Api.Models.Entities;

public enum StripeWalletTopUpStage { Reserved, PaymentCreated, AwaitingPayment, PaymentConfirmed, WalletCredited, Completed }

public class StripeWalletTopUp
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public required string StripeCustomerId { get; set; }
    public int AmountCents { get; set; }
    public string Currency { get; set; } = "EUR";
    public required string ReturnUrl { get; set; }
    public StripeWalletTopUpStage Stage { get; set; }
    public string? StripeSessionId { get; set; }
    public string? PaymentUrl { get; set; }
    public string? StripePaymentIntentId { get; set; }
    public string? ExternalEventId { get; set; }
    public DateTime? PaymentConfirmedAtUtc { get; set; }
    public Guid? LedgerEntryId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
}
