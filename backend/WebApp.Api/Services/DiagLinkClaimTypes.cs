namespace WebApp.Api.Services;

/// <summary>
/// Custom claim types added to the ClaimsPrincipal after resolving a dbo.Users record, regardless of
/// which authentication scheme produced the principal (DiagLinkSession or Microsoft JWT bearer).
/// ClaimTypes.Role and ClaimTypes.Email (standard) are used for Role/Email; these two are custom
/// because ASP.NET Core has no standard claim for them.
/// </summary>
public static class DiagLinkClaimTypes
{
    /// <summary>dbo.Users.Id (Guid, as string) — the canonical DiagLink user id, distinct from the
    /// Microsoft 'oid' claim, so callers never need to know which auth path produced the principal.</summary>
    public const string UserId = "diaglink_user_id";

    /// <summary>dbo.Users.CompanyId (Guid, as string).</summary>
    public const string CompanyId = "company_id";
}
