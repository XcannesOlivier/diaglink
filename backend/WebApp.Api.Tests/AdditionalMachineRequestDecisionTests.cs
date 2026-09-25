using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Stripe;
using WebApp.Api.Data;
using WebApp.Api.Models;
using WebApp.Api.Models.Entities;
using WebApp.Api.Services;

namespace WebApp.Api.Tests;

[TestClass]
public class AdditionalMachineRequestDecisionTests
{
    [TestMethod]
    public async Task CaptureFailureFreezesOnceAndRetryCreatesOneActiveMachineAtBusinessEntitiesCreated()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Gateway.CaptureError = new StripeException("temporary");
        var activation = new DateTimeOffset(2026, 9, 24, 10, 30, 0, TimeSpan.Zero);
        fixture.Clock.SetUtcNow(activation);

        await Assert.ThrowsExactlyAsync<StripeException>(() => fixture.Service.AcceptAsync(fixture.RequestId, default));
        var frozen = await fixture.Store.GetAsync(fixture.PaymentId, default);
        var expected = MachineRequestFirstPeriodPricing.Calculate(activation.UtcDateTime);
        Assert.IsNotNull(frozen);
        Assert.AreEqual("authorized", frozen.Status);
        Assert.AreEqual(MachineRequestProvisioningStage.AmountFinalized, frozen.ProvisioningStage);
        Assert.AreEqual(activation.UtcDateTime, frozen.ActivatedAtUtc);
        Assert.AreEqual(expected.FirstPeriodEndUtc, frozen.FirstPeriodEndUtc);
        Assert.AreEqual(expected.ServiceAmountCents, frozen.ServiceAmountCents);
        Assert.AreEqual(fixture.PreparationCents + 1000 + expected.ServiceAmountCents, frozen.FinalCaptureAmountCents);
        Assert.IsTrue(frozen.FinalCaptureAmountCents <= frozen.AmountCents);
        Assert.AreEqual(0, await fixture.Db.Machines.CountAsync());
        Assert.AreEqual(0, await fixture.Db.EmailOutbox.CountAsync());

        fixture.Gateway.CaptureError = null;
        fixture.Clock.SetUtcNow(activation.AddDays(1));
        var accepted = await fixture.Service.AcceptAsync(fixture.RequestId, default);
        var retry = await fixture.Service.AcceptAsync(fixture.RequestId, default);
        var completed = await fixture.Store.GetAsync(fixture.PaymentId, default);

