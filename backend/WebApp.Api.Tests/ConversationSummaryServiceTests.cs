using System.Reflection;
using Microsoft.Data.Sqlite;
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
public sealed class ConversationSummaryServiceTests
{
    [TestMethod]
    public async Task ElevenUnsummarizedMessages_DoesNotSummarize()
    {
        await using var fixture = await SummaryFixture.CreateAsync(11, "RÃ©sumÃ© existant Ã  conserver.");
        var summarizer = new FakeConversationSummarizer();
        var service = fixture.CreateService(summarizer);

        await service.MaybeSummarizeAsync(fixture.ConversationId);

        Assert.AreEqual(0, summarizer.CallCount);
        Assert.AreEqual("RÃ©sumÃ© existant Ã  conserver.", await fixture.GetTechnicalSummaryAsync());
        Assert.AreEqual(0, await fixture.Db.ConversationMessages.CountAsync(message => message.IsSummarized));
    }

    [TestMethod]
    public async Task TwelveUnsummarizedMessages_SummarizesSixOldest()
    {
        await using var fixture = await SummaryFixture.CreateAsync(12);
        var summarizer = new FakeConversationSummarizer("nouveau rÃ©sumÃ©");

        await fixture.CreateService(summarizer).MaybeSummarizeAsync(fixture.ConversationId);

        Assert.AreEqual(1, summarizer.CallCount);
        CollectionAssert.AreEqual(
            Enumerable.Range(0, 6).Select(index => $"message-{index}").ToList(),
            summarizer.Messages.Select(message => message.Content).ToList());
        Assert.AreEqual("nouveau rÃ©sumÃ©", await fixture.GetTechnicalSummaryAsync());
        CollectionAssert.AreEqual(
            Enumerable.Repeat(true, 6).Concat(Enumerable.Repeat(false, 6)).ToList(),
            await fixture.GetSummarizedFlagsAsync());
    }

    [TestMethod]
    public async Task ExistingTechnicalSummary_IsPassedToSummarizer()
    {
        const string existingSummary = "rÃ©sumÃ© technique existant";
        await using var fixture = await SummaryFixture.CreateAsync(12, existingSummary);
        var summarizer = new FakeConversationSummarizer("rÃ©sumÃ© technique remplacÃ©");

        await fixture.CreateService(summarizer).MaybeSummarizeAsync(fixture.ConversationId);

        Assert.AreEqual(existingSummary, summarizer.ExistingSummary);
        Assert.AreEqual("rÃ©sumÃ© technique remplacÃ©", await fixture.GetTechnicalSummaryAsync());
    }

    [TestMethod]
    public async Task MessagesArePassedInChronologicalOrderWithRoles()
    {
        await using var fixture = await SummaryFixture.CreateAsync(12);
        var summarizer = new FakeConversationSummarizer();

        await fixture.CreateService(summarizer).MaybeSummarizeAsync(fixture.ConversationId);

        var expected = Enumerable.Range(0, 6)
            .Select(index => $"{(index % 2 == 0 ? "user" : "assistant")}|message-{index}")
            .ToList();
        CollectionAssert.AreEqual(
            expected,
            summarizer.Messages.Select(message => $"{message.Role}|{message.Content}").ToList());
    }

    [TestMethod]
    public async Task SuccessfulSummary_PropagatesUsageCallback()
    {
        await using var fixture = await SummaryFixture.CreateAsync(12);
        var expectedUsage = CreateUsage("summary-response");
        var summarizer = new FakeConversationSummarizer("nouveau rÃ©sumÃ©", expectedUsage);
        var receivedUsage = new List<AiResponseUsage>();

        await fixture.CreateService(summarizer).MaybeSummarizeAsync(
            fixture.ConversationId,
            onUsage: receivedUsage.Add);

        Assert.AreEqual(1, summarizer.CallCount);
        Assert.AreEqual(1, receivedUsage.Count);
        Assert.AreEqual(expectedUsage, receivedUsage[0]);
    }

    [TestMethod]
    public async Task SummarizerFailure_DoesNotModifyConversationState()
    {
        const string existingSummary = "rÃ©sumÃ© inchangÃ©";
        await using var fixture = await SummaryFixture.CreateAsync(12, existingSummary);
        var summarizer = new FakeConversationSummarizer
        {
            ExceptionToThrow = new InvalidOperationException("summarizer failure"),
        };

        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            fixture.CreateService(summarizer).MaybeSummarizeAsync(fixture.ConversationId));

