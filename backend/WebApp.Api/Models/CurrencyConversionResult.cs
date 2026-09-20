namespace WebApp.Api.Models;

public record CurrencyConversionResult
{
    public bool IsConvertible { get; init; }
    public string? FailureReason { get; init; }
    public decimal SourceAmount { get; init; }
    public string? SourceCurrency { get; init; }
    public decimal? ConvertedAmount { get; init; }
    public string TargetCurrency { get; init; } = "EUR";
    public decimal? ExchangeRate { get; init; }
    public Guid? ExchangeRateId { get; init; }
    public DateTime? ExchangeRateEffectiveFromUtc { get; init; }
}
