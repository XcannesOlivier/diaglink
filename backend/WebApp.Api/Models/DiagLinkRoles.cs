namespace WebApp.Api.Models;

/// <summary>
/// The 3 DiagLink role values, as stored verbatim in dbo.Users.Role. Never hardcode these strings
/// elsewhere — always reference this class so a typo can't silently create a 4th, unrecognized role.
/// </summary>
public static class DiagLinkRoles
{
    public const string Technician = "technician";
    public const string CompanyAdmin = "company_admin";
    public const string SuperAdmin = "diaglink_super_admin";
}