        Assert.AreEqual("summarizer failure", exception.Message);
        Assert.AreEqual(existingSummary, await fixture.GetTechnicalSummaryAsync());
        Assert.AreEqual(0, await fixture.Db.ConversationMessages.CountAsync(message => message.IsSummarized));
    }

    [TestMethod]
    public async Task FourteenUnsummarizedMessages_SummarizesOnlyOneBatch()
    {
        await using var fixture = await SummaryFixture.CreateAsync(14);
        var summarizer = new FakeConversationSummarizer();

        await fixture.CreateService(summarizer).MaybeSummarizeAsync(fixture.ConversationId);

        Assert.AreEqual(1, summarizer.CallCount);
        CollectionAssert.AreEqual(
            Enumerable.Range(0, 6).Select(index => $"message-{index}").ToList(),
            summarizer.Messages.Select(message => message.Content).ToList());
        Assert.AreEqual(8, await fixture.Db.ConversationMessages.CountAsync(message => !message.IsSummarized));
    }

    [TestMethod]
    public void SummaryContractConstants_AreTwelveSixAndSix()
    {
        var flags = BindingFlags.NonPublic | BindingFlags.Static;
        var summaryBatchSize = typeof(ConversationSummaryService)
            .GetField("SummaryBatchSize", flags)!
            .GetRawConstantValue();
        var triggerCount = typeof(ConversationSummaryService)
            .GetField("TriggerCount", flags)!
            .GetRawConstantValue();

        Assert.AreEqual(6, ConversationSummaryService.KeepRawMessageCount);
        Assert.AreEqual(11, ConversationSummaryService.MaxContextMessageCount);
        Assert.AreEqual(6, summaryBatchSize);
        Assert.AreEqual(12, triggerCount);
    }

    private static AiResponseUsage CreateUsage(string responseId) => new(
        AiUsageType.ConversationSummary,
        responseId,
        true,
        100,
        20,
        120,
        "summary-model",
        "response",
        new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

    private sealed class FakeConversationSummarizer : IConversationSummarizer
    {
        private readonly AiSummaryResult _result;

        public FakeConversationSummarizer(
            string resultText = "rÃ©sumÃ© gÃ©nÃ©rÃ©",
            AiResponseUsage? usage = null)
        {
            _result = new AiSummaryResult(resultText, usage ?? CreateUsage("fake-summary-response"));
        }

        public int CallCount { get; private set; }
        public string? ExistingSummary { get; private set; }
        public IReadOnlyList<ConversationMessage> Messages { get; private set; } = [];
        public Exception? ExceptionToThrow { get; init; }

        public Task<AiSummaryResult> SummarizeConversationAsync(
            string? existingSummary,
            IReadOnlyList<ConversationMessage> oldMessages,
            CancellationToken cancellationToken = default,
            Action<AiResponseUsage>? onUsage = null)
        {
            CallCount++;
            ExistingSummary = existingSummary;
            Messages = oldMessages.ToList();

            if (ExceptionToThrow is not null)
            {
                return Task.FromException<AiSummaryResult>(ExceptionToThrow);
            }

            onUsage?.Invoke(_result.Usage);
            return Task.FromResult(_result);
        }
    }

    private sealed class SummaryFixture : IAsyncDisposable
    {
        private SummaryFixture(SqliteConnection connection, DiagLinkDbContext db, Guid conversationId)
        {
            Connection = connection;
            Db = db;
            ConversationId = conversationId;
        }

        private SqliteConnection Connection { get; }
        public DiagLinkDbContext Db { get; }
        public Guid ConversationId { get; }

        public static async Task<SummaryFixture> CreateAsync(int messageCount, string? technicalSummary = null)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<DiagLinkDbContext>()
                .UseSqlite(connection)
                .Options;
            var db = new DiagLinkDbContext(options);

            await db.Database.EnsureCreatedAsync();
            await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS Machines (Id TEXT PRIMARY KEY)");

            var conversationId = Guid.NewGuid();
            var firstMessageAt = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            db.Conversations.Add(new Conversation
            {
                Id = conversationId,
                ConversationPublicId = $"summary-{conversationId:N}",
                UserObjectId = "summary-test-user",
                TechnicalSummary = technicalSummary,
                CreatedAtUtc = firstMessageAt.AddMinutes(-1),
                UpdatedAtUtc = firstMessageAt.AddMinutes(-1),
            });

            for (var index = 0; index < messageCount; index++)
            {
                db.ConversationMessages.Add(new ConversationMessage
                {
                    ConversationId = conversationId,
                    Role = index % 2 == 0 ? "user" : "assistant",
                    Content = $"message-{index}",
                    IsSummarized = false,
                    CreatedAtUtc = firstMessageAt.AddMinutes(index),
                });
            }

            await db.SaveChangesAsync();
            return new SummaryFixture(connection, db, conversationId);
        }

        public ConversationSummaryService CreateService(IConversationSummarizer summarizer) => new(
            new ConversationHistoryRepository(Db, NullLogger<ConversationHistoryRepository>.Instance),
            summarizer,
            NullLogger<ConversationSummaryService>.Instance);

        public Task<string?> GetTechnicalSummaryAsync() => Db.Conversations
            .AsNoTracking()
            .Where(conversation => conversation.Id == ConversationId)
            .Select(conversation => conversation.TechnicalSummary)
            .SingleAsync();

        public Task<List<bool>> GetSummarizedFlagsAsync() => Db.ConversationMessages
            .AsNoTracking()
            .Where(message => message.ConversationId == ConversationId)
            .OrderBy(message => message.CreatedAtUtc)
            .Select(message => message.IsSummarized)
            .ToListAsync();

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }
}
