namespace WebApp.Api.Models;

/// <summary>One company machine, annotated with whether the target user currently has access —
/// backs the checkbox list rendered by UsersView's machine-assignment panel.</summary>
public record UserMachineAccessDto
{
    public required string MachineId { get; init; }
    public required string Name { get; init; }
    // Secondary identifier from legacy dbo.Machines
    public string? Reference { get; init; }
    public required bool Assigned { get; init; }
}

/// <summary>Body for PUT /api/company/users/{userId}/machines — the complete set of machine ids the
/// target user should have access to. Replaces (not merges with) the existing UserMachineAccess rows.</summary>
public record ReplaceUserMachinesRequest
{
    public required List<string> MachineIds { get; init; }
}
