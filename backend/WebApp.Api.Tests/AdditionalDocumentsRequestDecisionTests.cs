using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebApp.Api.Data;
using WebApp.Api.Models;
using WebApp.Api.Models.Entities;
using WebApp.Api.Services;
using Payment = WebApp.Api.Services.MachineRequestPayment;

namespace WebApp.Api.Tests;

[TestClass]
public class AdditionalDocumentsRequestDecisionTests
{
    [TestMethod]
    public async Task AcceptAuthorizedCapturesExactAmountAndTreatsWithoutProvisioning()
    {
        await using var f = await Fixture.CreateAsync(pages: 1);
        var result = await f.Decisions.AcceptAsync(f.RequestId, default);
        Assert.IsNotNull(result); Assert.AreEqual("captured", result.Payment.Status);
        Assert.AreEqual(MachineRequestStatuses.Treated, result.Request.Status);
        Assert.AreEqual(50, f.Gateway.CapturedAmount); Assert.AreEqual(1, f.Gateway.CaptureMutations);
        var row = await f.Db.MachineRequestPayments.SingleAsync();
        Assert.AreEqual(MachineRequestPaymentStatus.Captured, row.Status);
        Assert.AreEqual(MachineRequestProvisioningStage.AwaitingAcceptance, row.ProvisioningStage);
        Assert.IsNull(row.ActivatedAtUtc); Assert.IsNull(row.FirstPeriodEndUtc); Assert.IsNull(row.ServiceAmountCents);
        Assert.IsNull(row.FinalCaptureAmountCents); Assert.IsNull(row.ProvisioningCompletedAtUtc);
        var outbox = await f.Db.EmailOutbox.SingleAsync();
        Assert.AreEqual(EmailNotificationType.RequestAccepted, outbox.NotificationType);
        StringAssert.Contains(outbox.PayloadJson, "\"capturedAmountCents\":50");
        Assert.AreEqual(0, await f.Db.MachineBillingPeriods.CountAsync()); Assert.AreEqual(0, await f.Db.CompanyWallets.CountAsync());
        Assert.AreEqual(0, await f.Db.CreditLedger.CountAsync()); Assert.AreEqual(0, await f.Db.StripeMachineAdditions.CountAsync());
    }

    [TestMethod]
    public async Task RejectAuthorizedCancelsAndRejectsWithoutCapture()
    {
        await using var f = await Fixture.CreateAsync(pages: 1);
        var result = await f.Decisions.RejectAsync(f.RequestId, default);
        Assert.IsNotNull(result); Assert.AreEqual("cancelled", result.Payment.Status);
        Assert.AreEqual(MachineRequestStatuses.Rejected, result.Request.Status);
        Assert.AreEqual(1, f.Gateway.CancelMutations); Assert.AreEqual(50, f.Gateway.CancelledAmount);
        Assert.AreEqual(0, f.Gateway.CaptureMutations);
        Assert.AreEqual(EmailNotificationType.RequestRejected,
            (await f.Db.EmailOutbox.SingleAsync()).NotificationType);
    }

    [TestMethod]
    public async Task StripeTerminalBeforeSqlIsRecoveredWithoutSecondMutation()
    {
        await using var captured = await Fixture.CreateAsync(); captured.Gateway.StripeStatus = "captured";
        Assert.AreEqual("captured", (await captured.Decisions.AcceptAsync(captured.RequestId, default))!.Payment.Status);
        Assert.AreEqual(0, captured.Gateway.CaptureMutations);

        await using var cancelled = await Fixture.CreateAsync(); cancelled.Gateway.StripeStatus = "cancelled";
        Assert.AreEqual("cancelled", (await cancelled.Decisions.RejectAsync(cancelled.RequestId, default))!.Payment.Status);
        Assert.AreEqual(0, cancelled.Gateway.CancelMutations);
    }

    [TestMethod]
    public async Task SqlTerminalWithPendingBlobFinishesBlobAndDoubleClickIsIdempotent()
    {
        await using var accepted = await Fixture.CreateAsync(); await accepted.SetSqlStatusAsync(MachineRequestPaymentStatus.Captured);
        await accepted.Decisions.AcceptAsync(accepted.RequestId, default); await accepted.Decisions.AcceptAsync(accepted.RequestId, default);
        Assert.AreEqual(MachineRequestStatuses.Treated, (await accepted.Storage.GetAsync(accepted.RequestId))!.Status);
        Assert.AreEqual(0, accepted.Gateway.CaptureMutations);
        Assert.AreEqual(1, await accepted.Db.EmailOutbox.CountAsync());

        await using var rejected = await Fixture.CreateAsync(); await rejected.SetSqlStatusAsync(MachineRequestPaymentStatus.Cancelled);
        await rejected.Decisions.RejectAsync(rejected.RequestId, default); await rejected.Decisions.RejectAsync(rejected.RequestId, default);
        Assert.AreEqual(MachineRequestStatuses.Rejected, (await rejected.Storage.GetAsync(rejected.RequestId))!.Status);
        Assert.AreEqual(0, rejected.Gateway.CancelMutations);
        Assert.AreEqual(1, await rejected.Db.EmailOutbox.CountAsync());
    }

