using System.Data;
using Microsoft.EntityFrameworkCore;
using WebApp.Api.Data;
using WebApp.Api.Models;
using WebApp.Api.Models.Entities;

namespace WebApp.Api.Services;

/// <summary>Debits only the trusted server-side real EUR remainder after MachineIncluded.</summary>
public sealed class CompanyWalletDebitService(DbContextOptions<DiagLinkDbContext> options)
{
    public static decimal CommercialAmount(decimal realCost) => Math.Round(realCost * 3m, 6, MidpointRounding.AwayFromZero);
    private const decimal MaxAmount = 999999999999.999999m;
    private static bool ValidAmount(decimal amount) => amount >= 0 && amount <= MaxAmount && decimal.Round(amount, 6) == amount;

    public async Task<CompanyWalletDebitResult> DebitAsync(Guid usageRecordId, decimal remainingRealAiCostEur,
        string currency = "EUR", CancellationToken ct = default)
    {
        await using var strategyContext = new DiagLinkDbContext(options);
        var strategy = strategyContext.Database.CreateExecutionStrategy();
        // Each replay reloads the balance and ledger using a fresh change tracker.
        return await strategy.ExecuteAsync(token => DebitAttemptAsync(usageRecordId, remainingRealAiCostEur, currency, token), ct);
    }

    private async Task<CompanyWalletDebitResult> DebitAttemptAsync(Guid usageRecordId, decimal remainingRealAiCostEur,
        string currency, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var result = new CompanyWalletDebitResult { UsageRecordId = usageRecordId,
            RemainingRealAiCostEur = remainingRealAiCostEur, RemainingRealAiCostUncovered = remainingRealAiCostEur };
        CompanyWalletDebitResult Fail(string reason) => result with { Status = reason, FailureReason = reason };
        if (currency != "EUR") return Fail("InvalidCurrency");
        if (!ValidAmount(remainingRealAiCostEur) || remainingRealAiCostEur == 0) return Fail("InvalidCost");
        var required = CommercialAmount(remainingRealAiCostEur);
        if (required > MaxAmount) return Fail("InvalidCost");
        result = result with { CommercialDebitRequired = required, RemainingCommercialCreditRequired = required };

        await using var db = new DiagLinkDbContext(options);
        if (!db.Database.IsSqlServer() && db.Database.ProviderName != "Microsoft.EntityFrameworkCore.Sqlite")
            return Fail("UnsupportedDatabaseProvider");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var usage = await db.AiUsageRecords.AsNoTracking().SingleOrDefaultAsync(u => u.Id == usageRecordId, ct);
        if (usage == null) return Fail("UsageNotFound");
        result = result with { CompanyId = usage.CompanyId, MachineId = usage.MachineId };
        if (usage.CompanyId == null || !await db.Companies.AnyAsync(c => c.Id == usage.CompanyId, ct))
            return Fail("CompanyMissing");
        if (usage.MachineId != null && !await db.Machines.AnyAsync(m => m.Id == usage.MachineId && m.CompanyId == usage.CompanyId, ct))
            return Fail("DataInconsistency");

        // One update lock per company wallet, retained through the ledger insert and commit.
        var wallets = db.Database.IsSqlServer()
            ? db.CompanyWallets.FromSqlInterpolated($"SELECT * FROM [dbo].[CompanyWallets] WITH (UPDLOCK, HOLDLOCK) WHERE [CompanyId] = {usage.CompanyId.Value}")
            : db.CompanyWallets.AsQueryable();
        var wallet = await wallets.SingleOrDefaultAsync(w => w.CompanyId == usage.CompanyId, ct);
        if (wallet == null) return Fail("WalletNotFound");
        if (wallet.Currency != "EUR") return Fail("InvalidCurrency");
        if (!ValidAmount(wallet.Balance)) return Fail("DataInconsistency");
        result = result with { WalletBalanceBeforeEur = wallet.Balance, WalletBalanceAfterEur = wallet.Balance };
        var previous = await db.CreditLedger.AsNoTracking().SingleOrDefaultAsync(e => e.AiUsageRecordId == usageRecordId
            && e.EntryType == "AiUsage" && e.BucketType == "CompanyWallet", ct);
        if (previous != null)
        {
            if (previous.CompanyId != usage.CompanyId || previous.MachineId != usage.MachineId || previous.Currency != "EUR"
                || previous.CommercialCreditAmount != required
                || previous.RealAiCost != remainingRealAiCostEur || previous.BalanceAfter is not >= 0)
                return Fail("DataInconsistency");
            return result with { Success = true, Status = "AlreadyProcessed", WalletLedgerEntryId = previous.Id,
                RemainingCommercialCreditRequired = 0, CommercialCreditMissing = 0,
                RemainingRealAiCostUncovered = 0 };
        }

        // All-or-nothing: leave balance, timestamp and ledger untouched if funds are insufficient.
        if (wallet.Balance < required)
            return result with { Status = "InsufficientWalletBalance", CommercialCreditMissing = required - wallet.Balance };
        var debit = required;
        Guid? ledgerId = null;
        if (debit > 0)
        {
            wallet.Balance -= debit;
            wallet.UpdatedAtUtc = DateTime.UtcNow;
            ledgerId = Guid.NewGuid();
            db.CreditLedger.Add(new CreditLedgerEntry
            {
                Id = ledgerId.Value, CompanyId = usage.CompanyId.Value, MachineId = usage.MachineId,
                MachineBillingPeriodId = null, AiUsageRecordId = usage.Id,
                EntryType = "AiUsage", BucketType = "CompanyWallet", RealAiCost = remainingRealAiCostEur,
                CommercialCreditAmount = debit, BalanceAfter = wallet.Balance, Currency = "EUR",
                ExternalEventId = null, Notes = null, CreatedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
        return result with { Success = true, Status = "Processed",
            WalletDebitEur = debit, WalletBalanceAfterEur = wallet.Balance, WalletLedgerEntryId = ledgerId,
            RemainingCommercialCreditRequired = 0, CommercialCreditMissing = 0, RemainingRealAiCostUncovered = 0 };
    }
}

