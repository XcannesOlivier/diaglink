using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Primitives;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebApp.Api.Data;
using WebApp.Api.Models;
using WebApp.Api.Models.Entities;
using WebApp.Api.Services;

namespace WebApp.Api.Tests;

[TestClass]
public class AdditionalMachineRequestEndpointsTests
{
    [TestMethod]
    public async Task AuthorizedOwner_CreatesAdditionalRequestFromServerIdentityWithoutBusinessWrites()
    {
        await using var db = Database(); var identity = await SeedAsync(db); var blobs = new Blobs(); var gateway = new Gateway();
        var paymentId = await PaymentAsync(db, identity, 416);

        var result = await SubmitAsync(db, blobs, gateway, identity, paymentId, 416);

        Assert.AreEqual(StatusCodes.Status201Created, ((IStatusCodeHttpResult)result).StatusCode);
        var payment = await db.MachineRequestPayments.AsNoTracking().SingleAsync(item => item.Id == paymentId);
        var record = await new MachineRequestStorageService(blobs).GetAsync(payment.MachineRequestId!);
        Assert.IsNotNull(record);
        Assert.AreEqual(MachineRequestKind.AdditionalMachine, record.RequestKind);
        Assert.AreEqual(identity.CompanyId, record.CompanyId);
        Assert.AreEqual(identity.UserId, record.RequestedByUserId);
        Assert.AreEqual(paymentId, record.Payment!.PaymentRequestId);
        Assert.AreEqual("SQL Company", record.Client.Company);
        Assert.AreEqual("SQL", record.Client.FirstName);
        Assert.AreEqual("Admin", record.Client.LastName);
        Assert.AreEqual("admin@sql.test", record.Client.Email);
        Assert.AreEqual(416, record.Pricing.TotalPages);
        Assert.AreEqual(1, gateway.AdditionalReads);
        Assert.AreEqual(0, gateway.Captures);
        Assert.AreEqual(0, await db.Machines.CountAsync());
        Assert.AreEqual(0, await db.MachineBillingPeriods.CountAsync());
        Assert.AreEqual(0, await db.CompanyWallets.CountAsync());
        Assert.AreEqual(0, await db.StripeMachineAdditions.CountAsync());
    }

    [TestMethod]
    public async Task PendingOrInitialPayment_IsRejectedWithoutBlob()
    {
        await using var db = Database(); var identity = await SeedAsync(db); var blobs = new Blobs(); var gateway = new Gateway();
        var pending = await PaymentAsync(db, identity, 10, MachineRequestPaymentStatus.Pending);
        var initial = await PaymentAsync(db, identity, 10, kind: MachineRequestKind.InitialMachine);

        var pendingResult = await SubmitAsync(db, blobs, gateway, identity, pending, 10);
        var initialResult = await SubmitAsync(db, blobs, gateway, identity, initial, 10);

        Assert.AreEqual(StatusCodes.Status409Conflict, ((IStatusCodeHttpResult)pendingResult).StatusCode);
        Assert.IsInstanceOfType<Microsoft.AspNetCore.Http.HttpResults.ForbidHttpResult>(initialResult);
        Assert.HasCount(0, blobs.Names);
    }

