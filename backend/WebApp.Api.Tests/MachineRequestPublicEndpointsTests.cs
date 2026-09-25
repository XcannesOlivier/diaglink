using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using UglyToad.PdfPig.Writer;
using WebApp.Api.Models;
using WebApp.Api.Data;
using WebApp.Api.Models.Entities;
using WebApp.Api.Services;

namespace WebApp.Api.Tests;

[TestClass]
public class MachineRequestPublicEndpointsTests
{
    [TestMethod]
    public async Task CreateAsync_AcceptsMultiplePdfFilesAndReturnsNoBlobDetails()
    {
        var blobs = new InMemoryBlobClient();
        var result = await SubmitAsync(blobs, ValidFields(), Pdf("manual.pdf", 250), Pdf("schema.pdf", 300));

        Assert.AreEqual(StatusCodes.Status201Created, ((IStatusCodeHttpResult)result).StatusCode);
        var response = (MachineRequestCreatedResponse)((IValueHttpResult)result).Value!;
        Assert.AreEqual(MachineRequestStatuses.Pending, response.Status);
        Assert.AreEqual(2, response.DocumentCount);
        Assert.AreEqual(550, response.TotalPages);
        Assert.AreEqual(150, response.AdditionalPages);
        Assert.AreEqual(140.40m, response.PreparationTotal);

        var responseJson = JsonSerializer.Serialize(response);
        Assert.IsFalse(responseJson.Contains("blob", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(responseJson.Contains("documents/", StringComparison.OrdinalIgnoreCase));
        Assert.HasCount(3, blobs.Names);
    }

    [TestMethod]
    [DataRow(400, 0, "99.90")]
    [DataRow(550, 150, "140.40")]
    [DataRow(1000, 600, "261.90")]
    public async Task CreateAsync_CountsPagesAndCalculatesServerPricing(int pageCount, int additionalPages, string expectedTotal)
    {
        var fields = ValidFields();
        fields["totalPages"] = "1";
        fields["preparationTotal"] = "0.01";

        var result = await SubmitAsync(new InMemoryBlobClient(), fields, Pdf("manual.pdf", pageCount));

        var response = (MachineRequestCreatedResponse)((IValueHttpResult)result).Value!;
        Assert.AreEqual(pageCount, response.TotalPages);
        Assert.AreEqual(additionalPages, response.AdditionalPages);
        Assert.AreEqual(decimal.Parse(expectedTotal, System.Globalization.CultureInfo.InvariantCulture), response.PreparationTotal);
    }

    [TestMethod]
    public async Task CreateAsync_RejectsMissingRequiredFieldAndMissingPdf()
    {
        var fields = ValidFields();
        fields.Remove("company");

        var missingField = await SubmitAsync(new InMemoryBlobClient(), fields, Pdf("manual.pdf", 1));
        var missingPdf = await SubmitAsync(new InMemoryBlobClient(), ValidFields());

        Assert.AreEqual(StatusCodes.Status400BadRequest, ((IStatusCodeHttpResult)missingField).StatusCode);
        Assert.AreEqual(StatusCodes.Status400BadRequest, ((IStatusCodeHttpResult)missingPdf).StatusCode);
    }

    [TestMethod]
    public async Task CreateAsync_RejectsNonPdfAndCorruptPdfWithoutStoringAnything()
    {
        var nonPdfBlobs = new InMemoryBlobClient();
        var corruptBlobs = new InMemoryBlobClient();

        var nonPdf = await SubmitAsync(nonPdfBlobs, ValidFields(), File("notes.txt", "plain text"u8.ToArray()));
        var corrupt = await SubmitAsync(corruptBlobs, ValidFields(), File("broken.pdf", "%PDF-not-valid"u8.ToArray()));

        Assert.AreEqual(StatusCodes.Status400BadRequest, ((IStatusCodeHttpResult)nonPdf).StatusCode);
        Assert.AreEqual(StatusCodes.Status400BadRequest, ((IStatusCodeHttpResult)corrupt).StatusCode);
        Assert.HasCount(0, nonPdfBlobs.Names);
        Assert.HasCount(0, corruptBlobs.Names);
    }

    [TestMethod]
    public async Task CreateAsync_RejectsUnknownAndPendingPayments()
    {
        var unknown = await SubmitDetailedAsync(new InMemoryBlobClient(), ValidFields(), false,
            MachineRequestPaymentStatus.Authorized, 400, 9990, Pdf("manual.pdf", 1));
        var pending = await SubmitDetailedAsync(new InMemoryBlobClient(), ValidFields(), true,
            MachineRequestPaymentStatus.Pending, 400, 9990, Pdf("manual.pdf", 1));

        Assert.AreEqual(StatusCodes.Status404NotFound, ((IStatusCodeHttpResult)unknown.Result).StatusCode);
        Assert.AreEqual(StatusCodes.Status409Conflict, ((IStatusCodeHttpResult)pending.Result).StatusCode);
    }

    [TestMethod]
    public async Task CreateAsync_LinksPaymentAndRetryReturnsSameRequestWithoutDuplicate()
    {
        var blobs = new InMemoryBlobClient();
        var first = await SubmitDetailedAsync(blobs, ValidFields(), true,
            MachineRequestPaymentStatus.Authorized, 400, 9990, Pdf("manual.pdf", 400));
        var firstResponse = (MachineRequestCreatedResponse)((IValueHttpResult)first.Result).Value!;
        var linked = await first.Database.MachineRequestPayments.AsNoTracking().SingleAsync(item => item.Id == first.PaymentId);
        Assert.AreEqual(firstResponse.RequestId, linked.MachineRequestId);

        var retry = await SubmitDetailedAsync(blobs, ValidFields(), false,
            MachineRequestPaymentStatus.Authorized, 400, 9990, first.Database, first.PaymentId, Pdf("manual.pdf", 400));
        var retryResponse = (MachineRequestCreatedResponse)((IValueHttpResult)retry.Result).Value!;
        Assert.AreEqual(StatusCodes.Status200OK, ((IStatusCodeHttpResult)retry.Result).StatusCode);
        Assert.AreEqual(firstResponse.RequestId, retryResponse.RequestId);
        Assert.AreEqual(1, blobs.Names.Count(name => name.EndsWith("/request.json", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task CreateAsync_StoresServerComparisonAndMarksInsufficientAuthorization()
    {
        var blobs = new InMemoryBlobClient();
        var submission = await SubmitDetailedAsync(blobs, ValidFields(), true,
            MachineRequestPaymentStatus.Authorized, 400, 9990, Pdf("manual.pdf", 550));
        var response = (MachineRequestCreatedResponse)((IValueHttpResult)submission.Result).Value!;
        var storage = new MachineRequestStorageService(blobs);
        var record = await storage.GetAsync(response.RequestId);

        Assert.IsTrue(response.AuthorizationInsufficient);
        Assert.IsNotNull(record?.Payment);
        Assert.AreEqual(400, record.Payment.AuthorizedPages);
        Assert.AreEqual(550, record.Payment.ReceivedPages);
        Assert.AreEqual(9990, record.Payment.AuthorizedAmountCents);
        Assert.AreEqual(14040, record.Payment.RecalculatedAmountCents);
        Assert.AreEqual(4050, record.Payment.AmountDifferenceCents);
    }

    [TestMethod]
    public async Task CreateAsync_NewAuthorizationKeepsMaximumSubscriptionInServerComparison()
    {
        var blobs = new InMemoryBlobClient();
        var submission = await SubmitDetailedAsync(blobs, ValidFields(), true,
            MachineRequestPaymentStatus.Authorized, 400, 12980, Pdf("manual.pdf", 550));
        var response = (MachineRequestCreatedResponse)((IValueHttpResult)submission.Result).Value!;
        var record = await new MachineRequestStorageService(blobs).GetAsync(response.RequestId);

        Assert.IsTrue(response.AuthorizationInsufficient);
        Assert.IsNotNull(record?.Payment);
        Assert.AreEqual(12980, record.Payment.AuthorizedAmountCents);
        Assert.AreEqual(17030, record.Payment.RecalculatedAmountCents);
        Assert.AreEqual(4050, record.Payment.AmountDifferenceCents);
    }

    private static async Task<IResult> SubmitAsync(InMemoryBlobClient blobs, Dictionary<string, string> fields, params IFormFile[] files)
        => (await SubmitDetailedAsync(blobs, fields, true, MachineRequestPaymentStatus.Authorized, 550, 14040, files)).Result;

    private static Task<(IResult Result, DiagLinkDbContext Database, Guid PaymentId)> SubmitDetailedAsync(
        InMemoryBlobClient blobs, Dictionary<string, string> fields, bool seedPayment,
        MachineRequestPaymentStatus status, int authorizedPages, long authorizedAmount, params IFormFile[] files) =>
        SubmitDetailedAsync(blobs, fields, seedPayment, status, authorizedPages, authorizedAmount, null, null, files);

    private static async Task<(IResult Result, DiagLinkDbContext Database, Guid PaymentId)> SubmitDetailedAsync(
        InMemoryBlobClient blobs, Dictionary<string, string> fields, bool seedPayment,
        MachineRequestPaymentStatus status, int authorizedPages, long authorizedAmount,
        DiagLinkDbContext? existingDatabase, Guid? existingPaymentId, params IFormFile[] files)
    {
        var paymentId = existingPaymentId ?? Guid.NewGuid();
        fields["paymentRequestId"] = paymentId.ToString();
        var context = new DefaultHttpContext();
        context.Request.ContentType = "multipart/form-data; boundary=test";
        var formFields = fields.ToDictionary(pair => pair.Key, pair => new StringValues(pair.Value));
        context.Features.Set<IFormFeature>(new FormFeature(new FormCollection(formFields, new FormFileCollection())));
        foreach (var file in files) ((FormFileCollection)context.Request.Form.Files).Add(file);

        var db = existingDatabase ?? new DiagLinkDbContext(new DbContextOptionsBuilder<DiagLinkDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var now = DateTime.UtcNow;
        if (seedPayment) db.MachineRequestPayments.Add(new WebApp.Api.Models.Entities.MachineRequestPayment
        {
            Id = paymentId, Status = status, EstimatedTotalPages = authorizedPages,
            AmountCents = authorizedAmount, Currency = "EUR", StripeSessionId = "cs_test_local",
            StripePaymentIntentId = status == MachineRequestPaymentStatus.Pending ? null : "pi_test_local",
            AuthorizationEventId = status == MachineRequestPaymentStatus.Pending ? null : $"evt_{paymentId:N}",
            CreatedAtUtc = now, UpdatedAtUtc = now,
            AuthorizedAtUtc = status == MachineRequestPaymentStatus.Pending ? null : now
        });
        if (seedPayment) await db.SaveChangesAsync();
        var paymentService = new MachineRequestPaymentService(new MachineRequestPaymentStore(db), new AuthorizedGateway());
        var result = await MachineRequestPublicEndpoints.CreateAsync(
            context,
            new MachineRequestStorageService(blobs),
            paymentService,
            new PdfPigPageCounter(),
            CancellationToken.None);
        return (result, db, paymentId);
    }

    private static Dictionary<string, string> ValidFields() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["firstName"] = " Claire ",
        ["lastName"] = "Martin",
        ["company"] = "Ateliers Martin",
        ["email"] = "claire@example.com",
        ["phone"] = "+33 6 12 34 56 78",
        ["machineName"] = "Compresseur",
        ["manufacturer"] = "Atlas Copco",
        ["model"] = "GA90",
    };

    private static IFormFile Pdf(string name, int pages)
    {
        var builder = new PdfDocumentBuilder();
        for (var page = 0; page < pages; page++) builder.AddPage(100, 100);
        return File(name, builder.Build());
    }

    private static IFormFile File(string name, byte[] content)
    {
        var file = new FormFile(new MemoryStream(content), 0, content.Length, "documents", name)
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/pdf",
        };
        return file;
    }

    private sealed class InMemoryBlobClient : IMachineRequestBlobClient
    {
        private readonly Dictionary<string, byte[]> _blobs = new(StringComparer.Ordinal);
        public IReadOnlyCollection<string> Names => _blobs.Keys;
        public Task EnsurePrivateContainerAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public async Task UploadAsync(string blobName, Stream content, string contentType, bool overwrite, CancellationToken cancellationToken = default)
        {
            using var copy = new MemoryStream();
            await content.CopyToAsync(copy, cancellationToken);
            _blobs.Add(blobName, copy.ToArray());
        }

        public Task<Stream?> OpenReadAsync(string blobName, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream?>(_blobs.TryGetValue(blobName, out var bytes) ? new MemoryStream(bytes, writable: false) : null);

        public async IAsyncEnumerable<string> ListNamesAsync(string? prefix = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var name in _blobs.Keys.Where(name => prefix is null || name.StartsWith(prefix, StringComparison.Ordinal)))
            {
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

    private sealed class AuthorizedGateway : IMachineRequestPaymentGateway
    {
        public Task<MachineRequestCheckout> CreateAsync(WebApp.Api.Services.MachineRequestPayment payment, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<MachineRequestPaymentProof> ReadAsync(WebApp.Api.Services.MachineRequestPayment payment, string sessionId, CancellationToken ct) =>
            Task.FromResult(new MachineRequestPaymentProof("authorized", payment.StripePaymentIntentId!));
        public Task<MachineRequestPaymentProof> CaptureAsync(WebApp.Api.Services.MachineRequestPayment payment, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<MachineRequestPaymentProof> CancelAsync(WebApp.Api.Services.MachineRequestPayment payment, CancellationToken ct) =>
            throw new NotSupportedException();
    }
}
