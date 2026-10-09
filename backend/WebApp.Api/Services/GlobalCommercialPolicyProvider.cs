using Microsoft.Extensions.Options;
using WebApp.Api.Models;

namespace WebApp.Api.Services;

public sealed class GlobalCommercialPolicyProvider(
    IOptions<CommercialPolicyOptions> options) : ICommercialPolicyProvider
{
    public Task<CommercialPolicySnapshot> ResolveAsync(
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var configured = options.Value;
        var instructions = configured.Instructions?
            .Where(instruction => !string.IsNullOrWhiteSpace(instruction))
            .Select(instruction => instruction.Trim())
            .ToArray() ?? [];

        var snapshot = configured.Enabled && instructions.Length > 0
            ? new CommercialPolicySnapshot(true, string.Join('\n', instructions))
            : new CommercialPolicySnapshot(false, string.Empty);

        return Task.FromResult(snapshot);
    }
}
