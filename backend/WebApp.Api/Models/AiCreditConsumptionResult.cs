namespace WebApp.Api.Models;

public record AiCreditConsumptionResult
{
    public bool Success { get; init; }
    public string Status { get; init; } = "Rejected";
    public string? FailureReason { get; init; }
    public Guid UsageRecordId { get; init; }
    public Guid? MachineId { get; init; }
    public Guid? CompanyId { get; init; }
    public Guid? BillingPeriodId { get; init; }
    public decimal RealAiCostEur { get; init; }
    /// <summary>Amount newly debited by this invocation; zero for AlreadyProcessed.</summary>
    public decimal MachineDebitEur { get; init; }
    public decimal RemainingIncludedBudgetEur { get; init; }
    public decimal RemainingRealAiCostForWalletEur { get; init; }
    public bool WalletDebitRequired { get; init; }
    public Guid? MachineLedgerEntryId { get; init; }
}
