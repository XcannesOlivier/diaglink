namespace WebApp.Api.Models.Entities;

/// <summary>Operational balance cache of commercial DiagLink credits; history belongs in CreditLedger.</summary>
public class CompanyWallet
{
    public Guid CompanyId { get; set; }
    public decimal Balance { get; set; }
    public required string Currency { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
