namespace WebApp.Api.Models;

public record AiUsageMetricsDto(long EventCount, long ChatResponseCount, long ConversationSummaryCount, long VisionToolCount,
    long KnownUsageCount, long UnknownUsageCount, long CompletedCount, long NotCompletedCount,
    long? InputTokens, long? OutputTokens, long? TotalTokens,
    long? CacheReadInputTokens, long? CacheCreationInputTokens,
    long? CacheCreation5mInputTokens, long? CacheCreation1hInputTokens);
public record AiUsageSummaryDto(DateTimeOffset? From, DateTimeOffset To, string? UsageType,
    AiUsageMetricsDto Metrics, long UnassignedCompanyCount, long UnassignedMachineCount, long UnassignedUserCount);
public record CompanyUsageDto(Guid? CompanyId, string CompanyName, long MachineCountUsed, long UserCountUsed, AiUsageMetricsDto Metrics);
public record MachineUsageDto(Guid? MachineId, string MachineName, long UserCountUsed, AiUsageMetricsDto Metrics);
public record UserUsageDto(Guid? UserId, string UserDisplayName, string? Email, AiUsageMetricsDto Metrics);

public record AiUsageFilter(DateTimeOffset? From, DateTimeOffset To, AiUsageType? UsageType)
{
    public static bool TryParse(string? from, string? to, string? usageType, out AiUsageFilter filter)
    {
        filter = new(null, DateTimeOffset.UtcNow, null);
        bool ParseDate(string value, out DateTimeOffset date)
        {
            date = default;
            return System.Text.RegularExpressions.Regex.IsMatch(value, @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d{1,7})?(Z|[+-]\d{2}:\d{2})$") &&
                DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out date);
        }
        DateTimeOffset start = default, end = default;
        if (from != null && !ParseDate(from, out start)) return false;
        if (to != null && !ParseDate(to, out end)) return false;
        AiUsageType? type = usageType switch {
            null => null, "ChatResponse" => AiUsageType.ChatResponse,
            "VisionTool" => AiUsageType.VisionTool,
            "ConversationSummary" => AiUsageType.ConversationSummary, _ => null };
        if (usageType != null && type == null) return false;
        filter = new(from == null ? null : start.ToUniversalTime(), to == null ? filter.To : end.ToUniversalTime(), type);
        return filter.From == null || filter.From < filter.To;
    }
    public static bool TryScope(string? value, out Guid? id)
    {
        id = null;
        if (value == "unassigned") return true;
        if (!Guid.TryParse(value, out var parsed)) return false;
        id = parsed;
        return true;
    }
}
