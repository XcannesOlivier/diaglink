using Microsoft.EntityFrameworkCore;
using WebApp.Api.Data;
using WebApp.Api.Models;
using WebApp.Api.Models.Entities;

namespace WebApp.Api.Repositories;

/// <summary>
/// Best-effort persistence of Foundry conversations/messages into the 'chat' schema.
/// Foundry remains the source of truth — callers must catch and log failures, never let them break the chat stream.
/// </summary>
public class ConversationHistoryRepository
{
    private readonly DiagLinkDbContext _db;
    private readonly ILogger<ConversationHistoryRepository> _logger;

    public ConversationHistoryRepository(DiagLinkDbContext db, ILogger<ConversationHistoryRepository> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task CreateConversationAsync(
        string foundryConversationId,
        string userObjectId,
        string? agentName,
        Guid? machineId,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        _db.Conversations.Add(new Conversation
        {
            Id = Guid.NewGuid(),
            FoundryConversationId = foundryConversationId,
            UserObjectId = userObjectId,
            AgentName = agentName,
            MachineId = machineId,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<long?> AddMessageAsync(
        string foundryConversationId,
        string userObjectId,
        string role,
        string content,
        CancellationToken cancellationToken)
    {
        var result = await AddMessageAsync(
            foundryConversationId,
            userObjectId,
            role,
            content,
            Array.Empty<TechnicalVisualReference>(),
            cancellationToken);
        return result?.MessageId;
    }

    public async Task<ConversationMessagePersistenceResult?> AddMessageAsync(
        string foundryConversationId,
        string userObjectId,
        string role,
        string content,
        IReadOnlyList<TechnicalVisualReference> visuals,
        CancellationToken cancellationToken)
    {
        // Ownership check and lookup in the same query — never write to another user's conversation.
        var conversation = await _db.Conversations
            .FirstOrDefaultAsync(
                c => c.FoundryConversationId == foundryConversationId && c.UserObjectId == userObjectId,
                cancellationToken);

        if (conversation is null)
        {
            _logger.LogWarning(
                "No SQL row found for FoundryConversationId {FoundryConversationId} owned by the current user; message not persisted.",
                foundryConversationId);
            return null;
        }

        var now = DateTime.UtcNow;
        var message = new ConversationMessage
        {
            ConversationId = conversation.Id,
            Role = role,
            Content = content,
            IsSummarized = false,
            CreatedAtUtc = now
        };

        var assetKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var visual in visuals)
        {
            if (!assetKeys.Add(visual.AssetKey))
            {
                continue;
            }

            message.Visuals.Add(new ConversationMessageVisual
            {
                DocumentId = visual.DocumentId,
                Page = visual.Page,
                AssetType = visual.AssetType,
                Tile = visual.Tile,
                Name = visual.Name,
                AssetKey = visual.AssetKey,
                DisplayOrder = message.Visuals.Count
            });
        }

        _db.ConversationMessages.Add(message);
        conversation.UpdatedAtUtc = now;

        await _db.SaveChangesAsync(cancellationToken);
        return new ConversationMessagePersistenceResult(
            message.Id,
            message.Visuals
                .OrderBy(visual => visual.DisplayOrder)
                .Select(ToVisualInfo)
                .ToList());
    }

    /// <summary>
    /// Counts messages not yet folded into <see cref="Conversation.TechnicalSummary"/>.
    /// </summary>
    public Task<int> CountUnsummarizedMessagesAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        return _db.ConversationMessages
            .CountAsync(m => m.ConversationId == conversationId && !m.IsSummarized, cancellationToken);
    }

    /// <summary>
    /// Returns not-yet-summarized messages for a conversation, oldest first.
    /// </summary>
    public Task<List<ConversationMessage>> GetUnsummarizedMessagesAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        return _db.ConversationMessages
            .Where(m => m.ConversationId == conversationId && !m.IsSummarized)
            .OrderBy(m => m.CreatedAtUtc)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Flags the given messages as folded into a technical summary.
    /// </summary>
    public Task MarkMessagesSummarizedAsync(IEnumerable<long> messageIds, CancellationToken cancellationToken)
    {
        return _db.ConversationMessages
            .Where(m => messageIds.Contains(m.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.IsSummarized, true), cancellationToken);
    }

    /// <summary>
    /// Updates the conversation's condensed technical summary.
    /// </summary>
    public Task UpdateTechnicalSummaryAsync(Guid conversationId, string technicalSummary, CancellationToken cancellationToken)
    {
        return _db.Conversations
            .Where(c => c.Id == conversationId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.TechnicalSummary, technicalSummary)
                .SetProperty(c => c.UpdatedAtUtc, DateTime.UtcNow), cancellationToken);
    }

    /// <summary>
    /// Returns the conversation's current condensed technical summary, or null if none exists yet.
    /// </summary>
    public Task<string?> GetTechnicalSummaryAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        return _db.Conversations
            .Where(c => c.Id == conversationId)
            .Select(c => c.TechnicalSummary)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// Returns a conversation's condensed technical summary scoped to its owning user in the same
    /// query as the lookup — mirrors <see cref="GetConversationMessagesAsync"/>'s isolation. Returns
    /// null both when the conversation doesn't exist and when it belongs to a different user.
    /// </summary>
    public Task<string?> GetTechnicalSummaryForUserAsync(
        string foundryConversationId,
        string userObjectId,
        CancellationToken cancellationToken)
    {
        return _db.Conversations
            .AsNoTracking()
            .Where(c => c.FoundryConversationId == foundryConversationId && c.UserObjectId == userObjectId)
            .Select(c => c.TechnicalSummary)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// Resolves the SQL <see cref="Conversation.Id"/> for a given Foundry conversation id, or null if no row exists.
    /// </summary>
    public Task<Guid?> GetConversationIdByFoundryIdAsync(string foundryConversationId, CancellationToken cancellationToken)
    {
        return _db.Conversations
            .Where(c => c.FoundryConversationId == foundryConversationId)
            .Select(c => (Guid?)c.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// Resolves a conversation's SQL id and bound MachineId, scoped to its owning user in the same
    /// query as the lookup. Returns null both when the conversation doesn't exist and when it belongs
    /// to a different user (indistinguishable by design — caller must map to 404). MachineId is null
    /// for legacy conversations created before machine-scoped chat existed.
    /// </summary>
    public Task<ConversationOwnershipInfo?> GetConversationOwnershipInfoAsync(
        string foundryConversationId,
        string userObjectId,
        CancellationToken cancellationToken)
    {
        return _db.Conversations
            .AsNoTracking()
            .Where(c => c.FoundryConversationId == foundryConversationId && c.UserObjectId == userObjectId)
            .Select(c => new ConversationOwnershipInfo(c.Id, c.MachineId))
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// Lists a user's conversations (most recent first), with a display title derived from each
    /// conversation's first user message. Strictly scoped to <paramref name="userObjectId"/> —
    /// never returns another user's conversations. When <paramref name="machineId"/> is supplied,
    /// only conversations bound to that machine are returned. The returned <c>Id</c> is the
    /// FoundryConversationId, matching the identifier the frontend already treats as its conversationId.
    /// </summary>
    public async Task<List<ConversationSummary>> ListConversationsForUserAsync(
        string userObjectId,
        Guid? machineId,
        CancellationToken cancellationToken)
    {
        var query = _db.Conversations
            .AsNoTracking()
            .Where(c => c.UserObjectId == userObjectId);

        if (machineId.HasValue)
        {
            query = query.Where(c => c.MachineId == machineId.Value);
        }

        var conversations = await query
            .OrderByDescending(c => c.CreatedAtUtc)
            .Select(c => new { c.Id, c.FoundryConversationId, c.CreatedAtUtc, c.MachineId })
            .ToListAsync(cancellationToken);

        if (conversations.Count == 0)
        {
            return new List<ConversationSummary>();
        }

        var conversationIds = conversations.Select(c => c.Id).ToList();

        // Oldest-first per-conversation user messages, grouped in-memory to pick each conversation's first one.
        var firstUserMessageByConversation = (await _db.ConversationMessages
            .AsNoTracking()
            .Where(m => conversationIds.Contains(m.ConversationId) && m.Role == "user")
            .OrderBy(m => m.CreatedAtUtc)
            .Select(m => new { m.ConversationId, m.Content })
            .ToListAsync(cancellationToken))
            .GroupBy(m => m.ConversationId)
            .ToDictionary(g => g.Key, g => g.First().Content);

        var machineIds = conversations.Where(c => c.MachineId.HasValue).Select(c => c.MachineId!.Value).Distinct().ToList();
        var machineNamesById = machineIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.Machines
                .AsNoTracking()
                .Where(m => machineIds.Contains(m.Id))
                .Select(m => new { m.Id, m.Name })
                .ToDictionaryAsync(m => m.Id, m => m.Name, cancellationToken);

        return conversations
            .Select(c => new ConversationSummary
            {
                Id = c.FoundryConversationId,
                Title = BuildTitle(firstUserMessageByConversation.GetValueOrDefault(c.Id)),
                CreatedAt = new DateTimeOffset(c.CreatedAtUtc, TimeSpan.Zero).ToUnixTimeSeconds(),
                MachineId = c.MachineId?.ToString(),
                MachineName = c.MachineId.HasValue ? machineNamesById.GetValueOrDefault(c.MachineId.Value) : null
            })
            .ToList();
    }

    /// <summary>
    /// Loads a conversation's messages in chronological order, scoped to the owning user in the
    /// same query as the conversation lookup. Returns null when no conversation matches — either
    /// because it doesn't exist or because it belongs to a different user (indistinguishable by design).
    /// </summary>
    public async Task<List<ConversationMessageInfo>?> GetConversationMessagesAsync(
        string foundryConversationId,
        string userObjectId,
        CancellationToken cancellationToken)
    {
        var conversation = await _db.Conversations
            .AsNoTracking()
            .Include(c => c.Messages.OrderBy(m => m.CreatedAtUtc))
            .ThenInclude(m => m.Visuals.OrderBy(v => v.DisplayOrder))
            .FirstOrDefaultAsync(
                c => c.FoundryConversationId == foundryConversationId && c.UserObjectId == userObjectId,
                cancellationToken);

        if (conversation is null)
        {
            return null;
        }

        return conversation.Messages
            .Select(m => new ConversationMessageInfo
            {
                Role = m.Role,
                Content = m.Content,
                Visuals = m.Visuals
                    .OrderBy(v => v.DisplayOrder)
                    .Select(ToVisualInfo)
                    .ToList()
            })
            .ToList();
    }

    private static ConversationMessageVisualInfo ToVisualInfo(ConversationMessageVisual visual) => new()
    {
        Id = visual.Id,
        DocumentId = visual.DocumentId,
        Page = visual.Page,
        AssetType = visual.AssetType,
        Tile = visual.Tile,
        Name = visual.Name,
        DisplayOrder = visual.DisplayOrder
    };

    /// <summary>
    /// Returns the most recent non-summarized messages for a conversation, scoped to its owning user
    /// in the same query as the lookup (never returns another user's messages). Selects the
    /// <paramref name="limit"/> most recent messages (by <see cref="ConversationMessage.CreatedAtUtc"/>,
    /// then <see cref="ConversationMessage.Id"/> for determinism), then returns them oldest-first —
    /// ready to render straight into a prompt.
    /// </summary>
    public async Task<List<ConversationMessage>> GetRecentUnsummarizedMessagesForUserAsync(
        string foundryConversationId,
        string userObjectId,
        int limit,
        CancellationToken cancellationToken)
    {
        var mostRecentFirst = await _db.ConversationMessages
            .AsNoTracking()
            .Where(m => m.Conversation!.FoundryConversationId == foundryConversationId
                     && m.Conversation.UserObjectId == userObjectId
                     && !m.IsSummarized)
            .OrderByDescending(m => m.CreatedAtUtc)
            .ThenByDescending(m => m.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);

        mostRecentFirst.Reverse();
        return mostRecentFirst;
    }

    /// <summary>
    /// Builds a display title from a conversation's first user message, truncated the same way
    /// conversation titles were previously derived from the raw first message text.
    /// </summary>
    private static string? BuildTitle(string? firstUserMessageContent)
    {
        if (string.IsNullOrEmpty(firstUserMessageContent))
        {
            return null;
        }

        return firstUserMessageContent.Length > 50
            ? firstUserMessageContent[..50] + "..."
            : firstUserMessageContent;
    }
}
