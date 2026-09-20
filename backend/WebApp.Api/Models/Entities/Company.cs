namespace WebApp.Api.Models.Entities;

/// <summary>
/// DiagLink client company — owned by this app (dbo.Companies). dbo.Users.CompanyId points at
/// this table logically only (no SQL FK: dbo.Users is excluded from this app's migrations).
/// </summary>
public class Company
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string Status { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
