using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WebApp.Api.Data;
using WebApp.Api.Models.Entities;
namespace WebApp.Api.Services;

public sealed class StripeSubscriptionPaymentService(DbContextOptions<DiagLinkDbContext> options,
    IStripeSubscriptionPaymentGateway gateway, MachineBillingPeriodService periods, IStripeLifecycleGateway? lifecycleGateway = null)
{
    public Task<string> ProcessAsync(string invoiceId, string eventId, string subscriptionId, CancellationToken ct) =>
        ProcessCoreAsync(invoiceId, eventId, subscriptionId, null, ct);

    // Explicit operator-only entry point, deliberately not exposed through an HTTP endpoint or webhook.
    public Task<string> ReconcileTestClockPaymentAsync(SubscriptionReconciliationRequest request, CancellationToken ct = default) =>
        ProcessCoreAsync(request.InvoiceId, request.EventId, request.SubscriptionId, request, ct);

    private async Task<string> ProcessCoreAsync(string invoiceId, string eventId, string subscriptionId,
        SubscriptionReconciliationRequest? reconciliation, CancellationToken ct)
    {
        await using var read = new DiagLinkDbContext(options);
        var account = await read.BillingAccounts.AsNoTracking().SingleOrDefaultAsync(a => a.StripeSubscriptionId == subscriptionId, ct);
        if (account == null) return "SubscriptionNotLinked";
        var saved = await read.StripeSubscriptionPayments.AsNoTracking().SingleOrDefaultAsync(p => p.StripeInvoiceId == invoiceId, ct);
        if (saved != null && (saved.CompanyId != account.CompanyId || saved.StripeSubscriptionId != subscriptionId))
            return "ReconciliationRequired";
        if (reconciliation == null && saved?.CompletedAtUtc != null) return "AlreadyCompleted";
        var invoice = reconciliation == null ? await gateway.ReadAsync(account, invoiceId, ct)
            : await gateway.ReadForReconciliationAsync(account, reconciliation, ct);
        var now = invoice.VerifiedTestClockUtc ?? DateTime.UtcNow;
        if ((invoice.VerifiedTestClockId == null) != (invoice.VerifiedTestClockUtc == null)) return "ReconciliationRequired";
        if (reconciliation != null && (invoice.VerifiedTestClockId == null || account.CompanyId != reconciliation.CompanyId
            || invoice.StartUtc != reconciliation.StartUtc || invoice.EndUtc != reconciliation.EndUtc
            || invoice.Quantity != 1 || invoice.AmountPaidCents != reconciliation.AmountPaidCents
            || reconciliation.AmountPaidCents != 2990 || invoice.BillingReason != "subscription_cycle"))
            return "ReconciliationRequired";
        if (invoice.Id != invoiceId || invoice.SubscriptionId != subscriptionId) return "ReconciliationRequired";
        if(invoice.Quantity==0 && invoice.AmountPaidCents==0 && invoice.Status=="paid")
            return await read.Machines.AnyAsync(m=>m.CompanyId==account.CompanyId && m.Status=="active",ct) ? "ReconciliationRequired" : "NoBillableMachines";
        if (!invoice.Confirmed) return "AwaitingPayment";
        if (invoice.EndUtc <= invoice.StartUtc || invoice.PaidAtUtc > now || invoice.PaidAtUtc < invoice.StartUtc
            || (reconciliation == null && invoice.EndUtc <= now) || invoice.StartUtc > now
            || invoice.BillingReason is not ("subscription_create" or "subscription_cycle")) return "ReconciliationRequired";

        var operation = await Locked(account.CompanyId, async db =>
        {
            var existing = await db.StripeSubscriptionPayments.SingleOrDefaultAsync(p => p.StripeInvoiceId == invoiceId, ct);
            if (existing != null)
            {
                if (existing.PeriodStartUtc != invoice.StartUtc || existing.PeriodEndUtc != invoice.EndUtc
                    || existing.AmountPaidCents != invoice.AmountPaidCents || existing.PaymentReference != invoice.PaymentReference
                    || (reconciliation != null && (existing.ExternalEventId != eventId
                        || !JsonSerializer.Deserialize<Guid[]>(existing.MachineIdsJson)!.SequenceEqual(new[]{reconciliation.MachineId}))))
                    throw new SubscriptionReconciliationException("Paid invoice changed.");
                return existing;
            }
            var linked = await db.BillingAccounts.SingleAsync(a => a.Id == account.Id, ct);
            if (linked.StripeSubscriptionId != subscriptionId || linked.StripeCustomerId != account.StripeCustomerId
                || (reconciliation == null && linked.CurrentPeriodEndUtc > invoice.EndUtc)
                || await db.StripeSubscriptionPayments.AnyAsync(p => p.CompanyId == account.CompanyId && p.CompletedAtUtc == null, ct))
                throw new SubscriptionReconciliationException("Pending or newer cycle requires reconciliation.");
            var machines = await db.Machines.Where(m => m.CompanyId == account.CompanyId && m.Status == "active")
                .OrderBy(m => m.Id).Select(m => m.Id).ToArrayAsync(ct);
            if (reconciliation != null && !machines.SequenceEqual(new[]{reconciliation.MachineId}))
                throw new SubscriptionReconciliationException("Explicit recipient does not match active machine.");
            if (machines.LongLength != invoice.Quantity || machines.Length == 0)
                throw new SubscriptionReconciliationException("Paid quantity does not match active machines.");
            var op = new StripeSubscriptionPayment
            {
                Id = Guid.NewGuid(), CompanyId = account.CompanyId, StripeSubscriptionId = subscriptionId,
                StripeInvoiceId = invoiceId, ExternalEventId = eventId, BillingReason = invoice.BillingReason,
                PeriodStartUtc = invoice.StartUtc, PeriodEndUtc = invoice.EndUtc,
                AmountPaidCents = invoice.AmountPaidCents, PaymentConfirmedAtUtc = invoice.PaidAtUtc!.Value,
                PaymentReference = invoice.PaymentReference!, MachineIdsJson = JsonSerializer.Serialize(machines)
            };
            db.StripeSubscriptionPayments.Add(op);
            return op;
        }, ct);
        if (operation.CompletedAtUtc != null) return "AlreadyCompleted";
        // Each machine transaction is independently idempotent. The frozen list survives a crash
        // between machines; retries reuse exactly the same paid cycle and recipients.
        foreach (var machineId in JsonSerializer.Deserialize<Guid[]>(operation.MachineIdsJson)!)
        {
            var result = operation.BillingReason == "subscription_create"
                ? await periods.CreateInitialPeriodAsync(machineId, invoice.StartUtc, invoice.StartUtc, invoice.EndUtc, ct, account.CompanyId)
                : await periods.RenewPeriodAsync(machineId, invoice.StartUtc, invoice.EndUtc, ct, account.CompanyId);
            if (!result.Success || result.IncludedAiBudgetRealCost != 10m
                || result.PeriodStartUtc != invoice.StartUtc || result.PeriodEndUtc != invoice.EndUtc) return "ReconciliationRequired";
        }
        return await Locked(account.CompanyId, async db =>
        {
            var op = await db.StripeSubscriptionPayments.SingleAsync(p => p.Id == operation.Id, ct);
            if (op.CompletedAtUtc != null) return "AlreadyCompleted";
            var linked = await db.BillingAccounts.SingleAsync(a => a.Id == account.Id, ct);
            if (linked.StripeSubscriptionId != subscriptionId || (reconciliation == null && linked.CurrentPeriodEndUtc > invoice.EndUtc))
                throw new SubscriptionReconciliationException("Subscription advanced during processing.");
            if (reconciliation != null)
            {
                // Preserve the newer unpaid cycle, status and invoice. Only finish the verified historical receipt.
                op.CompletedAtUtc = DateTime.UtcNow;
                return "Completed";
            }
            linked.SubscriptionStatus = invoice.SubscriptionStatus;
            if(lifecycleGateway!=null)
            {
                var current=await lifecycleGateway.ReadAsync(linked,ct);
                linked.SubscriptionStatus=current.Status;linked.CancelAtPeriodEnd=current.CancelAtPeriodEnd;
                linked.LatestInvoiceId=current.InvoiceId;linked.LatestInvoiceStatus=current.InvoiceStatus;linked.AmountRemainingCents=current.Remaining;
            }
            linked.CurrentPeriodStartUtc = invoice.StartUtc;
            linked.CurrentPeriodEndUtc = invoice.EndUtc;
            linked.UpdatedAtUtc = DateTime.UtcNow;
            op.CompletedAtUtc = DateTime.UtcNow;
            return "Completed";
        }, ct);
    }

    private async Task<T> Locked<T>(Guid companyId, Func<DiagLinkDbContext, Task<T>> action, CancellationToken ct)
    {
        await using var strategy = new DiagLinkDbContext(options);
        return await strategy.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var db = new DiagLinkDbContext(options);
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var companies = db.Database.IsSqlServer()
                ? db.Companies.FromSqlInterpolated($"SELECT * FROM [dbo].[Companies] WITH (UPDLOCK,HOLDLOCK) WHERE [Id]={companyId}")
                : db.Companies.AsQueryable();
            if (!await companies.AnyAsync(c => c.Id == companyId && c.Status == "active", ct))
                throw new SubscriptionReconciliationException("Active company required.");
            var result = await action(db);
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            return result;
        });
    }
}

