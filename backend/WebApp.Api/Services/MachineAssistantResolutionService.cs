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

    /// <summary>Performs the runtime-neutral access, existence and entitlement checks.</summary>
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

    /// <summary>Builds the legacy Hosted Agent configuration after that runtime is selected.</summary>
    public MachineAssistantResolution ResolveHostedAgent(Machine machine)
    {
        ArgumentNullException.ThrowIfNull(machine);

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
        }, machine);
    }

    /// <summary>
    /// Resolves the assistant configuration for <paramref name="machineId"/>, returning
    /// <see cref="MachineAssistantResolutionKind.MachineNotAccessible"/> for both "doesn't exist" and
    /// "exists but caller can't see it" — callers must map this to 404, never 403 (anti-enumeration).
    /// </summary>
    public async Task<MachineAssistantResolution> ResolveAsync(ClaimsPrincipal user, Guid machineId, CancellationToken cancellationToken)
    {
        var machineResolution = await ResolveMachineAsync(user, machineId, cancellationToken);
        if (machineResolution.Kind == MachineResolutionKind.MachineNotAccessible)
        {
            return MachineAssistantResolution.NotAccessible();
        }

        if (machineResolution.Kind == MachineResolutionKind.MachineDisabled)
        {
            return MachineAssistantResolution.Disabled();
        }

        return ResolveHostedAgent(machineResolution.Machine!);
    }
}
