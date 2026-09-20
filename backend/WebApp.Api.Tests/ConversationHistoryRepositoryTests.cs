using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebApp.Api.Data;
using WebApp.Api.Models.Entities;
using WebApp.Api.Repositories;

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
    public async Task AddMessageAsync_OtherUsersConversationId_DoesNotPersistMessage()
    {
        var dbName = Guid.NewGuid().ToString();
        const string foundryConversationId = "conv-shared-id";
        const string ownerUserObjectId = "user-A";
        const string attackerUserObjectId = "user-B";

        await using (var seedContext = CreateContext(dbName))
        {
            await CreateRepository(seedContext).CreateConversationAsync(
                foundryConversationId, ownerUserObjectId, agentName: null, machineId: null, cancellationToken: CancellationToken.None);
        }

        // User B attempts to add a message to a FoundryConversationId owned by User A.
        await using (var attackContext = CreateContext(dbName))
        {
            var messageId = await CreateRepository(attackContext).AddMessageAsync(
                foundryConversationId, attackerUserObjectId, "user", "message injecte par B", CancellationToken.None);
            Assert.IsNull(messageId);
        }

        await using var verifyContext = CreateContext(dbName);
        Assert.AreEqual(0, await verifyContext.ConversationMessages.CountAsync());
    }

    [TestMethod]
    public async Task AddMessageAsync_OwningUser_PersistsMessage()
    {
        var dbName = Guid.NewGuid().ToString();
        const string foundryConversationId = "conv-owned-id";
        const string ownerUserObjectId = "user-A";

        await using (var seedContext = CreateContext(dbName))
        {
            await CreateRepository(seedContext).CreateConversationAsync(
                foundryConversationId, ownerUserObjectId, agentName: null, machineId: null, cancellationToken: CancellationToken.None);
        }

        await using (var writeContext = CreateContext(dbName))
        {
            var messageId = await CreateRepository(writeContext).AddMessageAsync(
                foundryConversationId, ownerUserObjectId, "user", "message legitime de A", CancellationToken.None);
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
        const string foundryConversationId = "conv-with-summary";
        const string ownerUserObjectId = "user-A";
        const string attackerUserObjectId = "user-B";

        await using (var seedContext = CreateContext(dbName))
        {
            await CreateRepository(seedContext).CreateConversationAsync(
                foundryConversationId, ownerUserObjectId, agentName: null, machineId: null, cancellationToken: CancellationToken.None);

            // Set TechnicalSummary directly (ExecuteUpdateAsync is unsupported by the EF Core InMemory provider).
            var conversation = await seedContext.Conversations.FirstAsync(c => c.FoundryConversationId == foundryConversationId);
            conversation.TechnicalSummary = "Resume confidentiel de A";
            await seedContext.SaveChangesAsync();
        }

        // User B attempts to read User A's TechnicalSummary via A's FoundryConversationId.
        await using var readContext = CreateContext(dbName);
        var summaryForAttacker = await CreateRepository(readContext)
            .GetTechnicalSummaryForUserAsync(foundryConversationId, attackerUserObjectId, CancellationToken.None);

        Assert.IsNull(summaryForAttacker);
    }

    [TestMethod]
    public async Task GetTechnicalSummaryForUserAsync_OwningUser_ReturnsSummary()
    {
        var dbName = Guid.NewGuid().ToString();
        const string foundryConversationId = "conv-with-summary-2";
        const string ownerUserObjectId = "user-A";
        const string summary = "Resume technique de A";

        await using (var seedContext = CreateContext(dbName))
        {
            await CreateRepository(seedContext).CreateConversationAsync(
                foundryConversationId, ownerUserObjectId, agentName: null, machineId: null, cancellationToken: CancellationToken.None);

            var conversation = await seedContext.Conversations.FirstAsync(c => c.FoundryConversationId == foundryConversationId);
            conversation.TechnicalSummary = summary;
            await seedContext.SaveChangesAsync();
        }

        await using var readContext = CreateContext(dbName);
        var result = await CreateRepository(readContext)
            .GetTechnicalSummaryForUserAsync(foundryConversationId, ownerUserObjectId, CancellationToken.None);

        Assert.AreEqual(summary, result);
    }

    /// <summary>
    /// Seeds a conversation with N messages ("msg-0".."msg-{count-1}"), evenly spaced one second
    /// apart so ordering is deterministic, alternating user/assistant roles.
    /// </summary>
    private static async Task<Guid> SeedConversationWithMessagesAsync(
        string dbName,
        string foundryConversationId,
        string userObjectId,
        int count,
        HashSet<int>? summarizedIndexes = null)
    {
        await using var context = CreateContext(dbName);
        var conversation = new Conversation
        {
            Id = Guid.NewGuid(),
            FoundryConversationId = foundryConversationId,
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
        const string foundryConversationId = "conv-recent-1";
        await SeedConversationWithMessagesAsync(dbName, foundryConversationId, "user-A", count: 4);

        await using var readContext = CreateContext(dbName);
        var result = await CreateRepository(readContext)
            .GetRecentUnsummarizedMessagesForUserAsync(foundryConversationId, "user-B", limit: 6, CancellationToken.None);

        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public async Task GetRecentUnsummarizedMessagesForUserAsync_OwningUser_ReturnsMessages()
    {
        var dbName = Guid.NewGuid().ToString();
        const string foundryConversationId = "conv-recent-2";
        await SeedConversationWithMessagesAsync(dbName, foundryConversationId, "user-A", count: 4);

        await using var readContext = CreateContext(dbName);
        var result = await CreateRepository(readContext)
            .GetRecentUnsummarizedMessagesForUserAsync(foundryConversationId, "user-A", limit: 6, CancellationToken.None);

        Assert.AreEqual(4, result.Count);
    }

    [TestMethod]
    public async Task GetRecentUnsummarizedMessagesForUserAsync_ExcludesSummarizedMessages()
    {
        var dbName = Guid.NewGuid().ToString();
        const string foundryConversationId = "conv-recent-3";
        // 8 messages, the 4 oldest (indexes 0-3) already folded into TechnicalSummary.
        await SeedConversationWithMessagesAsync(
            dbName, foundryConversationId, "user-A", count: 8, summarizedIndexes: [0, 1, 2, 3]);

        await using var readContext = CreateContext(dbName);
        var result = await CreateRepository(readContext)
            .GetRecentUnsummarizedMessagesForUserAsync(foundryConversationId, "user-A", limit: 6, CancellationToken.None);

        Assert.IsTrue(result.All(m => m.Content is "msg-4" or "msg-5" or "msg-6" or "msg-7"));
    }

    [TestMethod]
    public async Task GetRecentUnsummarizedMessagesForUserAsync_RespectsLimit()
    {
        var dbName = Guid.NewGuid().ToString();
        const string foundryConversationId = "conv-recent-4";
        await SeedConversationWithMessagesAsync(dbName, foundryConversationId, "user-A", count: 10);

        await using var readContext = CreateContext(dbName);
        var result = await CreateRepository(readContext)
            .GetRecentUnsummarizedMessagesForUserAsync(foundryConversationId, "user-A", limit: 6, CancellationToken.None);

        Assert.AreEqual(6, result.Count);
    }

    [TestMethod]
    public async Task GetRecentUnsummarizedMessagesForUserAsync_SelectsMostRecentNotOldest()
    {
        var dbName = Guid.NewGuid().ToString();
        const string foundryConversationId = "conv-recent-5";
        // 10 messages: msg-0..msg-9 (msg-9 is the most recent).
        await SeedConversationWithMessagesAsync(dbName, foundryConversationId, "user-A", count: 10);

        await using var readContext = CreateContext(dbName);
        var result = await CreateRepository(readContext)
            .GetRecentUnsummarizedMessagesForUserAsync(foundryConversationId, "user-A", limit: 6, CancellationToken.None);

        var contents = result.Select(m => m.Content).ToList();
        CollectionAssert.AreEqual(
            new[] { "msg-4", "msg-5", "msg-6", "msg-7", "msg-8", "msg-9" },
            contents);
    }

    [TestMethod]
    public async Task GetRecentUnsummarizedMessagesForUserAsync_ReturnsChronologicalOrder()
    {
        var dbName = Guid.NewGuid().ToString();
        const string foundryConversationId = "conv-recent-6";
        await SeedConversationWithMessagesAsync(dbName, foundryConversationId, "user-A", count: 6);

        await using var readContext = CreateContext(dbName);
        var result = await CreateRepository(readContext)
            .GetRecentUnsummarizedMessagesForUserAsync(foundryConversationId, "user-A", limit: 6, CancellationToken.None);

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
        const string foundryConversationId = "conv-no-dup";
        const string userObjectId = "user-A";
        await SeedConversationWithMessagesAsync(dbName, foundryConversationId, userObjectId, count: 2);

        List<ConversationMessage> beforeSave;
        await using (var fetchContext = CreateContext(dbName))
        {
            beforeSave = await CreateRepository(fetchContext)
                .GetRecentUnsummarizedMessagesForUserAsync(foundryConversationId, userObjectId, limit: 6, CancellationToken.None);
        }

        Assert.IsFalse(beforeSave.Any(m => m.Content == "current question"));

        await using (var saveContext = CreateContext(dbName))
        {
            await CreateRepository(saveContext).AddMessageAsync(
                foundryConversationId, userObjectId, "user", "current question", CancellationToken.None);
        }

        List<ConversationMessage> afterSave;
        await using (var refetchContext = CreateContext(dbName))
        {
            afterSave = await CreateRepository(refetchContext)
                .GetRecentUnsummarizedMessagesForUserAsync(foundryConversationId, userObjectId, limit: 6, CancellationToken.None);
        }

        Assert.IsTrue(afterSave.Any(m => m.Content == "current question"));
    }
}

