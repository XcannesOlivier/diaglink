using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using WebApp.Api.Data;
using WebApp.Api.Models;

namespace WebApp.Api.Services;

/// <summary>Resolves the authorized machine context for Claude Direct chat requests.</summary>
public class MachineAssistantResolutionService
{
    private readonly DiagLinkDbContext _db;
    private readonly MachineAccessService _machineAccessService;

    public MachineAssistantResolutionService(DiagLinkDbContext db, MachineAccessService machineAccessService)
    {
        _db = db;
        _machineAccessService = machineAccessService;
    }

    public async Task<MachineResolution> ResolveMachineAsync(
        ClaimsPrincipal user,
        Guid machineId,
        CancellationToken cancellationToken)
    {
        if (!await _machineAccessService.CanAccessMachineAsync(user, machineId, cancellationToken))
        {
            return MachineResolution.NotAccessible();
        }

        var machine = await _db.Machines
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == machineId, cancellationToken);

        if (machine is null)
        {
            return MachineResolution.NotAccessible();
        }

        if (!await MachineEntitlements.Eligible(_db, DateTime.UtcNow)
                .AnyAsync(m => m.Id == machineId, cancellationToken))
        {
            return MachineResolution.Disabled();
        }

        return MachineResolution.Ok(machine);
    }
}
