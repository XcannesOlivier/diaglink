namespace WebApp.Api.Models.Entities;

/// <summary>Historical provider prices. Price changes should create a new effective-dated row.</summary>
public class AiPricing
{
    public Guid Id { get; set; }
    public required string Provider { get; set; }
    public required string Model { get; set; }
    public string? UsageType { get; set; }
    public decimal InputPricePerMillion { get; set; }
    public decimal OutputPricePerMillion { get; set; }
    public required string Currency { get; set; }
    public DateTime EffectiveFromUtc { get; set; }
    public DateTime? EffectiveToUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
