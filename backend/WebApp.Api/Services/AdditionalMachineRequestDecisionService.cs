using WebApp.Api.Models;
using WebApp.Api.Models.Entities;

namespace WebApp.Api.Services;

public sealed record AdditionalMachineRequestDecision(MachineRequestRecord Request, MachineRequestPaymentResult Payment);

public sealed class AdditionalMachineRequestDecisionService(
    MachineRequestStorageService storage,
    MachineRequestPaymentService payments,
    MachineRequestPaymentStore paymentStore)
{
    public async Task<AdditionalMachineRequestDecision?> AcceptAsync(string requestId, CancellationToken ct)
    {
        var request = await storage.GetAsync(requestId, ct);
        if (request is null) return null;
        var payment = await ValidateAsync(request, ct);
        if (request.Status == MachineRequestStatuses.Rejected)
            throw new InvalidOperationException("Une demande refusée ne peut plus être acceptée.");

        var preparationCents = MachineRequestPreparationPricing.ToCents(request.Pricing.PreparationTotal);
        var captured = await payments.CaptureFromAdminDecisionAsync(payment.PaymentRequestId, preparationCents, request, ct)
            ?? throw new InvalidOperationException("Le paiement est introuvable.");
        if (captured.Status != "captured") throw new InvalidOperationException("La capture Stripe n'est pas confirmée.");
        payment = await payments.ReadPaymentAsync(payment.PaymentRequestId, ct)
            ?? throw new InvalidOperationException("Le paiement est introuvable après capture.");
        payment = await paymentStore.CreateAndAttachAdditionalMachineAsync(payment, request.Machine, ct);
        var updated = request.Status == MachineRequestStatuses.Treated
            ? request
            : await storage.UpdateStatusAsync(request.RequestId, MachineRequestStatuses.Treated, ct)
                ?? throw new InvalidOperationException("La demande n'a pas pu être marquée comme traitée.");
        return new(updated, await payments.ReadAsync(payment.PaymentRequestId, ct)
            ?? throw new InvalidOperationException("Le paiement est introuvable."));
    }

    public async Task<AdditionalMachineRequestDecision?> RejectAsync(string requestId, CancellationToken ct)
    {
        var request = await storage.GetAsync(requestId, ct);
        if (request is null) return null;
        var payment = await ValidateAsync(request, ct);
        if (payment.Status == "captured" || payment.ProvisioningStage >= MachineRequestProvisioningStage.AmountFinalized)
            throw new InvalidOperationException("Une demande dont la capture ou le provisioning a commencé ne peut plus être refusée.");
        await payments.CancelFromAdminDecisionAsync(payment.PaymentRequestId, request, ct);
        var updated = request.Status == MachineRequestStatuses.Rejected
            ? request
            : await storage.UpdateStatusAsync(request.RequestId, MachineRequestStatuses.Rejected, ct)
                ?? throw new InvalidOperationException("La demande n'a pas pu être marquée comme refusée.");
        return new(updated, await payments.ReadAsync(payment.PaymentRequestId, ct)
            ?? throw new InvalidOperationException("Le paiement est introuvable."));
    }

    private async Task<MachineRequestPayment> ValidateAsync(MachineRequestRecord request, CancellationToken ct)
    {
        if (request.RequestKind != MachineRequestKind.AdditionalMachine || request.CompanyId is null
            || request.RequestedByUserId is null || request.Payment is null)
            throw new InvalidOperationException("Cette action est réservée aux demandes AdditionalMachine.");
        var payment = await payments.ReadPaymentAsync(request.Payment.PaymentRequestId, ct)
            ?? throw new InvalidOperationException("Le paiement lié est introuvable.");
        if (payment.RequestKind != MachineRequestKind.AdditionalMachine || payment.CompanyId != request.CompanyId
            || payment.RequestedByUserId != request.RequestedByUserId
            || payment.MachineRequestId != request.RequestId)
            throw new InvalidOperationException("Le paiement et la demande AdditionalMachine sont incohérents.");
        return payment;
    }
}
