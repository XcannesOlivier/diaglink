using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Options;
using WebApp.Api.Models;
using WebApp.Api.Models.Entities;

namespace WebApp.Api.Services;

public sealed class ClaudeDirectChatRuntime(
    IClaudeDirectChatRequestFactory requestFactory,
    IClaudeDirectChatService chatService,
    IOptions<ClaudeDirectChatOptions> options) : IClaudeDirectChatRuntime
{
    public async IAsyncEnumerable<StreamChunk> StreamMessageAsync(
        Machine machine,
        string message,
        [EnumeratorCancellation] CancellationToken cancellationToken = default,
        IReadOnlyList<ClaudeDirectUserImage>? userImages = null)
    {
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        var request = await requestFactory.CreateAsync(
            machine,
            [new ClaudeDirectMessage("user", message, userImages)],
            cancellationToken);
        ClaudeDirectChatResult? result = null;
        await foreach (var update in chatService.StreamAsync(request, cancellationToken))
        {
            if (!string.IsNullOrEmpty(update.TextDelta))
            {
                yield return StreamChunk.Text(update.TextDelta);
            }

            if (update.CompletedResult is not null)
            {
                result = update.CompletedResult;
            }
        }

        if (result is null)
        {
            throw new ClaudeDirectRuntimeException("Claude Direct ended without a completed result.");
        }

        foreach (var toolName in ToolNames(result))
        {
            yield return StreamChunk.ToolUse(toolName);
        }

        if (result.Visuals.Count > 0)
        {
            yield return StreamChunk.WithVisuals(result.Visuals);
        }

        if (result.Sources.Count > 0)
        {
            yield return StreamChunk.WithSources(result.Sources);
        }

        if (result.WebCitations.Count > 0)
        {
            yield return StreamChunk.WithAnnotations(result.WebCitations.Select(ToAnnotation).ToList());
        }

        if (result.Suggestions.Count > 0)
        {
            yield return StreamChunk.WithSuggestions(result.Suggestions);
        }

        var unrecoveredErrors = result.Errors.Where(error => !error.Recovered).ToArray();
        var finalCall = result.Calls.LastOrDefault();
        var callBreakdownJson = result.Calls.Count == 0
            ? null
            : JsonSerializer.Serialize(result.Calls.Select(call => new
            {
                callNumber = call.CallNumber,
                inputTokens = call.InputTokens,
                outputTokens = call.OutputTokens,
                cacheReadInputTokens = call.CacheReadInputTokens,
                cacheCreationInputTokens = call.CacheCreationInputTokens,
                cacheCreation5mInputTokens = call.CacheCreation5mInputTokens,
                cacheCreation1hInputTokens = call.CacheCreation1hInputTokens,
                totalTokens = call.TotalTokens,
                model = call.Model,
                stopReason = call.StopReason,
                tools = call.Tools
            }));
        yield return new StreamChunk
        {
            Usage = new AiResponseUsage(
                AiUsageType.ChatResponse,
                finalCall?.ResponseId,
                unrecoveredErrors.Length == 0 && !string.IsNullOrWhiteSpace(result.FinalText),
                ToTokenCount(result.Usage.InputTokens),
                ToTokenCount(result.Usage.OutputTokens),
                ToTokenCount(result.Usage.TotalTokens),
                result.Model,
                "response",
                DateTimeOffset.UtcNow)
            {
                Provider = "Anthropic",
                Deployment = options.Value.Deployment,
                CallBreakdownJson = callBreakdownJson,
                CacheReadInputTokens = ToTokenCount(result.Usage.CacheReadInputTokens),
                CacheCreationInputTokens = ToTokenCount(result.Usage.CacheCreationInputTokens),
                CacheCreation5mInputTokens = ToTokenCount(result.Usage.CacheCreation5mInputTokens),
                CacheCreation1hInputTokens = ToTokenCount(result.Usage.CacheCreation1hInputTokens),
                WebSearchRequests = ToTokenCount(result.Usage.WebSearchRequests)
            }
        };

        if (unrecoveredErrors.Length > 0)
        {
            throw new ClaudeDirectRuntimeException(unrecoveredErrors[0].Message);
        }
    }

    private static IEnumerable<string> ToolNames(ClaudeDirectChatResult result)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        if (result.McpCalls.Any(call => string.Equals(call.Name, "file_search", StringComparison.OrdinalIgnoreCase)) &&
            names.Add("file_search"))
        {
            yield return "file_search";
        }

        foreach (var toolUse in result.ToolUses)
        {
            if (names.Add(toolUse.Name))
            {
                yield return toolUse.Name;
            }
        }
    }

    private static AnnotationInfo ToAnnotation(ClaudeDirectWebCitation citation) => new()
    {
        Type = "uri_citation",
        Label = string.IsNullOrWhiteSpace(citation.Title) ? citation.Url : citation.Title,
        Url = citation.Url,
        Quote = citation.CitedText
    };

    private static int ToTokenCount(long value) => checked((int)value);
}

public sealed class ClaudeDirectRuntimeException(string message) : Exception(message);
