using WebApp.Api.Models;

namespace WebApp.Api.Services;

public interface ICommercialPolicyProvider
{
    Task<CommercialPolicySnapshot> ResolveAsync(
        Guid companyId,
        CancellationToken cancellationToken = default);
}
