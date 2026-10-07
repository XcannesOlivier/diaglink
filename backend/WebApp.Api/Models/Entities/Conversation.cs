namespace WebApp.Api.Models.Entities;

/// <summary>
/// A persisted DiagLink conversation keyed by its stable public identifier.
/// </summary>
public class Conversation
{
    public Guid Id { get; set; }
    public required string ConversationPublicId { get; set; }
    public required string UserObjectId { get; set; }
    public string? Entreprise { get; set; }
    public string? Machine { get; set; }
    public string? AgentName { get; set; }
    /// <summary>
    /// Real FK to <see cref="Machine"/>, source of truth for Claude Direct configuration on new and
    /// resumed conversations. Nullable only for historical rows, which remain readable but cannot be resumed.
    /// </summary>
    public Guid? MachineId { get; set; }
    public string? TechnicalSummary { get; set; }
    public string? TechnicalStateJson { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public ICollection<ConversationMessage> Messages { get; set; } = new List<ConversationMessage>();
}
