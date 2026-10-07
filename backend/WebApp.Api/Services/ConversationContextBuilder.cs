using System.Text;
using WebApp.Api.Models.Entities;

namespace WebApp.Api.Services;

/// <summary>
/// Builds the full text sent to the Foundry agent from SQL alone: the conversation's condensed
/// <see cref="Conversation.TechnicalSummary"/>, its most recent non-summarized messages, and the
/// current question. SQL is the only source of conversational memory here — Foundry itself is
/// called statelessly by the Claude Direct runtime and never
/// supplies history back into this context. Never persisted to SQL and never shown to the user —
/// the raw <c>request.Message</c> is what gets saved and displayed.
/// </summary>
public static class ConversationContextBuilder
{
    private const string MemoryUsageInstructions =
        "La mémoire technique et les échanges précédents servent uniquement de contexte conversationnel.\n" +
        "Les informations documentaires vérifiables doivent être confirmées via les documents/file search.\n" +
        "En cas de contradiction, la documentation retrouvée fait foi.";

    public static string BuildMessage(
        string? technicalSummary,
        IReadOnlyList<ConversationMessage> recentMessages,
        string currentQuestion)
    {
        var hasSummary = !string.IsNullOrWhiteSpace(technicalSummary);
        var hasRecent = recentMessages.Count > 0;

        // Nothing to inject yet (e.g. brand-new conversation) — behave exactly as before.
        if (!hasSummary && !hasRecent)
        {
            return currentQuestion;
        }

        var sb = new StringBuilder();
        sb.AppendLine(MemoryUsageInstructions);
        sb.AppendLine();

        if (hasSummary)
        {
            sb.AppendLine("[MEMOIRE TECHNIQUE]");
            sb.AppendLine(technicalSummary);
            sb.AppendLine("[/MEMOIRE TECHNIQUE]");
            sb.AppendLine();
        }

        if (hasRecent)
        {
            sb.AppendLine("[DERNIERS ECHANGES]");
            foreach (var m in recentMessages)
            {
                var label = string.Equals(m.Role, "assistant", StringComparison.OrdinalIgnoreCase)
                    ? "Assistant"
                    : "Utilisateur";
                sb.AppendLine($"{label}: {m.Content}");
            }
            sb.AppendLine("[/DERNIERS ECHANGES]");
            sb.AppendLine();
        }

        sb.AppendLine("[NOUVELLE QUESTION]");
        sb.AppendLine(currentQuestion);
        sb.Append("[/NOUVELLE QUESTION]");

        return sb.ToString();
    }
}
