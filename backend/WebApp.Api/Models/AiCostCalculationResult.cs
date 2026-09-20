namespace WebApp.Api.Models;

/// <summary>A read-only valuation, with nullable costs when the usage cannot be valued.</summary>
public sealed record AiCostCalculationResult
{
    public required Guid UsageRecordId { get; init; }
    public bool IsValuable { get; init; }
    public string? FailureReason { get; init; }
    public string? Provider { get; init; }
    public string? Model { get; init; }
    public string? UsageType { get; init; }
    public string? Currency { get; init; }
    public long? InputTokens { get; init; }
    public long? OutputTokens { get; init; }
    /// <summary>Unrounded component; not intended for direct decimal(18,6) persistence.</summary>
    public decimal? InputCost { get; init; }
    /// <summary>Unrounded component; not intended for direct decimal(18,6) persistence.</summary>
    public decimal? OutputCost { get; init; }
    /// <summary>Sum of raw components rounded once to six places, AwayFromZero.</summary>
    public decimal? RealAiCost { get; init; }
    public Guid? PricingId { get; init; }
    public DateTime? PricingEffectiveFromUtc { get; init; }
}
