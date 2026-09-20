using Microsoft.EntityFrameworkCore;
using WebApp.Api.Data;

namespace WebApp.Api.Services;

public record AiCreditAccess(bool Allowed, string Status, string Message);

/// <summary>Read-only admission check. No reservation, debit or pricing mutation.</summary>
public sealed class AiCreditAccessService(DiagLinkDbContext db)
{
    public const string ExhaustedMessage = "Crédit IA épuisé. Rechargez le portefeuille de votre entreprise pour continuer. L’historique reste accessible.";
    public async Task<AiCreditAccess> CheckAsync(Guid? machineId, CancellationToken ct = default)
    {
        var machine = await db.Machines.AsNoTracking().SingleOrDefaultAsync(m => m.Id == machineId, ct);
        if (machine == null) return new(false, "AiCreditExhausted", ExhaustedMessage);
        var now = DateTime.UtcNow;
        var periods = await db.MachineBillingPeriods.AsNoTracking().Where(p => p.MachineId == machine.Id
            && p.PeriodStartUtc <= now && now < p.PeriodEndUtc).ToListAsync(ct);
        if (periods.Count == 1 && periods[0].Status == "Active"
            && periods[0].IncludedAiUsedRealCost >= 0
            && periods[0].IncludedAiBudgetRealCost > periods[0].IncludedAiUsedRealCost)
            return new(true, "MachineIncludedAvailable", "Budget IA inclus disponible.");
        var wallet = await db.CompanyWallets.AsNoTracking().SingleOrDefaultAsync(w => w.CompanyId == machine.CompanyId, ct);
        if (wallet?.Currency == "EUR" && wallet.Balance > 0)
        {
            // All-or-nothing wallet debits leave a positive balance on failure. Account for
            // unsettled valuable usage without retrying billing or changing the ledger.
            decimal pending = 0;
            var usages = await db.AiUsageRecords.AsNoTracking().Where(u => u.CompanyId == machine.CompanyId
                && !db.CreditLedger.Any(e => e.AiUsageRecordId == u.Id && e.EntryType == "AiUsage"
                    && e.BucketType == "CompanyWallet")).ToListAsync(ct);
            var calculator = new AiCostCalculator(db);
            var converter = new AiCostCurrencyConverter(db);
            foreach (var usage in usages)
            {
                var cost = await calculator.CalculateAsync(usage, ct);
                if (!cost.IsValuable || cost.RealAiCost == null) continue;
                var converted = await converter.ConvertAsync(cost.RealAiCost.Value, cost.Currency, usage, ct);
                if (!converted.IsConvertible || converted.ConvertedAmount == null) continue;
                var entries = await db.CreditLedger.AsNoTracking().Where(e => e.AiUsageRecordId == usage.Id
                    && e.EntryType == "AiUsage" && e.BucketType == "MachineIncluded").ToListAsync(ct);
                var remainder = converted.ConvertedAmount.Value - entries.Sum(e => e.RealAiCost ?? 0);
                if (remainder > 0) pending += CompanyWalletDebitService.CommercialAmount(remainder);
                if (pending >= wallet.Balance) return new(false, "AiCreditExhausted", ExhaustedMessage);
            }
            return new(true, "CompanyWalletAvailable", "Crédit entreprise disponible.");
        }
        return new(false, "AiCreditExhausted", ExhaustedMessage);
    }
}
