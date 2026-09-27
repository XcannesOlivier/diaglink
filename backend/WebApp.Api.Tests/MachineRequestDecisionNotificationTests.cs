using System.Data.Common;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Stripe;
using WebApp.Api.Data;
using WebApp.Api.Models;
using WebApp.Api.Models.Entities;
using WebApp.Api.Services;

namespace WebApp.Api.Tests;

[TestClass]
public sealed class MachineRequestDecisionNotificationTests
{
    [TestMethod]
    public async Task InitialAcceptCapturesOnceAndFreezesFinalAmountInSingleOutbox()
    {
        await using var fixture = Fixture.Create();
        var first = await fixture.Service.CaptureFromAdminDecisionAsync(
            fixture.PaymentId, fixture.PreparationCents, fixture.Request, default);
        var retry = await fixture.Service.CaptureFromAdminDecisionAsync(
            fixture.PaymentId, fixture.PreparationCents, fixture.Request, default);

        Assert.AreEqual("captured", first!.Status);
        Assert.AreEqual("captured", retry!.Status);
        Assert.AreEqual(1, fixture.Gateway.CaptureCalls);
        var row = await fixture.Db.MachineRequestPayments.SingleAsync();
        Assert.AreEqual(MachineRequestPaymentStatus.Captured, row.Status);
        Assert.IsNotNull(row.CapturedAtUtc);
        Assert.IsNotNull(row.FinalCaptureAmountCents);
        var message = await fixture.Db.EmailOutbox.SingleAsync();
        Assert.AreEqual(EmailNotificationType.RequestAccepted, message.NotificationType);
        var payload = JsonSerializer.Deserialize<RequestAcceptedEmailPayload>(message.PayloadJson,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.AreEqual(row.FinalCaptureAmountCents, payload.CapturedAmountCents);
        Assert.AreEqual("client@example.test", message.RecipientEmail);
    }

    [TestMethod]
    public async Task InitialRejectCancelsOnceAndCreatesOneRejectedOutbox()
    {
        await using var fixture = Fixture.Create();
        await fixture.Service.CancelFromAdminDecisionAsync(fixture.PaymentId, fixture.Request, default);
        await fixture.Service.CancelFromAdminDecisionAsync(fixture.PaymentId, fixture.Request, default);

        Assert.AreEqual(1, fixture.Gateway.CancelCalls);
        var row = await fixture.Db.MachineRequestPayments.SingleAsync();
        Assert.AreEqual(MachineRequestPaymentStatus.Cancelled, row.Status);
        Assert.IsNotNull(row.CancelledAtUtc);
        Assert.AreEqual(EmailNotificationType.RequestRejected,
            (await fixture.Db.EmailOutbox.SingleAsync()).NotificationType);
    }

    [TestMethod]
    public async Task StripeFailureCreatesNoDecisionOutboxAndLeavesPaymentAuthorized()
    {
        await using var capture = Fixture.Create();
        capture.Gateway.CaptureFailure = new StripeException("capture failed");
        await Assert.ThrowsExactlyAsync<StripeException>(() => capture.Service.CaptureFromAdminDecisionAsync(
            capture.PaymentId, capture.PreparationCents, capture.Request, default));
        Assert.AreEqual(MachineRequestPaymentStatus.Authorized,
            (await capture.Db.MachineRequestPayments.SingleAsync()).Status);
        Assert.AreEqual(0, await capture.Db.EmailOutbox.CountAsync());

        await using var cancel = Fixture.Create();
        cancel.Gateway.CancelFailure = new StripeException("cancel failed");
        await Assert.ThrowsExactlyAsync<StripeException>(() => cancel.Service.CancelFromAdminDecisionAsync(
            cancel.PaymentId, cancel.Request, default));
        Assert.AreEqual(MachineRequestPaymentStatus.Authorized,
            (await cancel.Db.MachineRequestPayments.SingleAsync()).Status);
        Assert.AreEqual(0, await cancel.Db.EmailOutbox.CountAsync());
    }

    [TestMethod]
    public async Task TerminalHistoricalRowsAreNotBackfilledWithoutExplicitDecisionCall()
    {
        await using var captured = Fixture.Create(MachineRequestPaymentStatus.Captured);
        await using var cancelled = Fixture.Create(MachineRequestPaymentStatus.Cancelled);
        Assert.AreEqual(0, await captured.Db.EmailOutbox.CountAsync());
        Assert.AreEqual(0, await cancelled.Db.EmailOutbox.CountAsync());
    }

    [TestMethod]
    public async Task RelationalOutboxInsertFailureRollsBackFinancialTransition()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<DiagLinkDbContext>().UseSqlite(connection).Options;
        await using var db = new DiagLinkDbContext(options);
        await db.Database.EnsureCreatedAsync();
        // SQLite ignores SQL Server schemas; satisfy the optional MachineRequestPayment FK
        // whose principal table is otherwise emitted under the model's default schema name.
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS Companies (Id TEXT PRIMARY KEY);");
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS Machines (Id TEXT PRIMARY KEY);");
        var fixture = Fixture.Seed(db);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER fail_decision_outbox BEFORE INSERT ON EmailOutbox
            BEGIN SELECT RAISE(ABORT, 'forced outbox failure'); END;
            """);
        var payment = await fixture.Store.GetAsync(fixture.PaymentId, default);
        var notification = MachineRequestDecisionNotifications.Rejected(fixture.Request, payment!);

        var failure = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            fixture.Store.CompleteAdminDecisionAsync(payment!, "cancelled", notification, default));
        Assert.IsInstanceOfType<DbUpdateException>(failure.InnerException);
        db.ChangeTracker.Clear();
        var persisted = await db.MachineRequestPayments.AsNoTracking().SingleAsync();
        Assert.AreEqual(MachineRequestPaymentStatus.Authorized, persisted.Status);
        Assert.IsNull(persisted.CancelledAtUtc);
        Assert.AreEqual(0, await db.EmailOutbox.CountAsync());
    }

    [TestMethod]
    public async Task InitialAcceptRunsTransactionInsideSqlServerRetryingExecutionStrategy()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var probe = new DecisionTransactionProbe();
        var options = new DbContextOptionsBuilder<DiagLinkDbContext>()
            .UseSqlite(connection)
            .ReplaceService<IExecutionStrategyFactory, FinancialExecutionStrategyTests.RetryFactory>()
            .AddInterceptors(probe)
            .Options;
        await using var db = new DecisionDbContext(options);
        await db.Database.EnsureCreatedAsync();
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS Companies (Id TEXT PRIMARY KEY);");
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS Machines (Id TEXT PRIMARY KEY);");
        var fixture = Fixture.Seed(db);

        var result = await fixture.Service.CaptureFromAdminDecisionAsync(
            fixture.PaymentId, fixture.PreparationCents, fixture.Request, default);

        Assert.AreEqual("captured", result!.Status);
        Assert.IsTrue(probe.ObservedRetryingStrategy);
        Assert.AreEqual(MachineRequestPaymentStatus.Captured,
            (await db.MachineRequestPayments.AsNoTracking().SingleAsync()).Status);
        Assert.AreEqual(1, await db.EmailOutbox.CountAsync());
    }

    [TestMethod]
    public void AcceptedAndRejectedTemplatesHaveExactStageSemantics()
    {
        foreach (var kind in Enum.GetValues<MachineRequestKind>())
        {
            var accepted = new RequestAcceptedEmailPayload(kind, Guid.NewGuid().ToString("N"), "Alice",
                "client@example.test", "Machine A", 11403, "EUR");
            var acceptedContent = RequestReceivedEmailTemplate.Build(EmailNotificationType.RequestAccepted,
                JsonSerializer.Serialize(accepted, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            StringAssert.Contains(acceptedContent.TextBody, "114,03");
            StringAssert.Contains(acceptedContent.TextBody, "encaissé");
            Assert.IsFalse(acceptedContent.TextBody.Contains("machine est prête", StringComparison.OrdinalIgnoreCase));
            Assert.IsFalse(acceptedContent.TextBody.Contains("documents sont disponibles", StringComparison.OrdinalIgnoreCase));
            Assert.IsFalse(acceptedContent.TextBody.Contains("documents intégrés", StringComparison.OrdinalIgnoreCase));

            var rejected = new RequestRejectedEmailPayload(kind, accepted.MachineRequestId, "Alice",
                accepted.RecipientEmail, accepted.MachineName, "EUR");
            var rejectedContent = RequestReceivedEmailTemplate.Build(EmailNotificationType.RequestRejected,
                JsonSerializer.Serialize(rejected, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            StringAssert.Contains(rejectedContent.TextBody, "n’a pas été acceptée");
            StringAssert.Contains(rejectedContent.TextBody, "Aucun montant n’a été encaissé");
        }
    }

    [TestMethod]
    public async Task WorkerSendsAcceptedAndRejectedAndPersistsProviderIds()
    {
        await using var fixture = Fixture.Create();
        var outbox = new EmailOutboxStore(fixture.Db, TimeProvider.System);
        var accepted = MachineRequestDecisionNotifications.Accepted(fixture.Request,
            (await fixture.Store.GetAsync(fixture.PaymentId, default))! with
            { FinalCaptureAmountCents = fixture.PreparationCents });
        var rejected = MachineRequestDecisionNotifications.Rejected(fixture.Request,
            (await fixture.Store.GetAsync(fixture.PaymentId, default))!);
        await outbox.EnqueueAsync(accepted);
        await outbox.EnqueueAsync(rejected);
        var sender = new Sender();
        var processor = new EmailOutboxProcessor(outbox, sender, NullLogger<EmailOutboxProcessor>.Instance);

        Assert.AreEqual(2, await processor.ProcessOnceAsync());
        var messages = await fixture.Db.EmailOutbox.OrderBy(item => item.NotificationType).ToListAsync();
        Assert.IsTrue(messages.All(item => item.Status == EmailOutboxStatus.Sent));
        Assert.IsTrue(messages.All(item => item.ProviderOperationId == "acs-decision"));
        Assert.AreEqual(2, sender.Calls);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public DiagLinkDbContext Db { get; }
        public MachineRequestPaymentStore Store { get; }
        public Gateway Gateway { get; } = new();
        public MachineRequestPaymentService Service { get; }
        public Guid PaymentId { get; private init; }
        public long PreparationCents { get; private set; }
        public MachineRequestRecord Request { get; private set; } = null!;

        private Fixture(DiagLinkDbContext db)
        {
            Db = db;
            Store = new(db);
            Service = new(Store, Gateway, new FixedTimeProvider());
        }

        public static Fixture Create(MachineRequestPaymentStatus status = MachineRequestPaymentStatus.Authorized)
        {
            var db = new DiagLinkDbContext(new DbContextOptionsBuilder<DiagLinkDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            return Seed(db, status);
        }

        public static Fixture Seed(DiagLinkDbContext db,
            MachineRequestPaymentStatus status = MachineRequestPaymentStatus.Authorized)
        {
            var fixture = new Fixture(db) { PaymentId = Guid.NewGuid() };
            fixture.PreparationCents = MachineRequestPreparationPricing.CalculatePreparationCents(400);
            var now = DateTime.UtcNow;
            var amount = MachineRequestPreparationPricing.CalculateMaximumAuthorizationCents(400);
            var requestId = fixture.PaymentId.ToString("N");
            var entity = new WebApp.Api.Models.Entities.MachineRequestPayment
            {
                Id = fixture.PaymentId,
                RequestKind = MachineRequestKind.InitialMachine,
                Status = status,
                EstimatedTotalPages = 400,
                AmountCents = amount,
                Currency = "EUR",
                Email = "client@example.test",
                StripeSessionId = "cs_test",
                StripePaymentIntentId = "pi_test",
                AuthorizationEventId = "evt_test",
                MachineRequestId = requestId,
                RequestLinkedAtUtc = now,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                AuthorizedAtUtc = now,
                CapturedAtUtc = status == MachineRequestPaymentStatus.Captured ? now : null,
                CancelledAtUtc = status == MachineRequestPaymentStatus.Cancelled ? now : null,
                FinalCaptureAmountCents = status == MachineRequestPaymentStatus.Captured ? fixture.PreparationCents : null,
                RowVersion = [1]
            };
            if (db.Database.IsSqlite())
            {
                db.Database.ExecuteSqlInterpolated($"""
                    INSERT INTO MachineRequestPayments
                    (Id, RequestKind, Status, EstimatedTotalPages, AmountCents, Currency, Email,
                     StripeSessionId, StripePaymentIntentId, AuthorizationEventId, MachineRequestId,
                     CreatedAtUtc, UpdatedAtUtc, AuthorizedAtUtc, RequestLinkedAtUtc, ProvisioningStage, RowVersion)
                    VALUES ({entity.Id}, {(int)entity.RequestKind}, {(int)entity.Status}, {entity.EstimatedTotalPages},
                     {entity.AmountCents}, {entity.Currency}, {entity.Email}, {entity.StripeSessionId},
                     {entity.StripePaymentIntentId}, {entity.AuthorizationEventId}, {entity.MachineRequestId},
                     {entity.CreatedAtUtc}, {entity.UpdatedAtUtc}, {entity.AuthorizedAtUtc}, {entity.RequestLinkedAtUtc},
                     {(int)entity.ProvisioningStage}, {entity.RowVersion})
                    """);
            }
            else
            {
                db.MachineRequestPayments.Add(entity);
                db.SaveChanges();
            }
            fixture.Request = new(requestId, DateTimeOffset.UtcNow, MachineRequestStatuses.Pending,
                new("Alice", "Martin", "Garage", "client@example.test", "+33123456789"),
                new("Machine A", "Maker", "Model", null, null),
                [new("manual.pdf", $"{requestId}/documents/manual.pdf", 10, 400)],
                new(400, 400, 0, 99.90m, 0.27m, 99.90m, 29.90m),
                new(fixture.PaymentId, 400, amount, "EUR", 400, amount, 0, 0, false));
            return fixture;
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 9, 25, 10, 0, 0, TimeSpan.Zero);
    }

    private sealed class DecisionTransactionProbe : DbTransactionInterceptor
    {
        public bool ObservedRetryingStrategy { get; private set; }

        public override ValueTask<DbTransaction> TransactionStartedAsync(DbConnection connection,
            TransactionEndEventData eventData, DbTransaction result, CancellationToken cancellationToken = default)
        {
            ObservedRetryingStrategy = ExecutionStrategy.Current is SqlServerRetryingExecutionStrategy;
            return ValueTask.FromResult(result);
        }
    }

    private sealed class DecisionDbContext(DbContextOptions<DiagLinkDbContext> options) : DiagLinkDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            var rowVersion = modelBuilder.Entity<EmailOutbox>().Property(item => item.RowVersion)
                .ValueGeneratedNever().Metadata;
            rowVersion.SetBeforeSaveBehavior(PropertySaveBehavior.Save);
            rowVersion.SetAfterSaveBehavior(PropertySaveBehavior.Save);
        }
    }

    private sealed class Gateway : IMachineRequestPaymentGateway
    {
        public int CaptureCalls { get; private set; }
        public int CancelCalls { get; private set; }
        public Exception? CaptureFailure { get; set; }
        public Exception? CancelFailure { get; set; }
        public Task<MachineRequestPaymentProof> CaptureAsync(WebApp.Api.Services.MachineRequestPayment payment,
            CancellationToken ct)
        {
            CaptureCalls++;
            if (CaptureFailure is not null) return Task.FromException<MachineRequestPaymentProof>(CaptureFailure);
            return Task.FromResult(new MachineRequestPaymentProof("captured", payment.StripePaymentIntentId!));
        }
        public Task<MachineRequestPaymentProof> CancelAsync(WebApp.Api.Services.MachineRequestPayment payment,
            CancellationToken ct)
        {
            CancelCalls++;
            if (CancelFailure is not null) return Task.FromException<MachineRequestPaymentProof>(CancelFailure);
            return Task.FromResult(new MachineRequestPaymentProof("cancelled", payment.StripePaymentIntentId!));
        }
        public Task<MachineRequestCheckout> CreateAsync(WebApp.Api.Services.MachineRequestPayment payment,
            CancellationToken ct) => throw new AssertFailedException();
        public Task<MachineRequestPaymentProof> ReadAsync(WebApp.Api.Services.MachineRequestPayment payment,
            string sessionId, CancellationToken ct) => throw new AssertFailedException();
    }

    private sealed class Sender : ITransactionalEmailSender
    {
        public int Calls { get; private set; }
        public Task<string?> SendAsync(string recipientEmail, string subject, string textBody,
            string? htmlBody, CancellationToken cancellationToken,
            string? replyToEmail = null, string? replyToName = null)
        {
            Calls++;
            return Task.FromResult<string?>("acs-decision");
        }
    }
}
