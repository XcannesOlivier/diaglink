using Microsoft.EntityFrameworkCore;
using WebApp.Api.Data;
using WebApp.Api.Models;
using WebApp.Api.Models.Entities;

namespace WebApp.Api.Services;

/// <summary>
/// Values one technical usage without modifying it or any financial table.
/// Pricing intervals are [EffectiveFromUtc, EffectiveToUtc).
/// Input/output costs retain decimal precision; only the raw sum is rounded,
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
            OutputTokens = record.OutputTokens
        };
        // Give historical, unattributed records a stable reason, even if tokens are also missing.
        if (string.IsNullOrWhiteSpace(record.Provider)) return result with { FailureReason = "ProviderMissing" };
        if (string.IsNullOrWhiteSpace(record.Model)) return result with { FailureReason = "ModelMissing" };
        if (!record.Available) return result with { FailureReason = "UsageUnavailable" };
        if (record.InputTokens == null) return result with { FailureReason = "InputTokensMissing" };
        if (record.OutputTokens == null) return result with { FailureReason = "OutputTokensMissing" };
        if (record.InputTokens < 0 || record.OutputTokens < 0) return result with { FailureReason = "NegativeTokenCount" };

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
        if (pricing.InputPricePerMillion < 0 || pricing.OutputPricePerMillion < 0)
            return result with { FailureReason = "InvalidPricing" };
        if (string.IsNullOrWhiteSpace(pricing.Currency) || pricing.Currency.Length != 3
            || pricing.Currency.Any(c => c < 'A' || c > 'Z'))
            return result with { FailureReason = "InvalidCurrency" };
        try
        {
            var input = record.InputTokens.Value / 1_000_000m * pricing.InputPricePerMillion;
            var output = record.OutputTokens.Value / 1_000_000m * pricing.OutputPricePerMillion;
            var total = Math.Round(input + output, 6, MidpointRounding.AwayFromZero);
            if (total > MaxPersistableCost) return result with { FailureReason = "CostOutOfRange" };
            return result with { IsValuable = true, InputCost = input, OutputCost = output, RealAiCost = total };
        }
        catch (OverflowException)
        {
            return result with { FailureReason = "CostOutOfRange" };
        }
    }
}
