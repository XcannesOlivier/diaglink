using Microsoft.Extensions.Options;
using WebApp.Api.Models;
using WebApp.Api.Models.Entities;

namespace WebApp.Api.Services;

/// <summary>Builds lightweight Claude Direct metadata exclusively from local machine/configuration data.</summary>
public sealed class ClaudeDirectAgentMetadataFactory(IOptions<ClaudeDirectChatOptions> options)
{
    private const string DefaultDescription =
        "Votre support pour diagnostiquer, localiser et intervenir plus vite";

    public AgentMetadataResponse Create(Machine machine)
    {
        ArgumentNullException.ThrowIfNull(machine);

        return new AgentMetadataResponse
        {
            Id = $"claude-direct-{machine.Id:N}",
            Object = "agent",
            CreatedAt = new DateTimeOffset(
                DateTime.SpecifyKind(machine.CreatedAtUtc, DateTimeKind.Utc)).ToUnixTimeSeconds(),
            Name = $"Assistant-Technique-{machine.Name}",
            Description = DefaultDescription,
            Model = options.Value.Deployment,
            Instructions = null,
            Metadata = new Dictionary<string, string> { ["logo"] = "Avatar_Default.svg" },
            StarterPrompts = null,
            Capabilities = AgentCapabilities.ClaudeDirect
        };
    }
}
