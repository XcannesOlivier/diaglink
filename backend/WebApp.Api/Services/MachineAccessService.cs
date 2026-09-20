using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using WebApp.Api.Data;
using WebApp.Api.Models;
using WebApp.Api.Models.Entities;

namespace WebApp.Api.Services;

/// <summary>
/// Central machine-authorization logic. Every decision is derived exclusively from the
/// Role/CompanyId/UserId claims already resolved server-side (see DiagLinkClaimTypes) — never
/// from a CompanyId or UserId a client could pass as a request parameter.
/// </summary>
public class MachineAccessService
{
    private readonly DiagLinkDbContext _db;

    public MachineAccessService(DiagLinkDbContext db)
    {
        _db = db;
    }

    public record AccessibleMachine(Machine Machine, bool IsAccessible);

    public async Task<bool> CanAccessMachineAsync(ClaimsPrincipal user, Guid machineId, CancellationToken cancellationToken)
    {
        var role = user.FindFirst(ClaimTypes.Role)?.Value;

        if (role == DiagLinkRoles.SuperAdmin)
        {
            return await _db.Machines.AsNoTracking().AnyAsync(m => m.Id == machineId, cancellationToken);
        }

        if (!TryGetCompanyId(user, out var companyId))
        {
            return false;
        }

        var machine = await _db.Machines.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == machineId, cancellationToken);

        if (machine is null || machine.CompanyId != companyId)
        {
            return false;
        }

        if (role == DiagLinkRoles.CompanyAdmin)
        {
            return true;
        }

        if (role == DiagLinkRoles.Technician)
        {
            if (!TryGetUserId(user, out var userId))
            {
                return false;
            }

            return await _db.UserMachineAccess.AsNoTracking()
                .AnyAsync(a => a.UserId == userId && a.MachineId == machineId, cancellationToken);
        }

        return false;
    }

    /// <summary>Machines visible to the caller for GET /api/machines, including whether each is usable.</summary>
    public async Task<List<AccessibleMachine>> GetAccessibleMachinesAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var role = user.FindFirst(ClaimTypes.Role)?.Value;

        if (role == DiagLinkRoles.SuperAdmin)
        {
            return await _db.Machines.AsNoTracking().OrderBy(m => m.Name)
                .Select(m => new AccessibleMachine(m, true)).ToListAsync(cancellationToken);
        }

        if (!TryGetCompanyId(user, out var companyId))
        {
            return [];
        }

        if (role == DiagLinkRoles.CompanyAdmin)
        {
            return await _db.Machines.AsNoTracking()
                .Where(m => m.CompanyId == companyId)
                .OrderBy(m => m.Name)
                .Select(m => new AccessibleMachine(m, true)).ToListAsync(cancellationToken);
        }

        if (role == DiagLinkRoles.Technician)
        {
            if (!TryGetUserId(user, out var userId))
            {
                return [];
            }

            var accessibleMachineIds = await _db.UserMachineAccess.AsNoTracking()
                .Where(a => a.UserId == userId)
                .Select(a => a.MachineId)
                .ToHashSetAsync(cancellationToken);

            return await _db.Machines.AsNoTracking()
                .Where(m => m.CompanyId == companyId)
                .OrderBy(m => m.Name)
                .Select(m => new AccessibleMachine(m, accessibleMachineIds.Contains(m.Id)))
                .ToListAsync(cancellationToken);
        }

        return [];
    }

    private static bool TryGetCompanyId(ClaimsPrincipal user, out Guid companyId)
        => Guid.TryParse(user.FindFirst(DiagLinkClaimTypes.CompanyId)?.Value, out companyId);

    private static bool TryGetUserId(ClaimsPrincipal user, out Guid userId)
        => Guid.TryParse(user.FindFirst(DiagLinkClaimTypes.UserId)?.Value, out userId);
}
