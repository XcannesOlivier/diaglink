namespace WebApp.Api.Models.Entities;

/// <summary>One AI event. Historical references intentionally have no cascading foreign keys.</summary>
public class AiUsageRecord
{
    public Guid Id { get; set; }
    public AiUsageType UsageType { get; set; }
    public bool Available { get; set; }
    public bool Completed { get; set; }
    public Guid? UserId { get; set; }
    public Guid? CompanyId { get; set; }
    public Guid? MachineId { get; set; }
    public Guid? ConversationId { get; set; }
    public long? AssistantMessageId { get; set; }
    public string? ConversationPublicId { get; set; }
    public string? ResponseId { get; set; }
    public string? CallId { get; set; }
    public string? ParentResponseId { get; set; }
    public string? Model { get; set; }
    public string? Provider { get; set; }
    public string? Deployment { get; set; }
    public string? ModelSource { get; set; }
    public string? CallBreakdownJson { get; set; }
    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public int? TotalTokens { get; set; }
    public int? CacheReadInputTokens { get; set; }
    public int? CacheCreationInputTokens { get; set; }
    public int? CacheCreation5mInputTokens { get; set; }
    public int? CacheCreation1hInputTokens { get; set; }
    public int WebSearchRequests { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
