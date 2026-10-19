namespace WebApp.Api.Models.Entities;

/// <summary>A selected Web Search citation persisted with an assistant message.</summary>
public class ConversationMessageWebCitation
{
    public long Id { get; set; }
    public long ConversationMessageId { get; set; }
    public required string Url { get; set; }
    public required string Label { get; set; }
    public string? Quote { get; set; }
    public int DisplayOrder { get; set; }

    public ConversationMessage? ConversationMessage { get; set; }
}
