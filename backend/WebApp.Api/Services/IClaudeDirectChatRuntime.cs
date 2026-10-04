using WebApp.Api.Models;
using WebApp.Api.Models.Entities;

namespace WebApp.Api.Services;

public interface IClaudeDirectChatRuntime
{
    IAsyncEnumerable<StreamChunk> StreamMessageAsync(
        Machine machine,
        string message,
        CancellationToken cancellationToken = default);
}
