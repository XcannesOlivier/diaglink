using WebApp.Api.Models.Entities;

namespace WebApp.Api.Services;

/// <summary>Computes the fast SQL-backed assistant indicator for the selected transitional runtime.</summary>
public sealed class MachineAssistantReadiness(IAiChatRuntimeSelector runtimeSelector)
{
    public bool IsAssistantConfigured(Machine machine, bool eligible)
    {
        ArgumentNullException.ThrowIfNull(machine);

        if (!eligible || string.IsNullOrWhiteSpace(machine.ProjectEndpoint))
        {
            return false;
        }

        return runtimeSelector.Select(machine) == AiChatRuntime.ClaudeDirect
            ? !string.IsNullOrWhiteSpace(machine.BlobPrefix) &&
              !string.IsNullOrWhiteSpace(machine.VectorStoreId)
            : !string.IsNullOrWhiteSpace(machine.FoundryAgentId);
    }
}
