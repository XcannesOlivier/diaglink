using System.Runtime.CompilerServices;
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
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        var request = await requestFactory.CreateAsync(
            machine,
            [new ClaudeDirectMessage("user", message)],
            cancellationToken);
        var result = await chatService.CompleteAsync(request, cancellationToken);

        foreach (var toolName in ToolNames(result))
        {
            yield return StreamChunk.ToolUse(toolName);
        }

        if (result.Visuals.Count > 0)
        {
            yield return StreamChunk.WithVisuals(result.Visuals);
        }

        if (!string.IsNullOrWhiteSpace(result.FinalText))
        {
            yield return StreamChunk.Text(result.FinalText);
        }

        var unrecoveredErrors = result.Errors.Where(error => !error.Recovered).ToArray();
        var finalCall = result.Calls.LastOrDefault();
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
                null,
                DateTimeOffset.UtcNow)
            {
                Provider = "Anthropic",
                Deployment = options.Value.Deployment
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

    private static int ToTokenCount(long value) => checked((int)value);
}

public sealed class ClaudeDirectRuntimeException(string message) : Exception(message);
