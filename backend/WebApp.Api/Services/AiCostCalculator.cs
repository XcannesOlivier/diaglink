using Microsoft.EntityFrameworkCore;
using WebApp.Api.Data;
using WebApp.Api.Models;
using WebApp.Api.Models.Entities;

namespace WebApp.Api.Services;

/// <summary>
/// Values one technical usage without modifying it or any financial table.
/// Pricing intervals are [EffectiveFromUtc, EffectiveToUtc).
/// Cost components retain decimal precision; only the raw sum is rounded,
/// once, to six decimal places using MidpointRounding.AwayFromZero.
/// </summary>
public sealed class AiCostCalculator(DiagLinkDbContext db)
{
    private const decimal MaxPersistableCost = 999999999999.999999m;

    public async Task<AiCostCalculationResult> CalculateAsync(Guid usageRecordId, CancellationToken ct = default)
    {
        var record = await db.AiUsageRecords.AsNoTracking()
            .SingleOrDefaultAsync(r => r.Id == usageRecordId, ct);
        return record == null
            ? new AiCostCalculationResult { UsageRecordId = usageRecordId, FailureReason = "UsageRecordNotFound" }
            : await CalculateAsync(record, ct);
    }

    public async Task<AiCostCalculationResult> CalculateAsync(AiUsageRecord record, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        ct.ThrowIfCancellationRequested();
        var result = new AiCostCalculationResult
        {
            UsageRecordId = record.Id,
            Provider = record.Provider,
            Model = record.Model,
            UsageType = record.UsageType.ToString(),
            InputTokens = record.InputTokens,
            OutputTokens = record.OutputTokens,
            CacheReadInputTokens = record.CacheReadInputTokens,
            CacheCreationInputTokens = record.CacheCreationInputTokens,
            CacheCreation5mInputTokens = record.CacheCreation5mInputTokens,
            CacheCreation1hInputTokens = record.CacheCreation1hInputTokens,
            WebSearchRequests = record.WebSearchRequests
        };
        // Give historical, unattributed records a stable reason, even if tokens are also missing.
        if (string.IsNullOrWhiteSpace(record.Provider)) return result with { FailureReason = "ProviderMissing" };
        if (string.IsNullOrWhiteSpace(record.Model)) return result with { FailureReason = "ModelMissing" };
        if (!record.Available) return result with { FailureReason = "UsageUnavailable" };
        if (record.InputTokens == null) return result with { FailureReason = "InputTokensMissing" };
        if (record.OutputTokens == null) return result with { FailureReason = "OutputTokensMissing" };
        if (record.InputTokens < 0 || record.OutputTokens < 0) return result with { FailureReason = "NegativeTokenCount" };
        if (record.WebSearchRequests < 0) return result with { FailureReason = "NegativeWebSearchRequestCount" };

        var cacheReadTokens = record.CacheReadInputTokens ?? 0;
        var cacheCreationTokens = record.CacheCreationInputTokens ??
            (long)(record.CacheCreation5mInputTokens ?? 0) + (record.CacheCreation1hInputTokens ?? 0);
        var hasCreationBreakdown = record.CacheCreation5mInputTokens.HasValue || record.CacheCreation1hInputTokens.HasValue;
        var cacheCreation5mTokens = hasCreationBreakdown
            ? record.CacheCreation5mInputTokens ?? 0
            : cacheCreationTokens;
        var cacheCreation1hTokens = record.CacheCreation1hInputTokens ?? 0;
        if (cacheReadTokens < 0 || cacheCreationTokens < 0 || cacheCreation5mTokens < 0 || cacheCreation1hTokens < 0)
            return result with { FailureReason = "NegativeTokenCount" };
        if (cacheCreation5mTokens > cacheCreationTokens ||
            cacheCreation1hTokens != cacheCreationTokens - cacheCreation5mTokens)
            return result with { FailureReason = "InconsistentCacheTokenCount" };
        result = result with
        {
            CacheReadInputTokens = cacheReadTokens,
            CacheCreationInputTokens = cacheCreationTokens,
            CacheCreation5mInputTokens = cacheCreation5mTokens,
            CacheCreation1hInputTokens = cacheCreation1hTokens
        };

        var type = record.UsageType.ToString();
        var candidates = await db.AiPricing.AsNoTracking()
            .Where(p => p.Provider == record.Provider && p.Model == record.Model
                && (p.UsageType == type || p.UsageType == null)
                && p.EffectiveFromUtc <= record.CreatedAtUtc
                && (p.EffectiveToUtc == null || record.CreatedAtUtc < p.EffectiveToUtc))
            .ToListAsync(ct);
        // SQL collations may ignore case or trailing spaces. Reject these loose matches
        // after the indexed SQL filter; never normalize provider/model identifiers.
        var exact = candidates.Where(p =>
            string.Equals(p.Provider, record.Provider, StringComparison.Ordinal)
            && string.Equals(p.Model, record.Model, StringComparison.Ordinal)).ToArray();
        var selected = exact.Where(p => string.Equals(p.UsageType, type, StringComparison.Ordinal)).ToArray();
        if (selected.Length == 0) selected = exact.Where(p => p.UsageType == null).ToArray();
        if (selected.Length == 0) return result with { FailureReason = "PricingNotFound" };
        if (selected.Length > 1) return result with { FailureReason = "AmbiguousPricing" };
        var pricing = selected[0];
        result = result with
        {
            PricingId = pricing.Id,
            PricingEffectiveFromUtc = pricing.EffectiveFromUtc,
            Currency = pricing.Currency
        };
        if (pricing.InputPricePerMillion < 0 || pricing.OutputPricePerMillion < 0 ||
            pricing.CacheReadPricePerMillion < 0 || pricing.CacheCreation5mPricePerMillion < 0 ||
            pricing.CacheCreation1hPricePerMillion < 0 || pricing.WebSearchPricePerRequest < 0)
            return result with { FailureReason = "InvalidPricing" };
        if (cacheReadTokens > 0 && pricing.CacheReadPricePerMillion == null)
            return result with { FailureReason = "CacheReadPricingMissing" };
        if (cacheCreation5mTokens > 0 && pricing.CacheCreation5mPricePerMillion == null)
            return result with { FailureReason = "CacheCreation5mPricingMissing" };
        if (cacheCreation1hTokens > 0 && pricing.CacheCreation1hPricePerMillion == null)
            return result with { FailureReason = "CacheCreation1hPricingMissing" };
        if (record.WebSearchRequests > 0 && pricing.WebSearchPricePerRequest == null)
            return result with { FailureReason = "WebSearchPricingMissing" };
        if (string.IsNullOrWhiteSpace(pricing.Currency) || pricing.Currency.Length != 3
            || pricing.Currency.Any(c => c < 'A' || c > 'Z'))
            return result with { FailureReason = "InvalidCurrency" };
        try
        {
            var input = record.InputTokens.Value / 1_000_000m * pricing.InputPricePerMillion;
            var output = record.OutputTokens.Value / 1_000_000m * pricing.OutputPricePerMillion;
            var cacheRead = cacheReadTokens / 1_000_000m * pricing.CacheReadPricePerMillion.GetValueOrDefault();
            var cacheCreation5m = cacheCreation5mTokens / 1_000_000m * pricing.CacheCreation5mPricePerMillion.GetValueOrDefault();
            var cacheCreation1h = cacheCreation1hTokens / 1_000_000m * pricing.CacheCreation1hPricePerMillion.GetValueOrDefault();
            var webSearch = record.WebSearchRequests * pricing.WebSearchPricePerRequest.GetValueOrDefault();
            var total = Math.Round(input + output + cacheRead + cacheCreation5m + cacheCreation1h + webSearch, 6, MidpointRounding.AwayFromZero);
            if (total > MaxPersistableCost) return result with { FailureReason = "CostOutOfRange" };
            return result with
            {
                IsValuable = true,
                InputCost = input,
                OutputCost = output,
                CacheReadCost = cacheRead,
                CacheCreation5mCost = cacheCreation5m,
                CacheCreation1hCost = cacheCreation1h,
                WebSearchCost = webSearch,
                RealAiCost = total
            };
        }
        catch (OverflowException)
        {
            return result with { FailureReason = "CostOutOfRange" };
        }
    }
}
