namespace WebApp.Api.Models.Entities;

/// <summary>
/// A persisted DiagLink conversation, correlated to its Foundry-side conversation via <see cref="FoundryConversationId"/>.
/// </summary>
public class Conversation
{
    public Guid Id { get; set; }
    public required string FoundryConversationId { get; set; }
    public required string UserObjectId { get; set; }
    public string? Entreprise { get; set; }
    public string? Machine { get; set; }
    public string? AgentName { get; set; }
    /// <summary>
    /// Real FK to <see cref="Machine"/>, source of truth for agent resolution on new/resumed
    /// conversations. Nullable because pre-existing conversations predate this column (legacy
    /// fallback, see AgentFrameworkService). Set once at creation and never changed afterwards.
    /// </summary>
    public Guid? MachineId { get; set; }
    public string? TechnicalSummary { get; set; }
    public string? TechnicalStateJson { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public ICollection<ConversationMessage> Messages { get; set; } = new List<ConversationMessage>();
}
