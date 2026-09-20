using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using WebApp.Api.Data;
using WebApp.Api.Models;
using WebApp.Api.Models.Entities;

namespace WebApp.Api.Services;

/// <summary>
/// Resolves which Foundry agent (if any) is bound to a machine for a given caller, enforcing access
/// control (via <see cref="MachineAccessService"/>) before ever touching MachineAssistantConfiguration.
/// This is the single choke point used by /api/chat/stream for both new and resumed conversations —
/// never trust a client-supplied agent identifier, always re-derive it from SQL on every call.
/// </summary>
public class MachineAssistantResolutionService
{
    private readonly DiagLinkDbContext _db;
    private readonly MachineAccessService _machineAccessService;

    public MachineAssistantResolutionService(DiagLinkDbContext db, MachineAccessService machineAccessService)
    {
        _db = db;
        _machineAccessService = machineAccessService;
    }

    /// <summary>
    /// Resolves the assistant configuration for <paramref name="machineId"/>, returning
    /// <see cref="MachineAssistantResolutionKind.MachineNotAccessible"/> for both "doesn't exist" and
    /// "exists but caller can't see it" — callers must map this to 404, never 403 (anti-enumeration).
    /// </summary>
    public async Task<MachineAssistantResolution> ResolveAsync(ClaimsPrincipal user, Guid machineId, CancellationToken cancellationToken)
    {
        if (!await _machineAccessService.CanAccessMachineAsync(user, machineId, cancellationToken))
        {
            return MachineAssistantResolution.NotAccessible();
        }

        // Resolve assistant configuration directly from legacy dbo.Machines row. This makes
        // dbo.Machines the single source of truth for Foundry configuration.
        var machine = await _db.Machines
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == machineId, cancellationToken);

        if (machine is null)
        {
            return MachineAssistantResolution.NotConfigured();
        }

        if (!await MachineEntitlements.Eligible(_db, DateTime.UtcNow).AnyAsync(m => m.Id == machineId, cancellationToken))
        {
            return MachineAssistantResolution.Disabled();
        }

        // Consider configured when FoundryAgentId and ProjectEndpoint are present
        if (string.IsNullOrEmpty(machine.FoundryAgentId) || string.IsNullOrEmpty(machine.ProjectEndpoint))
        {
            return MachineAssistantResolution.NotConfigured();
        }

        return MachineAssistantResolution.Ok(new ResolvedAssistantConfiguration
        {
            CompanyId = machine.CompanyId,
            ProjectEndpoint = machine.ProjectEndpoint,
            AgentId = machine.FoundryAgentId,
            AgentVersion = machine.AgentVersion,
            AgentName = null,
        });
    }
}
