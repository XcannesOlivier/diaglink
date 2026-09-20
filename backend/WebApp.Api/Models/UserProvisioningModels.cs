namespace WebApp.Api.Models;

/// <summary>Body for both POST /api/company/users and POST /api/companies/{companyId}/users —
/// CompanyId/Status are always forced server-side, never accepted from the client. Role is
/// client-supplied but restricted server-side to technician/company_admin (see
/// UserProvisioningService) — diaglink_super_admin can never be created through this endpoint.</summary>
public record CreateTechnicianRequest
{
    public required string Email { get; init; }
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public required string PhoneNumber { get; init; }
    public required string Role { get; init; }
}
