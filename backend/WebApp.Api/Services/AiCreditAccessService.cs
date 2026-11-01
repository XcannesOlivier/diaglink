using Microsoft.EntityFrameworkCore;
using WebApp.Api.Data;
using WebApp.Api.Models.Entities;

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

            var priceableUsages = usages.Where(usage =>
                AiCostCalculator.Calculate(usage, []).FailureReason == "PricingNotFound").ToArray();
            IReadOnlyCollection<AiPricing> pricing = [];
            if (priceableUsages.Length > 0)
            {
                var providers = priceableUsages.Select(usage => usage.Provider!).Distinct(StringComparer.Ordinal).ToArray();
                var models = priceableUsages.Select(usage => usage.Model!).Distinct(StringComparer.Ordinal).ToArray();
                var usageTypes = priceableUsages.Select(usage => usage.UsageType.ToString()).Distinct(StringComparer.Ordinal).ToArray();
                var earliestUsageUtc = priceableUsages.Min(usage => usage.CreatedAtUtc);
                var latestUsageUtc = priceableUsages.Max(usage => usage.CreatedAtUtc);
                pricing = await db.AiPricing.AsNoTracking().Where(price =>
                    providers.Contains(price.Provider)
                    && models.Contains(price.Model)
                    && (price.UsageType == null || usageTypes.Contains(price.UsageType))
                    && price.EffectiveFromUtc <= latestUsageUtc
                    && (price.EffectiveToUtc == null || earliestUsageUtc < price.EffectiveToUtc)).ToListAsync(ct);
            }

            var valuedUsages = new List<(AiUsageRecord Usage, decimal RealCost, string Currency)>();
            foreach (var usage in usages)
            {
                ct.ThrowIfCancellationRequested();
                var cost = AiCostCalculator.Calculate(usage, pricing);
                if (!cost.IsValuable || cost.RealAiCost == null) continue;
                valuedUsages.Add((usage, cost.RealAiCost.Value, cost.Currency!));
            }

            var ratesNeeded = valuedUsages.Where(item =>
                AiCostCurrencyConverter.Convert(item.RealCost, item.Currency, item.Usage.CreatedAtUtc, [])
                    .FailureReason == "ExchangeRateNotFound").ToArray();
            IReadOnlyCollection<ExchangeRate> exchangeRates = [];
            if (ratesNeeded.Length > 0)
            {
                var currencies = ratesNeeded.Select(item => item.Currency).Distinct(StringComparer.Ordinal).ToArray();
                var earliestUsageUtc = ratesNeeded.Min(item => item.Usage.CreatedAtUtc);
                var latestUsageUtc = ratesNeeded.Max(item => item.Usage.CreatedAtUtc);
                exchangeRates = await db.ExchangeRates.AsNoTracking().Where(rate =>
                    currencies.Contains(rate.BaseCurrency)
                    && rate.QuoteCurrency == "EUR"
                    && rate.EffectiveFromUtc <= latestUsageUtc
                    && (rate.EffectiveToUtc == null || earliestUsageUtc < rate.EffectiveToUtc)).ToListAsync(ct);
            }

            var convertedUsages = new List<(AiUsageRecord Usage, decimal ConvertedAmount)>();
            foreach (var item in valuedUsages)
            {
                ct.ThrowIfCancellationRequested();
                var converted = AiCostCurrencyConverter.Convert(
                    item.RealCost, item.Currency, item.Usage.CreatedAtUtc, exchangeRates);
                if (!converted.IsConvertible || converted.ConvertedAmount == null) continue;
                convertedUsages.Add((item.Usage, converted.ConvertedAmount.Value));
            }

            var convertedUsageIds = convertedUsages.Select(item => item.Usage.Id).ToArray();
            var includedEntries = convertedUsageIds.Length == 0
                ? []
                : await db.CreditLedger.AsNoTracking().Where(entry =>
                    entry.AiUsageRecordId.HasValue
                    && convertedUsageIds.Contains(entry.AiUsageRecordId.Value)
                    && entry.EntryType == "AiUsage"
                    && entry.BucketType == "MachineIncluded").ToListAsync(ct);
            var includedByUsage = includedEntries.ToLookup(entry => entry.AiUsageRecordId!.Value);

            foreach (var item in convertedUsages)
            {
                ct.ThrowIfCancellationRequested();
                var remainder = item.ConvertedAmount - includedByUsage[item.Usage.Id].Sum(entry => entry.RealAiCost ?? 0);
                if (remainder > 0) pending += CompanyWalletDebitService.CommercialAmount(remainder);
                if (pending >= wallet.Balance) return new(false, "AiCreditExhausted", ExhaustedMessage);
            }
            return new(true, "CompanyWalletAvailable", "Crédit entreprise disponible.");
        }
        return new(false, "AiCreditExhausted", ExhaustedMessage);
    }
}
