using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebApp.Api.Data;
using WebApp.Api.Models;
using WebApp.Api.Repositories;

namespace WebApp.Api.Tests;

[TestClass]
public class AiUsageRepositoryTests
{
    private static DbContextOptions<DiagLinkDbContext> Options() =>
        new DbContextOptionsBuilder<DiagLinkDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;

    private static AiUsageMeasurement Measurement(AiUsageType type = AiUsageType.ChatResponse) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "conv-1", 42,
            new AiResponseUsage(type, "response-1", true, 100, 25, 125, "model-a", "response", "3",
                new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.FromHours(2))));

    [TestMethod]
    public async Task KnownUsage_PreservesTokensContextAndUtcTimestamp()
    {
        var options = Options();
        var measurement = Measurement();
        await new AiUsageRepository(options, NullLogger<AiUsageRepository>.Instance).RecordAsync(measurement, CancellationToken.None);
        await using var db = new DiagLinkDbContext(options);
        var record = await db.AiUsageRecords.SingleAsync();
        Assert.AreEqual(100, record.InputTokens);
        Assert.AreEqual(25, record.OutputTokens);
        Assert.AreEqual(125, record.TotalTokens);
        Assert.IsTrue(record.Available && record.Completed);
        Assert.AreEqual(measurement.CompanyId, record.CompanyId);
        Assert.AreEqual(measurement.MachineId, record.MachineId);
        Assert.AreEqual(measurement.UserId, record.UserId);
        Assert.AreEqual(measurement.SqlConversationId, record.ConversationId);
        Assert.AreEqual(42L, record.AssistantMessageId);
        Assert.AreEqual("conv-1", record.FoundryConversationId);
        Assert.AreEqual("model-a", record.Model);
        Assert.AreEqual("3", record.AgentVersion);
        Assert.AreEqual(measurement.Response.TimestampUtc.UtcDateTime, record.CreatedAtUtc);
    }

    [TestMethod]
    public async Task UnknownUsageAndMissingSqlReferences_RemainNull()
    {
        var options = Options();
        var measurement = Measurement();
        measurement = measurement with { UserId = null, CompanyId = null, MachineId = null,
            SqlConversationId = null, AssistantMessageId = null,
            Response = measurement.Response with { InputTokens = null, OutputTokens = null, TotalTokens = null } };
        await new AiUsageRepository(options, NullLogger<AiUsageRepository>.Instance).RecordAsync(measurement, CancellationToken.None);
        await using var db = new DiagLinkDbContext(options);
        var record = await db.AiUsageRecords.SingleAsync();
        Assert.IsFalse(record.Available);
        Assert.IsTrue(record.Completed);
        Assert.IsNull(record.InputTokens);
        Assert.IsNull(record.OutputTokens);
        Assert.IsNull(record.TotalTokens);
        Assert.IsNull(record.UserId);
        Assert.IsNull(record.CompanyId);
        Assert.IsNull(record.MachineId);
        Assert.IsNull(record.ConversationId);
        Assert.IsNull(record.AssistantMessageId);
    }

    [TestMethod]
    public async Task SameEventRecordedTwice_IsIgnoredButSeparateCallsRemainDistinct()
    {
        var options = Options();
        var repository = new AiUsageRepository(options, NullLogger<AiUsageRepository>.Instance);
        var chat = Measurement();
        await repository.RecordAsync(chat, CancellationToken.None);
        await repository.RecordAsync(chat with { }, CancellationToken.None);
        var retry = Measurement() with { Response = chat.Response with { ResponseId = "response-retry" } };
        await repository.RecordAsync(retry, CancellationToken.None);
        await repository.RecordAsync(Measurement(AiUsageType.ConversationSummary), CancellationToken.None);
        await using var db = new DiagLinkDbContext(options);
        Assert.AreEqual(3, await db.AiUsageRecords.CountAsync());
        Assert.AreEqual(2, await db.AiUsageRecords.CountAsync(r => r.UsageType == AiUsageType.ChatResponse));
        Assert.AreEqual(1, await db.AiUsageRecords.CountAsync(r => r.UsageType == AiUsageType.ConversationSummary));
    }

    [TestMethod]
    public async Task InitialPlaceholderIsSkipped_IdentifiedInterruptedResponseIsRecorded()
    {
        var options = Options();
        var repository = new AiUsageRepository(options, NullLogger<AiUsageRepository>.Instance);
        var m = Measurement();
        m = m with { Response = m.Response with { Completed = false, ResponseId = null,
            InputTokens = null, OutputTokens = null, TotalTokens = null } };
        await repository.RecordAsync(m, CancellationToken.None);
        await using var db = new DiagLinkDbContext(options);
        Assert.AreEqual(0, await db.AiUsageRecords.CountAsync());
        await repository.RecordAsync(m with { Response = m.Response with { ResponseId = "identified" } }, CancellationToken.None);
        var record = await db.AiUsageRecords.SingleAsync();
        Assert.IsFalse(record.Completed);
        Assert.IsFalse(record.Available);
    }

    [TestMethod]
    public async Task FailedSaveIsLoggedAndDoesNotThrowToChatCaller()
    {
        var baseOptions = Options();
        var options = new DbContextOptionsBuilder<DiagLinkDbContext>(baseOptions)
            .AddInterceptors(new FailedSave()).Options;
        var logger = new ErrorLogger();
        await new AiUsageRepository(options, logger).RecordAsync(Measurement(), CancellationToken.None);
        Assert.AreEqual(1, logger.Errors);
        await using var db = new DiagLinkDbContext(baseOptions);
        Assert.AreEqual(0, await db.AiUsageRecords.CountAsync());
    }

    [TestMethod]
    public async Task VisionCapturePreservesCorrelationContextAndInterruptedCalls()
    {
        var options = Options();
        var repository = new AiUsageRepository(options, NullLogger<AiUsageRepository>.Instance);
        var chat = Measurement();
        var usage = new AiResponseUsage(AiUsageType.VisionTool, null, false, null, null, null,
            null, null, null, DateTimeOffset.UtcNow) { CallId = "call_1", ParentResponseId = "parent_1" };
        var captured = new VisionUsageCapture(Guid.NewGuid(), usage).WithContext(chat);
        await repository.RecordAsync(captured, default);
        await repository.RecordAsync(captured, default);
        await using var db = new DiagLinkDbContext(options);
        var row = await db.AiUsageRecords.SingleAsync();
        Assert.AreEqual(AiUsageType.VisionTool, row.UsageType);
        Assert.AreEqual(captured.EventId, row.Id);
        Assert.AreEqual("call_1", row.CallId);
        Assert.AreEqual("parent_1", row.ParentResponseId);
        Assert.IsNull(row.ResponseId);
        Assert.IsNull(row.AssistantMessageId);
        Assert.IsFalse(row.Available || row.Completed);
        Assert.AreEqual(chat.CompanyId, row.CompanyId);
        Assert.AreEqual(chat.MachineId, row.MachineId);
        Assert.AreEqual(chat.UserId, row.UserId);
        Assert.AreEqual(chat.SqlConversationId, row.ConversationId);
        Assert.AreEqual(chat.FoundryConversationId, row.FoundryConversationId);
        var completed = new VisionUsageCapture(Guid.NewGuid(), usage with {
            CallId = "call_2", ResponseId = "vision_internal", Completed = true,
            InputTokens = 10, OutputTokens = 2, TotalTokens = 12 }).WithContext(chat);
        await repository.RecordAsync(completed, default);
        var final = await db.AiUsageRecords.SingleAsync(r => r.CallId == "call_2");
        Assert.IsTrue(final.Completed && final.Available);
        Assert.AreEqual("vision_internal", final.ResponseId);
        Assert.AreEqual("parent_1", final.ParentResponseId);
    }

    [TestMethod]
    public async Task FailedVisionSaveDoesNotPreventChatOrOtherVisionSaves()
    {
        var baseOptions = Options();
        var options = new DbContextOptionsBuilder<DiagLinkDbContext>(baseOptions)
            .AddInterceptors(new FailOneVision()).Options;
        var logger = new ErrorLogger();
        var repository = new AiUsageRepository(options, logger);
        await repository.RecordAsync(Measurement(AiUsageType.VisionTool), default);
        await repository.RecordAsync(Measurement(), default);
        await repository.RecordAsync(Measurement(AiUsageType.VisionTool), default);
        await using var db = new DiagLinkDbContext(baseOptions);
        Assert.AreEqual(1, logger.Errors);
        Assert.AreEqual(1, await db.AiUsageRecords.CountAsync(r => r.UsageType == AiUsageType.ChatResponse));
        Assert.AreEqual(1, await db.AiUsageRecords.CountAsync(r => r.UsageType == AiUsageType.VisionTool));
    }

    private sealed class FailOneVision : SaveChangesInterceptor
    {
        private bool failed;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!failed && eventData.Context!.ChangeTracker.Entries<WebApp.Api.Models.Entities.AiUsageRecord>()
                .Any(e => e.Entity.UsageType == AiUsageType.VisionTool))
            {
                failed = true;
                throw new InvalidOperationException("Simulated Vision failure");
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class FailedSave : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Simulated SQL failure");
    }

    private sealed class ErrorLogger : ILogger<AiUsageRepository>
    {
        public int Errors { get; private set; }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Error && exception is not null) Errors++;
        }
    }
}
