using WebApp.Api.Models;
using WebApp.Api.Models.Entities;

namespace WebApp.Api.Services;

public interface IConversationSummarizer
{
    Task<AiSummaryResult> SummarizeConversationAsync(
        string? existingSummary,
        IReadOnlyList<ConversationMessage> oldMessages,
        CancellationToken cancellationToken = default,
        Action<AiResponseUsage>? onUsage = null);
}
