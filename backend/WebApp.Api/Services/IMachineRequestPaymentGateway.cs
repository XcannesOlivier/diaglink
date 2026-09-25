namespace WebApp.Api.Services;

public record MachineRequestCheckout(string SessionId, string CheckoutUrl);
public record MachineRequestPaymentProof(string Status, string PaymentIntentId);

public interface IMachineRequestPaymentGateway
{
    Task<MachineRequestCheckout> CreateAsync(MachineRequestPayment payment, CancellationToken ct);
    Task<MachineRequestCheckout> CreateAdditionalAsync(MachineRequestPayment payment,
        AdditionalMachinePaymentContext context, CancellationToken ct) =>
        throw new NotSupportedException("Additional-machine Checkout is not supported by this gateway.");
    Task<MachineRequestPaymentProof> ReadAsync(MachineRequestPayment payment, string sessionId, CancellationToken ct);
    Task<MachineRequestPaymentProof> ReadAdditionalAsync(MachineRequestPayment payment, string sessionId,
        AdditionalMachinePaymentContext context, CancellationToken ct) =>
        throw new NotSupportedException("Additional-machine verification is not supported by this gateway.");
    Task<MachineRequestCheckout> CreateAdditionalDocumentsAsync(MachineRequestPayment payment,
        AdditionalDocumentsContext context, CancellationToken ct) =>
        throw new NotSupportedException("Additional-documents Checkout is not supported by this gateway.");
    Task<MachineRequestPaymentProof> ReadAdditionalDocumentsAsync(MachineRequestPayment payment, string sessionId,
        AdditionalDocumentsContext context, CancellationToken ct) =>
        throw new NotSupportedException("Additional-documents verification is not supported by this gateway.");
    Task<MachineRequestPaymentProof> CaptureAsync(MachineRequestPayment payment, CancellationToken ct);
    Task<MachineRequestPaymentProof> CaptureAdditionalAsync(MachineRequestPayment payment,
        AdditionalMachinePaymentContext context, CancellationToken ct) => CaptureAsync(payment, ct);
    Task<MachineRequestPaymentProof> CaptureAdditionalDocumentsAsync(MachineRequestPayment payment,
        AdditionalDocumentsContext context, CancellationToken ct) => CaptureAsync(payment, ct);
    Task<MachineRequestPaymentProof> CancelAsync(MachineRequestPayment payment, CancellationToken ct);
    Task<MachineRequestPaymentProof> CancelAdditionalAsync(MachineRequestPayment payment,
        AdditionalMachinePaymentContext context, CancellationToken ct) => CancelAsync(payment, ct);
    Task<MachineRequestPaymentProof> CancelAdditionalDocumentsAsync(MachineRequestPayment payment,
        AdditionalDocumentsContext context, CancellationToken ct) => CancelAsync(payment, ct);
    Task<MachineRequestPaymentProof> AbandonAdditionalDocumentsCheckoutAsync(MachineRequestPayment payment,
        AdditionalDocumentsContext context, CancellationToken ct) =>
        throw new NotSupportedException("Additional-documents Checkout abandonment is not supported by this gateway.");
}
