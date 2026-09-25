using WebApp.Api.Models;
using WebApp.Api.Models.Entities;

namespace WebApp.Api.Services;

public sealed record RequestAcceptedEmailPayload(
    MachineRequestKind RequestKind,
    string MachineRequestId,
    string? FirstName,
    string RecipientEmail,
    string MachineName,
    long CapturedAmountCents,
    string Currency);

public sealed record RequestRejectedEmailPayload(
    MachineRequestKind RequestKind,
    string MachineRequestId,
    string? FirstName,
    string RecipientEmail,
    string MachineName,
    string Currency);

public sealed record RequestReadyEmailPayload(
    MachineRequestKind RequestKind,
    string MachineRequestId,
    string? FirstName,
    string RecipientEmail,
    string MachineName,
    DateTime ReadyAtUtc);

public static class MachineRequestDecisionNotifications
{
    public static EmailOutboxEnqueue Accepted(MachineRequestRecord request, MachineRequestPayment payment)
    {
        Validate(request, payment);
        var capturedAmount = payment.RequestKind == MachineRequestKind.AdditionalDocuments
            ? payment.AmountCents
            : payment.FinalCaptureAmountCents
                ?? throw new InvalidOperationException("Le montant final capturé est absent.");
        if (capturedAmount <= 0 || capturedAmount > payment.AmountCents)
            throw new InvalidOperationException("Le montant final capturé est incohérent.");
        var payload = new RequestAcceptedEmailPayload(request.RequestKind, request.RequestId,
            Name(request.Client.FirstName), request.Client.Email.Trim(), request.Machine.MachineName,
            capturedAmount, payment.Currency);
        return new(request.RequestId, payment.PaymentRequestId, EmailNotificationType.RequestAccepted,
            payload.RecipientEmail, payload.FirstName, payload);
    }

    public static EmailOutboxEnqueue Rejected(MachineRequestRecord request, MachineRequestPayment payment)
    {
        Validate(request, payment);
        var payload = new RequestRejectedEmailPayload(request.RequestKind, request.RequestId,
            Name(request.Client.FirstName), request.Client.Email.Trim(), request.Machine.MachineName,
            payment.Currency);
        return new(request.RequestId, payment.PaymentRequestId, EmailNotificationType.RequestRejected,
            payload.RecipientEmail, payload.FirstName, payload);
    }

    public static EmailOutboxEnqueue Ready(MachineRequestRecord request, MachineRequestPayment payment,
        DateTime readyAtUtc)
    {
        Validate(request, payment);
        var type = request.RequestKind == MachineRequestKind.AdditionalDocuments
            ? EmailNotificationType.DocumentsReady
            : EmailNotificationType.MachineReady;
        var payload = new RequestReadyEmailPayload(request.RequestKind, request.RequestId,
            Name(request.Client.FirstName), request.Client.Email.Trim(), request.Machine.MachineName, readyAtUtc);
        return new(request.RequestId, payment.PaymentRequestId, type,
            payload.RecipientEmail, payload.FirstName, payload);
    }

    private static void Validate(MachineRequestRecord request, MachineRequestPayment payment)
    {
        if (request.RequestId != payment.MachineRequestId || request.RequestKind != payment.RequestKind
            || request.Payment?.PaymentRequestId != payment.PaymentRequestId
            || request.Payment.AuthorizedAmountCents != payment.AmountCents
            || request.Payment.AuthorizedPages != payment.TotalPages
            || !string.Equals(request.Payment.Currency, payment.Currency, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(request.Client.Email)
            || string.IsNullOrWhiteSpace(request.Machine.MachineName))
            throw new InvalidOperationException("Le paiement et la demande ne permettent pas de notifier la décision.");
        if (request.RequestKind == MachineRequestKind.InitialMachine)
        {
            if (string.IsNullOrWhiteSpace(payment.Email)
                || !string.Equals(request.Client.Email.Trim(), payment.Email.Trim(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("L'adresse de la demande ne correspond pas au paiement.");
        }
        else if (request.CompanyId != payment.CompanyId || request.RequestedByUserId != payment.RequestedByUserId)
            throw new InvalidOperationException("Le demandeur durable ne correspond pas au paiement.");
        if (request.RequestKind == MachineRequestKind.AdditionalDocuments
            && (request.TargetMachineId != payment.TargetMachineId || string.IsNullOrWhiteSpace(payment.Email)
                || !string.Equals(request.Client.Email.Trim(), payment.Email.Trim(), StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("La demande documentaire ne correspond pas au paiement.");
    }

    private static string? Name(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
