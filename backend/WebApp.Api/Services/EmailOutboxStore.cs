using System.Net.Mail;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WebApp.Api.Data;
using WebApp.Api.Models.Entities;

namespace WebApp.Api.Services;

public sealed record EmailOutboxEnqueue(
    string MachineRequestId,
    Guid? PaymentRequestId,
    EmailNotificationType NotificationType,
    string RecipientEmail,
    string? RecipientName,
    object Payload);

public sealed record ClaimedEmailOutbox(EmailOutbox Entry, Guid LeaseId);

public sealed class EmailOutboxStore(DiagLinkDbContext db, TimeProvider timeProvider)
{
    private static readonly JsonSerializerOptions PayloadOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan MaximumRetryDelay = TimeSpan.FromHours(1);

    internal static FormattableString BuildSqlServerClaimQuery(int batchSize, DateTime now) => $@"
                    SELECT TOP ({batchSize}) * FROM [dbo].[EmailOutbox] WITH (UPDLOCK, READPAST, ROWLOCK, READCOMMITTEDLOCK)
                    WHERE [Status] = {(int)EmailOutboxStatus.Pending}
                      AND [NextAttemptAtUtc] <= {now}
                      AND ([LockedUntilUtc] IS NULL OR [LockedUntilUtc] <= {now})
                    ORDER BY [CreatedAtUtc], [Id]";

    public async Task<EmailOutbox> EnqueueAsync(EmailOutboxEnqueue request, CancellationToken ct = default)
    {
        var entry = CreatePending(request, timeProvider.GetUtcNow().UtcDateTime);
        var normalizedRequestId = entry.MachineRequestId;
        var existing = await db.EmailOutbox.SingleOrDefaultAsync(item =>
            item.MachineRequestId == normalizedRequestId && item.NotificationType == request.NotificationType, ct);
        if (existing is not null) return existing;
        db.EmailOutbox.Add(entry);
        try
        {
            await db.SaveChangesAsync(ct);
            return entry;
        }
        catch (DbUpdateException exception)
        {
            db.Entry(entry).State = EntityState.Detached;
            var concurrent = await db.EmailOutbox.SingleOrDefaultAsync(item =>
                item.MachineRequestId == normalizedRequestId && item.NotificationType == request.NotificationType, ct);
            return concurrent ?? throw new InvalidOperationException("The outbox entry could not be created.", exception);
        }
    }

    internal static EmailOutbox CreatePending(EmailOutboxEnqueue request, DateTime now)
    {
        if (!Guid.TryParseExact(request.MachineRequestId, "N", out var requestId))
            throw new ArgumentException("A stable N-format machine request id is required.", nameof(request));
        if (!MailAddress.TryCreate(request.RecipientEmail?.Trim(), out var recipient))
            throw new ArgumentException("A valid recipient email is required.", nameof(request));
        ArgumentNullException.ThrowIfNull(request.Payload);
        return new EmailOutbox
        {
            Id = Guid.NewGuid(),
            MachineRequestId = requestId.ToString("N"),
            PaymentRequestId = request.PaymentRequestId,
            NotificationType = request.NotificationType,
            RecipientEmail = recipient.Address,
            RecipientName = string.IsNullOrWhiteSpace(request.RecipientName) ? null : request.RecipientName.Trim(),
            PayloadJson = JsonSerializer.Serialize(request.Payload, PayloadOptions),
            Status = EmailOutboxStatus.Pending,
            NextAttemptAtUtc = now,
            CreatedAtUtc = now
        };
    }

    public async Task<IReadOnlyList<ClaimedEmailOutbox>> ClaimEligibleAsync(int batchSize, TimeSpan leaseDuration,
        CancellationToken ct = default)
    {
        if (batchSize is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(batchSize));
        if (leaseDuration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(leaseDuration));
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async token =>
        {
            var now = timeProvider.GetUtcNow().UtcDateTime;
            var provider = db.Database.ProviderName ?? "";

            await using var transaction = db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, token)
                : null;
            IQueryable<EmailOutbox> query = db.EmailOutbox;
            if (provider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
                query = db.EmailOutbox.FromSqlInterpolated(BuildSqlServerClaimQuery(batchSize, now));
            else
                query = query.Where(item => item.Status == EmailOutboxStatus.Pending
                        && item.NextAttemptAtUtc <= now
                        && (item.LockedUntilUtc == null || item.LockedUntilUtc <= now))
                    .OrderBy(item => item.CreatedAtUtc).ThenBy(item => item.Id).Take(batchSize);

            var entries = await query.ToListAsync(token);
            var claimed = new List<ClaimedEmailOutbox>(entries.Count);
            foreach (var entry in entries)
            {
                var leaseId = Guid.NewGuid();
                entry.LeaseId = leaseId;
                entry.LockedUntilUtc = now.Add(leaseDuration);
                entry.LastAttemptAtUtc = now;
                claimed.Add(new(entry, leaseId));
            }
            await db.SaveChangesAsync(token);
            if (transaction is not null) await transaction.CommitAsync(token);
            return claimed;
        }, ct);
    }

    public async Task<bool> MarkSentAsync(Guid id, Guid leaseId, string? providerOperationId,
        CancellationToken ct = default)
    {
        var entry = await db.EmailOutbox.SingleOrDefaultAsync(item => item.Id == id, ct);
        if (entry is null || entry.Status != EmailOutboxStatus.Pending || entry.LeaseId != leaseId) return false;
        entry.Status = EmailOutboxStatus.Sent;
        entry.SentAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        entry.ProviderOperationId = string.IsNullOrWhiteSpace(providerOperationId) ? null : providerOperationId.Trim();
        entry.LeaseId = null;
        entry.LockedUntilUtc = null;
        entry.LastError = null;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> RescheduleAfterFailureAsync(Guid id, Guid leaseId, string safeError,
        CancellationToken ct = default)
    {
        var entry = await db.EmailOutbox.SingleOrDefaultAsync(item => item.Id == id, ct);
        if (entry is null || entry.Status != EmailOutboxStatus.Pending || entry.LeaseId != leaseId) return false;
        var now = timeProvider.GetUtcNow().UtcDateTime;
        entry.AttemptCount++;
        entry.NextAttemptAtUtc = now.Add(RetryDelay(entry.AttemptCount));
        entry.LastError = SanitizeError(safeError);
        entry.LeaseId = null;
        entry.LockedUntilUtc = null;
        await db.SaveChangesAsync(ct);
        return true;
    }

    internal static TimeSpan RetryDelay(int attemptCount)
    {
        var minutes = Math.Pow(2, Math.Clamp(attemptCount - 1, 0, 6));
        return TimeSpan.FromMinutes(Math.Min(minutes, MaximumRetryDelay.TotalMinutes));
    }

    private static string SanitizeError(string error)
    {
        var value = string.IsNullOrWhiteSpace(error) ? "Email delivery failed." : error.Trim();
        return value.Length <= 1000 ? value : value[..1000];
    }
}
