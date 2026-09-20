namespace WebApp.Api.Models.Entities;

/// <summary>Company billing identifiers reserved for a future Stripe integration.</summary>
public class BillingAccount
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string? StripeCustomerId { get; set; }
    public string? StripeSubscriptionId { get; set; }
    public string? SubscriptionStatus { get; set; }
    public DateTime? CurrentPeriodStartUtc { get; set; }
    public DateTime? CurrentPeriodEndUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public bool CancelAtPeriodEnd { get; set; }
    public string? LatestInvoiceId { get; set; }
    public string? LatestInvoiceStatus { get; set; }
    public long? AmountRemainingCents { get; set; }
}
