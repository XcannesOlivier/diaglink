namespace WebApp.Api.Models;

public record MachineDocumentDto(string Id, string Name);

public record MachineDto
{
    public required string Id { get; init; }
    public required string CompanyId { get; init; }
    public required string Name { get; init; }
    // Secondary identifier from legacy dbo.Machines
    public string? Reference { get; init; }
    public required string Status { get; init; }
    /// <summary>
    /// True when a configured MachineAssistantConfiguration row exists for this machine. Never exposes
    /// AgentId/ProjectEndpoint/VectorStoreId — those stay server-side only.
    /// </summary>
    public required bool HasAssistantConfigured { get; init; }
    /// <summary>Whether the requesting user is authorized to use this machine.</summary>
    public required bool IsAccessible { get; init; }
}
