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
    public IReadOnlyList<ConversationMessageVisualInfo> Visuals { get; init; } = [];
    public IReadOnlyList<ConversationMessageSourceReferenceInfo> Sources { get; init; } = [];
}

public record ConversationMessageVisualInfo
{
    public long Id { get; init; }
    public required string DocumentId { get; init; }
    public int Page { get; init; }
    public required string AssetType { get; init; }
    public string? Tile { get; init; }
    public required string Name { get; init; }
    public int DisplayOrder { get; init; }
}

public record ConversationMessageSourceReferenceInfo
{
    public long Id { get; init; }
    public int PdfPage { get; init; }
    public required string DisplayPage { get; init; }
    public required string Label { get; init; }
    public int StartIndex { get; init; }
    public int EndIndex { get; init; }
    public int DisplayOrder { get; init; }
}

public sealed record ConversationMessagePersistenceResult(
    long MessageId,
    IReadOnlyList<ConversationMessageVisualInfo> Visuals,
    IReadOnlyList<ConversationMessageSourceReferenceInfo> Sources);
