using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebApp.Api.Data;
using WebApp.Api.Models;
using WebApp.Api.Models.Entities;
using WebApp.Api.Services;

namespace WebApp.Api.Tests;

[TestClass]
public class AdditionalDocumentsRequestEndpointsTests
{
    [TestMethod]
    public async Task RouteRequiresCompanyAdminPolicy()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddAuthorization(options => options.AddPolicy("CompanyAdminOnly", p => p.RequireRole(DiagLinkRoles.CompanyAdmin)));
        await using var app = builder.Build(); app.MapAdditionalDocumentsRequests();
        var endpoints = ((IEndpointRouteBuilder)app).DataSources.SelectMany(s => s.Endpoints).Cast<RouteEndpoint>().ToArray();
        Assert.HasCount(4, endpoints);
        Assert.IsTrue(endpoints.All(endpoint => endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()
            .Any(a => a.Policy == "CompanyAdminOnly")));
    }

    [TestMethod]
    public async Task StagesServerOwnedPendingAttemptWithoutStripeOrProvisioning()
    {
        await using var f = await Fixture.CreateAsync();
        var result = await f.SubmitAsync(pages: 370);
        Assert.AreEqual(StatusCodes.Status201Created, ((IStatusCodeHttpResult)result).StatusCode);
        var response = (AdditionalDocumentsStagedResponse)((IValueHttpResult)result).Value!;
        Assert.AreEqual(370, response.TotalPages); Assert.AreEqual(9990, response.AmountCents);

        var payment = await f.Db.MachineRequestPayments.AsNoTracking().SingleAsync();
        Assert.AreEqual(MachineRequestKind.AdditionalDocuments, payment.RequestKind);
        Assert.AreEqual(f.CompanyId, payment.CompanyId); Assert.AreEqual(f.UserId, payment.RequestedByUserId);
        Assert.AreEqual(f.MachineId, payment.TargetMachineId); Assert.IsNull(payment.MachineId);
        Assert.AreEqual(MachineRequestPaymentStatus.Pending, payment.Status);
        Assert.IsNull(payment.StripeSessionId); Assert.IsNull(payment.StripePaymentIntentId); Assert.IsNull(payment.AuthorizationEventId);
        Assert.IsNull(payment.ActivatedAtUtc); Assert.IsNull(payment.FirstPeriodEndUtc); Assert.IsNull(payment.ServiceAmountCents);
        Assert.IsNull(payment.FinalCaptureAmountCents); Assert.IsNull(payment.ProvisioningCompletedAtUtc);
        Assert.AreEqual(MachineRequestProvisioningStage.AwaitingAcceptance, payment.ProvisioningStage);
        Assert.AreEqual(0, await f.Db.MachineBillingPeriods.CountAsync());

        var record = await f.Storage.GetAsync(response.RequestId);
        Assert.IsNotNull(record); Assert.AreEqual(MachineRequestKind.AdditionalDocuments, record.RequestKind);
        Assert.AreEqual(f.MachineId, record.TargetMachineId); Assert.AreEqual(0, record.Pricing.IncludedPages);
        Assert.AreEqual(.27m, record.Pricing.AdditionalPagePrice); Assert.AreEqual(99.90m, record.Pricing.PreparationTotal);
        Assert.AreEqual(0m, record.Pricing.MonthlySubscriptionPrice);
        Assert.IsTrue(f.Blobs.Names.Any(n => n.Contains("/documents/", StringComparison.Ordinal)));
    }

    [TestMethod]
    [DataRow(1, 50L)]
    [DataRow(2, 54L)]
    public async Task StagingAppliesTheAdditionalDocumentsRequestMinimum(int pages, long expectedAmountCents)
    {
        await using var f = await Fixture.CreateAsync();

        var result = await f.SubmitAsync(pages);
        var response = (AdditionalDocumentsStagedResponse)((IValueHttpResult)result).Value!;
        var payment = await f.Db.MachineRequestPayments.AsNoTracking().SingleAsync();
        var record = await f.Storage.GetAsync(response.RequestId);

        Assert.AreEqual(pages, response.TotalPages);
        Assert.AreEqual(expectedAmountCents, response.AmountCents);
        Assert.AreEqual(pages, payment.EstimatedTotalPages);
        Assert.AreEqual(expectedAmountCents, payment.AmountCents);
        Assert.AreEqual(expectedAmountCents / 100m, record!.Pricing.PreparationTotal);
        Assert.AreEqual(.27m, record.Pricing.AdditionalPagePrice);
    }

    [TestMethod]
    public async Task RetryIsIdempotentAndChangedMachineOrDocumentConflicts()
    {
        await using var f = await Fixture.CreateAsync(); var key = Guid.NewGuid();
        var first = await f.SubmitAsync(3, key: key); var count = f.Blobs.Names.Count;
        var retry = await f.SubmitAsync(3, key: key);
        var changed = await f.SubmitAsync(3, key: key, content: "different");
        var otherMachine = new Machine { Id = Guid.NewGuid(), CompanyId = f.CompanyId, Name = "Other", Status = "active", CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow };
        f.Db.Machines.Add(otherMachine); await f.Db.SaveChangesAsync();
        var changedMachine = await f.SubmitAsync(3, key: key, machineId: otherMachine.Id);
        Assert.AreEqual(((AdditionalDocumentsStagedResponse)((IValueHttpResult)first).Value!).RequestId,
            ((AdditionalDocumentsStagedResponse)((IValueHttpResult)retry).Value!).RequestId);
        Assert.AreEqual(count, f.Blobs.Names.Count);
        Assert.AreEqual(StatusCodes.Status409Conflict, ((IStatusCodeHttpResult)changed).StatusCode);
        Assert.AreEqual(StatusCodes.Status409Conflict, ((IStatusCodeHttpResult)changedMachine).StatusCode);
        Assert.AreEqual(1, await f.Db.MachineRequestPayments.CountAsync());
    }

    [TestMethod]
    public async Task RejectsRolesForeignOrInactiveMachine()
    {
        await using var f = await Fixture.CreateAsync();
        Assert.IsInstanceOfType<Microsoft.AspNetCore.Http.HttpResults.ForbidHttpResult>(await f.SubmitAsync(1, role: DiagLinkRoles.Technician));
        Assert.IsInstanceOfType<Microsoft.AspNetCore.Http.HttpResults.ForbidHttpResult>(await f.SubmitAsync(1, role: DiagLinkRoles.SuperAdmin));
        Assert.AreEqual(StatusCodes.Status409Conflict, ((IStatusCodeHttpResult)await f.SubmitAsync(1, machineId: Guid.NewGuid())).StatusCode);
        var machine = await f.Db.Machines.SingleAsync(); machine.Status = "inactive"; await f.Db.SaveChangesAsync();
        Assert.AreEqual(StatusCodes.Status409Conflict, ((IStatusCodeHttpResult)await f.SubmitAsync(1)).StatusCode);
        Assert.AreEqual(0, await f.Db.MachineRequestPayments.CountAsync());
    }

    [TestMethod]
    public async Task EnforcesPdfAndMultipartLimitsAndServerPageCount()
    {
        await using var f = await Fixture.CreateAsync();
        Assert.AreEqual(StatusCodes.Status400BadRequest, ((IStatusCodeHttpResult)await f.SubmitAsync(1, fileCount: 0)).StatusCode);
        Assert.AreEqual(StatusCodes.Status400BadRequest, ((IStatusCodeHttpResult)await f.SubmitAsync(1, fileCount: 11)).StatusCode);
        Assert.AreEqual(StatusCodes.Status400BadRequest, ((IStatusCodeHttpResult)await f.SubmitAsync(1, fileName: "manual.txt")).StatusCode);
        Assert.AreEqual(StatusCodes.Status400BadRequest, ((IStatusCodeHttpResult)await f.SubmitAsync(1, length: MachineRequestUploadLimits.MaxDocumentBytes + 1)).StatusCode);
        Assert.AreEqual(StatusCodes.Status413PayloadTooLarge, ((IStatusCodeHttpResult)await f.SubmitAsync(1, fileCount: 5, length: 45L * 1024 * 1024)).StatusCode);
        f.Counter.Throws = true;
        Assert.AreEqual(StatusCodes.Status400BadRequest, ((IStatusCodeHttpResult)await f.SubmitAsync(1)).StatusCode);
    }

    [TestMethod]
    public async Task MissingOrInvalidIdempotencyKeyIsRejectedBeforePersistence()
    {
        await using var f = await Fixture.CreateAsync();
        Assert.AreEqual(StatusCodes.Status400BadRequest, ((IStatusCodeHttpResult)await f.SubmitAsync(1, keyHeader: "bad")).StatusCode);
        Assert.AreEqual(StatusCodes.Status400BadRequest, ((IStatusCodeHttpResult)await f.SubmitAsync(1, keyHeader: null)).StatusCode);
        Assert.AreEqual(0, await f.Db.MachineRequestPayments.CountAsync());
    }

    [TestMethod]
    public async Task InterruptedBlobWriteCanBeRetriedWithSameDurableAttempt()
    {
        await using var f = await Fixture.CreateAsync(); var key = Guid.NewGuid();
        f.Blobs.FailNextDocumentUpload = true;
        await Assert.ThrowsExactlyAsync<IOException>(() => f.SubmitAsync(4, key: key));
        Assert.AreEqual(1, await f.Db.MachineRequestPayments.CountAsync());
        Assert.HasCount(0, f.Blobs.Names);

        var retry = await f.SubmitAsync(4, key: key);
        Assert.AreEqual(StatusCodes.Status201Created, ((IStatusCodeHttpResult)retry).StatusCode);
        Assert.AreEqual(1, await f.Db.MachineRequestPayments.CountAsync());
        Assert.AreEqual(2, f.Blobs.Names.Count);
    }

    [TestMethod]
    public async Task PendingAdditionalDocumentsIsHiddenFromAdminListWithoutChangingLegacyVisibility()
    {
        await using var f = await Fixture.CreateAsync();
        await f.SubmitAsync(2);
        var legacy = await f.Storage.CreateAsync(new MachineRequestDraft(
            new("A", "B", "Legacy", "legacy@test", ""), new("Legacy", "Maker", "Model", null, null)),
            [Upload("legacy.pdf", 1, "legacy")]);
        var payments = new MachineRequestPaymentService(new MachineRequestPaymentStore(f.Db), new NoStripeGateway());
        var result = await MachineRequestAdminEndpoints.ListAsync(f.Storage, payments, default);
        var items = ((IValueHttpResult)result).Value as IEnumerable<MachineRequestListItem>;
        Assert.IsNotNull(items); Assert.HasCount(1, items.ToList()); Assert.AreEqual(legacy.RequestId, items.Single().RequestId);

        var pending = await new MachineRequestPaymentStore(f.Db).GetAsync(f.Db.MachineRequestPayments.Single().Id, default);
        await new MachineRequestPaymentStore(f.Db).AuthorizeAsync(pending!, "pi_authorized", "evt_authorized", default);
        var authorizedResult = await MachineRequestAdminEndpoints.ListAsync(f.Storage, payments, default);
        var authorizedItems = ((IValueHttpResult)authorizedResult).Value as IEnumerable<MachineRequestListItem>;
        Assert.IsNotNull(authorizedItems); Assert.HasCount(2, authorizedItems.ToList());
    }

    [TestMethod]
    public async Task GenericStatusPatchCannotMutateAdditionalDocuments()
    {
        await using var f = await Fixture.CreateAsync();
        var payments = new MachineRequestPaymentService(new MachineRequestPaymentStore(f.Db), new NoStripeGateway());

        var treatedStage = (AdditionalDocumentsStagedResponse)((IValueHttpResult)await f.SubmitAsync(1)).Value!;
        await f.Storage.UpdateStatusAsync(treatedStage.RequestId, MachineRequestStatuses.Treated);
        var treatedToPending = await MachineRequestAdminEndpoints.UpdateStatusAsync(treatedStage.RequestId,
            new(MachineRequestStatuses.Pending), f.Storage, payments, default);

        var rejectedStage = (AdditionalDocumentsStagedResponse)((IValueHttpResult)await f.SubmitAsync(1)).Value!;
        await f.Storage.UpdateStatusAsync(rejectedStage.RequestId, MachineRequestStatuses.Rejected);
        var rejectedToPending = await MachineRequestAdminEndpoints.UpdateStatusAsync(rejectedStage.RequestId,
            new(MachineRequestStatuses.Pending), f.Storage, payments, default);

        var authorizedStage = (AdditionalDocumentsStagedResponse)((IValueHttpResult)await f.SubmitAsync(1)).Value!;
        var store = new MachineRequestPaymentStore(f.Db);
        var pending = await store.GetAsync(authorizedStage.PaymentRequestId, default);
        await store.AuthorizeAsync(pending!, "pi_authorized", "evt_authorized", default);
        var authorizedToTreated = await MachineRequestAdminEndpoints.UpdateStatusAsync(authorizedStage.RequestId,
            new(MachineRequestStatuses.Treated), f.Storage, payments, default);

        Assert.AreEqual(StatusCodes.Status409Conflict, ((IStatusCodeHttpResult)treatedToPending).StatusCode);
        Assert.AreEqual(StatusCodes.Status409Conflict, ((IStatusCodeHttpResult)rejectedToPending).StatusCode);
        Assert.AreEqual(StatusCodes.Status409Conflict, ((IStatusCodeHttpResult)authorizedToTreated).StatusCode);
        Assert.AreEqual(MachineRequestStatuses.Treated, (await f.Storage.GetAsync(treatedStage.RequestId))!.Status);
        Assert.AreEqual(MachineRequestStatuses.Rejected, (await f.Storage.GetAsync(rejectedStage.RequestId))!.Status);
        Assert.AreEqual(MachineRequestStatuses.Pending, (await f.Storage.GetAsync(authorizedStage.RequestId))!.Status);
    }

    private static MachineRequestDocumentUpload Upload(string name, int pages, string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        return new(name, "application/pdf", bytes.Length, pages, new MemoryStream(bytes));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public DiagLinkDbContext Db { get; } public BlobFake Blobs { get; } = new(); public Counter Counter { get; } = new();
        public MachineRequestStorageService Storage { get; } public Guid UserId { get; } = Guid.NewGuid();
        public Guid CompanyId { get; } = Guid.NewGuid(); public Guid MachineId { get; } = Guid.NewGuid();
        private Fixture(DiagLinkDbContext db) { Db = db; Storage = new(Blobs); }
        public static async Task<Fixture> CreateAsync()
        {
            var db = new DiagLinkDbContext(new DbContextOptionsBuilder<DiagLinkDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            var f = new Fixture(db); var now = DateTime.UtcNow;
            db.Companies.Add(new Company { Id = f.CompanyId, Name = "Company", Status = "active", CreatedAtUtc = now, UpdatedAtUtc = now });
            db.Users.Add(new User { Id = f.UserId, CompanyId = f.CompanyId, Email = "admin@test", Role = DiagLinkRoles.CompanyAdmin, Status = "active", CreatedAt = now, UpdatedAt = now });
            db.Machines.Add(new Machine { Id = f.MachineId, CompanyId = f.CompanyId, Name = "Atlas", Status = "active", CreatedAtUtc = now, UpdatedAtUtc = now });
            db.BillingAccounts.Add(new BillingAccount { Id = Guid.NewGuid(), CompanyId = f.CompanyId, StripeCustomerId = "cus_existing", CreatedAtUtc = now, UpdatedAtUtc = now });
            await db.SaveChangesAsync(); return f;
        }
        public async Task<IResult> SubmitAsync(int pages, Guid? key = null, Guid? machineId = null, string content = "pdf",
            string fileName = "manual.pdf", int fileCount = 1, long? length = null, string role = DiagLinkRoles.CompanyAdmin,
            string? keyHeader = "auto")
        {
            Counter.Pages = pages; var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(DiagLinkClaimTypes.UserId, UserId.ToString()), new Claim(DiagLinkClaimTypes.CompanyId, CompanyId.ToString()),
                new Claim(ClaimTypes.Role, role)], "test")) };
            http.Request.ContentType = "multipart/form-data; boundary=test";
            if (keyHeader is not null) http.Request.Headers["Idempotency-Key"] = keyHeader == "auto" ? (key ?? Guid.NewGuid()).ToString() : keyHeader;
            var files = new FormFileCollection();
            for (var i = 0; i < fileCount; i++) { var bytes = Encoding.UTF8.GetBytes(content); var file = new FormFile(new MemoryStream(bytes), 0, length ?? bytes.Length, "documents", fileName) { Headers = new HeaderDictionary(), ContentType = "application/pdf" }; files.Add(file); }
            http.Features.Set<IFormFeature>(new FormFeature(new FormCollection(new Dictionary<string, StringValues>
            {
                ["companyId"] = Guid.NewGuid().ToString(), ["totalPages"] = "999999", ["amountCents"] = "1"
            }, files)));
            return await AdditionalDocumentsRequestEndpoints.CreateAsync(machineId ?? MachineId, http,
                new AdditionalDocumentsContextResolver(Db, new DiagLinkUserLookupService(Db)), new MachineRequestPaymentStore(Db), Storage, Counter, default);
        }
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
    private sealed class Counter : IPdfPageCounter { public int Pages { get; set; } public bool Throws { get; set; }
        public Task<int> CountPagesAsync(Stream content, CancellationToken cancellationToken = default) => Throws ? throw new InvalidDataException() : Task.FromResult(Pages); }
    private sealed class BlobFake : IMachineRequestBlobClient
    {
        private readonly Dictionary<string, byte[]> data = new(StringComparer.Ordinal); public IReadOnlyCollection<string> Names => data.Keys;
        public bool FailNextDocumentUpload { get; set; }
        public Task EnsurePrivateContainerAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public async Task UploadAsync(string blobName, Stream content, string contentType, bool overwrite, CancellationToken cancellationToken = default) { if (FailNextDocumentUpload && blobName.Contains("/documents/")) { FailNextDocumentUpload = false; throw new IOException("simulated"); } using var ms = new MemoryStream(); await content.CopyToAsync(ms, cancellationToken); if (!overwrite) data.Add(blobName, ms.ToArray()); else data[blobName] = ms.ToArray(); }
        public Task<Stream?> OpenReadAsync(string blobName, CancellationToken cancellationToken = default) => Task.FromResult<Stream?>(data.TryGetValue(blobName, out var b) ? new MemoryStream(b) : null);
        public async IAsyncEnumerable<string> ListNamesAsync(string? prefix = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) { foreach (var n in data.Keys.Where(n => prefix is null || n.StartsWith(prefix))) { yield return n; await Task.Yield(); } }
        public Task DeletePrefixAsync(string prefix, CancellationToken cancellationToken = default) { foreach (var n in data.Keys.Where(n => n.StartsWith(prefix)).ToList()) data.Remove(n); return Task.CompletedTask; }
    }
    private sealed class NoStripeGateway : IMachineRequestPaymentGateway
    {
        public Task<MachineRequestCheckout> CreateAsync(WebApp.Api.Services.MachineRequestPayment payment, CancellationToken ct) => throw new AssertFailedException();
        public Task<MachineRequestPaymentProof> ReadAsync(WebApp.Api.Services.MachineRequestPayment payment, string sessionId, CancellationToken ct) => throw new AssertFailedException();
        public Task<MachineRequestPaymentProof> CaptureAsync(WebApp.Api.Services.MachineRequestPayment payment, CancellationToken ct) => throw new AssertFailedException();
        public Task<MachineRequestPaymentProof> CancelAsync(WebApp.Api.Services.MachineRequestPayment payment, CancellationToken ct) => throw new AssertFailedException();
    }
}
