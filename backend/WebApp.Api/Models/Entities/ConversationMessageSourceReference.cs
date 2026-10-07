namespace WebApp.Api.Models.Entities;

/// <summary>A durable private mapping from assistant text to a verified PDF page.</summary>
public class ConversationMessageSourceReference
{
    public long Id { get; set; }
    public long ConversationMessageId { get; set; }
    public required string DocumentId { get; set; }
    public int PdfPage { get; set; }
    public required string DisplayPage { get; set; }
    public required string Label { get; set; }
    public int StartIndex { get; set; }
    public int EndIndex { get; set; }
    public int DisplayOrder { get; set; }

    public ConversationMessage? ConversationMessage { get; set; }
}