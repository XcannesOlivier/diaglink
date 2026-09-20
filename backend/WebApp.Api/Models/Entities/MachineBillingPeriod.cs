namespace WebApp.Api.Models.Entities;

/// <summary>One machine's included provider-cost budget for a period, without carry-over.</summary>
public class MachineBillingPeriod
{
    public Guid Id { get; set; }
    public Guid MachineId { get; set; }
    public DateTime PeriodStartUtc { get; set; }
    public DateTime PeriodEndUtc { get; set; }
    public decimal IncludedAiBudgetRealCost { get; set; }
    public decimal IncludedAiUsedRealCost { get; set; } = 0m;
    public required string Status { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
