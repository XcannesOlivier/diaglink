namespace WebApp.Api.Models;

public sealed record ClaudeDirectMachineContext(
    string ProjectEndpoint,
    string ToolboxName,
    string ToolboxVersion,
    string McpEndpoint,
    string VectorStoreId,
    string BlobPrefix,
    string Description);

public sealed record ClaudeDirectUserImage(string MediaType, string Base64Data);

public sealed record ClaudeDirectMessage(
    string Role,
    string Text,
    IReadOnlyList<ClaudeDirectUserImage>? Images = null);

public sealed record ClaudeDirectChatRequest(
    ClaudeDirectMachineContext Machine,
    string SystemPrompt,
    IReadOnlyList<ClaudeDirectMessage> Messages);

public sealed record ClaudeDirectToolUse(
    string Id,
    string Name,
    string InputJson);

public sealed record ClaudeDirectMcpCall(
    string Id,
    string Name,
    bool? IsError);

public enum ClaudeDirectDocumentResolutionMode
{
    Exact,
    UniqueMcpDocument,
    UniqueExistingPage,
    Ambiguous,
    NotFound
}

public sealed record ClaudeDirectDocumentResolution(
    string? RequestedDocumentId,
    string? ResolvedDocumentId,
    ClaudeDirectDocumentResolutionMode ResolutionMode);

public sealed record ClaudeDirectCallUsage(
    int CallNumber,
    long InputTokens,
    long OutputTokens,
    string Model,
    string StopReason,
    string? ResponseId,
    string? RequestId)
{
    public long TotalTokens => checked(InputTokens + OutputTokens);
    public long CacheReadInputTokens { get; init; }
    public long CacheCreationInputTokens { get; init; }
    public long CacheCreation5mInputTokens { get; init; }
    public long CacheCreation1hInputTokens { get; init; }
    public long WebSearchRequests { get; init; }
    public IReadOnlyList<string> Tools { get; init; } = [];
}

public sealed record ClaudeDirectAggregateUsage(
    long InputTokens,
    long OutputTokens,
    long TotalTokens)
{
    public long CacheReadInputTokens { get; init; }
    public long CacheCreationInputTokens { get; init; }
    public long CacheCreation5mInputTokens { get; init; }
    public long CacheCreation1hInputTokens { get; init; }
    public long WebSearchRequests { get; init; }
}

public sealed record ClaudeDirectError(
    string Code,
    string Message,
    int? CallNumber = null,
    string? ToolUseId = null,
    bool Recovered = false);

public sealed record ClaudeDirectWebCitation(
    string Url,
    string? Title,
    string? CitedText);

public sealed record ClaudeDirectChatResult(
    string? FinalText,
    IReadOnlyList<ClaudeDirectToolUse> ToolUses,
    IReadOnlyList<ClaudeDirectMcpCall> McpCalls,
    IReadOnlyList<TechnicalVisualReference> Visuals,
    IReadOnlyList<TechnicalSourceReference> Sources,
    IReadOnlyList<ClaudeDirectDocumentResolution> DocumentResolutions,
    IReadOnlyList<ClaudeDirectCallUsage> Calls,
    ClaudeDirectAggregateUsage Usage,
    string? Model,
    string? StopReason,
    IReadOnlyList<ClaudeDirectError> Errors)
{
    public IReadOnlyList<ClaudeDirectWebCitation> WebCitations { get; init; } = [];
}
