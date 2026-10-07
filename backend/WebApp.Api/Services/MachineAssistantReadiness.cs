using WebApp.Api.Models.Entities;

namespace WebApp.Api.Services;

/// <summary>Computes the fast SQL-backed Claude Direct assistant indicator.</summary>
public sealed class MachineAssistantReadiness
{
    public bool IsAssistantConfigured(Machine machine, bool eligible)
    {
        ArgumentNullException.ThrowIfNull(machine);

        if (!eligible || string.IsNullOrWhiteSpace(machine.ProjectEndpoint))
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(machine.BlobPrefix) &&
               !string.IsNullOrWhiteSpace(machine.VectorStoreId);
    }
}
