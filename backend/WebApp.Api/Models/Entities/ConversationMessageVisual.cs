namespace WebApp.Api.Models.Entities;

/// <summary>
/// A durable reference to a technical image used by an assistant message.
/// </summary>
public class ConversationMessageVisual
{
    public long Id { get; set; }
    public long ConversationMessageId { get; set; }
    public required string DocumentId { get; set; }
    public int Page { get; set; }
    public required string AssetType { get; set; }
    public string? Tile { get; set; }
    public required string Name { get; set; }
    public required string AssetKey { get; set; }
    public int DisplayOrder { get; set; }

    public ConversationMessage? ConversationMessage { get; set; }
}
