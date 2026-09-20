using WebApp.Api.Models.Entities;
namespace WebApp.Api.Services;

public record StripeTopUpPayment(string SessionId, string? Url, string Status, string? PaymentIntentId = null, DateTime? PaidAtUtc = null);
public interface IStripeWalletTopUpGateway
{
    Task<StripeTopUpPayment> CreateAsync(StripeWalletTopUp operation, CancellationToken ct);
    Task<StripeTopUpPayment> ReadAsync(StripeWalletTopUp operation, string sessionId, CancellationToken ct);
}
