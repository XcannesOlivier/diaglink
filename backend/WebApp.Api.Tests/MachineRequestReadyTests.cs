using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebApp.Api.Data;
using WebApp.Api.Models;
using WebApp.Api.Models.Entities;
using WebApp.Api.Services;

namespace WebApp.Api.Tests;

[TestClass]
public sealed class MachineRequestReadyTests
{
    [TestMethod]
    [DataRow(MachineRequestPaymentStatus.Pending)]
    [DataRow(MachineRequestPaymentStatus.Authorized)]
    [DataRow(MachineRequestPaymentStatus.Cancelled)]
    [DataRow(MachineRequestPaymentStatus.Abandoned)]
    public async Task NonCapturedPaymentCannotBecomeReady(MachineRequestPaymentStatus status)
    {
        await using var fixture = Fixture.Create(MachineRequestKind.InitialMachine, status);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.MarkReadyAsync());
        Assert.AreEqual(0, await fixture.Db.EmailOutbox.CountAsync());
        Assert.AreEqual(MachineRequestPreparationStatus.Pending,
            (await fixture.Db.MachineRequestPayments.SingleAsync()).PreparationStatus);
    }

    [TestMethod]
    [DataRow(MachineRequestKind.InitialMachine, EmailNotificationType.MachineReady)]
    [DataRow(MachineRequestKind.AdditionalMachine, EmailNotificationType.MachineReady)]
    [DataRow(MachineRequestKind.AdditionalDocuments, EmailNotificationType.DocumentsReady)]
    public async Task CapturedTreatedRequestBecomesReadyWithExactlyItsExpectedNotification(
        MachineRequestKind kind, EmailNotificationType expected)
    {
        await using var fixture = Fixture.Create(kind, MachineRequestPaymentStatus.Captured);
        var first = await fixture.MarkReadyAsync();
        var second = await fixture.MarkReadyAsync();

        Assert.AreEqual(MachineRequestPreparationStatus.Ready, first.PreparationStatus);
        Assert.AreEqual(first.ReadyAtUtc, second.ReadyAtUtc);
        Assert.AreEqual(fixture.AdminId, first.ReadyByUserId);
        var message = await fixture.Db.EmailOutbox.SingleAsync();
        Assert.AreEqual(expected, message.NotificationType);
        Assert.AreNotEqual(kind == MachineRequestKind.AdditionalDocuments
            ? EmailNotificationType.MachineReady : EmailNotificationType.DocumentsReady, message.NotificationType);
    }

    [TestMethod]
    public async Task ExplicitRetryRepairsReadyWithoutOutbox()
    {
        await using var fixture = Fixture.Create(MachineRequestKind.InitialMachine,
            MachineRequestPaymentStatus.Captured);
        await fixture.MarkReadyAsync();
        fixture.Db.EmailOutbox.Remove(await fixture.Db.EmailOutbox.SingleAsync());
        await fixture.Db.SaveChangesAsync();

        await fixture.MarkReadyAsync();
        Assert.AreEqual(1, await fixture.Db.EmailOutbox.CountAsync());
    }

    [TestMethod]
    public async Task CapturedHistoricalRequestIsNeverMarkedReadyAutomatically()
    {
        await using var fixture = Fixture.Create(MachineRequestKind.InitialMachine,
            MachineRequestPaymentStatus.Captured);
        var payment = await fixture.Store.GetAsync(fixture.PaymentId, default);
        Assert.AreEqual(MachineRequestPreparationStatus.Pending, payment!.PreparationStatus);
        Assert.IsNull(payment.ReadyAtUtc);
        Assert.AreEqual(0, await fixture.Db.EmailOutbox.CountAsync());
    }

    [TestMethod]
    public void ReadyTemplatesAreSpecificAndHtmlEncoded()
    {
        var machine = new RequestReadyEmailPayload(MachineRequestKind.InitialMachine,
            Guid.NewGuid().ToString("N"), "<Alice>", "client@example.test", "Machine <A>", DateTime.UtcNow);
        var machineContent = RequestReceivedEmailTemplate.Build(EmailNotificationType.MachineReady,
            JsonSerializer.Serialize(machine, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.AreEqual("DiagLink — Votre machine est prête", machineContent.Subject);
        StringAssert.Contains(machineContent.TextBody, "maintenant disponibles");
        StringAssert.Contains(machineContent.HtmlBody, "&lt;Alice&gt;");
        StringAssert.Contains(machineContent.HtmlBody, "Machine &lt;A&gt;");

        var documents = machine with { RequestKind = MachineRequestKind.AdditionalDocuments };
        var documentContent = RequestReceivedEmailTemplate.Build(EmailNotificationType.DocumentsReady,
            JsonSerializer.Serialize(documents, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.AreEqual("DiagLink — Vos nouveaux documents sont disponibles", documentContent.Subject);
        StringAssert.Contains(documentContent.TextBody, "ont été intégrés");
    }

    [TestMethod]
    public async Task AcsFailureKeepsReadyAndReschedulesOutbox()
    {
        await using var fixture = Fixture.Create(MachineRequestKind.AdditionalDocuments,
            MachineRequestPaymentStatus.Captured);
        await fixture.MarkReadyAsync();
        var outbox = new EmailOutboxStore(fixture.Db, TimeProvider.System);
        var processor = new EmailOutboxProcessor(outbox, new FailingSender(),
            NullLogger<EmailOutboxProcessor>.Instance);
        await processor.ProcessOnceAsync();

        Assert.AreEqual(MachineRequestPreparationStatus.Ready,
            (await fixture.Db.MachineRequestPayments.SingleAsync()).PreparationStatus);
        var message = await fixture.Db.EmailOutbox.SingleAsync();
        Assert.AreEqual(EmailOutboxStatus.Pending, message.Status);
        Assert.AreEqual(1, message.AttemptCount);
    }

    [TestMethod]
    public async Task SuccessfulDeliveryPersistsProviderOperationId()
    {
        await using var fixture = Fixture.Create(MachineRequestKind.InitialMachine,
            MachineRequestPaymentStatus.Captured);
        await fixture.MarkReadyAsync();
        var processor = new EmailOutboxProcessor(new EmailOutboxStore(fixture.Db, TimeProvider.System),
            new SuccessfulSender(), NullLogger<EmailOutboxProcessor>.Instance);

        Assert.AreEqual(1, await processor.ProcessOnceAsync());
        var message = await fixture.Db.EmailOutbox.SingleAsync();
        Assert.AreEqual(EmailOutboxStatus.Sent, message.Status);
        Assert.AreEqual("acs-ready", message.ProviderOperationId);
    }

    [TestMethod]
    public async Task RelationalOutboxFailureRollsBackReadyTransition()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<DiagLinkDbContext>().UseSqlite(connection).Options;
        await using var db = new DiagLinkDbContext(options);
        await db.Database.EnsureCreatedAsync();
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS Companies (Id TEXT PRIMARY KEY);");
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS Machines (Id TEXT PRIMARY KEY);");
        var paymentId = Guid.NewGuid();
        var requestId = paymentId.ToString("N");
        var now = DateTime.UtcNow;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO MachineRequestPayments
            (Id, RequestKind, Status, EstimatedTotalPages, AmountCents, Currency, Email,
             StripePaymentIntentId, AuthorizationEventId, MachineRequestId, CreatedAtUtc,
             UpdatedAtUtc, AuthorizedAtUtc, CapturedAtUtc, RequestLinkedAtUtc,
             ProvisioningStage, PreparationStatus, RowVersion)
            VALUES ({paymentId}, {(int)MachineRequestKind.InitialMachine},
             {(int)MachineRequestPaymentStatus.Captured}, {10}, {9990L}, {"EUR"},
             {"client@example.test"}, {"pi_test"}, {"evt_test"}, {requestId}, {now}, {now},
             {now}, {now}, {now}, {(int)MachineRequestProvisioningStage.AwaitingAcceptance},
             {(int)MachineRequestPreparationStatus.Pending}, {new byte[] { 1 }})
            """);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER fail_ready_outbox BEFORE INSERT ON EmailOutbox
            BEGIN SELECT RAISE(ABORT, 'forced outbox failure'); END;
            """);
        var request = new MachineRequestRecord(requestId, DateTimeOffset.UtcNow,
            MachineRequestStatuses.Treated,
            new("Alice", "Martin", "Garage", "client@example.test", "+331"),
            new("Machine A", "Maker", "Model", null, null), [],
            new(10, 400, 0, 99.9m, .27m, 99.9m, 29.9m),
            new(paymentId, 10, 9990, "EUR", 10, 9990, 0, 0, false));
        var store = new MachineRequestPaymentStore(db);
        var payment = await store.GetAsync(paymentId, default);

        var failure = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            store.MarkReadyAsync(payment!, request, Guid.NewGuid(), default));
        Assert.IsInstanceOfType<DbUpdateException>(failure.InnerException);
        db.ChangeTracker.Clear();
        var persisted = await db.MachineRequestPayments.AsNoTracking().SingleAsync();
        Assert.AreEqual(MachineRequestPreparationStatus.Pending, persisted.PreparationStatus);
        Assert.IsNull(persisted.ReadyAtUtc);
        Assert.IsNull(persisted.ReadyByUserId);
        Assert.AreEqual(0, await db.EmailOutbox.CountAsync());
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public DiagLinkDbContext Db { get; }
        public MachineRequestPaymentStore Store { get; }
        public Guid PaymentId { get; } = Guid.NewGuid();
        public Guid AdminId { get; } = Guid.NewGuid();
        public MachineRequestRecord Request { get; }

        private Fixture(MachineRequestKind kind, MachineRequestPaymentStatus status)
        {
            Db = new(new DbContextOptionsBuilder<DiagLinkDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            Store = new(Db);
            var requestId = PaymentId.ToString("N");
            var now = DateTime.UtcNow;
            var company = Guid.NewGuid();
            var requester = Guid.NewGuid();
            var target = Guid.NewGuid();
            Db.MachineRequestPayments.Add(new WebApp.Api.Models.Entities.MachineRequestPayment
            {
                Id = PaymentId, RequestKind = kind, Status = status, EstimatedTotalPages = 10,
                AmountCents = 9990, Currency = "EUR", Email = kind == MachineRequestKind.AdditionalMachine
                    ? null : "client@example.test", MachineRequestId = requestId, RequestLinkedAtUtc = now,
                CompanyId = kind == MachineRequestKind.InitialMachine ? null : company,
                RequestedByUserId = kind == MachineRequestKind.InitialMachine ? null : requester,
                TargetMachineId = kind == MachineRequestKind.AdditionalDocuments ? target : null,
                CreatedAtUtc = now, UpdatedAtUtc = now,
                AuthorizedAtUtc = status is MachineRequestPaymentStatus.Authorized or MachineRequestPaymentStatus.Captured
                    or MachineRequestPaymentStatus.Cancelled ? now : null,
                CapturedAtUtc = status == MachineRequestPaymentStatus.Captured ? now : null,
                CancelledAtUtc = status is MachineRequestPaymentStatus.Cancelled or MachineRequestPaymentStatus.Abandoned ? now : null,
                StripePaymentIntentId = status is MachineRequestPaymentStatus.Authorized or MachineRequestPaymentStatus.Captured
                    or MachineRequestPaymentStatus.Cancelled ? "pi_test" : null,
                AuthorizationEventId = status is MachineRequestPaymentStatus.Authorized or MachineRequestPaymentStatus.Captured
                    or MachineRequestPaymentStatus.Cancelled ? "evt_test" : null
            });
            Db.SaveChanges();
            Request = new(requestId, DateTimeOffset.UtcNow, MachineRequestStatuses.Treated,
                new("Alice", "Martin", "Garage", "client@example.test", "+331"),
                new("Machine A", "Maker", "Model", null, null), [],
                new(10, kind == MachineRequestKind.AdditionalDocuments ? 0 : 400, 0, 99.9m, .27m, 99.9m, 29.9m),
                new(PaymentId, 10, 9990, "EUR", 10, 9990, 0, 0, false), kind,
                kind == MachineRequestKind.InitialMachine ? null : company,
                kind == MachineRequestKind.InitialMachine ? null : requester,
                kind == MachineRequestKind.AdditionalDocuments ? target : null);
        }

        public static Fixture Create(MachineRequestKind kind, MachineRequestPaymentStatus status) => new(kind, status);
        public async Task<WebApp.Api.Services.MachineRequestPayment> MarkReadyAsync() =>
            await Store.MarkReadyAsync((await Store.GetAsync(PaymentId, default))!, Request, AdminId, default);
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class FailingSender : ITransactionalEmailSender
    {
        public Task<string?> SendAsync(string recipientEmail, string subject, string textBody,
            string? htmlBody, CancellationToken cancellationToken,
            string? replyToEmail = null, string? replyToName = null) =>
            Task.FromException<string?>(new IOException("ACS unavailable"));
    }

    private sealed class SuccessfulSender : ITransactionalEmailSender
    {
        public Task<string?> SendAsync(string recipientEmail, string subject, string textBody,
            string? htmlBody, CancellationToken cancellationToken,
            string? replyToEmail = null, string? replyToName = null) => Task.FromResult<string?>("acs-ready");
    }
}
