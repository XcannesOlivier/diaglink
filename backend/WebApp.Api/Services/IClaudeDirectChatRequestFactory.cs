using WebApp.Api.Models;
using WebApp.Api.Models.Entities;

namespace WebApp.Api.Services;

public interface IClaudeDirectChatRequestFactory
{
    Task<ClaudeDirectChatRequest> CreateAsync(
        Machine machine,
        IReadOnlyList<ClaudeDirectMessage> messages,
        CancellationToken cancellationToken = default);
}
