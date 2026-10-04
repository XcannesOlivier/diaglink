namespace WebApp.Api.Models;

using WebApp.Api.Models.Entities;

/// <summary>
/// Server-resolved Foundry agent configuration for a specific machine. Built exclusively from SQL
/// (<c>MachineAssistantConfiguration</c>) by <see cref="WebApp.Api.Services.MachineAssistantResolutionService"/>
/// — never accepted from the client, and never exposes AgentId/ProjectEndpoint back to the frontend.
/// </summary>
public record ResolvedAssistantConfiguration
{
    public Guid? CompanyId { get; init; }
    public required string ProjectEndpoint { get; init; }
    public required string AgentId { get; init; }
    public string? AgentVersion { get; init; }
    public string? AgentName { get; init; }
}

/// <summary>Outcome of the runtime-neutral machine access and entitlement checks.</summary>
public enum MachineResolutionKind
{
    MachineNotAccessible,
    MachineDisabled,
    Resolved,
}

/// <summary>Authorized machine context used before selecting an AI chat runtime.</summary>
public record MachineResolution
{
    public required MachineResolutionKind Kind { get; init; }
    public Machine? Machine { get; init; }

    public static MachineResolution NotAccessible() => new() { Kind = MachineResolutionKind.MachineNotAccessible };
    public static MachineResolution Disabled() => new() { Kind = MachineResolutionKind.MachineDisabled };
    public static MachineResolution Ok(Machine machine) => new() { Kind = MachineResolutionKind.Resolved, Machine = machine };
}

/// <summary>Outcome of resolving a machine's assistant configuration, distinguishing every failure mode.</summary>
public enum MachineAssistantResolutionKind
{
    /// <summary>Machine doesn't exist or the caller has no access — map to 404, never 403 (anti-enumeration).</summary>
    MachineNotAccessible,
    /// <summary>Machine exists and is accessible, but no MachineAssistantConfiguration row exists yet.</summary>
    AssistantNotConfigured,
    /// <summary>A configuration row exists but its Status is not "configured".</summary>
    AssistantDisabled,
    /// <summary>A valid, enabled configuration was found.</summary>
    Configured,
}

/// <summary>Result of <see cref="WebApp.Api.Services.MachineAssistantResolutionService"/> resolution.</summary>
public record MachineAssistantResolution
{
    public required MachineAssistantResolutionKind Kind { get; init; }
    public ResolvedAssistantConfiguration? Configuration { get; init; }
    public Machine? Machine { get; init; }

    public static MachineAssistantResolution NotAccessible() => new() { Kind = MachineAssistantResolutionKind.MachineNotAccessible };
    public static MachineAssistantResolution NotConfigured() => new() { Kind = MachineAssistantResolutionKind.AssistantNotConfigured };
    public static MachineAssistantResolution Disabled() => new() { Kind = MachineAssistantResolutionKind.AssistantDisabled };
    public static MachineAssistantResolution Ok(ResolvedAssistantConfiguration configuration, Machine machine) =>
        new() { Kind = MachineAssistantResolutionKind.Configured, Configuration = configuration, Machine = machine };
}
