using WebApp.Api.Models;
using WebApp.Api.Repositories;

namespace WebApp.Api.Services;

/// <summary>Observed best-effort post-stream persistence and billing; no background tasks.</summary>
public sealed class AiUsagePersistenceBillingService(AiUsageRepository repository,
    IAiUsageBillingOrchestrator orchestrator, ILogger<AiUsagePersistenceBillingService> logger)
{
    public async Task ProcessAsync(IEnumerable<AiUsageMeasurement> measurements)
    {
        var persisted = new List<(AiUsageMeasurement Measurement, Guid Id)>();
        // Persist all technical usages before a slow financial call can delay subsequent usage writes.
        foreach (var measurement in measurements)
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var write = await repository.RecordAsync(measurement, timeout.Token);
                if (write.Status is AiUsageRecordWriteStatus.Persisted or AiUsageRecordWriteStatus.AlreadyExists)
                    persisted.Add((measurement, write.UsageRecordId));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "AI usage persistence failed UsageRecordId={UsageRecordId} UsageType={UsageType} CompanyId={CompanyId} MachineId={MachineId}",
                    measurement.EventId, measurement.Response.UsageType, measurement.CompanyId, measurement.MachineId);
            }
        }
        foreach (var (measurement, id) in persisted)
        {
            try
            {
                // Independent of request disconnect and of the persistence timeout.
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                var result = await orchestrator.ProcessAsync(id, timeout.Token);
                var level = result.Status == "DataInconsistency" ? LogLevel.Error : result.Success ? LogLevel.Information : LogLevel.Warning;
                logger.Log(level, "AI usage billing UsageRecordId={UsageRecordId} UsageType={UsageType} CompanyId={CompanyId} MachineId={MachineId} BillingStatus={BillingStatus} FailureReason={FailureReason} RealAiCostEur={RealAiCostEur} MachineCoveredRealAiCostEur={MachineCoveredRealAiCostEur} WalletCoveredRealAiCostEur={WalletCoveredRealAiCostEur} RemainingRealAiCostEur={RemainingRealAiCostEur}",
                    id, measurement.Response.UsageType, result.CompanyId ?? measurement.CompanyId, result.MachineId ?? measurement.MachineId,
                    result.Status, result.FailureReason, result.RealAiCostEur, result.MachineCoveredRealAiCostEur,
                    result.WalletCoveredRealAiCostEur, result.RemainingRealAiCostEur);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "AI usage billing failed UsageRecordId={UsageRecordId} UsageType={UsageType} CompanyId={CompanyId} MachineId={MachineId} BillingStatus={BillingStatus}",
                    id, measurement.Response.UsageType, measurement.CompanyId, measurement.MachineId, "TechnicalFailure");
            }
        }
    }
}
