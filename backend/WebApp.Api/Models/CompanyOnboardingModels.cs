namespace WebApp.Api.Models;

public record CreateCompanyRequest
{
    public required string Name { get; init; }
}

public record CreateCompanyAdminRequest
{
    public required string Email { get; init; }
}

public record OnboardCompanyRequest
{
    public required string CompanyName { get; init; }
    public required string AdminEmail { get; init; }
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public required string PhoneNumber { get; init; }
}

/// <summary>Result of POST /api/companies/onboard — never the raw User/Company entities.</summary>
public record CompanyOnboardingResultDto
{
    public required CompanyDto Company { get; init; }
    public required CompanyUserDto Admin { get; init; }
}