    [TestMethod]
    public async Task RejectAfterCaptureAndAcceptAfterCancellationAreForbidden()
    {
        await using var captured = await Fixture.CreateAsync(); await captured.Decisions.AcceptAsync(captured.RequestId, default);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => captured.Decisions.RejectAsync(captured.RequestId, default));
        await using var cancelled = await Fixture.CreateAsync(); await cancelled.Decisions.RejectAsync(cancelled.RequestId, default);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => cancelled.Decisions.AcceptAsync(cancelled.RequestId, default));
    }

    [TestMethod]
    public async Task StripeDecisionFailureCreatesNoOutboxAndLeavesAuthorized()
    {
        await using var accepted = await Fixture.CreateAsync();
        accepted.Gateway.OperationError = new InvalidOperationException("capture failed");
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            accepted.Decisions.AcceptAsync(accepted.RequestId, default));
        Assert.AreEqual(MachineRequestPaymentStatus.Authorized,
            (await accepted.Db.MachineRequestPayments.SingleAsync()).Status);
        Assert.AreEqual(0, await accepted.Db.EmailOutbox.CountAsync());

        await using var rejected = await Fixture.CreateAsync();
        rejected.Gateway.OperationError = new InvalidOperationException("cancel failed");
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            rejected.Decisions.RejectAsync(rejected.RequestId, default));
        Assert.AreEqual(MachineRequestPaymentStatus.Authorized,
            (await rejected.Db.MachineRequestPayments.SingleAsync()).Status);
        Assert.AreEqual(0, await rejected.Db.EmailOutbox.CountAsync());
    }

    [TestMethod]
    public async Task PendingWrongKindMissingMachineAndIncoherentJsonAreRejectedBeforeStripe()
    {
        foreach (var invalid in new[] { "pending", "kind", "machine", "amount" })
        {
            await using var f = await Fixture.CreateAsync(invalid);
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => f.Decisions.AcceptAsync(f.RequestId, default));
            Assert.AreEqual(0, f.Gateway.CaptureMutations);
        }
    }

    [TestMethod]
    public async Task RequesterCanAbandonLegacyPendingAttemptWithoutStripeOrCurrentPricingValidation()
    {
        await using var f = await Fixture.CreateAsync("legacy-pending", pages: 1);

        var result = await f.Decisions.AbandonAsync(f.RequestId, f.Context, default);
        var row = await f.Db.MachineRequestPayments.SingleAsync();

        Assert.IsNotNull(result);
        Assert.AreEqual("abandoned", result.Payment.Status);
        Assert.AreEqual(MachineRequestStatuses.Rejected, result.Request.Status);
        Assert.AreEqual(MachineRequestPaymentStatus.Abandoned, row.Status);
        Assert.AreEqual(27, row.AmountCents);
        Assert.IsNotNull(row.CancelledAtUtc);
        Assert.IsNull(row.StripePaymentIntentId); Assert.IsNull(row.AuthorizationEventId); Assert.IsNull(row.AuthorizedAtUtc);
        Assert.AreEqual(0, f.Gateway.CancelMutations);
        Assert.AreEqual(0, f.Gateway.AbandonCheckoutCalls);
        Assert.AreEqual(0, await f.Db.EmailOutbox.CountAsync());
    }

    [TestMethod]
    public async Task RequesterCanAbandonNewMinimumAmountPendingAttempt()
    {
        await using var f = await Fixture.CreateAsync("pending-no-session", pages: 1);

        await f.Decisions.AbandonAsync(f.RequestId, f.Context, default);
        await f.Decisions.AbandonAsync(f.RequestId, f.Context, default);

        var row = await f.Db.MachineRequestPayments.SingleAsync();
        Assert.AreEqual(MachineRequestPaymentStatus.Abandoned, row.Status);
        Assert.AreEqual(50, row.AmountCents);
        Assert.AreEqual(0, f.Gateway.CancelMutations);
        Assert.AreEqual(0, f.Gateway.AbandonCheckoutCalls);
        Assert.AreEqual(0, await f.Db.EmailOutbox.CountAsync());
    }

    [TestMethod]
    public async Task RequesterAbandonmentCancelsAuthorizedPaymentAndIsIdempotent()
    {
        await using var f = await Fixture.CreateAsync();

        await f.Decisions.AbandonAsync(f.RequestId, f.Context, default);
        await f.Decisions.AbandonAsync(f.RequestId, f.Context, default);

        Assert.AreEqual(1, f.Gateway.CancelMutations);
        Assert.AreEqual(MachineRequestPaymentStatus.Cancelled,
            (await f.Db.MachineRequestPayments.SingleAsync()).Status);
        Assert.AreEqual(MachineRequestStatuses.Rejected, (await f.Storage.GetAsync(f.RequestId))!.Status);
        Assert.AreEqual(0, await f.Db.EmailOutbox.CountAsync());
    }

    [TestMethod]
    public async Task RequesterCannotAbandonCapturedOrAnotherCompanyAttempt()
    {
        await using var captured = await Fixture.CreateAsync();
        await captured.SetSqlStatusAsync(MachineRequestPaymentStatus.Captured);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            captured.Decisions.AbandonAsync(captured.RequestId, captured.Context, default));

        await using var foreign = await Fixture.CreateAsync();
        var wrongContext = foreign.Context with { CompanyId = Guid.NewGuid() };
        await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() =>
            foreign.Decisions.AbandonAsync(foreign.RequestId, wrongContext, default));
        Assert.AreEqual(0, foreign.Gateway.CancelMutations);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public DiagLinkDbContext Db { get; } public BlobFake Blobs { get; } = new(); public DecisionGateway Gateway { get; } = new();
        public MachineRequestStorageService Storage { get; } public AdditionalDocumentsRequestDecisionService Decisions { get; }
        public AdditionalDocumentsContext Context { get; private set; } = null!;
        public Guid PaymentId { get; } = Guid.NewGuid(); public string RequestId => PaymentId.ToString("N");
        private Fixture(DiagLinkDbContext db)
        {
            Db = db; Storage = new(Blobs); var payments = new MachineRequestPaymentService(new MachineRequestPaymentStore(db), Gateway);
            Decisions = new(Storage, payments, db);
        }
        public static async Task<Fixture> CreateAsync(string? invalid = null, int pages = 370)
        {
            var db = new DiagLinkDbContext(new DbContextOptionsBuilder<DiagLinkDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            var f = new Fixture(db); var company = Guid.NewGuid(); var user = Guid.NewGuid(); var machine = Guid.NewGuid(); var now = DateTime.UtcNow;
            db.Companies.Add(new Company { Id = company, Name = "Company", Status = "active", CreatedAtUtc = now, UpdatedAtUtc = now });
            db.Users.Add(new User { Id = user, CompanyId = company, Email = "admin@test", Role = DiagLinkRoles.CompanyAdmin, Status = "active", CreatedAt = now, UpdatedAt = now });
            if (invalid != "machine") db.Machines.Add(new Machine { Id = machine, CompanyId = company, Name = "Machine", Status = "active", CreatedAtUtc = now, UpdatedAtUtc = now });
            db.BillingAccounts.Add(new BillingAccount { Id = Guid.NewGuid(), CompanyId = company, StripeCustomerId = "cus_existing", CreatedAtUtc = now, UpdatedAtUtc = now });
            var legacyPending = invalid == "legacy-pending";
            var pendingWithoutSession = invalid is "legacy-pending" or "pending-no-session";
            var amountCents = legacyPending ? 27 : AdditionalDocumentsPricing.CalculateAmountCents(pages);
            db.MachineRequestPayments.Add(new WebApp.Api.Models.Entities.MachineRequestPayment
            {
                Id = f.PaymentId, RequestKind = invalid == "kind" ? MachineRequestKind.AdditionalMachine : MachineRequestKind.AdditionalDocuments,
                RequestedByUserId = user, CompanyId = company, TargetMachineId = machine,
                Status = invalid is "pending" or "legacy-pending" or "pending-no-session" ? MachineRequestPaymentStatus.Pending : MachineRequestPaymentStatus.Authorized,
                EstimatedTotalPages = pages, AmountCents = amountCents, Currency = "EUR", MachineRequestId = f.RequestId,
                Email = "admin@test",
                StripeSessionId = pendingWithoutSession ? null : "cs_docs", StripePaymentIntentId = pendingWithoutSession ? null : "pi_docs",
                AuthorizationEventId = pendingWithoutSession ? null : "evt_docs",
                CreatedAtUtc = now, UpdatedAtUtc = now, AuthorizedAtUtc = pendingWithoutSession ? null : now, RequestLinkedAtUtc = now
            });
            await db.SaveChangesAsync();
            var bytes = "pdf"u8.ToArray(); var reference = new MachineRequestPaymentReference(f.PaymentId, pages,
                invalid == "amount" ? amountCents + 1 : amountCents, "EUR", pages,
                invalid == "amount" ? amountCents + 1 : amountCents, 0, 0, false);
            await f.Storage.CreateAdditionalDocumentsAsync(new MachineRequestDraft(new("A", "Admin", "Company", "admin@test", ""),
                new("Machine", "", "", null, null)), [new("manual.pdf", "application/pdf", bytes.Length, pages, new MemoryStream(bytes))],
                f.RequestId, reference, company, user, machine);
            f.Context = new(user, company, machine, "cus_existing", "Company", "Machine", "admin@test", null, null, null);
            return f;
        }
        public async Task SetSqlStatusAsync(MachineRequestPaymentStatus status)
        { var row = await Db.MachineRequestPayments.SingleAsync(); row.Status = status; row.CapturedAtUtc = status == MachineRequestPaymentStatus.Captured ? DateTime.UtcNow : null; row.CancelledAtUtc = status is MachineRequestPaymentStatus.Cancelled or MachineRequestPaymentStatus.Abandoned ? DateTime.UtcNow : null; await Db.SaveChangesAsync(); }
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
    private sealed class DecisionGateway : IMachineRequestPaymentGateway
    {
        public string StripeStatus { get; set; } = "authorized"; public Exception? OperationError { get; set; }
        public int CaptureMutations, CancelMutations, AbandonCheckoutCalls; public long CapturedAmount, CancelledAmount;
        public Task<MachineRequestPaymentProof> CaptureAdditionalDocumentsAsync(Payment payment, AdditionalDocumentsContext context, CancellationToken ct)
        { if (OperationError is not null) return Task.FromException<MachineRequestPaymentProof>(OperationError); if (StripeStatus == "authorized") { CaptureMutations++; CapturedAmount = payment.AmountCents; StripeStatus = "captured"; } return Task.FromResult(new MachineRequestPaymentProof(StripeStatus, payment.StripePaymentIntentId!)); }
        public Task<MachineRequestPaymentProof> CancelAdditionalDocumentsAsync(Payment payment, AdditionalDocumentsContext context, CancellationToken ct)
        { if (OperationError is not null) return Task.FromException<MachineRequestPaymentProof>(OperationError); if (StripeStatus == "authorized") { CancelMutations++; CancelledAmount = payment.AmountCents; StripeStatus = "cancelled"; } return Task.FromResult(new MachineRequestPaymentProof(StripeStatus, payment.StripePaymentIntentId!)); }
        public Task<MachineRequestPaymentProof> AbandonAdditionalDocumentsCheckoutAsync(Payment payment, AdditionalDocumentsContext context, CancellationToken ct)
        { AbandonCheckoutCalls++; return Task.FromResult(new MachineRequestPaymentProof("abandoned", "")); }
        public Task<MachineRequestCheckout> CreateAsync(Payment payment, CancellationToken ct) => throw new AssertFailedException();
        public Task<MachineRequestPaymentProof> ReadAsync(Payment payment, string sessionId, CancellationToken ct) => throw new AssertFailedException();
        public Task<MachineRequestPaymentProof> CaptureAsync(Payment payment, CancellationToken ct) => throw new AssertFailedException();
        public Task<MachineRequestPaymentProof> CancelAsync(Payment payment, CancellationToken ct) => throw new AssertFailedException();
    }
    private sealed class BlobFake : IMachineRequestBlobClient
    {
        private readonly Dictionary<string, byte[]> data = new(StringComparer.Ordinal);
        public Task EnsurePrivateContainerAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public async Task UploadAsync(string name, Stream content, string type, bool overwrite, CancellationToken cancellationToken = default) { using var ms = new MemoryStream(); await content.CopyToAsync(ms, cancellationToken); if (!overwrite) data.Add(name, ms.ToArray()); else data[name] = ms.ToArray(); }
        public Task<Stream?> OpenReadAsync(string name, CancellationToken cancellationToken = default) => Task.FromResult<Stream?>(data.TryGetValue(name, out var b) ? new MemoryStream(b) : null);
        public async IAsyncEnumerable<string> ListNamesAsync(string? prefix = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) { foreach (var n in data.Keys.Where(n => prefix is null || n.StartsWith(prefix))) { yield return n; await Task.Yield(); } }
        public Task DeletePrefixAsync(string prefix, CancellationToken cancellationToken = default) { foreach (var n in data.Keys.Where(n => n.StartsWith(prefix)).ToList()) data.Remove(n); return Task.CompletedTask; }
    }
}
