using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebApp.Api.Data;
using WebApp.Api.Models.Entities;
using WebApp.Api.Models;
using WebApp.Api.Services;

namespace WebApp.Api.Tests;

[TestClass]
public class MachineRequestAdminEndpointsTests
{
    [TestMethod]
    public async Task ListEndpoint_RequiresSuperAdminAndRejectsOtherOrAnonymousUsers()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddAuthorization(options =>
            options.AddPolicy("SuperAdminOnly", policy => policy.RequireRole("diaglink_super_admin")));
        builder.Services.AddSingleton<IMachineRequestBlobClient, InMemoryBlobClient>();
        builder.Services.AddSingleton<MachineRequestStorageService>();
        await using var app = builder.Build();
        app.MapAdminMachineRequests();

        var endpoint = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints)
            .Cast<RouteEndpoint>()
            .Single(route => !route.RoutePattern.RawText!.Contains('{')
                && !route.RoutePattern.RawText.EndsWith("/history", StringComparison.Ordinal)
                && route.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Contains("GET"));
        Assert.IsTrue(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Any(item => item.Policy == "SuperAdminOnly"));
        Assert.IsNull(endpoint.Metadata.GetMetadata<IAllowAnonymous>());

        var authorization = app.Services.GetRequiredService<IAuthorizationService>();
        Assert.IsTrue((await authorization.AuthorizeAsync(User("diaglink_super_admin"), null, "SuperAdminOnly")).Succeeded);
        Assert.IsFalse((await authorization.AuthorizeAsync(User("company_admin"), null, "SuperAdminOnly")).Succeeded);
        Assert.IsFalse((await authorization.AuthorizeAsync(new ClaimsPrincipal(new ClaimsIdentity()), null, "SuperAdminOnly")).Succeeded);

        var provisioningEndpoint = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints)
            .Cast<RouteEndpoint>().Single(route => route.RoutePattern.RawText!.EndsWith(
                "/provisioning/business-entities", StringComparison.Ordinal));
        Assert.IsTrue(provisioningEndpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()
            .Any(item => item.Policy == "SuperAdminOnly"));
        Assert.IsNull(provisioningEndpoint.Metadata.GetMetadata<IAllowAnonymous>());

        var readyEndpoint = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints)
            .Cast<RouteEndpoint>().Single(route => route.RoutePattern.RawText!.EndsWith(
                "/{requestId}/ready", StringComparison.Ordinal));
        Assert.IsTrue(readyEndpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()
            .Any(item => item.Policy == "SuperAdminOnly"));
        Assert.IsNull(readyEndpoint.Metadata.GetMetadata<IAllowAnonymous>());

        foreach (var route in ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints)
            .Cast<RouteEndpoint>().Where(route => route.RoutePattern.RawText!.Contains("history", StringComparison.Ordinal)
                || route.RoutePattern.RawText.EndsWith("/{requestId}/archive", StringComparison.Ordinal)))
        {
            Assert.IsTrue(route.Metadata.GetOrderedMetadata<IAuthorizeData>().Any(item => item.Policy == "SuperAdminOnly"));
            Assert.IsNull(route.Metadata.GetMetadata<IAllowAnonymous>());
        }
    }

    [TestMethod]
    public async Task ListAsync_ReturnsNewestFirstWithoutBlobInformation()
    {
        var setup = await CreateStoredRequestsAsync();
        var result = await MachineRequestAdminEndpoints.ListAsync(setup.Service, setup.PaymentService, CancellationToken.None);
        var items = ((IValueHttpResult)result).Value as IEnumerable<MachineRequestListItem>;

        Assert.IsNotNull(items);
        var list = items.ToList();
        Assert.HasCount(2, list);
        Assert.AreEqual(setup.Second.RequestId, list[0].RequestId);
        Assert.IsFalse(JsonSerializer.Serialize(list).Contains("blob", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(JsonSerializer.Serialize(list).Contains("documents/", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task GetAsync_ReturnsSafeDetailAndNotFoundForMissingRequest()
    {
        var setup = await CreateStoredRequestsAsync();
        var result = await MachineRequestAdminEndpoints.GetAsync(setup.First.RequestId, setup.Service, setup.PaymentService, CancellationToken.None);
        var detail = ((IValueHttpResult)result).Value as MachineRequestDetail;

        Assert.IsNotNull(detail);
        Assert.AreEqual("Compresseur", detail.Machine.MachineName);
        Assert.HasCount(1, detail.Documents);
        Assert.AreEqual(24, detail.Documents[0].DocumentId.Length);
        var json = JsonSerializer.Serialize(detail);
        Assert.IsFalse(json.Contains("blobName", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(json.Contains("documents/", StringComparison.OrdinalIgnoreCase));

        var missing = await MachineRequestAdminEndpoints.GetAsync(Guid.NewGuid().ToString("N"), setup.Service, setup.PaymentService, CancellationToken.None);
        Assert.AreEqual(StatusCodes.Status404NotFound, ((IStatusCodeHttpResult)missing).StatusCode);
    }

    [TestMethod]
    public async Task DownloadDocumentAsync_ServesOwnedPdfAndRejectsDocumentFromAnotherRequest()
    {
        var setup = await CreateStoredRequestsAsync();
        var firstDetail = (MachineRequestDetail)((IValueHttpResult)await MachineRequestAdminEndpoints.GetAsync(setup.First.RequestId, setup.Service, setup.PaymentService, CancellationToken.None)).Value!;
        var secondDetail = (MachineRequestDetail)((IValueHttpResult)await MachineRequestAdminEndpoints.GetAsync(setup.Second.RequestId, setup.Service, setup.PaymentService, CancellationToken.None)).Value!;

        var valid = await MachineRequestAdminEndpoints.DownloadDocumentAsync(
            setup.First.RequestId, firstDetail.Documents[0].DocumentId, setup.Service, CancellationToken.None);
        var file = (IFileHttpResult)valid;
        Assert.AreEqual("application/pdf", file.ContentType);
        Assert.AreEqual("manual.pdf", file.FileDownloadName);

        var outsideRequest = await MachineRequestAdminEndpoints.DownloadDocumentAsync(
            setup.First.RequestId, secondDetail.Documents[0].DocumentId, setup.Service, CancellationToken.None);
        Assert.AreEqual(StatusCodes.Status404NotFound, ((IStatusCodeHttpResult)outsideRequest).StatusCode);
    }

    [TestMethod]
    public async Task UpdateStatusAsync_AllowsTreatedAndRejectedOnlyWithoutDocumentSideEffects()
    {
        var setup = await CreateStoredRequestsAsync();
        var documentUploadsBefore = setup.Blobs.DocumentUploadCount;

        var treated = await MachineRequestAdminEndpoints.UpdateStatusAsync(
            setup.First.RequestId, new(MachineRequestStatuses.Treated), setup.Service, setup.PaymentService, CancellationToken.None);
        var rejected = await MachineRequestAdminEndpoints.UpdateStatusAsync(
            setup.Second.RequestId, new(MachineRequestStatuses.Rejected), setup.Service, setup.PaymentService, CancellationToken.None);
        var invalid = await MachineRequestAdminEndpoints.UpdateStatusAsync(
            setup.First.RequestId, new("processed"), setup.Service, setup.PaymentService, CancellationToken.None);

        Assert.AreEqual(MachineRequestStatuses.Treated, ((MachineRequestDetail)((IValueHttpResult)treated).Value!).Status);
        Assert.AreEqual(MachineRequestStatuses.Rejected, ((MachineRequestDetail)((IValueHttpResult)rejected).Value!).Status);
        Assert.AreEqual(StatusCodes.Status400BadRequest, ((IStatusCodeHttpResult)invalid).StatusCode);
        Assert.AreEqual(documentUploadsBefore, setup.Blobs.DocumentUploadCount);
        Assert.HasCount(2, setup.Blobs.Names.Where(name => name.Contains("/documents/", StringComparison.Ordinal)).ToList());
    }

    [TestMethod]
    public async Task LinkedRequestRequiresConfirmedStripeTerminalStateBeforeStatusChange()
    {
        var blobs = new InMemoryBlobClient();
        var storage = new MachineRequestStorageService(blobs);
        var paymentId = Guid.NewGuid();
        var requestId = paymentId.ToString("N");
        var paymentReference = new MachineRequestPaymentReference(paymentId, 400, 9990, "EUR", 400, 9990, 0, 0, false);
        await storage.CreateAsync(Draft("Compresseur"), [Upload("manual.pdf", 400, "pdf")], requestId, paymentReference);

        var db = new DiagLinkDbContext(new DbContextOptionsBuilder<DiagLinkDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var now = DateTime.UtcNow;
        db.MachineRequestPayments.Add(new WebApp.Api.Models.Entities.MachineRequestPayment
        {
            Id = paymentId, Status = MachineRequestPaymentStatus.Authorized, EstimatedTotalPages = 400,
            AmountCents = 9990, Currency = "EUR", StripeSessionId = "cs_test", StripePaymentIntentId = "pi_test",
            AuthorizationEventId = "evt_test", MachineRequestId = requestId, CreatedAtUtc = now,
            UpdatedAtUtc = now, AuthorizedAtUtc = now, RequestLinkedAtUtc = now
        });
        await db.SaveChangesAsync();
        var payments = new MachineRequestPaymentService(new MachineRequestPaymentStore(db), new FinalizingGateway());

        var beforeCapture = await MachineRequestAdminEndpoints.UpdateStatusAsync(requestId,
            new(MachineRequestStatuses.Treated), storage, payments, CancellationToken.None);
        Assert.AreEqual(StatusCodes.Status409Conflict, ((IStatusCodeHttpResult)beforeCapture).StatusCode);

        Assert.AreEqual("captured", (await payments.CaptureAsync(paymentId, default))!.Status);
        var afterCapture = await MachineRequestAdminEndpoints.UpdateStatusAsync(requestId,
            new(MachineRequestStatuses.Treated), storage, payments, CancellationToken.None);
        Assert.AreEqual(MachineRequestStatuses.Treated,
            ((MachineRequestDetail)((IValueHttpResult)afterCapture).Value!).Status);
    }

    [TestMethod]
    public async Task ArchiveRequiresTerminalStatusAndHistoryCanRestoreWithoutDeletingDocuments()
    {
        var setup = await CreateStoredRequestsAsync();
        await setup.Service.UpdateStatusAsync(setup.First.RequestId, MachineRequestStatuses.Treated);
        var documentUploadsBefore = setup.Blobs.DocumentUploadCount;
        var userId = Guid.NewGuid();
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(DiagLinkClaimTypes.UserId, userId.ToString())], "test"))
        };

        var pending = await MachineRequestAdminEndpoints.UpdateArchiveAsync(setup.Second.RequestId,
            new(true), httpContext, setup.Service, setup.PaymentService, CancellationToken.None);
        Assert.AreEqual(StatusCodes.Status409Conflict, ((IStatusCodeHttpResult)pending).StatusCode);

        var archived = await MachineRequestAdminEndpoints.UpdateArchiveAsync(setup.First.RequestId,
            new(true), httpContext, setup.Service, setup.PaymentService, CancellationToken.None);
        var archivedDetail = (MachineRequestDetail)((IValueHttpResult)archived).Value!;
        Assert.IsTrue(archivedDetail.IsArchived);
        Assert.AreEqual(MachineRequestStatuses.Treated, archivedDetail.Status);
        Assert.AreEqual(userId, archivedDetail.ArchivedByUserId);

        var active = ((IEnumerable<MachineRequestListItem>)((IValueHttpResult)await MachineRequestAdminEndpoints.ListAsync(
            setup.Service, setup.PaymentService, CancellationToken.None)).Value!).ToList();
        var history = ((IEnumerable<MachineRequestListItem>)((IValueHttpResult)await MachineRequestAdminEndpoints.ListArchivedAsync(
            setup.Service, setup.PaymentService, CancellationToken.None)).Value!).ToList();
        Assert.IsFalse(active.Any(item => item.RequestId == setup.First.RequestId));
        Assert.AreEqual(setup.First.RequestId, history.Single().RequestId);
        Assert.AreEqual(MachineRequestStatuses.Treated, history.Single().Status);

        var restored = await MachineRequestAdminEndpoints.UpdateArchiveAsync(setup.First.RequestId,
            new(false), httpContext, setup.Service, setup.PaymentService, CancellationToken.None);
        var restoredDetail = (MachineRequestDetail)((IValueHttpResult)restored).Value!;
        Assert.IsFalse(restoredDetail.IsArchived);
        Assert.AreEqual(MachineRequestStatuses.Treated, restoredDetail.Status);
        Assert.AreEqual(documentUploadsBefore, setup.Blobs.DocumentUploadCount);
        Assert.HasCount(2, setup.Blobs.Names.Where(name => name.Contains("/documents/", StringComparison.Ordinal)).ToList());
    }

    private static ClaimsPrincipal User(string role) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.Role, role)], "test", ClaimTypes.Name, ClaimTypes.Role));

    private static async Task<(MachineRequestStorageService Service, MachineRequestPaymentService PaymentService, InMemoryBlobClient Blobs, MachineRequestRecord First, MachineRequestRecord Second)> CreateStoredRequestsAsync()
    {
        var blobs = new InMemoryBlobClient();
        var service = new MachineRequestStorageService(blobs);
        var first = await service.CreateAsync(Draft("Compresseur"), [Upload("manual.pdf", 400, "first-pdf")]);
        await Task.Delay(2);
        var second = await service.CreateAsync(Draft("Pompe"), [Upload("schema.pdf", 550, "second-pdf")]);
        var db = new DiagLinkDbContext(new DbContextOptionsBuilder<DiagLinkDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var paymentService = new MachineRequestPaymentService(new MachineRequestPaymentStore(db), new NoopPaymentGateway());
        return (service, paymentService, blobs, first, second);
    }

    private static MachineRequestDraft Draft(string machineName) => new(
        new("Claire", "Martin", "Ateliers Martin", "claire@example.com", "+33 6 12 34 56 78"),
        new(machineName, "Atlas Copco", "GA90", "SN-42", "Description"));

    private static MachineRequestDocumentUpload Upload(string name, int pages, string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        return new(name, "application/pdf", bytes.Length, pages, new MemoryStream(bytes));
    }

    private sealed class InMemoryBlobClient : IMachineRequestBlobClient
    {
        private readonly Dictionary<string, byte[]> _blobs = new(StringComparer.Ordinal);
        public IReadOnlyCollection<string> Names => _blobs.Keys;
        public int DocumentUploadCount { get; private set; }
        public Task EnsurePrivateContainerAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public async Task UploadAsync(string blobName, Stream content, string contentType, bool overwrite, CancellationToken cancellationToken = default)
        {
            using var copy = new MemoryStream();
            await content.CopyToAsync(copy, cancellationToken);
            _blobs[blobName] = copy.ToArray();
            if (blobName.Contains("/documents/", StringComparison.Ordinal)) DocumentUploadCount++;
        }

        public Task<Stream?> OpenReadAsync(string blobName, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream?>(_blobs.TryGetValue(blobName, out var bytes) ? new MemoryStream(bytes, writable: false) : null);

        public async IAsyncEnumerable<string> ListNamesAsync(string? prefix = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var name in _blobs.Keys.Where(name => prefix is null || name.StartsWith(prefix, StringComparison.Ordinal)).ToList())
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return name;
                await Task.Yield();
            }
        }

        public Task DeletePrefixAsync(string prefix, CancellationToken cancellationToken = default)
        {
            foreach (var name in _blobs.Keys.Where(name => name.StartsWith(prefix, StringComparison.Ordinal)).ToList()) _blobs.Remove(name);
            return Task.CompletedTask;
        }
    }

    private sealed class NoopPaymentGateway : IMachineRequestPaymentGateway
    {
        public Task<MachineRequestCheckout> CreateAsync(WebApp.Api.Services.MachineRequestPayment payment, CancellationToken ct) => throw new NotSupportedException();
        public Task<MachineRequestPaymentProof> ReadAsync(WebApp.Api.Services.MachineRequestPayment payment, string sessionId, CancellationToken ct) => throw new NotSupportedException();
        public Task<MachineRequestPaymentProof> CaptureAsync(WebApp.Api.Services.MachineRequestPayment payment, CancellationToken ct) => throw new NotSupportedException();
        public Task<MachineRequestPaymentProof> CancelAsync(WebApp.Api.Services.MachineRequestPayment payment, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class FinalizingGateway : IMachineRequestPaymentGateway
    {
        public Task<MachineRequestCheckout> CreateAsync(WebApp.Api.Services.MachineRequestPayment payment, CancellationToken ct) => throw new NotSupportedException();
        public Task<MachineRequestPaymentProof> ReadAsync(WebApp.Api.Services.MachineRequestPayment payment, string sessionId, CancellationToken ct) => throw new NotSupportedException();
        public Task<MachineRequestPaymentProof> CaptureAsync(WebApp.Api.Services.MachineRequestPayment payment, CancellationToken ct) =>
            Task.FromResult(new MachineRequestPaymentProof("captured", payment.StripePaymentIntentId!));
        public Task<MachineRequestPaymentProof> CancelAsync(WebApp.Api.Services.MachineRequestPayment payment, CancellationToken ct) =>
            Task.FromResult(new MachineRequestPaymentProof("cancelled", payment.StripePaymentIntentId!));
    }
}
