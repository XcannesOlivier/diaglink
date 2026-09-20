namespace WebApp.Api.Models;

public record MachineBillingPeriodResult
{
    public bool Success { get; init; }
    public string Status { get; init; } = "Rejected";
    public string? FailureReason { get; init; }
    public Guid MachineId { get; init; }
    public Guid? BillingPeriodId { get; init; }
    public DateTime? PeriodStartUtc { get; init; }
    public DateTime? PeriodEndUtc { get; init; }
    public decimal? IncludedAiBudgetRealCost { get; init; }
    public decimal? IncludedAiUsedRealCost { get; init; }
}