    [TestMethod]
    public async Task DifferentOwnerOrCompany_IsForbiddenBeforeStorage()
    {
        await using var db = Database(); var owner = await SeedAsync(db); var other = await SeedAsync(db, "Other Company", "other@sql.test");
        var paymentId = await PaymentAsync(db, owner, 10); var blobs = new Blobs(); var gateway = new Gateway();

        var otherCompany = await SubmitAsync(db, blobs, gateway, other, paymentId, 10);
        db.Users.Add(new User { Id = Guid.NewGuid(), CompanyId = owner.CompanyId, Email = "second@sql.test", Role = DiagLinkRoles.CompanyAdmin, Status = "active", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync(); var otherUser = new Identity(db.Users.Single(user => user.Email == "second@sql.test").Id, owner.CompanyId);
        var otherRequester = await SubmitAsync(db, blobs, gateway, otherUser, paymentId, 10);

        Assert.IsInstanceOfType<Microsoft.AspNetCore.Http.HttpResults.ForbidHttpResult>(otherCompany);
        Assert.IsInstanceOfType<Microsoft.AspNetCore.Http.HttpResults.ForbidHttpResult>(otherRequester);
        Assert.HasCount(0, blobs.Names);
    }

    [TestMethod]
    public async Task ChangedPdfPagesOrPrice_IsRejectedWithoutBlob()
    {
        await using var db = Database(); var identity = await SeedAsync(db); var paymentId = await PaymentAsync(db, identity, 416);
        var blobs = new Blobs();
        var result = await SubmitAsync(db, blobs, new Gateway(), identity, paymentId, 417);
        Assert.AreEqual(StatusCodes.Status409Conflict, ((IStatusCodeHttpResult)result).StatusCode);
        Assert.HasCount(0, blobs.Names);
    }

    [TestMethod]
    public async Task IdenticalRetryReturnsSameRequestAndIncompatibleRetryIsRejected()
    {
        await using var db = Database(); var identity = await SeedAsync(db); var paymentId = await PaymentAsync(db, identity, 10);
        var blobs = new Blobs(); var gateway = new Gateway();
        var first = await SubmitAsync(db, blobs, gateway, identity, paymentId, 10);
        var retry = await SubmitAsync(db, blobs, gateway, identity, paymentId, 10);
        var incompatible = await SubmitAsync(db, blobs, gateway, identity, paymentId, 10, "Different Machine");
        var incompatibleDocument = await SubmitAsync(db, blobs, gateway, identity, paymentId, 10, fileContent: "evil-pdf");

        var firstValue = (MachineRequestCreatedResponse)((IValueHttpResult)first).Value!;
        var retryValue = (MachineRequestCreatedResponse)((IValueHttpResult)retry).Value!;
        Assert.AreEqual(firstValue.RequestId, retryValue.RequestId);
        Assert.AreEqual(paymentId.ToString("N"), firstValue.RequestId);
        Assert.AreEqual(StatusCodes.Status200OK, ((IStatusCodeHttpResult)retry).StatusCode);
        Assert.AreEqual(StatusCodes.Status409Conflict, ((IStatusCodeHttpResult)incompatible).StatusCode);
        Assert.AreEqual(StatusCodes.Status409Conflict, ((IStatusCodeHttpResult)incompatibleDocument).StatusCode);
        Assert.AreEqual(1, blobs.Names.Count(name => name.EndsWith("/request.json", StringComparison.Ordinal)));
        Assert.AreEqual(2, blobs.Names.Count);
    }

    private sealed record Identity(Guid UserId, Guid CompanyId);
    private static DiagLinkDbContext Database() => new(new DbContextOptionsBuilder<DiagLinkDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static async Task<Identity> SeedAsync(DiagLinkDbContext db, string companyName = "SQL Company", string email = "admin@sql.test")
    {
        var now = DateTime.UtcNow; var companyId = Guid.NewGuid(); var userId = Guid.NewGuid();
        db.Companies.Add(new Company { Id = companyId, Name = companyName, Status = "active", CreatedAtUtc = now, UpdatedAtUtc = now });
        db.Users.Add(new User { Id = userId, CompanyId = companyId, FirstName = "SQL", LastName = "Admin", PhoneNumber = "+33123456789", Email = email, Role = DiagLinkRoles.CompanyAdmin, Status = "active", CreatedAt = now, UpdatedAt = now });
        db.BillingAccounts.Add(new BillingAccount { Id = Guid.NewGuid(), CompanyId = companyId, StripeCustomerId = $"cus_{userId:N}", StripeSubscriptionId = $"sub_{userId:N}", CreatedAtUtc = now, UpdatedAtUtc = now });
        await db.SaveChangesAsync(); return new(userId, companyId);
    }
    private static async Task<Guid> PaymentAsync(DiagLinkDbContext db, Identity owner, int pages,
        MachineRequestPaymentStatus status = MachineRequestPaymentStatus.Authorized, MachineRequestKind kind = MachineRequestKind.AdditionalMachine)
    {
        var id = Guid.NewGuid(); var now = DateTime.UtcNow;
        db.MachineRequestPayments.Add(new WebApp.Api.Models.Entities.MachineRequestPayment { Id = id, RequestKind = kind,
            RequestedByUserId = kind == MachineRequestKind.AdditionalMachine ? owner.UserId : null, CompanyId = kind == MachineRequestKind.AdditionalMachine ? owner.CompanyId : null,
            Status = status, EstimatedTotalPages = pages, AmountCents = MachineRequestPreparationPricing.CalculateMaximumAuthorizationCents(pages), Currency = "EUR",
            StripeSessionId = "cs_" + id.ToString("N"), StripePaymentIntentId = status == MachineRequestPaymentStatus.Authorized ? "pi_" + id.ToString("N") : null,
            AuthorizationEventId = status == MachineRequestPaymentStatus.Authorized ? "evt_" + id.ToString("N") : null, AuthorizedAtUtc = status == MachineRequestPaymentStatus.Authorized ? now : null,
            CreatedAtUtc = now, UpdatedAtUtc = now }); await db.SaveChangesAsync(); return id;
    }
    private static async Task<IResult> SubmitAsync(DiagLinkDbContext db, Blobs blobs, Gateway gateway, Identity identity,
        Guid paymentId, int countedPages, string machineName = "Compressor", string fileContent = "fake-pdf")
    {
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(DiagLinkClaimTypes.UserId, identity.UserId.ToString()), new Claim(DiagLinkClaimTypes.CompanyId, identity.CompanyId.ToString()),
            new Claim(ClaimTypes.Role, DiagLinkRoles.CompanyAdmin)], "test")) };
        http.Request.ContentType = "multipart/form-data; boundary=test";
        var fields = new Dictionary<string, StringValues> { ["paymentRequestId"] = paymentId.ToString(), ["machineName"] = machineName, ["manufacturer"] = "Atlas", ["model"] = "H23", ["companyId"] = Guid.NewGuid().ToString(), ["firstName"] = "Browser" };
        var bytes = System.Text.Encoding.UTF8.GetBytes(fileContent); var file = new FormFile(new MemoryStream(bytes), 0, bytes.Length, "documents", "manual.pdf") { Headers = new HeaderDictionary() };
        file.ContentType = "application/pdf";
        http.Features.Set<IFormFeature>(new FormFeature(new FormCollection(fields, new FormFileCollection { file })));
        var resolver = new AdditionalMachinePaymentContextResolver(db, new DiagLinkUserLookupService(db));
        var payments = new MachineRequestPaymentService(new MachineRequestPaymentStore(db), gateway, resolver, TimeProvider.System);
        return await AdditionalMachineRequestEndpoints.CreateAsync(http, resolver, payments, new MachineRequestStorageService(blobs), new Counter(countedPages), default);
    }
    private sealed class Counter(int pages) : IPdfPageCounter { public Task<int> CountPagesAsync(Stream content, CancellationToken cancellationToken = default) => Task.FromResult(pages); }
    private sealed class Gateway : IMachineRequestPaymentGateway
    {
        public int AdditionalReads { get; private set; } public int Captures { get; private set; }
        public Task<MachineRequestPaymentProof> ReadAdditionalAsync(WebApp.Api.Services.MachineRequestPayment payment, string sessionId, AdditionalMachinePaymentContext context, CancellationToken ct) { AdditionalReads++; return Task.FromResult(new MachineRequestPaymentProof("authorized", payment.StripePaymentIntentId!)); }
        public Task<MachineRequestCheckout> CreateAsync(WebApp.Api.Services.MachineRequestPayment payment, CancellationToken ct) => throw new AssertFailedException();
        public Task<MachineRequestCheckout> CreateAdditionalAsync(WebApp.Api.Services.MachineRequestPayment payment, AdditionalMachinePaymentContext context, CancellationToken ct) => throw new AssertFailedException();
        public Task<MachineRequestPaymentProof> ReadAsync(WebApp.Api.Services.MachineRequestPayment payment, string sessionId, CancellationToken ct) => throw new AssertFailedException();
        public Task<MachineRequestPaymentProof> CaptureAsync(WebApp.Api.Services.MachineRequestPayment payment, CancellationToken ct) { Captures++; throw new AssertFailedException(); }
        public Task<MachineRequestPaymentProof> CancelAsync(WebApp.Api.Services.MachineRequestPayment payment, CancellationToken ct) => throw new AssertFailedException();
    }
    private sealed class Blobs : IMachineRequestBlobClient
    {
        private readonly Dictionary<string, byte[]> values = new(StringComparer.Ordinal); public IReadOnlyCollection<string> Names => values.Keys;
        public Task EnsurePrivateContainerAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public async Task UploadAsync(string blobName, Stream content, string contentType, bool overwrite, CancellationToken cancellationToken = default) { using var copy = new MemoryStream(); await content.CopyToAsync(copy, cancellationToken); if (!overwrite) values.Add(blobName, copy.ToArray()); else values[blobName] = copy.ToArray(); }
        public Task<Stream?> OpenReadAsync(string blobName, CancellationToken cancellationToken = default) => Task.FromResult<Stream?>(values.TryGetValue(blobName, out var bytes) ? new MemoryStream(bytes) : null);
        public async IAsyncEnumerable<string> ListNamesAsync(string? prefix = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) { foreach (var name in values.Keys.Where(name => prefix is null || name.StartsWith(prefix))) { yield return name; await Task.Yield(); } }
        public Task DeletePrefixAsync(string prefix, CancellationToken cancellationToken = default) { foreach (var name in values.Keys.Where(name => name.StartsWith(prefix)).ToList()) values.Remove(name); return Task.CompletedTask; }
    }
}
