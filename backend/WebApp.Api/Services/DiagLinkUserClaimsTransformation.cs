using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Identity.Web;

namespace WebApp.Api.Services;

/// <summary>
/// Enriches a Microsoft JWT bearer principal with the same Role/CompanyId/Email claims that
/// DiagLinkSessionAuthenticationHandler already attaches for OTP sessions — so downstream code
/// (e.g. GET /api/auth/me, role policies) reads one uniform claim shape regardless of which
/// authentication scheme produced the principal.
///
/// Registered as IClaimsTransformation (not done inline in the JWT handler) because Microsoft.Identity.Web
/// owns creation of the JWT ClaimsIdentity — IClaimsTransformation is the standard ASP.NET Core extension
/// point to add claims after authentication, for a principal we don't otherwise control. It runs for every
/// authenticated request, so it is written to be a no-op for DiagLink sessions (already enriched) and for
/// Microsoft principals with no matching active dbo.Users row (chat still works via scope-based auth only;
/// role-gated endpoints like /api/auth/me will simply be unauthorized for that principal).
/// </summary>
public class DiagLinkUserClaimsTransformation : IClaimsTransformation
{
    private readonly DiagLinkUserLookupService _userLookupService;

    public DiagLinkUserClaimsTransformation(DiagLinkUserLookupService userLookupService)
    {
        _userLookupService = userLookupService;
    }

    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.HasClaim(c => c.Type == DiagLinkClaimTypes.CompanyId))
        {
            // Already enriched — either a DiagLink session (handler-set) or a prior call in the same request.
            return principal;
        }

        var oid = principal.GetObjectId();
        if (string.IsNullOrEmpty(oid))
        {
            // No Microsoft 'oid' claim: not a Microsoft JWT principal (or DiagLink session, which never has one).
            return principal;
        }

        var identity = principal.Identities.FirstOrDefault(i => i.IsAuthenticated);
        if (identity is null)
        {
            return principal;
        }

        var user = await _userLookupService.FindActiveUserByEntraObjectIdAsync(oid, CancellationToken.None);
        if (user is null)
        {
            return principal;
        }

        identity.AddClaim(new Claim(DiagLinkClaimTypes.UserId, user.Id.ToString()));
        identity.AddClaim(new Claim(ClaimTypes.Role, user.Role));
        identity.AddClaim(new Claim(DiagLinkClaimTypes.CompanyId, user.CompanyId.ToString()));
        if (!string.IsNullOrWhiteSpace(user.Email))
        {
            identity.AddClaim(new Claim(ClaimTypes.Email, user.Email));
        }

        return principal;
    }
}
