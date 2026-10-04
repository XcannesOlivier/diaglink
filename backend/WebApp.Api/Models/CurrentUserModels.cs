namespace WebApp.Api.Models;

/// <summary>Response for GET /api/auth/me — built exclusively from the caller's own ClaimsPrincipal.</summary>
public record CurrentUserResponse
{
    public required string UserId { get; init; }
    public required string CompanyId { get; init; }
    public required string Role { get; init; }
    public string? Email { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? PhoneNumber { get; init; }
}
