using WebApp.Api.Models;

namespace WebApp.Api.Services;

public interface IClaudeDirectChatService
{
    Task<ClaudeDirectChatResult> CompleteAsync(
        ClaudeDirectChatRequest request,
        CancellationToken cancellationToken = default);
}
