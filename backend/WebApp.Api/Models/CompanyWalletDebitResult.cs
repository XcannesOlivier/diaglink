namespace WebApp.Api.Models;

public record CompanyWalletDebitResult
{
    public bool Success { get; init; }
    public string Status { get; init; } = "Rejected";
    public string? FailureReason { get; init; }
    public Guid UsageRecordId { get; init; }
    public Guid? CompanyId { get; init; }
    public Guid? MachineId { get; init; }
    public decimal RemainingRealAiCostEur { get; init; }
    public decimal CommercialDebitRequired { get; init; }
    /// <summary>New debit in this invocation; zero on AlreadyProcessed.</summary>
    public decimal WalletDebitEur { get; init; }
    public decimal WalletBalanceBeforeEur { get; init; }
    public decimal WalletBalanceAfterEur { get; init; }
    public decimal CommercialCreditMissing { get; init; }
    public decimal RemainingCommercialCreditRequired { get; init; }
    public decimal RemainingRealAiCostUncovered { get; init; }
    public Guid? WalletLedgerEntryId { get; init; }
}
