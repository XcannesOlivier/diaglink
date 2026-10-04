using WebApp.Api.Models;
using WebApp.Api.Models.Entities;

namespace WebApp.Api.Services;

public sealed class ClaudeDirectChatRequestFactory(
    IClaudeDirectMachineConfigurationResolver resolver)
    : IClaudeDirectChatRequestFactory
{
    public async Task<ClaudeDirectChatRequest> CreateAsync(
        Machine machine,
        IReadOnlyList<ClaudeDirectMessage> messages,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(messages);

        var configuration = await resolver.ResolveAsync(machine, cancellationToken);
        return new(
            new(
                configuration.ProjectEndpoint,
                configuration.ToolboxName,
                configuration.ToolboxVersion,
                configuration.McpEndpoint,
                configuration.VectorStoreId,
                configuration.BlobPrefix,
                configuration.Description),
            configuration.SystemPrompt,
            messages);
    }
}
