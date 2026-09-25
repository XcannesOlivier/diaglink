namespace WebApp.Api.Models;

public record ConversationSummary
{
    public required string Id { get; init; }
    public string? Title { get; init; }
    public long CreatedAt { get; init; }
    public string? MachineId { get; init; }
    public string? MachineName { get; init; }
}

/// <summary>SQL-side conversation ownership + machine binding, used to authorize resumed conversations.</summary>
public record ConversationOwnershipInfo(Guid Id, Guid? MachineId);

public record ConversationMessageInfo
{
    public required string Role { get; init; }
    public required string Content { get; init; }
}
