using WebApp.Api.Models.Entities;

namespace WebApp.Api.Models;

public enum MachineResolutionKind
{
    MachineNotAccessible,
    MachineDisabled,
    Resolved,
}

public record MachineResolution
{
    public required MachineResolutionKind Kind { get; init; }
    public Machine? Machine { get; init; }

    public static MachineResolution NotAccessible() => new() { Kind = MachineResolutionKind.MachineNotAccessible };
    public static MachineResolution Disabled() => new() { Kind = MachineResolutionKind.MachineDisabled };
    public static MachineResolution Ok(Machine machine) => new() { Kind = MachineResolutionKind.Resolved, Machine = machine };
}
