using Microsoft.EntityFrameworkCore;
using WebApp.Api.Data;
using WebApp.Api.Models;
using WebApp.Api.Models.Entities;

namespace WebApp.Api.Services;

public sealed record RequestReceivedEmailPayload(
    MachineRequestKind RequestKind,
    string MachineRequestId,
    string? FirstName,
    string RecipientEmail,
    string MachineName,
    long AuthorizedAmountCents,
    string Currency);

public sealed class RequestReceivedNotificationService(
    DiagLinkDbContext db,
    MachineRequestPaymentStore payments,
    MachineRequestStorageService storage,
    EmailOutboxStore outbox,
    ILogger<RequestReceivedNotificationService> logger)
{
    public async Task<bool> TryEnqueueAsync(Guid paymentRequestId, CancellationToken ct = default)
    {
        try
        {
            return await EnqueueIfEligibleAsync(paymentRequestId, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            logger.LogWarning(exception,
                "RequestReceived enqueue deferred for payment {PaymentRequestId}", paymentRequestId);
            return false;
        }
    }

    public async Task<int> ReconcileAsync(int batchSize = 25, CancellationToken ct = default)
    {
        if (batchSize is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(batchSize));
        var candidates = await db.MachineRequestPayments.AsNoTracking()
            .Where(payment => payment.Status == MachineRequestPaymentStatus.Authorized
                && payment.MachineRequestId != null
                && !db.EmailOutbox.Any(message => message.MachineRequestId == payment.MachineRequestId
                    && message.NotificationType == EmailNotificationType.RequestReceived))
            .OrderBy(payment => payment.AuthorizedAtUtc)
            .Select(payment => payment.Id)
            .Take(batchSize)
            .ToListAsync(ct);

        var created = 0;
        foreach (var candidate in candidates)
            if (await TryEnqueueAsync(candidate, ct)) created++;
        return created;
    }

    internal async Task<bool> EnqueueIfEligibleAsync(Guid paymentRequestId, CancellationToken ct = default)
    {
        var payment = await payments.GetAsync(paymentRequestId, ct);
        if (payment is null || payment.Status != "authorized" || string.IsNullOrWhiteSpace(payment.MachineRequestId))
            return false;

        var request = await storage.GetAsync(payment.MachineRequestId, ct);
        if (!IsCoherent(payment, request)) return false;

        var payload = new RequestReceivedEmailPayload(
            request!.RequestKind,
            request.RequestId,
            NullIfWhiteSpace(request.Client.FirstName),
            request.Client.Email.Trim(),
            request.Machine.MachineName,
            payment.AmountCents,
            payment.Currency);
        await outbox.EnqueueAsync(new EmailOutboxEnqueue(
            request.RequestId,
            payment.PaymentRequestId,
            EmailNotificationType.RequestReceived,
            payload.RecipientEmail,
            payload.FirstName,
            payload), ct);
        return true;
    }

    private static bool IsCoherent(MachineRequestPayment payment, MachineRequestRecord? request)
    {
        if (request is null || request.Status != MachineRequestStatuses.Pending
            || request.RequestId != payment.MachineRequestId || request.RequestKind != payment.RequestKind
            || request.Payment is null || request.Payment.PaymentRequestId != payment.PaymentRequestId
            || request.Payment.AuthorizedPages != payment.TotalPages
            || request.Payment.AuthorizedAmountCents != payment.AmountCents
            || !string.Equals(request.Payment.Currency, payment.Currency, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(request.Client.Email)
            || string.IsNullOrWhiteSpace(request.Machine.MachineName)) return false;

        return payment.RequestKind switch
        {
            MachineRequestKind.InitialMachine => payment.CompanyId is null
                && payment.RequestedByUserId is null
                && EmailsEqual(request.Client.Email, payment.Email),
            MachineRequestKind.AdditionalMachine => payment.CompanyId is not null
                && payment.RequestedByUserId is not null
                && request.CompanyId == payment.CompanyId
                && request.RequestedByUserId == payment.RequestedByUserId
                && request.TargetMachineId is null
                && request.Pricing.TotalPages == payment.TotalPages,
            MachineRequestKind.AdditionalDocuments => payment.CompanyId is not null
                && payment.RequestedByUserId is not null
                && payment.TargetMachineId is not null
                && request.CompanyId == payment.CompanyId
                && request.RequestedByUserId == payment.RequestedByUserId
                && request.TargetMachineId == payment.TargetMachineId
                && request.Pricing.TotalPages == payment.TotalPages
                && EmailsEqual(request.Client.Email, payment.Email),
            _ => false
        };
    }

    private static bool EmailsEqual(string left, string? right) =>
        !string.IsNullOrWhiteSpace(right)
        && string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
