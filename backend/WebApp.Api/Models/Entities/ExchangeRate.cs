namespace WebApp.Api.Models.Entities;

/// <summary>Quote amount = base amount * Rate. Valid during [EffectiveFromUtc, EffectiveToUtc).</summary>
public class ExchangeRate
{
    public Guid Id { get; set; }
    public required string BaseCurrency { get; set; }
    public required string QuoteCurrency { get; set; }
    public decimal Rate { get; set; }
    public DateTime EffectiveFromUtc { get; set; }
    public DateTime? EffectiveToUtc { get; set; }
    public required string Source { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
