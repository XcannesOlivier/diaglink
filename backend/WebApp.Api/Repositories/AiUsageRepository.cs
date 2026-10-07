using Microsoft.EntityFrameworkCore;
using WebApp.Api.Data;
using WebApp.Api.Models;
using WebApp.Api.Models.Entities;

namespace WebApp.Api.Repositories;

public class AiUsageRepository(
    DbContextOptions<DiagLinkDbContext> options,
    ILogger<AiUsageRepository> logger)
{
    public virtual async Task<AiUsageRecordWriteResult> RecordAsync(AiUsageMeasurement measurement, CancellationToken cancellationToken)
    {
        var usage = measurement.Response;
        // Initial local placeholders are not evidence of an initiated provider response.
        if (!usage.Completed && string.IsNullOrWhiteSpace(usage.ResponseId))
            return new(measurement.EventId, AiUsageRecordWriteStatus.Skipped);
        try
        {
            // Never flush failed/pending conversation writes from the request's tracked DbContext.
            await using var db = new DiagLinkDbContext(options);
            if (await db.AiUsageRecords.AnyAsync(r => r.Id == measurement.EventId, cancellationToken))
                return new(measurement.EventId, AiUsageRecordWriteStatus.AlreadyExists);
            db.AiUsageRecords.Add(new AiUsageRecord
            {
                Id = measurement.EventId,
                UsageType = usage.UsageType,
                Available = usage.Available,
                Completed = usage.Completed,
                UserId = measurement.UserId,
                CompanyId = measurement.CompanyId,
                MachineId = measurement.MachineId,
                ConversationId = measurement.SqlConversationId,
                AssistantMessageId = measurement.AssistantMessageId,
                ConversationPublicId = measurement.ConversationPublicId,
                ResponseId = usage.ResponseId,
                CallId = usage.CallId,
                ParentResponseId = usage.ParentResponseId,
                Model = usage.Model,
                Provider = usage.Provider,
                Deployment = usage.Deployment,
                ModelSource = usage.ModelSource,
                CallBreakdownJson = usage.CallBreakdownJson,
                InputTokens = usage.InputTokens,
                OutputTokens = usage.OutputTokens,
                TotalTokens = usage.TotalTokens,
                CacheReadInputTokens = usage.CacheReadInputTokens,
                CacheCreationInputTokens = usage.CacheCreationInputTokens,
                CacheCreation5mInputTokens = usage.CacheCreation5mInputTokens,
                CacheCreation1hInputTokens = usage.CacheCreation1hInputTokens,
                WebSearchRequests = usage.WebSearchRequests,
                CreatedAtUtc = usage.TimestampUtc.UtcDateTime,
            });
            await db.SaveChangesAsync(cancellationToken);
            return new(measurement.EventId, AiUsageRecordWriteStatus.Persisted);
        }
        catch (Exception ex)
        {
            // Includes cancellation/timeouts: usage failure must never replace the chat response.
            logger.LogError(ex, "Failed to persist AI usage event {EventId}, response {ResponseId}, type {UsageType}",
                measurement.EventId, usage.ResponseId, usage.UsageType);
            return new(measurement.EventId, AiUsageRecordWriteStatus.Failed);
        }
    }
}
