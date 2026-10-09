using WebApp.Api.Models;

namespace WebApp.Api.Services;

public interface IClaudeDirectChatService
{
    Task<ClaudeDirectChatResult> CompleteAsync(
        ClaudeDirectChatRequest request,
        CancellationToken cancellationToken = default);

    async IAsyncEnumerable<ClaudeDirectChatStreamUpdate> StreamAsync(
        ClaudeDirectChatRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var result = await CompleteAsync(request, cancellationToken);
        if (!string.IsNullOrEmpty(result.FinalText))
        {
            yield return ClaudeDirectChatStreamUpdate.Text(result.FinalText);
        }

        yield return ClaudeDirectChatStreamUpdate.Completed(result);
    }
}
