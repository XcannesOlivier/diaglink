namespace WebApp.Api.Models;

public enum AiUsageRecordWriteStatus { Persisted, AlreadyExists, Failed, Skipped }
public record AiUsageRecordWriteResult(Guid UsageRecordId, AiUsageRecordWriteStatus Status);
