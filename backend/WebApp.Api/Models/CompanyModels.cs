namespace WebApp.Api.Models;

public record CompanyDto
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Status { get; init; }
}

/// <summary>Minimal projection of a dbo.Users row — never exposes EntraObjectId.</summary>
public record CompanyUserDto
{
    public required string Id { get; init; }
    public required string Email { get; init; }
    public required string Role { get; init; }
    public required string Status { get; init; }
    // Optional human-friendly name parts — may be null for legacy rows.
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? PhoneNumber { get; init; }
}
