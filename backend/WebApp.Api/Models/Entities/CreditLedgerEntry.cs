namespace WebApp.Api.Models.Entities;

/// <summary>
/// Financial journal entry intended to be append-only: corrections should be new entries.
/// Immutability enforcement and debit processing are outside this data-foundation step.
/// </summary>
public class CreditLedgerEntry
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid? MachineId { get; set; }
    public Guid? MachineBillingPeriodId { get; set; }
    public Guid? AiUsageRecordId { get; set; }
    public required string EntryType { get; set; }
    public required string BucketType { get; set; }
    /// <summary>Actual provider AI cost, separate from commercial DiagLink credits.</summary>
    public decimal? RealAiCost { get; set; }
    public decimal? CommercialCreditAmount { get; set; }
    /// <summary>Optional balance of the bucket affected by this entry.</summary>
    public decimal? BalanceAfter { get; set; }
    public string? Currency { get; set; }
    public string? ExternalEventId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public string? Notes { get; set; }
}
