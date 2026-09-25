using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebApp.Api.Data;
using WebApp.Api.Models;
using WebApp.Api.Models.Entities;
using WebApp.Api.Services;

namespace WebApp.Api.Tests;

[TestClass]
public sealed class RequestReceivedNotificationTests
{
    [TestMethod]
    [DataRow(MachineRequestKind.InitialMachine)]
    [DataRow(MachineRequestKind.AdditionalMachine)]
    public async Task AuthorizedWithoutCompleteRequestProducesNothing(MachineRequestKind kind)
    {
        await using var fixture = Fixture.Create(kind, MachineRequestPaymentStatus.Authorized);
        Assert.IsFalse(await fixture.Notifications.TryEnqueueAsync(fixture.PaymentId));
        Assert.AreEqual(0, await fixture.Db.EmailOutbox.CountAsync());
    }

    [TestMethod]
    [DataRow(MachineRequestKind.InitialMachine)]
    [DataRow(MachineRequestKind.AdditionalMachine)]
    [DataRow(MachineRequestKind.AdditionalDocuments)]
    public async Task AuthorizedCompletePendingRequestProducesExactlyOneFrozenNotification(MachineRequestKind kind)
    {
        await using var fixture = Fixture.Create(kind, MachineRequestPaymentStatus.Authorized);
        await fixture.CreateRequestAsync();

        Assert.IsTrue(await fixture.Notifications.TryEnqueueAsync(fixture.PaymentId));
        Assert.IsTrue(await fixture.Notifications.TryEnqueueAsync(fixture.PaymentId));
        Assert.AreEqual(0, await fixture.Notifications.ReconcileAsync());

        var message = await fixture.Db.EmailOutbox.SingleAsync();
        Assert.AreEqual(EmailNotificationType.RequestReceived, message.NotificationType);
        Assert.AreEqual("client@example.test", message.RecipientEmail);
        var payload = JsonSerializer.Deserialize<RequestReceivedEmailPayload>(message.PayloadJson,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.AreEqual(kind, payload.RequestKind);
        Assert.AreEqual(fixture.RequestId, payload.MachineRequestId);
        Assert.AreEqual("Alice", payload.FirstName);
        Assert.AreEqual("Machine A", payload.MachineName);
        Assert.AreEqual(12980L, payload.AuthorizedAmountCents);
        Assert.AreEqual("EUR", payload.Currency);
    }

    [TestMethod]
    public async Task AdditionalDocumentsPendingProducesNothingThenAuthorizedReconciliationProducesOne()
    {
        await using var fixture = Fixture.Create(MachineRequestKind.AdditionalDocuments,
            MachineRequestPaymentStatus.Pending);
        await fixture.CreateRequestAsync();
        Assert.IsFalse(await fixture.Notifications.TryEnqueueAsync(fixture.PaymentId));
        Assert.AreEqual(0, await fixture.Db.EmailOutbox.CountAsync());

        var payment = await fixture.Db.MachineRequestPayments.SingleAsync();
        payment.Status = MachineRequestPaymentStatus.Authorized;
        payment.StripePaymentIntentId = "pi_authorized";
        payment.AuthorizationEventId = "evt_authorized";
        payment.AuthorizedAtUtc = DateTime.UtcNow;
        await fixture.Db.SaveChangesAsync();

        Assert.AreEqual(1, await fixture.Notifications.ReconcileAsync());
        Assert.AreEqual(0, await fixture.Notifications.ReconcileAsync());
        Assert.AreEqual(1, await fixture.Db.EmailOutbox.CountAsync());
    }

    [TestMethod]
    [DataRow(MachineRequestKind.InitialMachine, "encaissé")]
    [DataRow(MachineRequestKind.AdditionalMachine, "machine prête")]
    [DataRow(MachineRequestKind.AdditionalDocuments, "documents intégrés")]
    public void TemplateUsesAuthorizationLanguageOnly(MachineRequestKind kind, string forbidden)
    {
        var payload = new RequestReceivedEmailPayload(kind, Guid.NewGuid().ToString("N"), "Alice",
            "client@example.test", "Machine <A>", 12980, "EUR");
        var content = RequestReceivedEmailTemplate.Build(EmailNotificationType.RequestReceived,
            JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        Assert.AreEqual(RequestReceivedEmailTemplate.Subject, content.Subject);
        StringAssert.Contains(content.TextBody, "autorisation");
        StringAssert.Contains(content.TextBody, "réservée");
        Assert.IsFalse(content.TextBody.Contains(forbidden, StringComparison.OrdinalIgnoreCase));
        if (kind == MachineRequestKind.AdditionalDocuments)
            StringAssert.Contains(content.TextBody, "demande d’ajout de documents");
        StringAssert.Contains(content.HtmlBody, "Machine &lt;A&gt;");
    }

    [TestMethod]
    public async Task WorkerMarksSentAndStoresProviderOperationId()
    {
        await using var fixture = Fixture.Create(MachineRequestKind.InitialMachine,
            MachineRequestPaymentStatus.Authorized);
        await fixture.CreateRequestAsync();
        await fixture.Notifications.TryEnqueueAsync(fixture.PaymentId);
        var sender = new FakeSender("acs-operation-42");
        var processor = new EmailOutboxProcessor(fixture.Outbox, sender,
            NullLogger<EmailOutboxProcessor>.Instance);

        Assert.AreEqual(1, await processor.ProcessOnceAsync());
        var message = await fixture.Db.EmailOutbox.SingleAsync();
        Assert.AreEqual(EmailOutboxStatus.Sent, message.Status);
        Assert.AreEqual("acs-operation-42", message.ProviderOperationId);
        Assert.AreEqual(1, sender.CallCount);
    }

    [TestMethod]
    public async Task SenderFailureReschedulesOutboxAndDoesNotChangeAuthorizedPayment()
    {
        await using var fixture = Fixture.Create(MachineRequestKind.AdditionalDocuments,
            MachineRequestPaymentStatus.Authorized);
        await fixture.CreateRequestAsync();
        await fixture.Notifications.TryEnqueueAsync(fixture.PaymentId);
        var processor = new EmailOutboxProcessor(fixture.Outbox, new FakeSender(exception: new IOException("ACS unavailable")),
            NullLogger<EmailOutboxProcessor>.Instance);

        Assert.AreEqual(1, await processor.ProcessOnceAsync());
        var message = await fixture.Db.EmailOutbox.SingleAsync();
        Assert.AreEqual(EmailOutboxStatus.Pending, message.Status);
        Assert.AreEqual(1, message.AttemptCount);
        Assert.IsNull(message.LeaseId);
        Assert.AreEqual(MachineRequestPaymentStatus.Authorized,
            (await fixture.Db.MachineRequestPayments.SingleAsync()).Status);
    }

    private sealed class FakeSender(string? operationId = null, Exception? exception = null)
        : ITransactionalEmailSender
    {
        public int CallCount { get; private set; }
        public Task<string?> SendAsync(string recipientEmail, string subject, string textBody,
            string? htmlBody, CancellationToken cancellationToken)
        {
            CallCount++;
            return exception is null ? Task.FromResult(operationId) : Task.FromException<string?>(exception);
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public Guid PaymentId { get; } = Guid.NewGuid();
        public string RequestId => PaymentId.ToString("N");
        public DiagLinkDbContext Db { get; }
        public MachineRequestStorageService Storage { get; }
        public EmailOutboxStore Outbox { get; }
        public RequestReceivedNotificationService Notifications { get; }
        private readonly MachineRequestKind kind;
        private readonly Guid companyId = Guid.NewGuid();
        private readonly Guid userId = Guid.NewGuid();
        private readonly Guid machineId = Guid.NewGuid();

        private Fixture(MachineRequestKind kind, MachineRequestPaymentStatus status)
        {
            this.kind = kind;
            Db = new(new DbContextOptionsBuilder<DiagLinkDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            var now = DateTime.UtcNow;
            Db.MachineRequestPayments.Add(new WebApp.Api.Models.Entities.MachineRequestPayment
            {
                Id = PaymentId,
                RequestKind = kind,
                RequestedByUserId = kind == MachineRequestKind.InitialMachine ? null : userId,
                CompanyId = kind == MachineRequestKind.InitialMachine ? null : companyId,
                TargetMachineId = kind == MachineRequestKind.AdditionalDocuments ? machineId : null,
                Status = status,
                EstimatedTotalPages = 400,
                AmountCents = 12980,
                Currency = "EUR",
                Email = kind == MachineRequestKind.AdditionalMachine ? null : "client@example.test",
                MachineRequestId = RequestId,
                RequestLinkedAtUtc = now,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                StripePaymentIntentId = status == MachineRequestPaymentStatus.Authorized ? "pi_authorized" : null,
                AuthorizationEventId = status == MachineRequestPaymentStatus.Authorized ? "evt_authorized" : null,
                AuthorizedAtUtc = status == MachineRequestPaymentStatus.Authorized ? now : null
            });
            Db.SaveChanges();
            Storage = new(new MemoryBlobs());
            Outbox = new(Db, TimeProvider.System);
            Notifications = new(Db, new MachineRequestPaymentStore(Db), Storage, Outbox,
                NullLogger<RequestReceivedNotificationService>.Instance);
        }

        public static Fixture Create(MachineRequestKind kind, MachineRequestPaymentStatus status) => new(kind, status);

        public async Task CreateRequestAsync()
        {
            var draft = new MachineRequestDraft(
                new("Alice", "Martin", "Garage", "client@example.test", "+33123456789"),
                new("Machine A", "Maker", "Model", null, null));
            var bytes = Encoding.UTF8.GetBytes("%PDF-test");
            var uploads = new[] { new MachineRequestDocumentUpload("manual.pdf", "application/pdf", bytes.Length,
                400, new MemoryStream(bytes)) };
            var payment = new MachineRequestPaymentReference(PaymentId, 400, 12980, "EUR", 400, 12980, 0, 0, false);
            if (kind == MachineRequestKind.InitialMachine)
                await Storage.CreateAsync(draft, uploads, RequestId, payment);
            else if (kind == MachineRequestKind.AdditionalMachine)
                await Storage.CreateAdditionalAsync(draft, uploads, RequestId, payment, companyId, userId);
            else
                await Storage.CreateAdditionalDocumentsAsync(draft, uploads, RequestId, payment, companyId, userId, machineId);
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class MemoryBlobs : IMachineRequestBlobClient
    {
        private readonly Dictionary<string, byte[]> blobs = new(StringComparer.Ordinal);
        public Task EnsurePrivateContainerAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public async Task UploadAsync(string blobName, Stream content, string contentType, bool overwrite,
            CancellationToken cancellationToken = default)
        {
            if (!overwrite && blobs.ContainsKey(blobName)) throw new InvalidOperationException("Blob exists.");
            using var copy = new MemoryStream();
            await content.CopyToAsync(copy, cancellationToken);
            blobs[blobName] = copy.ToArray();
        }
        public Task<Stream?> OpenReadAsync(string blobName, CancellationToken cancellationToken = default) =>
            Task.FromResult(blobs.TryGetValue(blobName, out var value) ? (Stream)new MemoryStream(value) : null);
        public async IAsyncEnumerable<string> ListNamesAsync(string? prefix = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var name in blobs.Keys.Where(name => prefix is null || name.StartsWith(prefix, StringComparison.Ordinal)))
            { cancellationToken.ThrowIfCancellationRequested(); yield return name; }
            await Task.CompletedTask;
        }
        public Task DeletePrefixAsync(string prefix, CancellationToken cancellationToken = default)
        {
            foreach (var name in blobs.Keys.Where(name => name.StartsWith(prefix, StringComparison.Ordinal)).ToArray())
                blobs.Remove(name);
            return Task.CompletedTask;
        }
    }
}
