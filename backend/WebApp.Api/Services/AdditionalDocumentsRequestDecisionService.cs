using Microsoft.EntityFrameworkCore;
using WebApp.Api.Data;
using WebApp.Api.Models;

namespace WebApp.Api.Services;

public sealed record AdditionalDocumentsRequestDecision(MachineRequestRecord Request, MachineRequestPaymentResult Payment);

public sealed class AdditionalDocumentsRequestDecisionService(
    MachineRequestStorageService storage,
    MachineRequestPaymentService payments,
    DiagLinkDbContext db)
{
    public async Task<AdditionalDocumentsRequestDecision?> AcceptAsync(string requestId, CancellationToken ct)
    {
        var request = await storage.GetAsync(requestId, ct);
        if (request is null) return null;
        var (payment, context) = await ValidateAsync(request, ct);
        if (request.Status == MachineRequestStatuses.Rejected || payment.Status is "cancelled" or "abandoned")
            throw new InvalidOperationException("Une demande AdditionalDocuments refusée ne peut plus être acceptée.");
        if (request.Status == MachineRequestStatuses.Treated && payment.Status != "captured")
            throw new InvalidOperationException("La demande traitée ne possède pas de capture confirmée.");

        var captured = await payments.CompleteAdditionalDocumentsFromAdminDecisionAsync(
            payment.PaymentRequestId, accept: true, context, request, ct)
            ?? throw new InvalidOperationException("Le paiement documentaire est introuvable.");
        if (captured.Status != "captured")
            throw new InvalidOperationException("La capture Stripe documentaire n'est pas confirmée.");
        var updated = request.Status == MachineRequestStatuses.Treated ? request
            : await storage.UpdateStatusAsync(request.RequestId, MachineRequestStatuses.Treated, ct)
                ?? throw new InvalidOperationException("La demande n'a pas pu être marquée comme traitée.");
        return new(updated, captured);
    }

    public async Task<AdditionalDocumentsRequestDecision?> RejectAsync(string requestId, CancellationToken ct)
    {
        var request = await storage.GetAsync(requestId, ct);
        if (request is null) return null;
        var (payment, context) = await ValidateAsync(request, ct);
        if (request.Status == MachineRequestStatuses.Treated || payment.Status == "captured")
            throw new InvalidOperationException("Une demande AdditionalDocuments encaissée ne peut plus être refusée.");
        if (request.Status == MachineRequestStatuses.Rejected && payment.Status != "cancelled")
            throw new InvalidOperationException("La demande refusée ne possède pas d'annulation confirmée.");

        var cancelled = await payments.CompleteAdditionalDocumentsFromAdminDecisionAsync(
            payment.PaymentRequestId, accept: false, context, request, ct)
            ?? throw new InvalidOperationException("Le paiement documentaire est introuvable.");
        if (cancelled.Status is not ("cancelled" or "abandoned"))
            throw new InvalidOperationException("L'annulation Stripe documentaire n'est pas confirmée.");
        var updated = request.Status == MachineRequestStatuses.Rejected ? request
            : await storage.UpdateStatusAsync(request.RequestId, MachineRequestStatuses.Rejected, ct)
                ?? throw new InvalidOperationException("La demande n'a pas pu être marquée comme refusée.");
        return new(updated, cancelled);
    }

    public async Task<AdditionalDocumentsRequestDecision?> AbandonAsync(string requestId,
        AdditionalDocumentsContext context, CancellationToken ct)
    {
        var request = await storage.GetAsync(requestId, ct);
        if (request is null) return null;
        if (request.RequestKind != MachineRequestKind.AdditionalDocuments || request.CompanyId != context.CompanyId
            || request.RequestedByUserId != context.UserId || request.TargetMachineId != context.MachineId
            || request.Payment is null || request.Payment.PaymentRequestId.ToString("N") != request.RequestId)
            throw new UnauthorizedAccessException("Cette demande documentaire n'appartient pas à l'utilisateur connecté.");
        var payment = await payments.ReadPaymentAsync(request.Payment.PaymentRequestId, ct)
            ?? throw new InvalidOperationException("Le paiement documentaire lié est introuvable.");
        if (payment.RequestKind != MachineRequestKind.AdditionalDocuments || payment.CompanyId != context.CompanyId
            || payment.RequestedByUserId != context.UserId || payment.TargetMachineId != context.MachineId
            || payment.MachineRequestId != request.RequestId)
            throw new UnauthorizedAccessException("La tentative documentaire n'appartient pas à l'utilisateur connecté.");
        if (request.Status == MachineRequestStatuses.Treated || payment.Status == "captured")
            throw new InvalidOperationException("Une demande documentaire encaissée ne peut plus être annulée.");

        var cancelled = await payments.AbandonAdditionalDocumentsAsync(payment.PaymentRequestId, context, ct)
            ?? throw new InvalidOperationException("Le paiement documentaire est introuvable.");
        if (cancelled.Status is not ("cancelled" or "abandoned"))
            throw new InvalidOperationException("L'annulation de la demande documentaire n'est pas confirmée.");
        var updated = request.Status == MachineRequestStatuses.Rejected ? request
            : await storage.UpdateStatusAsync(request.RequestId, MachineRequestStatuses.Rejected, ct)
                ?? throw new InvalidOperationException("La demande n'a pas pu être marquée comme annulée.");
        return new(updated, cancelled);
    }

    private async Task<(MachineRequestPayment Payment, AdditionalDocumentsContext Context)> ValidateAsync(
        MachineRequestRecord request, CancellationToken ct)
    {
        if (request.RequestKind != MachineRequestKind.AdditionalDocuments || request.CompanyId is null
            || request.RequestedByUserId is null || request.TargetMachineId is null || request.Payment is null)
            throw new InvalidOperationException("Cette action est réservée aux demandes AdditionalDocuments.");
        if (request.Status is not (MachineRequestStatuses.Pending or MachineRequestStatuses.Treated or MachineRequestStatuses.Rejected))
            throw new InvalidOperationException("Le statut de la demande documentaire est incohérent.");
        var payment = await payments.ReadPaymentAsync(request.Payment.PaymentRequestId, ct)
            ?? throw new InvalidOperationException("Le paiement documentaire lié est introuvable.");
        if (payment.RequestKind != MachineRequestKind.AdditionalDocuments
            || payment.CompanyId != request.CompanyId || payment.RequestedByUserId != request.RequestedByUserId
            || payment.TargetMachineId != request.TargetMachineId || payment.MachineRequestId != request.RequestId
            || payment.TotalPages != request.Pricing.TotalPages || payment.AmountCents <= 0 || payment.Currency != "EUR"
            || request.Payment.AuthorizedPages != payment.TotalPages
            || request.Payment.AuthorizedAmountCents != payment.AmountCents
            || request.Payment.RecalculatedAmountCents != payment.AmountCents
            || AdditionalDocumentsPricing.CalculateAmountCents(payment.TotalPages) != payment.AmountCents
            || MachineRequestPreparationPricing.ToCents(request.Pricing.PreparationTotal) != payment.AmountCents)
            throw new InvalidOperationException("Le paiement et le request.json AdditionalDocuments sont incohérents.");
        if (payment.Status is not ("authorized" or "captured" or "cancelled"))
            throw new InvalidOperationException("Le paiement documentaire doit être autorisé avant la décision.");

        var machine = await db.Machines.AsNoTracking().SingleOrDefaultAsync(item =>
            item.Id == request.TargetMachineId && item.CompanyId == request.CompanyId, ct)
            ?? throw new InvalidOperationException("La machine cible de la demande documentaire est introuvable.");
        var account = await db.BillingAccounts.AsNoTracking().SingleOrDefaultAsync(item =>
            item.CompanyId == request.CompanyId, ct);
        if (account is null || string.IsNullOrWhiteSpace(account.StripeCustomerId))
            throw new InvalidOperationException("Le Customer Stripe attendu est introuvable.");
        return (payment, new AdditionalDocumentsContext(request.RequestedByUserId.Value,
            request.CompanyId.Value, machine.Id, account.StripeCustomerId, request.Client.Company,
            machine.Name, request.Client.Email, request.Client.FirstName, request.Client.LastName, request.Client.Phone));
    }
}
