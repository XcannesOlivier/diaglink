using WebApp.Api.Models.Entities;

namespace WebApp.Api.Services;

public record StripePaymentConfirmation(string Reference, DateTime PaidAtUtc);
public record StripeAdditionInvoiceState(string Status, StripePaymentConfirmation? Payment = null,
    bool ReconciliationRequired = false);

public interface IStripeMachineAdditionGateway
{
    Task SetQuantityAsync(StripeMachineAddition operation, CancellationToken ct);
    Task<string> CreateInvoiceAsync(StripeMachineAddition operation, CancellationToken ct);
    Task AddInvoiceLinesAsync(StripeMachineAddition operation, CancellationToken ct);
    Task FinalizeInvoiceAsync(StripeMachineAddition operation, CancellationToken ct);
    // Read-only server verification. Does not initiate a charge or trust a caller-supplied paid flag.
    Task<StripeAdditionInvoiceState> GetInvoicePaymentAsync(StripeMachineAddition operation, CancellationToken ct);
}
