using WebApp.Api.Models;
namespace WebApp.Api.Services;

public interface IAiUsageBillingOrchestrator
{
    Task<AiUsageBillingResult> ProcessAsync(Guid usageRecordId, CancellationToken ct = default);
}
