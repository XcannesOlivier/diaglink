namespace WebApp.Api.Models.Entities;

/// <summary>
/// Maps to the pre-existing 'dbo.Users' table (owned outside this app — see DiagLinkDbContext).
/// </summary>
public class User
{
    public Guid Id { get; set; }
    public string? EntraObjectId { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? PhoneNumber { get; set; }
    public Guid CompanyId { get; set; }
    public required string Email { get; set; }
    public required string Role { get; set; }
    public required string Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
