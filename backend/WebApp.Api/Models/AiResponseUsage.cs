namespace WebApp.Api.Models;

public enum AiUsageType { ChatResponse, ConversationSummary, VisionTool }

/// <summary>One provider response, never a total across retries, tools or summaries.</summary>
public record AiResponseUsage(
    AiUsageType UsageType,
    string? ResponseId,
    bool Completed,
    int? InputTokens,
    int? OutputTokens,
    int? TotalTokens,
    string? Model,
    string? ModelSource,
    string? AgentVersion,
    DateTimeOffset TimestampUtc)
{
    public string? CallId { get; init; }
    public string? ParentResponseId { get; init; }
    public string? Provider { get; init; }
    public string? Deployment { get; init; }

    public bool Available => InputTokens.HasValue && OutputTokens.HasValue && TotalTokens.HasValue;
}

/// <summary>Server-side attribution only. Null IDs explicitly represent unavailable SQL context.</summary>
public record AiUsageMeasurement(
    Guid? UserId,
    Guid? CompanyId,
    Guid? MachineId,
    Guid? SqlConversationId,
    string FoundryConversationId,
    long? AssistantMessageId,
    AiResponseUsage Response)
{
    // Stable through 'with' updates, distinct for each actual provider call/attempt.
    public Guid EventId { get; init; } = Guid.NewGuid();
}

public record AiSummaryResult(string Text, AiResponseUsage Usage);

public record VisionUsageCapture(Guid EventId, AiResponseUsage Usage)
{
    public AiUsageMeasurement WithContext(AiUsageMeasurement context) =>
        context with { EventId = EventId, AssistantMessageId = null, Response = Usage };
}
