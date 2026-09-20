using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Web;
using WebApp.Api.Data;

namespace WebApp.Api.Services;

/// <summary>
/// Resolves the canonical UserObjectId used by chat.Conversations from either authentication path,
/// without changing that column's format (nvarchar(200) opaque string, unchanged, no migration):
/// - Microsoft JWT: the 'oid' claim, exactly as before.
/// - DiagLink session: dbo.Users.EntraObjectId when set (so a user who previously signed in with
///   Microsoft keeps the same history when switching to OTP), otherwise "diaglink:{User.Id}".
/// </summary>
public class UserIdentityService
{
    private readonly DiagLinkDbContext _db;

    public UserIdentityService(DiagLinkDbContext db)
    {
        _db = db;
    }

    public async Task<string?> GetCanonicalUserObjectIdAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        // With two AuthenticationSchemes on the same policy, user.Identity can be ambiguous —
        // inspect Identities explicitly rather than the ambient Identity.
        var diagLinkIdentity = user.Identities.FirstOrDefault(identity =>
            identity.IsAuthenticated && string.Equals(identity.AuthenticationType, DiagLinkAuthenticationDefaults.Scheme, StringComparison.Ordinal));
        if (diagLinkIdentity is null)
        {
            return user.GetObjectId();
        }

        var userIdClaim = diagLinkIdentity.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userIdClaim, out var userId))
        {
            return null;
        }

        var entraObjectId = await _db.Users
            .Where(u => u.Id == userId)
            .Select(u => u.EntraObjectId)
            .FirstOrDefaultAsync(cancellationToken);

        return !string.IsNullOrEmpty(entraObjectId) ? entraObjectId : $"diaglink:{userId}";
    }
}
