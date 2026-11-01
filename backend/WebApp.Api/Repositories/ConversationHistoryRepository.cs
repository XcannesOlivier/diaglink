using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text.Json;
using WebApp.Api.Data;
using WebApp.Api.Models;
using WebApp.Api.Models.Entities;
using WebApp.Api.Services;

namespace WebApp.Api.Repositories;

/// <summary>
/// Best-effort persistence of Foundry conversations/messages into the 'chat' schema.
/// Foundry remains the source of truth â€” callers must catch and log failures, never let them break the chat stream.
/// </summary>
public class ConversationHistoryRepository
{
    private readonly DiagLinkDbContext _db;
    private readonly ILogger<ConversationHistoryRepository> _logger;
    private readonly TechnicalPageMapResolver? _pageMapResolver;

    public ConversationHistoryRepository(
        DiagLinkDbContext db,
        ILogger<ConversationHistoryRepository> logger,
        TechnicalPageMapResolver? pageMapResolver = null)
    {
        _db = db;
        _logger = logger;
        _pageMapResolver = pageMapResolver;
    }

    public async Task CreateConversationAsync(
        string conversationPublicId,
        string userObjectId,
        string? agentName,
        Guid? machineId,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        _db.Conversations.Add(new Conversation
        {
            Id = Guid.NewGuid(),
            ConversationPublicId = conversationPublicId,
            UserObjectId = userObjectId,
            AgentName = agentName,
            MachineId = machineId,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<long?> AddMessageAsync(
        string conversationPublicId,
        string userObjectId,
        string role,
        string content,
        CancellationToken cancellationToken)
    {
        var result = await AddMessageAsync(
            conversationPublicId,
            userObjectId,
            role,
            content,
            Array.Empty<TechnicalVisualReference>(),
            Array.Empty<TechnicalSourceReference>(),
            Array.Empty<string>(),
            Array.Empty<AnnotationInfo>(),
            cancellationToken);
        return result?.MessageId;
    }

    public async Task<ConversationMessagePersistenceResult?> AddMessageAsync(
        string conversationPublicId,
        string userObjectId,
        string role,
        string content,
        IReadOnlyList<TechnicalVisualReference> visuals,
        CancellationToken cancellationToken) =>
        await AddMessageAsync(
            conversationPublicId,
            userObjectId,
            role,
            content,
            visuals,
            Array.Empty<TechnicalSourceReference>(),
            Array.Empty<string>(),
            Array.Empty<AnnotationInfo>(),
            cancellationToken);

    public async Task<ConversationMessagePersistenceResult?> AddMessageAsync(
        string conversationPublicId,
        string userObjectId,
        string role,
        string content,
        IReadOnlyList<TechnicalVisualReference> visuals,
        IReadOnlyList<TechnicalSourceReference> sources,
        CancellationToken cancellationToken) =>
        await AddMessageAsync(
            conversationPublicId,
            userObjectId,
            role,
            content,
            visuals,
            sources,
            Array.Empty<string>(),
            Array.Empty<AnnotationInfo>(),
            cancellationToken);

    public async Task<ConversationMessagePersistenceResult?> AddMessageAsync(
        string conversationPublicId,
        string userObjectId,
        string role,
        string content,
        IReadOnlyList<TechnicalVisualReference> visuals,
        IReadOnlyList<TechnicalSourceReference> sources,
        IReadOnlyList<string> suggestions,
        CancellationToken cancellationToken) =>
        await AddMessageAsync(
            conversationPublicId,
            userObjectId,
            role,
            content,
            visuals,
            sources,
            suggestions,
            Array.Empty<AnnotationInfo>(),
            cancellationToken);

    public async Task<ConversationMessagePersistenceResult?> AddMessageAsync(
        string conversationPublicId,
        string userObjectId,
        string role,
        string content,
        IReadOnlyList<TechnicalVisualReference> visuals,
        IReadOnlyList<TechnicalSourceReference> sources,
        IReadOnlyList<string> suggestions,
        IReadOnlyList<AnnotationInfo> webCitations,
        CancellationToken cancellationToken)
    {
        // Ownership check and lookup in the same query â€” never write to another user's conversation.
        var conversation = await _db.Conversations
            .FirstOrDefaultAsync(
                c => c.ConversationPublicId == conversationPublicId && c.UserObjectId == userObjectId,
                cancellationToken);

        if (conversation is null)
        {
            _logger.LogWarning(
                "No SQL row found for ConversationPublicId {ConversationPublicId} owned by the current user; message not persisted.",
                conversationPublicId);
            return null;
        }

        var now = DateTime.UtcNow;
        var message = new ConversationMessage
        {
            ConversationId = conversation.Id,
            Role = role,
            Content = content,
            SuggestionsJson = suggestions.Count == 0 ? null : JsonSerializer.Serialize(suggestions),
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

        foreach (var source in sources.OrderBy(source => source.DisplayOrder))
        {
            message.Sources.Add(new ConversationMessageSourceReference
            {
                DocumentId = source.DocumentId,
                PdfPage = source.PdfPage,
                DisplayPage = TechnicalSourcePageStorage.Store(source),
                Label = source.Label,
                StartIndex = source.StartIndex,
                EndIndex = source.EndIndex,
                DisplayOrder = message.Sources.Count
            });
        }

        var citationUrls = new HashSet<string>(StringComparer.Ordinal);
        foreach (var citation in webCitations)
        {
            if (!string.Equals(citation.Type, "uri_citation", StringComparison.Ordinal) ||
                !TryWebUri(citation.Url, out var citationUri) ||
                citationUri.AbsoluteUri.Length > 2048 ||
                !citationUrls.Add(citationUri.AbsoluteUri))
            {
                continue;
            }

            message.WebCitations.Add(new ConversationMessageWebCitation
            {
                Url = citationUri.AbsoluteUri,
                Label = Truncate(string.IsNullOrWhiteSpace(citation.Label) ? citationUri.AbsoluteUri : citation.Label, 512),
                Quote = string.IsNullOrWhiteSpace(citation.Quote) ? null : Truncate(citation.Quote, 2000),
                DisplayOrder = message.WebCitations.Count
            });
        }

        _db.ConversationMessages.Add(message);
        conversation.UpdatedAtUtc = now;

        await _db.SaveChangesAsync(cancellationToken);
        var orderedSources = sources.OrderBy(source => source.DisplayOrder).ToArray();
        return new ConversationMessagePersistenceResult(
            message.Id,
            message.Visuals
                .OrderBy(visual => visual.DisplayOrder)
                .Select(ToVisualInfo)
                .ToList(),
            message.Sources
                .OrderBy(source => source.DisplayOrder)
                .Select((source, index) => ToSourceInfo(
                    source,
                    index < orderedSources.Length ? orderedSources[index].DocumentName : source.DocumentId))
                .ToList(),
            message.WebCitations
                .OrderBy(citation => citation.DisplayOrder)
                .Select(ToWebCitationInfo)
                .ToList(),
            suggestions.ToArray());
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
    /// query as the lookup â€” mirrors <see cref="GetConversationMessagesAsync"/>'s isolation. Returns
    /// null both when the conversation doesn't exist and when it belongs to a different user.
    /// </summary>
    public Task<string?> GetTechnicalSummaryForUserAsync(
        string conversationPublicId,
        string userObjectId,
        CancellationToken cancellationToken)
    {
        return _db.Conversations
            .AsNoTracking()
            .Where(c => c.ConversationPublicId == conversationPublicId && c.UserObjectId == userObjectId)
            .Select(c => c.TechnicalSummary)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// Resolves the SQL <see cref="Conversation.Id"/> for a given Foundry conversation id, or null if no row exists.
    /// </summary>
    public Task<Guid?> GetConversationIdByPublicIdAsync(string conversationPublicId, CancellationToken cancellationToken)
    {
        return _db.Conversations
            .Where(c => c.ConversationPublicId == conversationPublicId)
            .Select(c => (Guid?)c.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// Resolves a conversation's SQL id and bound MachineId, scoped to its owning user in the same
    /// query as the lookup. Returns null both when the conversation doesn't exist and when it belongs
    /// to a different user (indistinguishable by design â€” caller must map to 404). MachineId is null
    /// for legacy conversations created before machine-scoped chat existed.
    /// </summary>
    public Task<ConversationOwnershipInfo?> GetConversationOwnershipInfoAsync(
        string conversationPublicId,
        string userObjectId,
        CancellationToken cancellationToken)
    {
        return _db.Conversations
            .AsNoTracking()
            .Where(c => c.ConversationPublicId == conversationPublicId && c.UserObjectId == userObjectId)
            .Select(c => new ConversationOwnershipInfo(c.Id, c.MachineId))
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// Returns only durable source metadata from the requested conversation, owner, and bound machine.
    /// Free-form message text is deliberately excluded from documentary evidence.
    /// </summary>
    public async Task<IReadOnlyList<HistoricalTechnicalSourceEvidence>> GetHistoricalTechnicalSourceEvidenceAsync(
        string conversationPublicId,
        string userObjectId,
        Guid machineId,
        CancellationToken cancellationToken)
    {
        var stored = await _db.ConversationMessageSourceReferences
            .AsNoTracking()
            .Where(source =>
                source.ConversationMessage!.Role == "assistant" &&
                source.ConversationMessage.Conversation!.ConversationPublicId == conversationPublicId &&
                source.ConversationMessage.Conversation.UserObjectId == userObjectId &&
                source.ConversationMessage.Conversation.MachineId == machineId)
            .Select(source => new
            {
                source.DocumentId,
                source.PdfPage,
                source.DisplayPage
            })
            .Distinct()
            .ToListAsync(cancellationToken);

        return stored
            .Select(source => TechnicalSourcePageStorage.TryReadPdfPage(source.DisplayPage, out var pdfPage)
                ? new HistoricalTechnicalSourceEvidence(
                    source.DocumentId,
                    source.PdfPage,
                    pdfPage.ToString(CultureInfo.InvariantCulture),
                    TechnicalSourcePageKind.Pdf,
                    TechnicalSourcePageStorage.ReadVersionToken(source.DisplayPage))
                : new HistoricalTechnicalSourceEvidence(
                    source.DocumentId,
                    source.PdfPage,
                    TechnicalSourcePageStorage.PublicDisplayPage(source.DisplayPage),
                    TechnicalSourcePageKind.Display,
                    TechnicalSourcePageStorage.ReadVersionToken(source.DisplayPage)))
            .Distinct()
            .ToArray();
    }

    /// <summary>
    /// Lists a user's conversations (most recent first), with a display title derived from each
    /// conversation's first user message. Strictly scoped to <paramref name="userObjectId"/> â€”
    /// never returns another user's conversations. When <paramref name="machineId"/> is supplied,
    /// only conversations bound to that machine are returned. The returned <c>Id</c> is the
    /// ConversationPublicId, matching the identifier the frontend already treats as its conversationId.
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
            .Select(c => new
            {
                c.Id,
                c.ConversationPublicId,
                c.CreatedAtUtc,
                LastActivityAtUtc = c.Messages
                    .Select(m => (DateTime?)m.CreatedAtUtc)
                    .Max() ?? c.CreatedAtUtc,
                c.MachineId
            })
            .OrderByDescending(c => c.LastActivityAtUtc)
            .ThenByDescending(c => c.Id)
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
                Id = c.ConversationPublicId,
                Title = BuildTitle(firstUserMessageByConversation.GetValueOrDefault(c.Id)),
                CreatedAt = new DateTimeOffset(c.CreatedAtUtc, TimeSpan.Zero).ToUnixTimeSeconds(),
                LastActivityAt = new DateTimeOffset(c.LastActivityAtUtc, TimeSpan.Zero).ToUnixTimeSeconds(),
                MachineId = c.MachineId?.ToString(),
                MachineName = c.MachineId.HasValue ? machineNamesById.GetValueOrDefault(c.MachineId.Value) : null
            })
            .ToList();
    }

    /// <summary>
    /// Loads a conversation's messages in chronological order, scoped to the owning user in the
    /// same query as the conversation lookup. Returns null when no conversation matches â€” either
    /// because it doesn't exist or because it belongs to a different user (indistinguishable by design).
    /// </summary>
    public async Task<List<ConversationMessageInfo>?> GetConversationMessagesAsync(
        string conversationPublicId,
        string userObjectId,
        CancellationToken cancellationToken)
    {
        var conversation = await _db.Conversations
            .AsNoTracking()
            .AsSplitQuery()
            .Include(c => c.Messages.OrderBy(m => m.CreatedAtUtc))
            .ThenInclude(m => m.Visuals.OrderBy(v => v.DisplayOrder))
            .Include(c => c.Messages.OrderBy(m => m.CreatedAtUtc))
            .ThenInclude(m => m.Sources.OrderBy(source => source.DisplayOrder))
            .Include(c => c.Messages.OrderBy(m => m.CreatedAtUtc))
            .ThenInclude(m => m.WebCitations.OrderBy(citation => citation.DisplayOrder))
            .FirstOrDefaultAsync(
                c => c.ConversationPublicId == conversationPublicId && c.UserObjectId == userObjectId,
                cancellationToken);

        if (conversation is null)
        {
            return null;
        }

        var sourceNames = await ResolveCurrentSourceNamesAsync(conversation, cancellationToken);

        return conversation.Messages
            .Select(m => new ConversationMessageInfo
            {
                Role = m.Role,
                Content = m.Content,
                CreatedAtUtc = m.CreatedAtUtc,
                Suggestions = DeserializeSuggestions(m.SuggestionsJson),
                Visuals = m.Visuals
                    .OrderBy(v => v.DisplayOrder)
                    .Select(ToVisualInfo)
                    .ToList(),
                Sources = m.Sources
                    .OrderBy(source => source.DisplayOrder)
                    .Where(source => _pageMapResolver is null || sourceNames.ContainsKey(SourceKey(source)))
                    .Select(source => ToSourceInfo(
                        source,
                        sourceNames.GetValueOrDefault(SourceKey(source), source.DocumentId)))
                    .ToList(),
                WebCitations = m.WebCitations
                    .OrderBy(citation => citation.DisplayOrder)
                    .Select(ToWebCitationInfo)
                    .ToList()
            })
            .ToList();
    }

    private async Task<Dictionary<string, string>> ResolveCurrentSourceNamesAsync(
        Conversation conversation,
        CancellationToken cancellationToken)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        if (_pageMapResolver is null || conversation.MachineId is not Guid machineId)
        {
            return names;
        }

        var blobPrefix = await _db.Machines
            .AsNoTracking()
            .Where(machine => machine.Id == machineId)
            .Select(machine => machine.BlobPrefix)
            .SingleOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(blobPrefix))
        {
            return names;
        }

        var sources = conversation.Messages.SelectMany(message => message.Sources).ToArray();
        foreach (var document in sources.GroupBy(source => source.DocumentId, StringComparer.Ordinal))
        {
            var displayPages = document
                .Where(source => !TechnicalSourcePageStorage.TryReadPdfPage(source.DisplayPage, out _))
                .Select(source => TechnicalSourcePageStorage.PublicDisplayPage(source.DisplayPage))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            TechnicalPageMapResolution? pageMap;
            try
            {
                pageMap = await _pageMapResolver.ResolveAsync(
                    blobPrefix,
                    document.Key,
                    displayPages,
                    cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogWarning(
                    exception,
                    "Historical technical source revalidation failed. DocumentId={DocumentId}",
                    document.Key);
                continue;
            }
            if (pageMap is null)
            {
                continue;
            }

            foreach (var source in document)
            {
                var storedVersion = TechnicalSourcePageStorage.ReadVersionToken(source.DisplayPage);
                if (storedVersion is not null &&
                    !string.Equals(storedVersion, pageMap.VersionToken, StringComparison.Ordinal))
                {
                    continue;
                }
                var currentPhysicalReference =
                    TechnicalSourcePageStorage.TryReadPdfPage(source.DisplayPage, out var storedPdfPage) &&
                    storedPdfPage == source.PdfPage &&
                    pageMap.PdfPages.Contains(source.PdfPage);
                var currentDisplayReference =
                    !TechnicalSourcePageStorage.TryReadPdfPage(source.DisplayPage, out _) &&
                    pageMap.PdfPagesByDisplayPage.TryGetValue(
                        TechnicalSourcePageStorage.PublicDisplayPage(source.DisplayPage),
                        out var currentPdfPages) &&
                    currentPdfPages.Contains(source.PdfPage);
                if (currentPhysicalReference || currentDisplayReference)
                {
                    names[SourceKey(source)] = pageMap.DocumentName;
                }
            }
        }

        return names;
    }

    private static string SourceKey(ConversationMessageSourceReference source) =>
        $"{source.Id}:{source.DocumentId}:{source.PdfPage}:{source.DisplayPage}";

    private static IReadOnlyList<string> DeserializeSuggestions(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<string[]>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
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

    private static ConversationMessageSourceReferenceInfo ToSourceInfo(
        ConversationMessageSourceReference source,
        string documentName) => new()
    {
        Id = source.Id,
        DocumentName = string.IsNullOrWhiteSpace(documentName) ? source.DocumentId : documentName,
        PdfPage = source.PdfPage,
        DisplayPage = TechnicalSourcePageStorage.PublicDisplayPage(source.DisplayPage),
        Label = source.Label,
        StartIndex = source.StartIndex,
        EndIndex = source.EndIndex,
        DisplayOrder = source.DisplayOrder
    };

    private static ConversationMessageWebCitationInfo ToWebCitationInfo(
        ConversationMessageWebCitation citation) => new()
    {
        Type = "uri_citation",
        Label = citation.Label,
        Url = citation.Url,
        Quote = citation.Quote,
        DisplayOrder = citation.DisplayOrder
    };

    private static bool TryWebUri(string? value, out Uri uri) =>
        Uri.TryCreate(value, UriKind.Absolute, out uri!) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    /// <summary>
    /// Returns the most recent non-summarized messages for a conversation, scoped to its owning user
    /// in the same query as the lookup (never returns another user's messages). Selects the
    /// <paramref name="limit"/> most recent messages (by <see cref="ConversationMessage.CreatedAtUtc"/>,
    /// then <see cref="ConversationMessage.Id"/> for determinism), then returns them oldest-first â€”
    /// ready to render straight into a prompt.
    /// </summary>
    public async Task<List<ConversationMessage>> GetRecentUnsummarizedMessagesForUserAsync(
        string conversationPublicId,
        string userObjectId,
        int limit,
        CancellationToken cancellationToken)
    {
        var mostRecentFirst = await _db.ConversationMessages
            .AsNoTracking()
            .Where(m => m.Conversation!.ConversationPublicId == conversationPublicId
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
