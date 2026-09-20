using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using WebApp.Api.Data;
using WebApp.Api.Models.Entities;

namespace WebApp.Api.Services;

/// <summary>
/// The trusted, active dbo.Users fields needed to authorize a request: never populate these from
/// anything the browser sends — only from a fresh dbo.Users row.
/// </summary>
public record DiagLinkUserRecord(Guid Id, string Email, Guid CompanyId, string Role, string? EntraObjectId);

/// <summary>
/// Single source of truth for resolving an active, role/company-eligible dbo.Users row, shared by
/// both authentication paths (DiagLinkSessionAuthenticationHandler and DiagLinkUserClaimsTransformation)
/// so the "is this user still allowed in?" rules are never duplicated or drift apart.
/// </summary>
public class DiagLinkUserLookupService
{
    private readonly DiagLinkDbContext _db;

    public DiagLinkUserLookupService(DiagLinkDbContext db)
    {
        _db = db;
    }

    public Task<DiagLinkUserRecord?> FindActiveUserByIdAsync(Guid userId, CancellationToken cancellationToken)
        => FindActiveUserAsync(u => u.Id == userId, cancellationToken);

    public Task<DiagLinkUserRecord?> FindActiveUserByEntraObjectIdAsync(string entraObjectId, CancellationToken cancellationToken)
        => FindActiveUserAsync(u => u.EntraObjectId == entraObjectId, cancellationToken);

    private async Task<DiagLinkUserRecord?> FindActiveUserAsync(
        Expression<Func<User, bool>> predicate,
        CancellationToken cancellationToken)
    {
        var user = await _db.Users
            .AsNoTracking()
            .Where(predicate)
            .FirstOrDefaultAsync(cancellationToken);

        if (user is null ||
            !string.Equals(user.Status, "active", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(user.Role) ||
            user.CompanyId == Guid.Empty)
        {
            // Deliberately a single generic failure path — an inactive user, a missing role, and an
            // unset company all mean the same thing to a caller: this user cannot be authenticated.
            return null;
        }

        return new DiagLinkUserRecord(user.Id, user.Email, user.CompanyId, user.Role, user.EntraObjectId);
    }
}