        Assert.IsNotNull(accepted); Assert.IsNotNull(retry); Assert.IsNotNull(completed);
        Assert.AreEqual("captured", accepted.Payment.Status);
        Assert.AreEqual(MachineRequestProvisioningStage.BusinessEntitiesCreated, completed.ProvisioningStage);
        Assert.AreEqual(frozen.ActivatedAtUtc, completed.ActivatedAtUtc);
        Assert.AreEqual(frozen.FinalCaptureAmountCents, completed.FinalCaptureAmountCents);
        Assert.AreEqual(1, await fixture.Db.Machines.CountAsync());
        var machine = await fixture.Db.Machines.SingleAsync();
        Assert.AreEqual(fixture.CompanyId, machine.CompanyId);
        Assert.AreEqual("active", machine.Status);
        Assert.AreEqual("Compressor B", machine.Name);
        Assert.AreEqual("SERIAL-B", machine.Reference);
        Assert.AreEqual(machine.Id, completed.MachineId);
        Assert.AreEqual(completed.FinalCaptureAmountCents, fixture.Gateway.Captured!.FinalCaptureAmountCents);
        Assert.AreEqual(0, await fixture.Db.MachineBillingPeriods.CountAsync());
        Assert.AreEqual(0, await fixture.Db.CompanyWallets.CountAsync());
        Assert.AreEqual(0, await fixture.Db.StripeMachineAdditions.CountAsync());
        Assert.AreEqual(2, fixture.Blobs.Names.Count);
        var acceptedNotification = await fixture.Db.EmailOutbox.SingleAsync();
        Assert.AreEqual(EmailNotificationType.RequestAccepted, acceptedNotification.NotificationType);
        StringAssert.Contains(acceptedNotification.PayloadJson,
            $"\"capturedAmountCents\":{completed.FinalCaptureAmountCents}");
    }

    [TestMethod]
    public async Task RejectCancelsOnceMarksRejectedAndNeverCreatesMachine()
    {
        await using var fixture = await Fixture.CreateAsync();
        var rejected = await fixture.Service.RejectAsync(fixture.RequestId, default);
        var retry = await fixture.Service.RejectAsync(fixture.RequestId, default);
        Assert.AreEqual(MachineRequestStatuses.Rejected, rejected!.Request.Status);
        Assert.AreEqual("cancelled", retry!.Payment.Status);
        Assert.AreEqual(1, fixture.Gateway.CancelCalls);
        Assert.AreEqual(0, await fixture.Db.Machines.CountAsync());
        Assert.AreEqual(0, await fixture.Db.MachineBillingPeriods.CountAsync());
        Assert.AreEqual(0, await fixture.Db.CompanyWallets.CountAsync());
        Assert.AreEqual(EmailNotificationType.RequestRejected,
            (await fixture.Db.EmailOutbox.SingleAsync()).NotificationType);
    }

    [TestMethod]
    public async Task RejectAfterCaptureConflictsWithoutCancellation()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Service.AcceptAsync(fixture.RequestId, default);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.Service.RejectAsync(fixture.RequestId, default));
        Assert.AreEqual(0, fixture.Gateway.CancelCalls);
        Assert.AreEqual(1, await fixture.Db.Machines.CountAsync());
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public DiagLinkDbContext Db { get; }
        public Blobs Blobs { get; } = new();
        public Gateway Gateway { get; } = new();
        public Clock Clock { get; } = new();
        public MachineRequestPaymentStore Store { get; }
        public AdditionalMachineRequestDecisionService Service { get; private set; } = null!;
        public Guid PaymentId { get; } = Guid.NewGuid();
        public Guid CompanyId { get; } = Guid.NewGuid();
        public string RequestId => PaymentId.ToString("N");
        public long PreparationCents { get; } = MachineRequestPreparationPricing.CalculatePreparationCents(416);

        private Fixture()
        {
            Db = new DiagLinkDbContext(new DbContextOptionsBuilder<DiagLinkDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            Store = new MachineRequestPaymentStore(Db);
        }
        public static async Task<Fixture> CreateAsync()
        {
            var f = new Fixture(); var now = DateTime.UtcNow; var userId = Guid.NewGuid();
            f.Db.Companies.Add(new Company { Id = f.CompanyId, Name = "Company", Status = "active", CreatedAtUtc = now, UpdatedAtUtc = now });
            f.Db.Users.Add(new User { Id = userId, CompanyId = f.CompanyId, Email = "admin@test", Role = DiagLinkRoles.CompanyAdmin, Status = "active", CreatedAt = now, UpdatedAt = now });
            f.Db.BillingAccounts.Add(new BillingAccount { Id = Guid.NewGuid(), CompanyId = f.CompanyId, StripeCustomerId = "cus_test", StripeSubscriptionId = "sub_test", CreatedAtUtc = now, UpdatedAtUtc = now });
            f.Db.MachineRequestPayments.Add(new WebApp.Api.Models.Entities.MachineRequestPayment
            {
                Id = f.PaymentId, RequestKind = MachineRequestKind.AdditionalMachine, RequestedByUserId = userId,
                CompanyId = f.CompanyId, Status = MachineRequestPaymentStatus.Authorized, EstimatedTotalPages = 416,
                AmountCents = MachineRequestPreparationPricing.CalculateMaximumAuthorizationCents(416), Currency = "EUR",
                StripeSessionId = "cs_test", StripePaymentIntentId = "pi_test", AuthorizationEventId = "evt_test",
                MachineRequestId = f.RequestId, RequestLinkedAtUtc = now, AuthorizedAtUtc = now, CreatedAtUtc = now, UpdatedAtUtc = now
            });
            await f.Db.SaveChangesAsync();
            var resolver = new AdditionalMachinePaymentContextResolver(f.Db, new DiagLinkUserLookupService(f.Db));
            var paymentService = new MachineRequestPaymentService(f.Store, f.Gateway, resolver, f.Clock);
            f.Service = new AdditionalMachineRequestDecisionService(new MachineRequestStorageService(f.Blobs), paymentService, f.Store);
            var bytes = "pdf-content"u8.ToArray(); var upload = new MachineRequestDocumentUpload("manual.pdf", "application/pdf", bytes.Length, 416, new MemoryStream(bytes));
            await new MachineRequestStorageService(f.Blobs).CreateAdditionalAsync(
                new MachineRequestDraft(new("SQL", "Admin", "Company", "admin@test", "+331"),
                    new("Compressor B", "Atlas", "H23", "SERIAL-B", "Description")), [upload], f.RequestId,
                new(f.PaymentId, 416, MachineRequestPreparationPricing.CalculateMaximumAuthorizationCents(416), "EUR", 416,
                    MachineRequestPreparationPricing.CalculateMaximumAuthorizationCents(416), 0, 0, false),
                f.CompanyId, userId);
            return f;
        }
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset now = new(2026, 9, 24, 10, 30, 0, TimeSpan.Zero);
        public void SetUtcNow(DateTimeOffset value) => now = value;
        public override DateTimeOffset GetUtcNow() => now;
    }
    private sealed class Gateway : IMachineRequestPaymentGateway
    {
        public Exception? CaptureError { get; set; }
        public WebApp.Api.Services.MachineRequestPayment? Captured { get; private set; }
        public int CancelCalls { get; private set; }
        public Task<MachineRequestPaymentProof> CaptureAsync(WebApp.Api.Services.MachineRequestPayment payment, CancellationToken ct)
        { Captured = payment; if (CaptureError is not null) throw CaptureError; return Task.FromResult(new MachineRequestPaymentProof("captured", payment.StripePaymentIntentId!)); }
        public Task<MachineRequestPaymentProof> CancelAsync(WebApp.Api.Services.MachineRequestPayment payment, CancellationToken ct)
        { CancelCalls++; return Task.FromResult(new MachineRequestPaymentProof("cancelled", payment.StripePaymentIntentId!)); }
        public Task<MachineRequestCheckout> CreateAsync(WebApp.Api.Services.MachineRequestPayment payment, CancellationToken ct) => throw new AssertFailedException();
        public Task<MachineRequestCheckout> CreateAdditionalAsync(WebApp.Api.Services.MachineRequestPayment payment, AdditionalMachinePaymentContext context, CancellationToken ct) => throw new AssertFailedException();
        public Task<MachineRequestPaymentProof> ReadAsync(WebApp.Api.Services.MachineRequestPayment payment, string sessionId, CancellationToken ct) => throw new AssertFailedException();
        public Task<MachineRequestPaymentProof> ReadAdditionalAsync(WebApp.Api.Services.MachineRequestPayment payment, string sessionId, AdditionalMachinePaymentContext context, CancellationToken ct) => throw new AssertFailedException();
    }
    private sealed class Blobs : IMachineRequestBlobClient
    {
        private readonly Dictionary<string, byte[]> data = new(); public IReadOnlyCollection<string> Names => data.Keys;
        public Task EnsurePrivateContainerAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public async Task UploadAsync(string blobName, Stream content, string contentType, bool overwrite, CancellationToken cancellationToken = default) { using var copy = new MemoryStream(); await content.CopyToAsync(copy, cancellationToken); if (overwrite) data[blobName] = copy.ToArray(); else data.Add(blobName, copy.ToArray()); }
        public Task<Stream?> OpenReadAsync(string blobName, CancellationToken cancellationToken = default) => Task.FromResult<Stream?>(data.TryGetValue(blobName, out var bytes) ? new MemoryStream(bytes) : null);
        public async IAsyncEnumerable<string> ListNamesAsync(string? prefix = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) { foreach (var name in data.Keys.Where(name => prefix is null || name.StartsWith(prefix))) { yield return name; await Task.Yield(); } }
        public Task DeletePrefixAsync(string prefix, CancellationToken cancellationToken = default) { foreach (var name in data.Keys.Where(name => name.StartsWith(prefix)).ToList()) data.Remove(name); return Task.CompletedTask; }
    }
}
