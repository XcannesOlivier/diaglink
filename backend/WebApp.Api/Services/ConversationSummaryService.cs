using WebApp.Api.Repositories;
using WebApp.Api.Models;

namespace WebApp.Api.Services;

/// <summary>
/// Bounds conversation context growth: folds older, non-summarized messages into
/// <see cref="WebApp.Api.Models.Entities.Conversation.TechnicalSummary"/> once a threshold is exceeded.
/// Does not touch the live Foundry conversation — no rotation happens here (future step).
/// </summary>
public class ConversationSummaryService
{
    /// <summary>
    /// Number of most-recent non-summarized messages always kept verbatim. Shared (internal) with
    /// <see cref="WebApp.Api.Program"/>, which uses the same count to size the "recent messages"
    /// window injected into the model's context — the two must stay in sync by construction.
    /// </summary>
    internal const int KeepRawMessageCount = 6;

    /// <summary>Fixed number of oldest messages folded into the summary each time the trigger fires.</summary>
    private const int SummaryBatchSize = 6;

    /// <summary>Non-summarized message count that triggers a batch summarization.</summary>
    private const int TriggerCount = KeepRawMessageCount + SummaryBatchSize; // 12

    private readonly ConversationHistoryRepository _historyRepository;
    private readonly AgentFrameworkService _agentService;
    private readonly ILogger<ConversationSummaryService> _logger;

    public ConversationSummaryService(
        ConversationHistoryRepository historyRepository,
        AgentFrameworkService agentService,
        ILogger<ConversationSummaryService> logger)
    {
        _historyRepository = historyRepository;
        _agentService = agentService;
        _logger = logger;
    }

    /// <summary>
    /// Summarizes the oldest <see cref="SummaryBatchSize"/> messages into the conversation's technical
    /// summary once the non-summarized count reaches <see cref="TriggerCount"/>. No-op otherwise —
    /// avoids resummarizing on every message.
    /// </summary>
    public async Task MaybeSummarizeAsync(Guid conversationId, CancellationToken cancellationToken = default,
        Action<AiResponseUsage>? onUsage = null)
    {
        var unsummarizedCount = await _historyRepository.CountUnsummarizedMessagesAsync(conversationId, cancellationToken);
        if (unsummarizedCount < TriggerCount)
        {
            return;
        }

        var unsummarizedMessages = await _historyRepository.GetUnsummarizedMessagesAsync(conversationId, cancellationToken);

        // Oldest-first list; fold a fixed batch of the oldest messages, always keep the rest verbatim.
        var toSummarize = unsummarizedMessages
            .Take(SummaryBatchSize)
            .ToList();

        if (toSummarize.Count == 0)
        {
            return;
        }

        var existingSummary = await _historyRepository.GetTechnicalSummaryAsync(conversationId, cancellationToken);

        // Capture separately before SQL writes, including unknown usage if the model call fails.
        var newSummary = await _agentService.SummarizeConversationAsync(existingSummary, toSummarize, cancellationToken, onUsage);

        await _historyRepository.UpdateTechnicalSummaryAsync(conversationId, newSummary.Text, cancellationToken);
        await _historyRepository.MarkMessagesSummarizedAsync(toSummarize.Select(m => m.Id), cancellationToken);

        _logger.LogInformation(
            "Summarized {Count} older messages into TechnicalSummary for conversation {ConversationId}",
            toSummarize.Count,
            conversationId);
    }
}
