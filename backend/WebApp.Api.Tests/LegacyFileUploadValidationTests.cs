using System.Net;
using Azure.Core.Pipeline;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using UglyToad.PdfPig.Writer;
using WebApp.Api.Services;

namespace WebApp.Api.Tests;

[TestClass]
public sealed class LegacyFileUploadValidationTests
{
    [TestMethod]
    public async Task UploadFilesAsync_ValidPdf_UploadsDocument()
    {
        var (service, transport) = CreateService();

        var uploaded = await service.UploadFilesAsync("documents", "Acme", "Press", Files(Pdf("manual.pdf")));

        Assert.HasCount(1, uploaded);
        Assert.AreEqual(1, transport.UploadCount);
    }

    [TestMethod]
    public async Task UploadFilesAsync_RenamedTextFile_RejectsBeforeBlobAccess()
    {
        var (service, transport) = CreateService();

        await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.UploadFilesAsync(
            "documents", "Acme", "Press", Files(File("notes.pdf", "application/pdf", "plain text"u8.ToArray()))));

        Assert.AreEqual(0, transport.RequestCount);
    }

    [TestMethod]
    public async Task UploadFilesAsync_PdfContentTypeWithInvalidContent_RejectsBeforeBlobAccess()
    {
        var (service, transport) = CreateService();

        await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.UploadFilesAsync(
            "documents", "Acme", "Press", Files(File("manual.pdf", "application/pdf", "%PDF-not-valid"u8.ToArray()))));

        Assert.AreEqual(0, transport.RequestCount);
    }

    [TestMethod]
    public async Task UploadFilesAsync_CorruptPdf_RejectsBeforeBlobAccess()
    {
        var (service, transport) = CreateService();
        var validPdf = PdfBytes();
        var corruptPdf = validPdf[..(validPdf.Length / 2)];

        await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.UploadFilesAsync(
            "documents", "Acme", "Press", Files(File("corrupt.pdf", "application/pdf", corruptPdf))));

        Assert.AreEqual(0, transport.RequestCount);
    }

    [TestMethod]
    public async Task UploadFilesAsync_FileOverMaximumSize_RejectsBeforeBlobAccess()
    {
        var (service, transport) = CreateService();
        var file = File("large.pdf", "application/pdf", PdfBytes(), MachineRequestUploadLimits.MaxDocumentBytes + 1);

        var exception = await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            service.UploadFilesAsync("documents", "Acme", "Press", Files(file)));

        StringAssert.Contains(exception.Message, MachineRequestUploadLimits.MaxDocumentBytes.ToString());
        Assert.AreEqual(0, transport.RequestCount);
    }

    [TestMethod]
    public async Task UploadFilesAsync_TooManyFiles_RejectsBeforeBlobAccess()
    {
        var (service, transport) = CreateService();
        var files = Enumerable.Range(1, MachineRequestUploadLimits.MaxDocumentCount + 1)
            .Select(index => Pdf($"manual-{index}.pdf"))
            .ToArray();

        await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            service.UploadFilesAsync("documents", "Acme", "Press", Files(files)));

        Assert.AreEqual(0, transport.RequestCount);
    }

    [TestMethod]
    public async Task UploadFilesAsync_CombinedSizeOverMaximum_RejectsBeforeBlobAccess()
    {
        var (service, transport) = CreateService();
        var logicalLength = MachineRequestUploadLimits.MaxCombinedDocumentBytes / 5 + 1;
        var files = Enumerable.Range(1, 5)
            .Select(index => File($"manual-{index}.pdf", "application/pdf", PdfBytes(), logicalLength))
            .ToArray();

        var exception = await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            service.UploadFilesAsync("documents", "Acme", "Press", Files(files)));

        StringAssert.Contains(exception.Message, MachineRequestUploadLimits.MaxCombinedDocumentBytes.ToString());
        Assert.AreEqual(0, transport.RequestCount);
    }

    [TestMethod]
    public async Task UploadFilesAsync_InvalidFileAfterValidFile_DoesNotStartAnyUpload()
    {
        var (service, transport) = CreateService();

        await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.UploadFilesAsync(
            "documents", "Acme", "Press", Files(Pdf("valid.pdf"),
                File("invalid.pdf", "application/pdf", "not a pdf"u8.ToArray()))));

        Assert.AreEqual(0, transport.UploadCount);
    }

    [TestMethod]
    public async Task UploadFilesAsync_BlobFailure_PropagatesFailure()
    {
        var (service, transport) = CreateService(failUploads: true);

        Exception? failure = null;
        try
        {
            await service.UploadFilesAsync("documents", "Acme", "Press", Files(Pdf("manual.pdf")));
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        Assert.IsNotNull(failure);
        StringAssert.Contains(failure.ToString(), "Simulated Blob failure.");
    }

    private static (BlobStorageService Service, RecordingBlobHandler Transport) CreateService(bool failUploads = false)
    {
        var transport = new RecordingBlobHandler { FailUploads = failUploads };
        var options = new BlobClientOptions
        {
            Transport = new HttpClientTransport(new HttpClient(transport)),
        };
        options.Retry.MaxRetries = 0;
        var client = new BlobServiceClient(new Uri("https://storage.test.local"), options);
        return (new BlobStorageService(client, new PdfPigPageCounter()), transport);
    }

    private static FormFileCollection Files(params IFormFile[] files)
    {
        var collection = new FormFileCollection();
        foreach (var file in files) collection.Add(file);
        return collection;
    }

    private static IFormFile Pdf(string name) => File(name, "application/pdf", PdfBytes());

    private static byte[] PdfBytes()
    {
        var builder = new PdfDocumentBuilder();
        builder.AddPage(100, 100);
        return builder.Build();
    }

    private static IFormFile File(string name, string contentType, byte[] content, long? logicalLength = null) =>
        new FormFile(new MemoryStream(content), 0, logicalLength ?? content.Length, "files", name)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType,
        };

    private sealed class RecordingBlobHandler : HttpMessageHandler
    {
        public bool FailUploads { get; init; }
        public int RequestCount { get; private set; }
        public int UploadCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            var isContainerRequest = request.RequestUri?.Query.Contains("restype=container", StringComparison.Ordinal) == true;
            if (!isContainerRequest) UploadCount++;
            if (!isContainerRequest && FailUploads) throw new HttpRequestException("Simulated Blob failure.");

            var response = new HttpResponseMessage(HttpStatusCode.Created)
            {
                RequestMessage = request,
                Content = new ByteArrayContent([]),
            };
            response.Headers.TryAddWithoutValidation("ETag", "\"test-etag\"");
            response.Headers.TryAddWithoutValidation("Last-Modified", DateTimeOffset.UtcNow.ToString("R"));
            response.Headers.TryAddWithoutValidation("x-ms-request-id", Guid.NewGuid().ToString("N"));
            return Task.FromResult(response);
        }
    }
}