using Microsoft.Extensions.Options;
using WebApp.Api.Models;
using WebApp.Api.Models.Entities;

namespace WebApp.Api.Services;

public enum AiChatRuntime
{
    HostedAgent,
    ClaudeDirect
}

public interface IAiChatRuntimeSelector
{
    AiChatRuntime Select(Machine? resolvedMachine);
}

public sealed class AiChatRuntimeSelector : IAiChatRuntimeSelector
{
    private readonly HashSet<Guid> enabledMachineIds;

    public AiChatRuntimeSelector(IOptions<ClaudeDirectChatOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        enabledMachineIds = new HashSet<Guid>(options.Value.EnabledMachineIds ?? []);
    }

    public AiChatRuntime Select(Machine? resolvedMachine) =>
        resolvedMachine is not null && enabledMachineIds.Contains(resolvedMachine.Id)
            ? AiChatRuntime.ClaudeDirect
            : AiChatRuntime.HostedAgent;
}

internal static class AiChatRuntimeDispatch
{
    internal static IAsyncEnumerable<StreamChunk> SelectStream(
        AiChatRuntime runtime,
        Func<IAsyncEnumerable<StreamChunk>> hostedAgent,
        Func<IAsyncEnumerable<StreamChunk>> claudeDirect) =>
        runtime == AiChatRuntime.ClaudeDirect ? claudeDirect() : hostedAgent();
}
