namespace WebApp.Api.Models;

public enum AiUsageType
{
    ChatResponse,
    ConversationSummary,
    // Historical read-only value. New Claude Direct code must not emit this usage type.
    VisionTool
}

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
    DateTimeOffset TimestampUtc)
{
    public string? CallId { get; init; }
    public string? ParentResponseId { get; init; }
    public string? Provider { get; init; }
    public string? Deployment { get; init; }
    public string? CallBreakdownJson { get; init; }
    public int? CacheReadInputTokens { get; init; }
    public int? CacheCreationInputTokens { get; init; }
    public int? CacheCreation5mInputTokens { get; init; }
    public int? CacheCreation1hInputTokens { get; init; }
    public int WebSearchRequests { get; init; }

    public bool Available => InputTokens.HasValue && OutputTokens.HasValue && TotalTokens.HasValue;
}

/// <summary>Server-side attribution only. Null IDs explicitly represent unavailable SQL context.</summary>
public record AiUsageMeasurement(
    Guid? UserId,
    Guid? CompanyId,
    Guid? MachineId,
    Guid? SqlConversationId,
    string ConversationPublicId,
    long? AssistantMessageId,
    AiResponseUsage Response)
{
    // Stable through 'with' updates, distinct for each actual provider call/attempt.
    public Guid EventId { get; init; } = Guid.NewGuid();
}

public record AiSummaryResult(string Text, AiResponseUsage Usage);
