using System.Data;
using Microsoft.EntityFrameworkCore;
using WebApp.Api.Data;
using WebApp.Api.Models;

namespace WebApp.Api.Services;

/// <summary>Consumes only included real EUR cost. Caller supplies a successfully valued, converted cost.</summary>
public sealed class AiCreditConsumptionService(DbContextOptions<DiagLinkDbContext> options)
{
    private const decimal MaxAmount = 999999999999.999999m;

    public async Task<AiCreditConsumptionResult> ConsumeAsync(Guid usageRecordId, decimal realAiCostEur,
        string currency, CancellationToken ct = default)
    {
        await using var strategyContext = new DiagLinkDbContext(options);
        var strategy = strategyContext.Database.CreateExecutionStrategy();
        // Recreate context AND result state on every attempt, including after an uncertain commit.
        return await strategy.ExecuteAsync(token => ConsumeAttemptAsync(usageRecordId, realAiCostEur, currency, token), ct);
    }

    private async Task<AiCreditConsumptionResult> ConsumeAttemptAsync(Guid usageRecordId, decimal realAiCostEur,
        string currency, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var result = new AiCreditConsumptionResult { UsageRecordId = usageRecordId, RealAiCostEur = realAiCostEur };
        AiCreditConsumptionResult Fail(string reason) => result with { Status = reason, FailureReason = reason };
        if (currency != "EUR") return Fail("InvalidCurrency");
        if (realAiCostEur <= 0 || realAiCostEur > MaxAmount || decimal.Round(realAiCostEur, 6) != realAiCostEur)
            return Fail("InvalidCost");
        // Dedicated context: never flush other request-local tracked changes.
        await using var db = new DiagLinkDbContext(options);
        if (!db.Database.IsSqlServer() && !string.Equals(db.Database.ProviderName, "Microsoft.EntityFrameworkCore.Sqlite", StringComparison.Ordinal))
            return Fail("UnsupportedDatabaseProvider");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var usage = await db.AiUsageRecords.AsNoTracking().SingleOrDefaultAsync(u => u.Id == usageRecordId, ct);
        if (usage == null) return Fail("UsageNotFound");
        result = result with { MachineId = usage.MachineId, CompanyId = usage.CompanyId };
        if (usage.MachineId == null) return Fail("MachineMissing");
        if (usage.CompanyId == null) return Fail("CompanyMissing");
        // UPDLOCK serializes every consumption for this machine, including distinct usages/periods.
        // HOLDLOCK + Serializable retain locks until ledger and period have committed together.
        var machines = db.Database.IsSqlServer()
            ? db.Machines.FromSqlInterpolated($"SELECT * FROM [dbo].[Machines] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {usage.MachineId.Value}")
            : db.Machines.AsQueryable();
        var machine = await machines.SingleOrDefaultAsync(m => m.Id == usage.MachineId, ct);
        if (machine == null) return Fail("MachineMissing");
        if (!await db.Companies.AnyAsync(c => c.Id == usage.CompanyId, ct)) return Fail("CompanyMissing");
        if (machine.CompanyId != usage.CompanyId) return Fail("DataInconsistency");
        var previous = await db.CreditLedger.AsNoTracking().SingleOrDefaultAsync(e => e.AiUsageRecordId == usageRecordId
            && e.EntryType == "AiUsage" && e.BucketType == "MachineIncluded", ct);
        if (previous != null)
        {
            if (previous.MachineId != usage.MachineId || previous.CompanyId != usage.CompanyId || previous.Currency != "EUR"
                || previous.RealAiCost is not > 0 || previous.RealAiCost > realAiCostEur || previous.BalanceAfter == null)
                return Fail("DataInconsistency");
            var remainder = realAiCostEur - previous.RealAiCost.Value;
            return result with { Success = true, Status = "AlreadyProcessed", BillingPeriodId = previous.MachineBillingPeriodId,
                MachineLedgerEntryId = previous.Id, RemainingIncludedBudgetEur = previous.BalanceAfter.Value,
                RemainingRealAiCostForWalletEur = remainder, WalletDebitRequired = remainder > 0 };
        }
        var periods = await db.MachineBillingPeriods.Where(p => p.MachineId == usage.MachineId
            && p.PeriodStartUtc <= usage.CreatedAtUtc && usage.CreatedAtUtc < p.PeriodEndUtc).ToListAsync(ct);
        if (periods.Count == 0) return Fail("BillingPeriodNotFound");
        if (periods.Count != 1) return Fail("AmbiguousBillingPeriod");
        var period = periods[0];
        result = result with { BillingPeriodId = period.Id };
        if (period.Status is not ("Active" or "Closed")) return Fail("BillingPeriodInactive");
        if (period.IncludedAiBudgetRealCost < 0 || period.IncludedAiBudgetRealCost > MaxAmount
            || period.IncludedAiUsedRealCost < 0 || period.IncludedAiUsedRealCost > period.IncludedAiBudgetRealCost)
            return Fail("DataInconsistency");
        var remaining = period.IncludedAiBudgetRealCost - period.IncludedAiUsedRealCost;
        var debit = Math.Min(realAiCostEur, remaining);
        var walletRemainder = realAiCostEur - debit;
        Guid? ledgerId = null;
        if (debit > 0)
        {
            period.IncludedAiUsedRealCost += debit;
            period.UpdatedAtUtc = DateTime.UtcNow;
            ledgerId = Guid.NewGuid();
            db.CreditLedger.Add(new Models.Entities.CreditLedgerEntry
            {
                Id = ledgerId.Value, CompanyId = usage.CompanyId.Value, MachineId = usage.MachineId,
                MachineBillingPeriodId = period.Id, AiUsageRecordId = usage.Id,
                EntryType = "AiUsage", BucketType = "MachineIncluded", RealAiCost = debit,
                CommercialCreditAmount = null, BalanceAfter = remaining - debit, Currency = "EUR",
                ExternalEventId = null, Notes = null, CreatedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
        return result with { Success = true, Status = walletRemainder > 0 ? "WalletDebitRequired" : "Processed",
            MachineDebitEur = debit, RemainingIncludedBudgetEur = remaining - debit,
            RemainingRealAiCostForWalletEur = walletRemainder, WalletDebitRequired = walletRemainder > 0,
            MachineLedgerEntryId = ledgerId };
    }
}
