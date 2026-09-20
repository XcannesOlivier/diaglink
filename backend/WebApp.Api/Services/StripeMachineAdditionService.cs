using System.Data;
using Microsoft.EntityFrameworkCore;
using WebApp.Api.Data;
using WebApp.Api.Models.Entities;

namespace WebApp.Api.Services;

public record StripeMachineAdditionResult(Guid OperationId, Guid MachineId, Guid? BillingPeriodId,
    string? InvoiceId, long SubscriptionQuantity, decimal AiAmountEur, decimal ServiceAmountEur, string Status);

/// <summary>Trusted activation workflow, not an endpoint. First-cycle addition only, never reactivation.</summary>
public sealed class StripeMachineAdditionService(DbContextOptions<DiagLinkDbContext> options,
    StripeBillingService billing, MachineBillingPeriodService periods,
    IStripeMachineAdditionGateway gateway, StripeBillingOptions stripeOptions)
{
    /// <summary>Payment-notification resume path: never enters quantity/invoice creation or finalization writes.</summary>
    public async Task<StripeMachineAdditionResult> ResumeInvoicePaymentAsync(Guid operationId, CancellationToken ct = default)
    {
        StripeMachineAddition op;
        await using (var db = new DiagLinkDbContext(options))
            op = await db.StripeMachineAdditions.AsNoTracking().SingleAsync(a => a.Id == operationId, ct);
        if (op.CompletedAtUtc != null) return Result(op, "AlreadyCompleted");
        if (op.StripeInvoiceId == null || op.Stage < StripeMachineAdditionStage.StripeQuantityUpdated)
            return Result(op, "OperationNotReady");
        if (op.Stage == StripeMachineAdditionStage.StripeQuantityUpdated)
        {
            // Finalization can have succeeded remotely before its SQL checkpoint was saved.
            // Only confirmed payment permits skipping that checkpoint; no finalization retry here.
            var invoice = await gateway.GetInvoicePaymentAsync(op, ct);
            if (invoice.ReconciliationRequired) return Result(op, "ReconciliationRequired");
            if (invoice.Status != "paid" || invoice.Payment == null) return Result(op, "AwaitingPayment");
            op = await Advance(op, StripeMachineAdditionStage.InvoiceFinalized, ct);
        }
        return await AddActiveMachineAsync(op.MachineId, DateTime.SpecifyKind(op.ActivatedAtUtc, DateTimeKind.Utc), ct);
    }

    public static int CalculateServiceCents(DateTime cycleStartUtc, DateTime cycleEndUtc, DateTime activatedAtUtc)
    {
        if (cycleStartUtc.Kind == DateTimeKind.Local || cycleEndUtc.Kind == DateTimeKind.Local
            || activatedAtUtc.Kind != DateTimeKind.Utc || cycleEndUtc <= cycleStartUtc
            || activatedAtUtc < cycleStartUtc || activatedAtUtc >= cycleEndUtc)
            throw new InvalidOperationException("Activation must be UTC within the current cycle.");
        return checked((int)decimal.Round(1990m * (cycleEndUtc.Ticks - activatedAtUtc.Ticks)
            / (cycleEndUtc.Ticks - cycleStartUtc.Ticks), 0, MidpointRounding.AwayFromZero));
    }

    public async Task<StripeMachineAdditionResult> AddActiveMachineAsync(Guid machineId, DateTime activatedAtUtc,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (activatedAtUtc.Kind != DateTimeKind.Utc || activatedAtUtc > DateTime.UtcNow)
            throw new InvalidOperationException("An authoritative past or present UTC activation timestamp is required.");
        StripeMachineAddition? op;
        await using (var db = new DiagLinkDbContext(options))
            op = await db.StripeMachineAdditions.AsNoTracking().Where(a => a.MachineId == machineId && (a.CompletedAtUtc == null || a.CycleEndUtc > activatedAtUtc || a.ActivatedAtUtc == activatedAtUtc)).OrderByDescending(a => a.CreatedAtUtc).FirstOrDefaultAsync(ct);
        if (op == null)
        {
            stripeOptions.Validate();
            Guid companyId;
            await using (var db = new DiagLinkDbContext(options))
            {
                var machine = await db.Machines.AsNoTracking().SingleOrDefaultAsync(m => m.Id == machineId, ct);
                if (machine?.Status != "active") throw new InvalidOperationException("Active machine required.");
                companyId = machine.CompanyId;
                if (!await db.Companies.AnyAsync(c => c.Id == companyId && c.Status == "active", ct))
                    throw new InvalidOperationException("Active company required.");
                if (await db.MachineBillingPeriods.AnyAsync(p => p.MachineId == machineId && p.PeriodEndUtc > activatedAtUtc, ct))
                    throw new InvalidOperationException("Machine already has billing history; first-cycle addition refused.");
            }
            var readStarted = DateTime.UtcNow;
            var subscription = await billing.GetExistingSubscriptionAsync(companyId, ct);
            if (subscription.Status != "active" || string.IsNullOrEmpty(subscription.ItemId)
                || subscription.Quantity < 0 || DateTime.UtcNow >= subscription.PeriodEndUtc)
                throw new InvalidOperationException("Active current Stripe subscription required.");
            var cents = CalculateServiceCents(subscription.PeriodStartUtc, subscription.PeriodEndUtc, activatedAtUtc);
            op = await InCompanyTransaction(companyId, async db =>
            {
                var existing = await db.StripeMachineAdditions.Where(a => a.MachineId == machineId && (a.CompletedAtUtc == null || a.CycleEndUtc > activatedAtUtc || a.ActivatedAtUtc == activatedAtUtc)).OrderByDescending(a => a.CreatedAtUtc).FirstOrDefaultAsync(ct);
                if (existing != null) return existing;
                if (await db.StripeMachineAdditions.AnyAsync(a => a.CompanyId == companyId &&
                    (a.CompletedAtUtc == null || a.CompletedAtUtc >= readStarted), ct))
                    throw new InvalidOperationException("Another company addition is pending or just completed; retry after it finishes.");
                var machine = await db.Machines.SingleOrDefaultAsync(m => m.Id == machineId && m.CompanyId == companyId, ct);
                if (machine?.Status != "active" || await db.MachineBillingPeriods.AnyAsync(p => p.MachineId == machineId && p.PeriodEndUtc > activatedAtUtc, ct))
                    throw new InvalidOperationException("Machine is inactive or already has billing history.");
                var account = await db.BillingAccounts.SingleAsync(a => a.CompanyId == companyId, ct);
                if (account.StripeCustomerId != subscription.CustomerId || account.StripeSubscriptionId != subscription.Id
                    || account.CurrentPeriodStartUtc != subscription.PeriodStartUtc || account.CurrentPeriodEndUtc != subscription.PeriodEndUtc)
                    throw new InvalidOperationException("BillingAccount cycle is stale; synchronization required.");
                var activeCount = await db.Machines.CountAsync(m => m.CompanyId == companyId && m.Status == "active", ct);
                if (activeCount <= subscription.Quantity)
                    throw new InvalidOperationException("Subscription quantity already covers active machines; addition refused.");
                var created = new StripeMachineAddition
                {
                    Id = Guid.NewGuid(), CompanyId = companyId, MachineId = machineId, BillingAccountId = account.Id,
                    ActivatedAtUtc = activatedAtUtc, CycleStartUtc = subscription.PeriodStartUtc, CycleEndUtc = subscription.PeriodEndUtc,
                    StripeCustomerId = subscription.CustomerId, StripeSubscriptionId = subscription.Id,
                    StripeSubscriptionItemId = subscription.ItemId, StripePriceId = stripeOptions.PriceId,
                    OriginalQuantity = subscription.Quantity, TargetQuantity = checked(subscription.Quantity + 1),
                    AiAmountCents = 1000, ServiceAmountCents = cents, CreatedAtUtc = DateTime.UtcNow
                };
                db.StripeMachineAdditions.Add(created);
                return created;
            }, ct);
        }
        if (op.ActivatedAtUtc != activatedAtUtc)
            throw new InvalidOperationException("Replay activation timestamp differs from the original operation.");
        if (op.CompletedAtUtc != null) return Result(op, "AlreadyCompleted");

        // Whole workflow is NOT replayed by EF. Each checkpoint is a short isolated transaction.
        while (op.CompletedAtUtc == null)
        {
            stripeOptions.Validate();
            switch (op.Stage)
            {
                case StripeMachineAdditionStage.Reserved:
                    if (!CanReplayCreation(op)) return Result(op, "ReconciliationRequired");
                    await gateway.SetQuantityAsync(op, ct);
                    op = await Advance(op, StripeMachineAdditionStage.StripeQuantityUpdated, ct);
                    break;
                case StripeMachineAdditionStage.StripeQuantityUpdated:
                    if (op.StripeInvoiceId == null)
                    {
                        if (!CanReplayCreation(op)) return Result(op, "ReconciliationRequired");
                        var invoiceId = await gateway.CreateInvoiceAsync(op, ct);
                        op = await Advance(op, StripeMachineAdditionStage.StripeQuantityUpdated, ct, invoiceId: invoiceId);
                    }
                    // Recovery after a finalized invoice's SQL checkpoint was lost is read-only,
                    // including beyond 23h. Never add invoice lines to an already finalized invoice.
                    var invoice = await gateway.GetInvoicePaymentAsync(op, ct);
                    if (invoice.ReconciliationRequired) return Result(op, "ReconciliationRequired");
                    if (invoice.Status == "draft")
                    {
                        if (!CanReplayCreation(op)) return Result(op, "ReconciliationRequired");
                        await gateway.AddInvoiceLinesAsync(op, ct);
                        await gateway.FinalizeInvoiceAsync(op, ct);
                    }
                    else if (invoice.Status is not ("open" or "paid")) return Result(op, "ReconciliationRequired");
                    op = await Advance(op, StripeMachineAdditionStage.InvoiceFinalized, ct);
                    break;
                case StripeMachineAdditionStage.InvoiceFinalized:
                    op = await Advance(op, StripeMachineAdditionStage.AwaitingPayment, ct);
                    break;
                case StripeMachineAdditionStage.AwaitingPayment:
                    var state = await gateway.GetInvoicePaymentAsync(op, ct);
                    if (state.ReconciliationRequired) return Result(op, "ReconciliationRequired");
                    if (state.Status != "paid" || state.Payment == null) return Result(op, "AwaitingPayment");
                    if (string.IsNullOrWhiteSpace(state.Payment.Reference) || state.Payment.Reference.Length > 2000
                        || state.Payment.PaidAtUtc.Kind != DateTimeKind.Utc || state.Payment.PaidAtUtc > DateTime.UtcNow
                        // Stripe timestamps have second precision; activation may contain subsecond ticks.
                        || state.Payment.PaidAtUtc < new DateTime(op.ActivatedAtUtc.Ticks - op.ActivatedAtUtc.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc))
                        return Result(op, "ReconciliationRequired");
                    op = await Advance(op, StripeMachineAdditionStage.PaymentConfirmed, ct, payment: state.Payment);
                    break;
                case StripeMachineAdditionStage.PaymentConfirmed:
                    // A crash may have occurred after creating the period but before saving its ID.
                    // Recover it even after cycle end, without ever creating an expired period.
                    Guid? existingPeriodId;
                    await using (var db = new DiagLinkDbContext(options))
                    {
                        var existing = await db.MachineBillingPeriods.AsNoTracking().Where(p => p.MachineId == machineId && p.PeriodEndUtc > op.ActivatedAtUtc).ToListAsync(ct);
                        if (existing.Count > 1 || existing.Any(p => p.PeriodStartUtc != op.ActivatedAtUtc
                            || p.PeriodEndUtc != op.CycleEndUtc || p.IncludedAiBudgetRealCost != 10m))
                            return Result(op, "ReconciliationRequired");
                        existingPeriodId = existing.SingleOrDefault()?.Id;
                    }
                    if (op.PaymentConfirmedAtUtc == null || op.PaymentReference == null
                        || op.PaymentConfirmedAtUtc >= op.CycleEndUtc) return Result(op, "ReconciliationRequired");
                    if (existingPeriodId == null)
                    {
                        if (DateTime.UtcNow >= op.CycleEndUtc) return Result(op, "ReconciliationRequired");
                        bool hasHistory;
                        await using (var db = new DiagLinkDbContext(options))
                            hasHistory = await db.MachineBillingPeriods.AnyAsync(p => p.MachineId == machineId, ct);
                        var period = hasHistory
                            ? await periods.RenewPeriodAsync(machineId, op.ActivatedAtUtc, op.CycleEndUtc, ct, op.CompanyId)
                            : await periods.CreateInitialPeriodAsync(machineId, op.ActivatedAtUtc, op.CycleStartUtc, op.CycleEndUtc, ct, op.CompanyId);
                        if (!period.Success || period.BillingPeriodId == null || period.IncludedAiBudgetRealCost != 10m
                            || period.PeriodStartUtc != op.ActivatedAtUtc || period.PeriodEndUtc != op.CycleEndUtc)
                            throw new InvalidOperationException("Initial machine period failed: " + period.Status);
                        existingPeriodId = period.BillingPeriodId;
                    }
                    op = await Advance(op, StripeMachineAdditionStage.MachineBillingPeriodCreated, ct, periodId: existingPeriodId);
                    break;
                case StripeMachineAdditionStage.MachineBillingPeriodCreated:
                    op = await Advance(op, StripeMachineAdditionStage.Completed, ct);
                    break;
                default: throw new InvalidOperationException("Invalid addition checkpoint.");
            }
        }
        return Result(op, "Completed");
    }

    private static bool CanReplayCreation(StripeMachineAddition op) =>
        DateTime.UtcNow - op.CreatedAtUtc < TimeSpan.FromHours(23) && DateTime.UtcNow < op.CycleEndUtc;

    private Task<StripeMachineAddition> Advance(StripeMachineAddition previous, StripeMachineAdditionStage stage,
        CancellationToken ct, Guid? periodId = null, string? invoiceId = null, StripePaymentConfirmation? payment = null)
        => InCompanyTransaction(previous.CompanyId, async db =>
    {
        var op = await db.StripeMachineAdditions.SingleAsync(a => a.Id == previous.Id, ct);
        if ((periodId != null && op.MachineBillingPeriodId != null && periodId != op.MachineBillingPeriodId)
            || (invoiceId != null && op.StripeInvoiceId != null && invoiceId != op.StripeInvoiceId)
            || (payment != null && op.PaymentReference != null && (op.PaymentReference != payment.Reference
                || op.PaymentConfirmedAtUtc != payment.PaidAtUtc)))
            throw new InvalidOperationException("Conflicting addition checkpoint; reconciliation required.");
        // Draft identity is durable while remaining at StripeQuantityUpdated.
        op.StripeInvoiceId ??= invoiceId;
        if (op.Stage < stage)
        {
            if ((int)op.Stage != (int)stage - 1) throw new InvalidOperationException("Out-of-order addition checkpoint.");
            op.Stage = stage;
            op.MachineBillingPeriodId ??= periodId;
            if (payment != null) { op.PaymentReference = payment.Reference; op.PaymentConfirmedAtUtc = payment.PaidAtUtc; }
            if (stage == StripeMachineAdditionStage.Completed) op.CompletedAtUtc = DateTime.UtcNow;
        }
        return op;
    }, ct);

    private async Task<T> InCompanyTransaction<T>(Guid companyId, Func<DiagLinkDbContext, Task<T>> action, CancellationToken ct)
    {
        await using var strategyDb = new DiagLinkDbContext(options);
        return await strategyDb.Database.CreateExecutionStrategy().ExecuteAsync(async token =>
        {
            await using var db = new DiagLinkDbContext(options);
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
            var companies = db.Database.IsSqlServer()
                ? db.Companies.FromSqlInterpolated($"SELECT * FROM [dbo].[Companies] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {companyId}")
                : db.Companies.AsQueryable();
            if (await companies.SingleOrDefaultAsync(c => c.Id == companyId, token) == null)
                throw new InvalidOperationException("Company not found.");
            var result = await action(db);
            await db.SaveChangesAsync(token);
            await tx.CommitAsync(token);
            return result;
        }, ct);
    }

    private static StripeMachineAdditionResult Result(StripeMachineAddition op, string status) => new(op.Id, op.MachineId,
        op.MachineBillingPeriodId, op.StripeInvoiceId, op.TargetQuantity, op.AiAmountCents / 100m, op.ServiceAmountCents / 100m, status);
}
