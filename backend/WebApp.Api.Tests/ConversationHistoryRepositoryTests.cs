using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebApp.Api.Data;
using WebApp.Api.Models;
using WebApp.Api.Models.Entities;
using WebApp.Api.Repositories;
using WebApp.Api.Services;

namespace WebApp.Api.Tests;

[TestClass]
public class ConversationHistoryRepositoryTests
{
    private static DiagLinkDbContext CreateContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<DiagLinkDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new DiagLinkDbContext(options);
    }

    private static ConversationHistoryRepository CreateRepository(DiagLinkDbContext context)
        => new(context, NullLogger<ConversationHistoryRepository>.Instance);

    [TestMethod]
    public async Task ListConversationsForUserAsync_FiltersByOwnerAndMachine()
    {
        var dbName = Guid.NewGuid().ToString();
        var machineA = Guid.NewGuid();
        var machineB = Guid.NewGuid();

        await using (var seedContext = CreateContext(dbName))
        {
            var repository = CreateRepository(seedContext);
            await repository.CreateConversationAsync("user-a-machine-a-1", "user-A", null, machineA, CancellationToken.None);
            await repository.CreateConversationAsync("user-a-machine-a-2", "user-A", null, machineA, CancellationToken.None);
            await repository.CreateConversationAsync("user-a-machine-b", "user-A", null, machineB, CancellationToken.None);
            await repository.CreateConversationAsync("user-b-machine-a", "user-B", null, machineA, CancellationToken.None);
        }

        await using var readContext = CreateContext(dbName);
        var repositoryUnderTest = CreateRepository(readContext);

        var conversationsA = await repositoryUnderTest.ListConversationsForUserAsync("user-A", machineA, CancellationToken.None);
        var conversationsB = await repositoryUnderTest.ListConversationsForUserAsync("user-A", machineB, CancellationToken.None);
        var allUserConversations = await repositoryUnderTest.ListConversationsForUserAsync("user-A", null, CancellationToken.None);

        CollectionAssert.AreEquivalent(
            new[] { "user-a-machine-a-1", "user-a-machine-a-2" },
            conversationsA.Select(conversation => conversation.Id).ToArray());
        CollectionAssert.AreEqual(new[] { "user-a-machine-b" }, conversationsB.Select(conversation => conversation.Id).ToArray());
        Assert.AreEqual(3, allUserConversations.Count);
        Assert.IsFalse(allUserConversations.Any(conversation => conversation.Id == "user-b-machine-a"));
    }

    [TestMethod]
    public async Task ListConversationsForUserAsync_WithoutMessages_UsesCreatedAtAsLastActivityAt()
    {
        var dbName = Guid.NewGuid().ToString();
        var createdAtUtc = new DateTime(2026, 10, 1, 8, 30, 0, DateTimeKind.Utc);

        await using (var writeContext = CreateContext(dbName))
        {
            writeContext.Conversations.Add(new Conversation
            {
                Id = Guid.NewGuid(),
                ConversationPublicId = "conversation-without-messages",
                UserObjectId = "user-A",
                CreatedAtUtc = createdAtUtc,
                UpdatedAtUtc = createdAtUtc.AddDays(2)
            });
            await writeContext.SaveChangesAsync();
        }

        await using var readContext = CreateContext(dbName);
        var summary = (await CreateRepository(readContext)
            .ListConversationsForUserAsync("user-A", null, CancellationToken.None)).Single();
        var expectedTimestamp = new DateTimeOffset(createdAtUtc).ToUnixTimeSeconds();

        Assert.AreEqual(expectedTimestamp, summary.CreatedAt);
        Assert.AreEqual(expectedTimestamp, summary.LastActivityAt);
    }

    [TestMethod]
    public async Task ListConversationsForUserAsync_WithOneMessage_UsesMessageCreatedAtAsLastActivityAt()
    {
        var dbName = Guid.NewGuid().ToString();
        var createdAtUtc = new DateTime(2026, 10, 1, 8, 30, 0, DateTimeKind.Utc);
        var messageCreatedAtUtc = new DateTime(2026, 10, 5, 14, 7, 57, DateTimeKind.Utc);
        var conversationId = Guid.NewGuid();

        await using (var writeContext = CreateContext(dbName))
        {
            writeContext.Conversations.Add(new Conversation
            {
                Id = conversationId,
                ConversationPublicId = "conversation-with-one-message",
                UserObjectId = "user-A",
                CreatedAtUtc = createdAtUtc,
                UpdatedAtUtc = messageCreatedAtUtc
            });
            writeContext.ConversationMessages.Add(new ConversationMessage
            {
                ConversationId = conversationId,
                Role = "assistant",
                Content = "answer",
                CreatedAtUtc = messageCreatedAtUtc
            });
            await writeContext.SaveChangesAsync();
        }

        await using var readContext = CreateContext(dbName);
        var summary = (await CreateRepository(readContext)
            .ListConversationsForUserAsync("user-A", null, CancellationToken.None)).Single();

        Assert.AreEqual(new DateTimeOffset(createdAtUtc).ToUnixTimeSeconds(), summary.CreatedAt);
        Assert.AreEqual(new DateTimeOffset(messageCreatedAtUtc).ToUnixTimeSeconds(), summary.LastActivityAt);
    }

    [TestMethod]
    public async Task ListConversationsForUserAsync_WithMultipleMessages_UsesLatestMessageAndIgnoresUpdatedAt()
    {
        var dbName = Guid.NewGuid().ToString();
        var createdAtUtc = new DateTime(2026, 10, 1, 8, 30, 0, DateTimeKind.Utc);
        var firstMessageAtUtc = new DateTime(2026, 10, 5, 14, 7, 57, DateTimeKind.Utc);
        var latestMessageAtUtc = new DateTime(2026, 10, 8, 9, 12, 34, DateTimeKind.Utc);
        var conversationId = Guid.NewGuid();

        await using (var writeContext = CreateContext(dbName))
        {
            writeContext.Conversations.Add(new Conversation
            {
                Id = conversationId,
                ConversationPublicId = "conversation-with-multiple-messages",
                UserObjectId = "user-A",
                CreatedAtUtc = createdAtUtc,
                UpdatedAtUtc = latestMessageAtUtc.AddDays(1)
            });
            writeContext.ConversationMessages.AddRange(
                new ConversationMessage
                {
                    ConversationId = conversationId,
                    Role = "user",
                    Content = "question",
                    CreatedAtUtc = firstMessageAtUtc
                },
                new ConversationMessage
                {
                    ConversationId = conversationId,
                    Role = "assistant",
                    Content = "answer",
                    CreatedAtUtc = latestMessageAtUtc
                });
            await writeContext.SaveChangesAsync();
        }

        await using var readContext = CreateContext(dbName);
        var summary = (await CreateRepository(readContext)
            .ListConversationsForUserAsync("user-A", null, CancellationToken.None)).Single();
        var expectedLastActivityAt = new DateTimeOffset(latestMessageAtUtc).ToUnixTimeSeconds();

        Assert.AreEqual(new DateTimeOffset(createdAtUtc).ToUnixTimeSeconds(), summary.CreatedAt);
        Assert.AreEqual(expectedLastActivityAt, summary.LastActivityAt);

        var json = System.Text.Json.JsonSerializer.Serialize(
            summary,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        using var document = System.Text.Json.JsonDocument.Parse(json);
        Assert.AreEqual(expectedLastActivityAt, document.RootElement.GetProperty("lastActivityAt").GetInt64());
    }

    [TestMethod]
    public async Task ListConversationsForUserAsync_OrdersByLastActivityThenStableId()
    {
        var dbName = Guid.NewGuid().ToString();
        var oldActiveId = Guid.Parse("00000000-0000-0000-0000-000000000010");
        var newerInactiveId = Guid.Parse("00000000-0000-0000-0000-000000000020");
        var updatedOnlyId = Guid.Parse("00000000-0000-0000-0000-000000000030");
        var tieLowId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var tieHighId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var oldActivityAtUtc = new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
        var newerCreationAtUtc = new DateTime(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc);
        var tiedActivityAtUtc = new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);
        var updatedOnlyCreationAtUtc = new DateTime(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc);

        await using (var writeContext = CreateContext(dbName))
        {
            writeContext.Conversations.AddRange(
                new Conversation
                {
                    Id = oldActiveId,
                    ConversationPublicId = "old-active",
                    UserObjectId = "user-A",
                    CreatedAtUtc = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc),
                    UpdatedAtUtc = oldActivityAtUtc
                },
                new Conversation
                {
                    Id = newerInactiveId,
                    ConversationPublicId = "newer-inactive",
                    UserObjectId = "user-A",
                    CreatedAtUtc = newerCreationAtUtc,
                    UpdatedAtUtc = newerCreationAtUtc
                },
                new Conversation
                {
                    Id = updatedOnlyId,
                    ConversationPublicId = "updated-only",
                    UserObjectId = "user-A",
                    CreatedAtUtc = updatedOnlyCreationAtUtc,
                    UpdatedAtUtc = oldActivityAtUtc.AddDays(10)
                },
                new Conversation
                {
                    Id = tieLowId,
                    ConversationPublicId = "tie-low",
                    UserObjectId = "user-A",
                    CreatedAtUtc = tiedActivityAtUtc,
                    UpdatedAtUtc = tiedActivityAtUtc
                },
                new Conversation
                {
                    Id = tieHighId,
                    ConversationPublicId = "tie-high",
                    UserObjectId = "user-A",
                    CreatedAtUtc = tiedActivityAtUtc,
                    UpdatedAtUtc = tiedActivityAtUtc
                });
            writeContext.ConversationMessages.Add(new ConversationMessage
            {
                ConversationId = oldActiveId,
                Role = "assistant",
                Content = "recent answer",
                CreatedAtUtc = oldActivityAtUtc
            });
            await writeContext.SaveChangesAsync();
        }

        await using var readContext = CreateContext(dbName);
        var summaries = await CreateRepository(readContext)
            .ListConversationsForUserAsync("user-A", null, CancellationToken.None);

        CollectionAssert.AreEqual(
            new[] { "old-active", "newer-inactive", "tie-high", "tie-low", "updated-only" },
            summaries.Select(summary => summary.Id).ToArray());
        Assert.AreEqual(
            new DateTimeOffset(oldActivityAtUtc).ToUnixTimeSeconds(),
            summaries.Single(summary => summary.Id == "old-active").LastActivityAt);
        Assert.AreEqual(
            new DateTimeOffset(newerCreationAtUtc).ToUnixTimeSeconds(),
            summaries.Single(summary => summary.Id == "newer-inactive").LastActivityAt);
        Assert.AreEqual(
            new DateTimeOffset(updatedOnlyCreationAtUtc).ToUnixTimeSeconds(),
            summaries.Single(summary => summary.Id == "updated-only").LastActivityAt);
    }

    [TestMethod]
    public async Task AddMessageAsync_OtherUsersConversationId_DoesNotPersistMessage()
    {
        var dbName = Guid.NewGuid().ToString();
        const string conversationPublicId = "conv-shared-id";
        const string ownerUserObjectId = "user-A";
        const string attackerUserObjectId = "user-B";

        await using (var seedContext = CreateContext(dbName))
        {
            await CreateRepository(seedContext).CreateConversationAsync(
                conversationPublicId, ownerUserObjectId, agentName: null, machineId: null, cancellationToken: CancellationToken.None);
        }

        // User B attempts to add a message to a ConversationPublicId owned by User A.
        await using (var attackContext = CreateContext(dbName))
        {
            var messageId = await CreateRepository(attackContext).AddMessageAsync(
                conversationPublicId, attackerUserObjectId, "user", "message injecte par B", CancellationToken.None);
            Assert.IsNull(messageId);
        }

        await using var verifyContext = CreateContext(dbName);
        Assert.AreEqual(0, await verifyContext.ConversationMessages.CountAsync());
    }

    [TestMethod]
    public async Task AddMessageAsync_OwningUser_PersistsMessage()
    {
        var dbName = Guid.NewGuid().ToString();
        const string conversationPublicId = "conv-owned-id";
        const string ownerUserObjectId = "user-A";

        await using (var seedContext = CreateContext(dbName))
        {
            await CreateRepository(seedContext).CreateConversationAsync(
                conversationPublicId, ownerUserObjectId, agentName: null, machineId: null, cancellationToken: CancellationToken.None);
        }

        await using (var writeContext = CreateContext(dbName))
        {
            var messageId = await CreateRepository(writeContext).AddMessageAsync(
                conversationPublicId, ownerUserObjectId, "user", "message legitime de A", CancellationToken.None);
            Assert.IsNotNull(messageId);
            Assert.AreEqual((await writeContext.ConversationMessages.SingleAsync()).Id, messageId.Value);
        }

        await using var verifyContext = CreateContext(dbName);
        Assert.AreEqual(1, await verifyContext.ConversationMessages.CountAsync());
    }

    [TestMethod]
    public async Task GetTechnicalSummaryForUserAsync_OtherUsersConversationId_ReturnsNull()
    {
        var dbName = Guid.NewGuid().ToString();
        const string conversationPublicId = "conv-with-summary";
        const string ownerUserObjectId = "user-A";
        const string attackerUserObjectId = "user-B";

        await using (var seedContext = CreateContext(dbName))
        {
            await CreateRepository(seedContext).CreateConversationAsync(
                conversationPublicId, ownerUserObjectId, agentName: null, machineId: null, cancellationToken: CancellationToken.None);

            // Set TechnicalSummary directly (ExecuteUpdateAsync is unsupported by the EF Core InMemory provider).
            var conversation = await seedContext.Conversations.FirstAsync(c => c.ConversationPublicId == conversationPublicId);
            conversation.TechnicalSummary = "Resume confidentiel de A";
            await seedContext.SaveChangesAsync();
        }

        // User B attempts to read User A's TechnicalSummary via A's ConversationPublicId.
        await using var readContext = CreateContext(dbName);
        var summaryForAttacker = await CreateRepository(readContext)
            .GetTechnicalSummaryForUserAsync(conversationPublicId, attackerUserObjectId, CancellationToken.None);

        Assert.IsNull(summaryForAttacker);
    }

    [TestMethod]
    public async Task GetTechnicalSummaryForUserAsync_OwningUser_ReturnsSummary()
    {
        var dbName = Guid.NewGuid().ToString();
        const string conversationPublicId = "conv-with-summary-2";
        const string ownerUserObjectId = "user-A";
        const string summary = "Resume technique de A";

        await using (var seedContext = CreateContext(dbName))
        {
            await CreateRepository(seedContext).CreateConversationAsync(
                conversationPublicId, ownerUserObjectId, agentName: null, machineId: null, cancellationToken: CancellationToken.None);

            var conversation = await seedContext.Conversations.FirstAsync(c => c.ConversationPublicId == conversationPublicId);
            conversation.TechnicalSummary = summary;
            await seedContext.SaveChangesAsync();
        }

        await using var readContext = CreateContext(dbName);
        var result = await CreateRepository(readContext)
            .GetTechnicalSummaryForUserAsync(conversationPublicId, ownerUserObjectId, CancellationToken.None);

        Assert.AreEqual(summary, result);
    }

    /// <summary>
    /// Seeds a conversation with N messages ("msg-0".."msg-{count-1}"), evenly spaced one second
    /// apart so ordering is deterministic, alternating user/assistant roles.
    /// </summary>
    private static async Task<Guid> SeedConversationWithMessagesAsync(
        string dbName,
        string conversationPublicId,
        string userObjectId,
        int count,
        HashSet<int>? summarizedIndexes = null)
    {
        await using var context = CreateContext(dbName);
        var conversation = new Conversation
        {
            Id = Guid.NewGuid(),
            ConversationPublicId = conversationPublicId,
            UserObjectId = userObjectId,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        };
        context.Conversations.Add(conversation);

        var baseTime = DateTime.UtcNow;
        for (var i = 0; i < count; i++)
        {
            context.ConversationMessages.Add(new ConversationMessage
            {
                ConversationId = conversation.Id,
                Role = i % 2 == 0 ? "user" : "assistant",
                Content = $"msg-{i}",
                IsSummarized = summarizedIndexes?.Contains(i) ?? false,
                CreatedAtUtc = baseTime.AddSeconds(i),
            });
        }

        await context.SaveChangesAsync();
        return conversation.Id;
    }

    [TestMethod]
    public async Task GetRecentUnsummarizedMessagesForUserAsync_OtherUser_ReturnsEmpty()
    {
        var dbName = Guid.NewGuid().ToString();
        const string conversationPublicId = "conv-recent-1";
        await SeedConversationWithMessagesAsync(dbName, conversationPublicId, "user-A", count: 4);

        await using var readContext = CreateContext(dbName);
        var result = await CreateRepository(readContext)
            .GetRecentUnsummarizedMessagesForUserAsync(conversationPublicId, "user-B", limit: 6, CancellationToken.None);

        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public async Task GetRecentUnsummarizedMessagesForUserAsync_OwningUser_ReturnsMessages()
    {
        var dbName = Guid.NewGuid().ToString();
        const string conversationPublicId = "conv-recent-2";
        await SeedConversationWithMessagesAsync(dbName, conversationPublicId, "user-A", count: 4);

        await using var readContext = CreateContext(dbName);
        var result = await CreateRepository(readContext)
            .GetRecentUnsummarizedMessagesForUserAsync(conversationPublicId, "user-A", limit: 6, CancellationToken.None);

        Assert.AreEqual(4, result.Count);
    }

    [TestMethod]
    public async Task GetRecentUnsummarizedMessagesForUserAsync_ExcludesSummarizedMessages()
    {
        var dbName = Guid.NewGuid().ToString();
        const string conversationPublicId = "conv-recent-3";
        // 8 messages, the 4 oldest (indexes 0-3) already folded into TechnicalSummary.
        await SeedConversationWithMessagesAsync(
            dbName, conversationPublicId, "user-A", count: 8, summarizedIndexes: [0, 1, 2, 3]);

        await using var readContext = CreateContext(dbName);
        var result = await CreateRepository(readContext)
            .GetRecentUnsummarizedMessagesForUserAsync(conversationPublicId, "user-A", limit: 6, CancellationToken.None);

        Assert.IsTrue(result.All(m => m.Content is "msg-4" or "msg-5" or "msg-6" or "msg-7"));
    }

    [TestMethod]
    public async Task GetRecentUnsummarizedMessagesForUserAsync_RespectsLimit()
    {
        var dbName = Guid.NewGuid().ToString();
        const string conversationPublicId = "conv-recent-4";
        await SeedConversationWithMessagesAsync(dbName, conversationPublicId, "user-A", count: 10);

        await using var readContext = CreateContext(dbName);
        var result = await CreateRepository(readContext)
            .GetRecentUnsummarizedMessagesForUserAsync(conversationPublicId, "user-A", limit: 6, CancellationToken.None);

        Assert.AreEqual(6, result.Count);
    }

    [TestMethod]
    [DataRow(8)]
    [DataRow(10)]
    public async Task GetRecentUnsummarizedMessagesForUserAsync_ContextWindowReturnsAllMessagesOldestFirst(int messageCount)
    {
        var dbName = Guid.NewGuid().ToString();
        const string conversationPublicId = "conv-context-window";
        await SeedConversationWithMessagesAsync(dbName, conversationPublicId, "user-A", count: messageCount);

        await using var readContext = CreateContext(dbName);
        var result = await CreateRepository(readContext)
            .GetRecentUnsummarizedMessagesForUserAsync(
                conversationPublicId,
                "user-A",
                ConversationSummaryService.MaxContextMessageCount,
                CancellationToken.None);

        CollectionAssert.AreEqual(
            Enumerable.Range(0, messageCount).Select(index => $"msg-{index}").ToList(),
            result.Select(message => message.Content).ToList());
    }

    [TestMethod]
    public async Task GetRecentUnsummarizedMessagesForUserAsync_SelectsMostRecentNotOldest()
    {
        var dbName = Guid.NewGuid().ToString();
        const string conversationPublicId = "conv-recent-5";
        // 10 messages: msg-0..msg-9 (msg-9 is the most recent).
        await SeedConversationWithMessagesAsync(dbName, conversationPublicId, "user-A", count: 10);

        await using var readContext = CreateContext(dbName);
        var result = await CreateRepository(readContext)
            .GetRecentUnsummarizedMessagesForUserAsync(conversationPublicId, "user-A", limit: 6, CancellationToken.None);

        var contents = result.Select(m => m.Content).ToList();
        CollectionAssert.AreEqual(
            new[] { "msg-4", "msg-5", "msg-6", "msg-7", "msg-8", "msg-9" },
            contents);
    }

    [TestMethod]
    public async Task GetRecentUnsummarizedMessagesForUserAsync_ReturnsChronologicalOrder()
    {
        var dbName = Guid.NewGuid().ToString();
        const string conversationPublicId = "conv-recent-6";
        await SeedConversationWithMessagesAsync(dbName, conversationPublicId, "user-A", count: 6);

        await using var readContext = CreateContext(dbName);
        var result = await CreateRepository(readContext)
            .GetRecentUnsummarizedMessagesForUserAsync(conversationPublicId, "user-A", limit: 6, CancellationToken.None);

        for (var i = 1; i < result.Count; i++)
        {
            Assert.IsTrue(result[i - 1].CreatedAtUtc <= result[i].CreatedAtUtc);
        }
        Assert.AreEqual("msg-0", result.First().Content);
        Assert.AreEqual("msg-5", result.Last().Content);
    }

    [TestMethod]
    public async Task GetRecentUnsummarizedMessagesForUserAsync_DoesNotIncludeMessageAddedAfterFetch()
    {
        // Proves the Program.cs ordering contract: fetching memory BEFORE persisting the current
        // question guarantees the question is never duplicated inside the "recent messages" window.
        var dbName = Guid.NewGuid().ToString();
        const string conversationPublicId = "conv-no-dup";
        const string userObjectId = "user-A";
        await SeedConversationWithMessagesAsync(dbName, conversationPublicId, userObjectId, count: 2);

        List<ConversationMessage> beforeSave;
        await using (var fetchContext = CreateContext(dbName))
        {
            beforeSave = await CreateRepository(fetchContext)
                .GetRecentUnsummarizedMessagesForUserAsync(conversationPublicId, userObjectId, limit: 6, CancellationToken.None);
        }

        Assert.IsFalse(beforeSave.Any(m => m.Content == "current question"));

        await using (var saveContext = CreateContext(dbName))
        {
            await CreateRepository(saveContext).AddMessageAsync(
                conversationPublicId, userObjectId, "user", "current question", CancellationToken.None);
        }

        List<ConversationMessage> afterSave;
        await using (var refetchContext = CreateContext(dbName))
        {
            afterSave = await CreateRepository(refetchContext)
                .GetRecentUnsummarizedMessagesForUserAsync(conversationPublicId, userObjectId, limit: 6, CancellationToken.None);
        }

        Assert.IsTrue(afterSave.Any(m => m.Content == "current question"));
    }

    [TestMethod]
    public async Task AddAssistantMessageAsync_WithoutVisuals_PersistsEmptyCollection()
    {
        var dbName = Guid.NewGuid().ToString();
        await SeedEmptyConversationAsync(dbName, "conv-no-visual", "user-A");

        await using (var writeContext = CreateContext(dbName))
        {
            await CreateRepository(writeContext).AddMessageAsync(
                "conv-no-visual", "user-A", "assistant", "answer", [], CancellationToken.None);
        }

        await using var readContext = CreateContext(dbName);
        var message = await readContext.ConversationMessages.Include(m => m.Visuals).SingleAsync();
        Assert.AreEqual("assistant", message.Role);
        Assert.HasCount(0, message.Visuals);
    }

    [TestMethod]
    public async Task AddAssistantMessageAsync_PersistsFullVisual()
    {
        var visual = FullVisual();
        var message = await PersistAssistantWithVisualsAsync([visual]);

        var persisted = message.Visuals.Single();
        Assert.AreEqual(visual.DocumentId, persisted.DocumentId);
        Assert.AreEqual(visual.Page, persisted.Page);
        Assert.AreEqual("full", persisted.AssetType);
        Assert.IsNull(persisted.Tile);
        Assert.AreEqual(visual.Name, persisted.Name);
        Assert.AreEqual(visual.AssetKey, persisted.AssetKey);
        Assert.AreEqual(0, persisted.DisplayOrder);
    }

    [TestMethod]
    public async Task AddAssistantMessageAsync_PersistsTileVisual()
    {
        var visual = TileVisual();
        var message = await PersistAssistantWithVisualsAsync([visual]);

        var persisted = message.Visuals.Single();
        Assert.AreEqual("tile", persisted.AssetType);
        Assert.AreEqual("r02-c01", persisted.Tile);
    }

    [TestMethod]
    public async Task AddAssistantMessageAsync_PreservesVisualOrder()
    {
        var message = await PersistAssistantWithVisualsAsync([TileVisual(), FullVisual()]);

        var visuals = message.Visuals.OrderBy(v => v.DisplayOrder).ToList();
        CollectionAssert.AreEqual(new[] { "tile", "full" }, visuals.Select(v => v.AssetType).ToArray());
        CollectionAssert.AreEqual(new[] { 0, 1 }, visuals.Select(v => v.DisplayOrder).ToArray());
    }

    [TestMethod]
    public async Task AddAssistantMessageAsync_ReturnsPersistedSqlIdsAndHistoryDtos()
    {
        var dbName = Guid.NewGuid().ToString();
        await SeedEmptyConversationAsync(dbName, "conv-persisted-result", "user-A");
        ConversationMessagePersistenceResult result;

        await using (var writeContext = CreateContext(dbName))
        {
            result = (await CreateRepository(writeContext).AddMessageAsync(
                "conv-persisted-result",
                "user-A",
                "assistant",
                "answer",
                [TileVisual(), FullVisual()],
                CancellationToken.None))!;
        }

        await using var readContext = CreateContext(dbName);
        var persistedIds = await readContext.ConversationMessageVisuals
            .OrderBy(visual => visual.DisplayOrder)
            .Select(visual => visual.Id)
            .ToListAsync();
        var history = await CreateRepository(readContext)
            .GetConversationMessagesAsync("conv-persisted-result", "user-A", CancellationToken.None);

        Assert.IsTrue(result.MessageId > 0);
        CollectionAssert.AreEqual(persistedIds, result.Visuals.Select(visual => visual.Id).ToList());
        CollectionAssert.AreEqual(result.Visuals.ToList(), history!.Single().Visuals.ToList());
    }

    [TestMethod]
    public async Task AddAssistantMessageAsync_DuplicateAssetKeyPersistsFirstOccurrenceOnly()
    {
        var first = FullVisual();
        var duplicate = first with { DocumentId = "replacement", Name = "replacement.png" };
        var message = await PersistAssistantWithVisualsAsync([first, duplicate]);

        Assert.HasCount(1, message.Visuals);
        Assert.AreEqual(first.DocumentId, message.Visuals.Single().DocumentId);
        Assert.AreEqual(first.Name, message.Visuals.Single().Name);
    }

    [TestMethod]
    public async Task AddAssistantMessageAsync_DifferentMessagesMayUseSameAssetKey()
    {
        var dbName = Guid.NewGuid().ToString();
        await SeedEmptyConversationAsync(dbName, "conv-shared-visual", "user-A");
        var visual = FullVisual();

        await using (var writeContext = CreateContext(dbName))
        {
            var repository = CreateRepository(writeContext);
            await repository.AddMessageAsync("conv-shared-visual", "user-A", "assistant", "first", [visual], CancellationToken.None);
            await repository.AddMessageAsync("conv-shared-visual", "user-A", "assistant", "second", [visual], CancellationToken.None);
        }

        await using var readContext = CreateContext(dbName);
        Assert.AreEqual(2, await readContext.ConversationMessageVisuals.CountAsync());
        Assert.AreEqual(2, await readContext.ConversationMessageVisuals.Select(v => v.ConversationMessageId).Distinct().CountAsync());
    }

    [TestMethod]
    public async Task AddMessageAsync_UserOverloadRemainsWithoutVisuals()
    {
        var dbName = Guid.NewGuid().ToString();
        await SeedEmptyConversationAsync(dbName, "conv-user", "user-A");

        await using (var writeContext = CreateContext(dbName))
        {
            await CreateRepository(writeContext).AddMessageAsync(
                "conv-user", "user-A", "user", "question", CancellationToken.None);
        }

        await using var readContext = CreateContext(dbName);
        Assert.AreEqual(1, await readContext.ConversationMessages.CountAsync());
        Assert.AreEqual(0, await readContext.ConversationMessageVisuals.CountAsync());
    }

    [TestMethod]
    public async Task GetConversationMessagesAsync_ReturnsVisualsInDisplayOrderWithoutAssetKey()
    {
        var dbName = Guid.NewGuid().ToString();
        await SeedEmptyConversationAsync(dbName, "conv-history", "user-A");
        await using (var writeContext = CreateContext(dbName))
        {
            await CreateRepository(writeContext).AddMessageAsync(
                "conv-history", "user-A", "assistant", "answer", [TileVisual(), FullVisual()], CancellationToken.None);
        }

        await using var readContext = CreateContext(dbName);
        var messages = await CreateRepository(readContext)
            .GetConversationMessagesAsync("conv-history", "user-A", CancellationToken.None);

        var message = messages!.Single();
        Assert.HasCount(2, message.Visuals);
        CollectionAssert.AreEqual(new[] { "tile", "full" }, message.Visuals.Select(v => v.AssetType).ToArray());
        Assert.IsTrue(message.Visuals.All(v => v.Id > 0));
        Assert.IsNull(typeof(ConversationMessageVisualInfo).GetProperty("AssetKey"));
        var json = System.Text.Json.JsonSerializer.Serialize(message, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        Assert.IsFalse(json.Contains("assetKey", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task GetConversationMessagesAsync_ReturnsPersistedCreatedAtUtc()
    {
        var dbName = Guid.NewGuid().ToString();
        const string conversationPublicId = "conv-created-at";
        var createdAtUtc = new DateTime(2026, 10, 5, 14, 7, 57, DateTimeKind.Utc);
        await SeedEmptyConversationAsync(dbName, conversationPublicId, "user-A");

        await using (var writeContext = CreateContext(dbName))
        {
            var conversationId = await writeContext.Conversations
                .Where(conversation => conversation.ConversationPublicId == conversationPublicId)
                .Select(conversation => conversation.Id)
                .SingleAsync();
            writeContext.ConversationMessages.Add(new ConversationMessage
            {
                ConversationId = conversationId,
                Role = "assistant",
                Content = "answer",
                CreatedAtUtc = createdAtUtc
            });
            await writeContext.SaveChangesAsync();
        }

        await using var readContext = CreateContext(dbName);
        var messages = await CreateRepository(readContext)
            .GetConversationMessagesAsync(conversationPublicId, "user-A", CancellationToken.None);

        var message = messages!.Single();
        Assert.AreEqual(createdAtUtc, message.CreatedAtUtc);

        var json = System.Text.Json.JsonSerializer.Serialize(
            message,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        using var document = System.Text.Json.JsonDocument.Parse(json);
        Assert.AreEqual(createdAtUtc, document.RootElement.GetProperty("createdAtUtc").GetDateTime());
    }

    [TestMethod]
    public async Task GetConversationMessagesAsync_LegacyMessageReturnsEmptyVisuals()
    {
        var dbName = Guid.NewGuid().ToString();
        await SeedConversationWithMessagesAsync(dbName, "conv-legacy", "user-A", count: 1);

        await using var readContext = CreateContext(dbName);
        var messages = await CreateRepository(readContext)
            .GetConversationMessagesAsync("conv-legacy", "user-A", CancellationToken.None);

        var message = messages!.Single();
        Assert.HasCount(0, message.Visuals);
        Assert.HasCount(0, message.Sources);
        Assert.HasCount(0, message.Suggestions);
    }

    [TestMethod]
    public async Task AddAssistantMessageAsync_PersistsAndReturnsSourcesWithoutPrivateFields()
    {
        var dbName = Guid.NewGuid().ToString();
        await SeedEmptyConversationAsync(dbName, "conv-sources", "user-A");
        var source = SourceReference();
        ConversationMessagePersistenceResult result;

        await using (var writeContext = CreateContext(dbName))
        {
            result = (await CreateRepository(writeContext).AddMessageAsync(
                "conv-sources",
                "user-A",
                "assistant",
                "Source : p. 70.",
                [FullVisual()],
                [source],
                CancellationToken.None))!;
        }

        await using var readContext = CreateContext(dbName);
        var history = await CreateRepository(readContext)
            .GetConversationMessagesAsync("conv-sources", "user-A", CancellationToken.None);
        var persisted = await readContext.ConversationMessageSourceReferences.SingleAsync();
        var historyMessage = history!.Single();

        Assert.AreEqual(source.DocumentId, persisted.DocumentId);
        Assert.AreEqual(source.PdfPage, persisted.PdfPage);
        Assert.IsTrue(result.Sources.Single().Id > 0);
        CollectionAssert.AreEqual(result.Sources.ToList(), historyMessage.Sources.ToList());
        Assert.IsNull(typeof(ConversationMessageSourceReferenceInfo).GetProperty("DocumentId"));
        Assert.IsNull(typeof(ConversationMessageSourceReferenceInfo).GetProperty("SourceBlob"));
        var json = System.Text.Json.JsonSerializer.Serialize(
            historyMessage.Sources,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        Assert.IsFalse(json.Contains("documentId", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(json.Contains("sourceBlob", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(json.Contains("https://", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(json.Contains("sig=", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task AddAssistantMessageAsync_MessageVisualsAndSourcesAreAtomic()
    {
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<DiagLinkDbContext>()
            .UseSqlite(connection)
            .Options;

        await using (var seedContext = new DiagLinkDbContext(options))
        {
            await seedContext.Database.EnsureCreatedAsync();
            await seedContext.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF;");
            await CreateRepository(seedContext).CreateConversationAsync(
                "conv-atomic", "user-A", null, null, CancellationToken.None);
        }

        await using (var writeContext = new DiagLinkDbContext(options))
        {
            await Assert.ThrowsExactlyAsync<DbUpdateException>(() =>
                CreateRepository(writeContext).AddMessageAsync(
                    "conv-atomic",
                    "user-A",
                    "assistant",
                    "Source : p. 70.",
                    [FullVisual()],
                    [SourceReference() with { PdfPage = 0 }],
                    CancellationToken.None));
        }

        await using var verifyContext = new DiagLinkDbContext(options);
        Assert.AreEqual(0, await verifyContext.ConversationMessages.CountAsync());
        Assert.AreEqual(0, await verifyContext.ConversationMessageVisuals.CountAsync());
        Assert.AreEqual(0, await verifyContext.ConversationMessageSourceReferences.CountAsync());
    }

    [TestMethod]
    public async Task GetConversationMessagesAsync_OtherUserCannotReadVisuals()
    {
        var dbName = Guid.NewGuid().ToString();
        await SeedEmptyConversationAsync(dbName, "conv-private-visual", "user-A");
        await using (var writeContext = CreateContext(dbName))
        {
            await CreateRepository(writeContext).AddMessageAsync(
                "conv-private-visual", "user-A", "assistant", "answer", [FullVisual()], CancellationToken.None);
        }

        await using var readContext = CreateContext(dbName);
        var messages = await CreateRepository(readContext)
            .GetConversationMessagesAsync("conv-private-visual", "user-B", CancellationToken.None);

        Assert.IsNull(messages);
    }

    [TestMethod]
    public async Task RemovingMessage_CascadesToVisuals()
    {
        var dbName = Guid.NewGuid().ToString();
        var message = await PersistAssistantWithVisualsAsync([FullVisual()], dbName);

        await using (var deleteContext = CreateContext(dbName))
        {
            var tracked = await deleteContext.ConversationMessages.Include(m => m.Visuals).SingleAsync(m => m.Id == message.Id);
            deleteContext.ConversationMessages.Remove(tracked);
            await deleteContext.SaveChangesAsync();
        }

        await using var verifyContext = CreateContext(dbName);
        Assert.AreEqual(0, await verifyContext.ConversationMessageVisuals.CountAsync());
        var foreignKey = verifyContext.Model.FindEntityType(typeof(ConversationMessageVisual))!
            .GetForeignKeys().Single(fk => fk.PrincipalEntityType.ClrType == typeof(ConversationMessage));
        Assert.AreEqual(DeleteBehavior.Cascade, foreignKey.DeleteBehavior);
    }

    private static TechnicalVisualReference FullVisual() => new(
        "manual", 71, "full", null, "manual_page-00071-full.png", "manual/page-00071/manual_page-00071-full.png");

    private static TechnicalVisualReference TileVisual() => new(
        "manual", 71, "tile", "r02-c01", "manual_page-00071-tile-r02-c01.png", "manual/page-00071/manual_page-00071-tile-r02-c01.png");

    [TestMethod]
    public async Task AddAssistantMessageAsync_PersistsSuggestionsOutsideContentAndReturnsThemInHistory()
    {
        var dbName = Guid.NewGuid().ToString();
        await SeedEmptyConversationAsync(dbName, "conv-suggestions", "user-A");
        await using (var writeContext = CreateContext(dbName))
        {
            await CreateRepository(writeContext).AddMessageAsync(
                "conv-suggestions",
                "user-A",
                "assistant",
                "Réponse visible",
                [],
                [],
                ["Où est le relais ?"],
                CancellationToken.None);
        }

        await using var readContext = CreateContext(dbName);
        var entity = await readContext.ConversationMessages.SingleAsync();
        var history = await CreateRepository(readContext)
            .GetConversationMessagesAsync("conv-suggestions", "user-A", CancellationToken.None);

        Assert.AreEqual("Réponse visible", entity.Content);
        Assert.IsFalse(entity.Content.Contains("diaglink_suggestion", StringComparison.Ordinal));
        CollectionAssert.AreEqual(
            new[] { "Où est le relais ?" },
            System.Text.Json.JsonSerializer.Deserialize<string[]>(entity.SuggestionsJson!)!);
        CollectionAssert.AreEqual(new[] { "Où est le relais ?" }, history!.Single().Suggestions.ToArray());
    }

    private static TechnicalSourceReference SourceReference() => new(
        "manual", 72, "70", "p. 70", 9, 14, 0);

    private static async Task SeedEmptyConversationAsync(string dbName, string conversationId, string userObjectId)
    {
        await using var context = CreateContext(dbName);
        await CreateRepository(context).CreateConversationAsync(
            conversationId, userObjectId, agentName: null, machineId: null, cancellationToken: CancellationToken.None);
    }

    private static async Task<ConversationMessage> PersistAssistantWithVisualsAsync(
        IReadOnlyList<TechnicalVisualReference> visuals,
        string? dbName = null)
    {
        dbName ??= Guid.NewGuid().ToString();
        await SeedEmptyConversationAsync(dbName, "conv-visuals", "user-A");
        await using (var writeContext = CreateContext(dbName))
        {
            await CreateRepository(writeContext).AddMessageAsync(
                "conv-visuals", "user-A", "assistant", "answer", visuals, CancellationToken.None);
        }

        await using var readContext = CreateContext(dbName);
        return await readContext.ConversationMessages.Include(m => m.Visuals).SingleAsync();
    }
}

