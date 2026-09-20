using Microsoft.EntityFrameworkCore;
using WebApp.Api.Data;
using WebApp.Api.Models;
using WebApp.Api.Models.Entities;

namespace WebApp.Api.Services;

/// <summary>Read-only conversion to EUR using the consumption timestamp, never the current time.</summary>
public sealed class AiCostCurrencyConverter(DiagLinkDbContext db)
{
    private const decimal MaxAmount = 999999999999.999999m;

    public Task<CurrencyConversionResult> ConvertAsync(decimal sourceAmount, string? sourceCurrency,
        AiUsageRecord usage, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(usage);
        return ConvertAsync(sourceAmount, sourceCurrency, usage.CreatedAtUtc, ct);
    }

    public async Task<CurrencyConversionResult> ConvertAsync(decimal sourceAmount, string? sourceCurrency,
        DateTime usageCreatedAtUtc, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var result = new CurrencyConversionResult { SourceAmount = sourceAmount, SourceCurrency = sourceCurrency };
        if (string.IsNullOrWhiteSpace(sourceCurrency)) return result with { FailureReason = "SourceCurrencyMissing" };
        if (sourceCurrency.Length != 3 || sourceCurrency.Any(c => c < 'A' || c > 'Z'))
            return result with { FailureReason = "InvalidSourceCurrency" };
        if (sourceAmount < 0) return result with { FailureReason = "InvalidSourceAmount" };
        // SQL datetime2 is materialized as Unspecified; it represents UTC in this model.
        if (usageCreatedAtUtc.Kind == DateTimeKind.Local)
            return result with { FailureReason = "InvalidUsageDate" };
        if (sourceCurrency == "EUR")
        {
            if (sourceAmount > MaxAmount) return result with { FailureReason = "ConvertedAmountOutOfRange" };
            // AiCostCalculator already supplies six-decimal costs. Identity conversion is exact.
            return result with { IsConvertible = true, ConvertedAmount = sourceAmount, ExchangeRate = 1m };
        }
        var candidates = await db.ExchangeRates.AsNoTracking().Where(r =>
            r.BaseCurrency == sourceCurrency && r.QuoteCurrency == "EUR" &&
            r.EffectiveFromUtc <= usageCreatedAtUtc &&
            (r.EffectiveToUtc == null || usageCreatedAtUtc < r.EffectiveToUtc)).ToListAsync(ct);
        // SQL collation must not silently normalize currency identifiers.
        var matches = candidates.Where(r => string.Equals(r.BaseCurrency, sourceCurrency, StringComparison.Ordinal)
            && string.Equals(r.QuoteCurrency, "EUR", StringComparison.Ordinal)).ToArray();
        if (matches.Length == 0) return result with { FailureReason = "ExchangeRateNotFound" };
        if (matches.Length > 1) return result with { FailureReason = "AmbiguousExchangeRate" };
        var rate = matches[0];
        result = result with { ExchangeRate = rate.Rate, ExchangeRateId = rate.Id,
            ExchangeRateEffectiveFromUtc = rate.EffectiveFromUtc };
        if (rate.Rate <= 0) return result with { FailureReason = "InvalidExchangeRate" };
        try
        {
            var converted = Math.Round(sourceAmount * rate.Rate, 6, MidpointRounding.AwayFromZero);
            if (converted > MaxAmount) return result with { FailureReason = "ConvertedAmountOutOfRange" };
            return result with { IsConvertible = true, ConvertedAmount = converted };
        }
        catch (OverflowException) { return result with { FailureReason = "ConvertedAmountOutOfRange" }; }
    }
}
