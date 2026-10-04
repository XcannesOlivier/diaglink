using WebApp.Api.Models;
using WebApp.Api.Models.Entities;

namespace WebApp.Api.Services;

public interface IClaudeDirectMachineConfigurationResolver
{
    Task<ClaudeDirectMachineConfiguration> ResolveAsync(
        Machine machine,
        CancellationToken cancellationToken = default);
}
