namespace WebApp.Api.Models;

public record AiUsageBillingResult
{
    public bool Success { get; init; }
    public string Status { get; init; } = "Rejected";
    public string? FailureReason { get; init; }
    public Guid UsageRecordId { get; init; }
    public Guid? CompanyId { get; init; }
    public Guid? MachineId { get; init; }
    public decimal? ProviderCost { get; init; }
    public string? ProviderCurrency { get; init; }
    public decimal? RealAiCostEur { get; init; }
    public decimal MachineCoveredRealAiCostEur { get; init; }
    public decimal WalletCoveredRealAiCostEur { get; init; }
    public decimal RemainingRealAiCostEur { get; init; }
    public decimal CommercialCreditDebitedEur { get; init; }
    public decimal? WalletBalanceAfterEur { get; init; }
    public bool BillingPeriodRequired { get; init; }
    public bool WalletCreditRequired { get; init; }
    public Guid? MachineLedgerEntryId { get; init; }
    public Guid? WalletLedgerEntryId { get; init; }
}
