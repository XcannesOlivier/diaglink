namespace WebApp.Api.Models.Entities;

public enum StripeMachineAdditionStage
{
    Reserved = 0,
    StripeQuantityUpdated = 1,
    InvoiceFinalized = 2,
    AwaitingPayment = 3,
    PaymentConfirmed = 4,
    MachineBillingPeriodCreated = 5,
    Completed = 6
}

/// <summary>Durable, immutable billing inputs and monotonic checkpoints for one machine's first cycle.</summary>
public class StripeMachineAddition
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid MachineId { get; set; }
    public Guid BillingAccountId { get; set; }
    public DateTime ActivatedAtUtc { get; set; }
    public DateTime CycleStartUtc { get; set; }
    public DateTime CycleEndUtc { get; set; }
    public required string StripeCustomerId { get; set; }
    public required string StripeSubscriptionId { get; set; }
    public required string StripeSubscriptionItemId { get; set; }
    public required string StripePriceId { get; set; }
    public long OriginalQuantity { get; set; }
    public long TargetQuantity { get; set; }
    public int AiAmountCents { get; set; }
    public int ServiceAmountCents { get; set; }
    public StripeMachineAdditionStage Stage { get; set; }
    public Guid? MachineBillingPeriodId { get; set; }
    public string? StripeInvoiceId { get; set; }
    /// <summary>First verified payment event; retries resume checkpoints rather than skipping unfinished work.</summary>
    public string? ExternalEventId { get; set; }
    /// <summary>Stable JSON array of verified Stripe InvoicePayment IDs, not a client assertion.</summary>
    public string? PaymentReference { get; set; }
    /// <summary>Stripe invoice paid_at, persisted before budget allocation.</summary>
    public DateTime? PaymentConfirmedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
}
