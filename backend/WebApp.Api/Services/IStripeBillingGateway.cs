namespace WebApp.Api.Services;

public record StripeSubscriptionSnapshot(string Id, string CustomerId, string Status,
    DateTime PeriodStartUtc, DateTime PeriodEndUtc, long Quantity)
{
    public string ItemId { get; init; } = "";
}

public interface IStripeBillingGateway
{
    Task ValidatePriceAsync(CancellationToken ct);
    Task PrepareInitialPaymentAsync(string subscriptionId, string customerId, Guid companyId, CancellationToken ct);
    Task<string> CreateCustomerAsync(Guid companyId, Guid accountId, CancellationToken ct);
    Task<string> GetCustomerAsync(string customerId, Guid companyId, CancellationToken ct);
    Task<StripeSubscriptionSnapshot> CreateSubscriptionAsync(string customerId, Guid companyId,
        Guid accountId, int quantity, CancellationToken ct);
    Task<StripeSubscriptionSnapshot> GetSubscriptionAsync(string subscriptionId, string customerId,
        Guid companyId, CancellationToken ct);
}
