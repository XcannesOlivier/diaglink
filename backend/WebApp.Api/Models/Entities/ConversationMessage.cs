namespace WebApp.Api.Models.Entities;

/// <summary>
/// A single message persisted within a <see cref="Conversation"/>.
/// </summary>
public class ConversationMessage
{
    public long Id { get; set; }
    public Guid ConversationId { get; set; }
    public required string Role { get; set; }
    public required string Content { get; set; }
    public int? TokenCount { get; set; }
    public bool IsSummarized { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public Conversation? Conversation { get; set; }
}
