using System.Data;
using Microsoft.EntityFrameworkCore;
using WebApp.Api.Data;
using WebApp.Api.Models.Entities;

namespace WebApp.Api.Services;

/// <summary>Trusted server-side API. Future endpoints must authorize company access before calling.</summary>
public sealed class StripeBillingService(DbContextOptions<DiagLinkDbContext> options,
    IStripeBillingGateway gateway, StripeBillingOptions stripeOptions)
{
    private const string Pending = "creation_pending";

    /// <summary>Links the Customer created by the initial machine-request Checkout; never creates a Stripe object.</summary>
    public Task<string> LinkExistingCustomerAsync(Guid companyId, string customerId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(customerId)) throw new InvalidOperationException("Customer Stripe absent.");
        return WithAccount(companyId, (db, account) =>
        {
            if (account.StripeCustomerId is { } existing && existing != customerId)
                throw new InvalidOperationException("Cette entreprise possède déjà un autre Customer Stripe ; une décision manuelle est requise.");
            if (account.StripeCustomerId is null)
            {
                account.StripeCustomerId = customerId;
                account.UpdatedAtUtc = DateTime.UtcNow;
            }
            return Task.FromResult(customerId);
        }, ct);
    }

    public Task<StripeSubscriptionSnapshot> LinkExistingSubscriptionAsync(Guid companyId,
        StripeSubscriptionSnapshot subscription, CancellationToken ct = default) =>
        WithAccount(companyId, (db, account) =>
        {
            if (account.StripeCustomerId != subscription.CustomerId)
                throw new InvalidOperationException("Le Customer du BillingAccount ne correspond pas à la Subscription.");
            if (account.StripeSubscriptionId is { } existing && existing != subscription.Id)
                throw new InvalidOperationException("Une autre Subscription est déjà liée à cette entreprise.");
            account.StripeSubscriptionId = subscription.Id;
            account.SubscriptionStatus = subscription.Status;
            account.CurrentPeriodStartUtc = subscription.PeriodStartUtc;
            account.CurrentPeriodEndUtc = subscription.PeriodEndUtc;
            account.UpdatedAtUtc = DateTime.UtcNow;
            return Task.FromResult(subscription);
        }, ct);

    /// <summary>Read existing Stripe state only; never creates a customer or subscription.</summary>
    public async Task<StripeSubscriptionSnapshot> GetExistingSubscriptionAsync(Guid companyId, CancellationToken ct = default)
    {
        stripeOptions.Validate();
        await using var db = new DiagLinkDbContext(options);
        var account = await db.BillingAccounts.AsNoTracking().SingleOrDefaultAsync(a => a.CompanyId == companyId, ct);
        if (account?.StripeCustomerId == null || account.StripeSubscriptionId == null)
            throw new InvalidOperationException("Existing linked Stripe subscription required.");
        await gateway.ValidatePriceAsync(ct);
        return await gateway.GetSubscriptionAsync(account.StripeSubscriptionId, account.StripeCustomerId, companyId, ct);
    }

    public async Task<string> GetOrCreateCustomerAsync(Guid companyId, CancellationToken ct = default)
    {
        stripeOptions.Validate();
        var account = await WithAccount(companyId, (db, a) => Task.FromResult(a), ct);
        if (account.StripeCustomerId != null)
            return await gateway.GetCustomerAsync(account.StripeCustomerId, companyId, ct);
        RequireRecent(account.CreatedAtUtc);
        var customerId = await gateway.CreateCustomerAsync(companyId, account.Id, ct);
        return await WithAccount(companyId, (db, a) =>
        {
            if (a.StripeCustomerId != null && a.StripeCustomerId != customerId)
                throw new InvalidOperationException("Conflicting Stripe customer; reconciliation required.");
            if (a.StripeCustomerId == null)
            {
                a.StripeCustomerId = customerId;
                a.UpdatedAtUtc = DateTime.UtcNow;
            }
            return Task.FromResult(customerId);
        }, ct);
    }

    public async Task<StripeSubscriptionSnapshot> GetOrCreateSubscriptionAsync(Guid companyId, CancellationToken ct = default)
    {
        stripeOptions.Validate();
        await gateway.ValidatePriceAsync(ct);
        // Reject empty/inactive companies before creating any external object.
        await using (var db = new DiagLinkDbContext(options))
        {
            if (!await db.Companies.AnyAsync(c => c.Id == companyId && c.Status == "active", ct))
                throw new InvalidOperationException("Company not found or inactive.");
            if (!await db.Machines.AnyAsync(m => m.CompanyId == companyId && m.Status == "active", ct))
                throw new InvalidOperationException("No billable machines.");
        }
        var customerId = await GetOrCreateCustomerAsync(companyId, ct);
        var reservation = await WithAccount(companyId, async (db, a) =>
        {
            var quantity = await db.Machines.CountAsync(m => m.CompanyId == companyId && m.Status == "active", ct);
            if (quantity == 0) throw new InvalidOperationException("No billable machines.");
            if (a.StripeSubscriptionId == null)
            {
                if (a.SubscriptionStatus == Pending) RequireRecent(a.UpdatedAtUtc);
                else if (a.SubscriptionStatus != null)
                    throw new InvalidOperationException("Unlinked subscription status; reconciliation required.");
                else { a.SubscriptionStatus = Pending; a.UpdatedAtUtc = DateTime.UtcNow; }
            }
            return (Account: a, Quantity: quantity);
        }, ct);
        // No Stripe call is inside an EF execution strategy or SQL transaction.
        var subscription = reservation.Account.StripeSubscriptionId is { } id
            ? await gateway.GetSubscriptionAsync(id, customerId, companyId, ct)
            : await gateway.CreateSubscriptionAsync(customerId, companyId, reservation.Account.Id, reservation.Quantity, ct);
        if (reservation.Account.StripeSubscriptionId != null && subscription.Status == "incomplete")
            await gateway.PrepareInitialPaymentAsync(subscription.Id, customerId, companyId, ct);
        await WithAccount(companyId, (db, a) =>
        {
            if (a.StripeCustomerId != customerId || (a.StripeSubscriptionId != null && a.StripeSubscriptionId != subscription.Id))
                throw new InvalidOperationException("Conflicting Stripe subscription; reconciliation required.");
            a.StripeSubscriptionId = subscription.Id;
            a.SubscriptionStatus = subscription.Status;
            a.CurrentPeriodStartUtc = subscription.PeriodStartUtc;
            a.CurrentPeriodEndUtc = subscription.PeriodEndUtc;
            a.UpdatedAtUtc = DateTime.UtcNow;
            return Task.FromResult(true);
        }, ct);
        return subscription;
    }

    private static void RequireRecent(DateTime started)
    {
        // Stripe may prune idempotency keys after 24h. Never blindly recreate after an uncertain old attempt.
        if (DateTime.UtcNow - started >= TimeSpan.FromHours(23))
            throw new InvalidOperationException("Unconfirmed Stripe creation is too old; reconciliation required before retry.");
    }

    private async Task<T> WithAccount<T>(Guid companyId,
        Func<DiagLinkDbContext, BillingAccount, Task<T>> action, CancellationToken ct)
    {
        await using var strategyDb = new DiagLinkDbContext(options);
        return await strategyDb.Database.CreateExecutionStrategy().ExecuteAsync(async token =>
        {
            await using var db = new DiagLinkDbContext(options);
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
            var companies = db.Database.IsSqlServer()
                ? db.Companies.FromSqlInterpolated($"SELECT * FROM [dbo].[Companies] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {companyId}")
                : db.Companies.AsQueryable();
            var company = await companies.SingleOrDefaultAsync(c => c.Id == companyId, token);
            if (company == null || company.Status != "active") throw new InvalidOperationException("Company not found or inactive.");
            var account = await db.BillingAccounts.SingleOrDefaultAsync(a => a.CompanyId == companyId, token);
            if (account == null)
            {
                var now = DateTime.UtcNow;
                account = new BillingAccount { Id = Guid.NewGuid(), CompanyId = companyId, CreatedAtUtc = now, UpdatedAtUtc = now };
                db.BillingAccounts.Add(account);
            }
            var result = await action(db, account);
            await db.SaveChangesAsync(token);
            await tx.CommitAsync(token);
            return result;
        }, ct);
    }
}
