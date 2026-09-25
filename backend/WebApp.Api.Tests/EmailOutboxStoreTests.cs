using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebApp.Api.Data;
using WebApp.Api.Models.Entities;
using WebApp.Api.Services;

namespace WebApp.Api.Tests;

[TestClass]
public class EmailOutboxStoreTests
{
    [TestMethod]
    public async Task EnqueueCreatesPendingImmutablePayloadAndIsIdempotent()
    {
        await using var fixture = Fixture.Create();
        var requestId = Guid.NewGuid().ToString("N");
        var first = await fixture.Store.EnqueueAsync(Request(requestId, EmailNotificationType.RequestReceived), default);
        var second = await fixture.Store.EnqueueAsync(Request(requestId, EmailNotificationType.RequestReceived), default);

        Assert.AreEqual(first.Id, second.Id);
        Assert.AreEqual(1, await fixture.Db.EmailOutbox.CountAsync());
        Assert.AreEqual(EmailOutboxStatus.Pending, first.Status);
        Assert.AreEqual(0, first.AttemptCount);
        Assert.AreEqual("client@example.test", first.RecipientEmail);
        StringAssert.Contains(first.PayloadJson, "Machine A");
    }

    [TestMethod]
    public async Task DifferentNotificationTypesForTheSameRequestAreAllowed()
    {
        await using var fixture = Fixture.Create();
        var requestId = Guid.NewGuid().ToString("N");
        await fixture.Store.EnqueueAsync(Request(requestId, EmailNotificationType.RequestReceived), default);
        await fixture.Store.EnqueueAsync(Request(requestId, EmailNotificationType.RequestAccepted), default);
        Assert.AreEqual(2, await fixture.Db.EmailOutbox.CountAsync());
    }

    [TestMethod]
    public void ModelHasDatabaseUniqueBusinessKey()
    {
        using var fixture = Fixture.Create();
        var entity = fixture.Db.Model.FindEntityType(typeof(EmailOutbox))!;
        var index = entity.GetIndexes().Single(item => item.Properties.Select(property => property.Name)
            .SequenceEqual([nameof(EmailOutbox.MachineRequestId), nameof(EmailOutbox.NotificationType)]));
        Assert.IsTrue(index.IsUnique);
    }

    [TestMethod]
    public async Task ClaimUsesLeaseAndExpiredLeaseCanBeReclaimed()
    {
        await using var fixture = Fixture.Create();
        var entry = await fixture.Store.EnqueueAsync(Request(Guid.NewGuid().ToString("N"), EmailNotificationType.RequestReceived), default);

        var first = (await fixture.Store.ClaimEligibleAsync(10, TimeSpan.FromMinutes(5), default)).Single();
        Assert.AreEqual(entry.Id, first.Entry.Id);
        Assert.AreEqual(first.LeaseId, first.Entry.LeaseId);
        Assert.AreEqual(0, (await fixture.Store.ClaimEligibleAsync(10, TimeSpan.FromMinutes(5), default)).Count);

        fixture.Time.Advance(TimeSpan.FromMinutes(6));
        var reclaimed = (await fixture.Store.ClaimEligibleAsync(10, TimeSpan.FromMinutes(5), default)).Single();
        Assert.AreEqual(entry.Id, reclaimed.Entry.Id);
        Assert.AreNotEqual(first.LeaseId, reclaimed.LeaseId);
        Assert.IsFalse(await fixture.Store.MarkSentAsync(entry.Id, first.LeaseId, "stale", default));
    }

    [TestMethod]
    public async Task MarkSentRequiresLeaseAndPersistsProviderOperationId()
    {
        await using var fixture = Fixture.Create();
        var entry = await fixture.Store.EnqueueAsync(Request(Guid.NewGuid().ToString("N"), EmailNotificationType.MachineReady), default);
        var claimed = (await fixture.Store.ClaimEligibleAsync(1, TimeSpan.FromMinutes(5), default)).Single();

        Assert.IsTrue(await fixture.Store.MarkSentAsync(entry.Id, claimed.LeaseId, "acs-operation-1", default));
        var sent = await fixture.Db.EmailOutbox.SingleAsync();
        Assert.AreEqual(EmailOutboxStatus.Sent, sent.Status);
        Assert.IsNotNull(sent.SentAtUtc);
        Assert.AreEqual("acs-operation-1", sent.ProviderOperationId);
        Assert.IsNull(sent.LeaseId);
        Assert.IsNull(sent.LockedUntilUtc);
        Assert.AreEqual(0, (await fixture.Store.ClaimEligibleAsync(1, TimeSpan.FromMinutes(5), default)).Count);
    }

    [TestMethod]
    public async Task FailureIncrementsAttemptSchedulesBoundedRetryAndReleasesLease()
    {
        await using var fixture = Fixture.Create();
        var entry = await fixture.Store.EnqueueAsync(Request(Guid.NewGuid().ToString("N"), EmailNotificationType.DocumentsReady), default);
        var claimed = (await fixture.Store.ClaimEligibleAsync(1, TimeSpan.FromMinutes(5), default)).Single();

        Assert.IsTrue(await fixture.Store.RescheduleAfterFailureAsync(entry.Id, claimed.LeaseId,
            new string('x', 1200), default));
        var failed = await fixture.Db.EmailOutbox.SingleAsync();
        Assert.AreEqual(EmailOutboxStatus.Pending, failed.Status);
        Assert.AreEqual(1, failed.AttemptCount);
        Assert.AreEqual(fixture.Time.GetUtcNow().UtcDateTime.AddMinutes(1), failed.NextAttemptAtUtc);
        Assert.AreEqual(1000, failed.LastError!.Length);
        Assert.IsNull(failed.LeaseId);
        Assert.IsNull(failed.LockedUntilUtc);
    }

    [TestMethod]
    public async Task ExistingOtpDevelopmentFallbackStillCompletesWithoutAcs()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var service = new EmailService(configuration, NullLogger<EmailService>.Instance,
            new TestHostEnvironment { EnvironmentName = Environments.Development });
        await service.SendLoginCodeAsync("client@example.test", "123456", default);
    }

    private static EmailOutboxEnqueue Request(string requestId, EmailNotificationType type) => new(
        requestId, Guid.NewGuid(), type, "client@example.test", "Client", new { machineName = "Machine A" });

    private sealed class Fixture : IAsyncDisposable, IDisposable
    {
        public DiagLinkDbContext Db { get; }
        public MutableTimeProvider Time { get; } = new(new DateTimeOffset(2026, 9, 25, 12, 0, 0, TimeSpan.Zero));
        public EmailOutboxStore Store { get; }
        private Fixture()
        {
            Db = new DiagLinkDbContext(new DbContextOptionsBuilder<DiagLinkDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            Store = new EmailOutboxStore(Db, Time);
        }
        public static Fixture Create() => new();
        public void Dispose() => Db.Dispose();
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan duration) => now = now.Add(duration);
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "";
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
