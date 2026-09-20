using System.Data;
using Microsoft.EntityFrameworkCore;
using WebApp.Api.Data;
using WebApp.Api.Models.Entities;
namespace WebApp.Api.Services;

public record StripeLifecycleSnapshot(string Status, DateTime Start, DateTime End, bool CancelAtPeriodEnd,
    string? InvoiceId, string? InvoiceStatus, long? Remaining, string ItemId, long Quantity);
public interface IStripeLifecycleGateway
{
    Task<StripeLifecycleSnapshot> ReadAsync(BillingAccount account, CancellationToken ct);
    Task SetQuantityAsync(BillingAccount account, StripeLifecycleSnapshot snapshot, int quantity, string key, CancellationToken ct);
}
public sealed class StripeLifecycleService(DbContextOptions<DiagLinkDbContext> options, IStripeLifecycleGateway gateway)
{
    public async Task<string> ProcessAsync(string subscriptionId, string eventId, string eventType, CancellationToken ct)
    {
        await using var read = new DiagLinkDbContext(options);
        var companyId = await read.BillingAccounts.Where(a => a.StripeSubscriptionId == subscriptionId).Select(a => (Guid?)a.CompanyId).SingleOrDefaultAsync(ct);
        if (companyId == null) return "SubscriptionNotLinked";
        return await read.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var db = new DiagLinkDbContext(options);
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var companies = db.Database.IsSqlServer() ? db.Companies.FromSqlInterpolated($"SELECT * FROM dbo.Companies WITH (UPDLOCK,HOLDLOCK) WHERE Id={companyId.Value}") : db.Companies.AsQueryable();
            await companies.SingleAsync(c => c.Id == companyId.Value, ct);
            var previous = await db.StripeLifecycleEvents.SingleOrDefaultAsync(e => e.Id == eventId, ct);
            if (previous != null) return previous.SubscriptionId == subscriptionId ? "AlreadyProcessed" : "ReconciliationRequired";
            var account = await db.BillingAccounts.SingleAsync(a => a.CompanyId == companyId.Value, ct);
            if (account.StripeSubscriptionId != subscriptionId) return "ReconciliationRequired";
            // Read current provider state while serialized with other lifecycle synchronizations:
            // a late notification cannot restore the historical status from its payload.
            var state = await gateway.ReadAsync(account, ct);
            if (eventType == "invoice.upcoming" && state.Status == "active" && !state.CancelAtPeriodEnd)
            {
                if (await db.StripeMachineAdditions.AnyAsync(a => a.CompanyId == companyId && a.CompletedAtUtc == null, ct)
                    || await db.StripeLifecycleEvents.AnyAsync(e => e.CompanyId == companyId && e.CompletedAtUtc == null,ct)) return "RetryRequired";
                var quantity = await db.Machines.CountAsync(m => m.CompanyId == companyId && m.Status == "active", ct);
                // This is the upcoming invoice notification, before invoice generation.
                await gateway.SetQuantityAsync(account, state, quantity, eventId, ct);
            }
            account.SubscriptionStatus = state.Status;
            account.CancelAtPeriodEnd = state.CancelAtPeriodEnd;
            account.CurrentPeriodStartUtc = state.Start; account.CurrentPeriodEndUtc = state.End;
            account.LatestInvoiceId = state.InvoiceId; account.LatestInvoiceStatus = state.InvoiceStatus;
            account.AmountRemainingCents = state.Remaining; account.UpdatedAtUtc = DateTime.UtcNow;
            db.StripeLifecycleEvents.Add(new() { Id=eventId, CompanyId=companyId.Value, SubscriptionId=subscriptionId, EventType=eventType, CreatedAtUtc=DateTime.UtcNow, CompletedAtUtc=DateTime.UtcNow });
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            return "Synchronized";
        });
    }
}
