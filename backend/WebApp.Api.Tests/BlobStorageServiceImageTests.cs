using System.Net;
using System.Text;
using Azure;
using Azure.Core.Pipeline;
using Azure.Storage.Blobs;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebApp.Api.Services;

namespace WebApp.Api.Tests;

[TestClass]
public sealed class BlobStorageServiceImageTests
{
    private static readonly byte[] Png = [137, 80, 78, 71, 13, 10, 26, 10, 1, 2, 3];

    [TestMethod]
    [DataRow("company/machine/manual/page-00075/manual_page-00075-full.png")]
    [DataRow("company/machine/manual/page-00075/manual_page-00075-tile-r02-c03.png")]
    public async Task OpenReadAsync_DownloadsFullAndTileWithOneGetAndNoHead(string blobName)
    {
        var transport = new RecordingHandler((request, _) =>
            Task.FromResult(Response(request, HttpStatusCode.OK, Png)));
        var service = CreateService(transport);

        await using var stream = await service.OpenReadAsync(blobName, default);
        Assert.IsNotNull(stream);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);

        CollectionAssert.AreEqual(Png, buffer.ToArray());
        CollectionAssert.AreEqual(new[] { HttpMethod.Get }, transport.Methods);
    }

    [TestMethod]
    public async Task OpenReadAsync_TrueNotFoundReturnsNull()
    {
        var transport = new RecordingHandler((request, _) => Task.FromResult(
            ErrorResponse(request, HttpStatusCode.NotFound, "BlobNotFound")));
        var service = CreateService(transport);

        Assert.IsNull(await service.OpenReadAsync("company/machine/missing.png", default));
        CollectionAssert.AreEqual(new[] { HttpMethod.Get }, transport.Methods);
    }

    [TestMethod]
    [DataRow(403, "AuthorizationFailure")]
    [DataRow(429, "ServerBusy")]
    [DataRow(503, "ServerBusy")]
    public async Task OpenReadAsync_NonNotFoundAzureErrorsRemainDiagnosable(int status, string errorCode)
    {
        var transport = new RecordingHandler((request, _) => Task.FromResult(
            ErrorResponse(request, (HttpStatusCode)status, errorCode)));
        var service = CreateService(transport);

        var exception = await Assert.ThrowsExactlyAsync<RequestFailedException>(() =>
            service.OpenReadAsync("company/machine/manual/page.png", default));

        Assert.AreEqual(status, exception.Status);
        Assert.AreEqual(errorCode, exception.ErrorCode);
        CollectionAssert.AreEqual(new[] { HttpMethod.Get }, transport.Methods);
    }

    [TestMethod]
    public async Task OpenReadAsync_PropagatesCancellation()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var transport = new RecordingHandler(async (_, cancellationToken) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new AssertFailedException("The simulated request should have been cancelled.");
        });
        var service = CreateService(transport);
        using var cancellation = new CancellationTokenSource();

        var opening = service.OpenReadAsync("company/machine/manual/page.png", cancellation.Token);
        await started.Task;
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => opening);
        CollectionAssert.AreEqual(new[] { HttpMethod.Get }, transport.Methods);
    }

    [TestMethod]
    public async Task OpenReadAsync_InvalidNameDoesNotReachStorage()
    {
        var transport = new RecordingHandler((_, _) =>
            throw new AssertFailedException("Storage must not be contacted for an invalid image name."));
        var service = CreateService(transport);

        Assert.IsNull(await service.OpenReadAsync("https://storage.test/secret.png", default));
        Assert.IsEmpty(transport.Methods);
    }

    private static BlobStorageService CreateService(HttpMessageHandler transport)
    {
        var options = new BlobClientOptions
        {
            Transport = new HttpClientTransport(new HttpClient(transport))
        };
        options.Retry.MaxRetries = 0;
        var client = new BlobServiceClient(new Uri("https://storage.test.local"), options);
        return new BlobStorageService(client, new PdfPigPageCounter());
    }

    private static HttpResponseMessage Response(
        HttpRequestMessage request,
        HttpStatusCode status,
        byte[] content)
    {
        var response = new HttpResponseMessage(status)
        {
            RequestMessage = request,
            Content = new ByteArrayContent(content)
        };
        response.Headers.TryAddWithoutValidation("ETag", "\"test-etag\"");
        response.Headers.TryAddWithoutValidation("Last-Modified", DateTimeOffset.UtcNow.ToString("R"));
        response.Headers.TryAddWithoutValidation("x-ms-request-id", Guid.NewGuid().ToString("N"));
        response.Headers.TryAddWithoutValidation("x-ms-version", "2025-11-05");
        response.Headers.TryAddWithoutValidation("x-ms-blob-type", "BlockBlob");
        return response;
    }

    private static HttpResponseMessage ErrorResponse(
        HttpRequestMessage request,
        HttpStatusCode status,
        string errorCode)
    {
        var response = new HttpResponseMessage(status)
        {
            RequestMessage = request,
            Content = new StringContent(
                $"<Error><Code>{errorCode}</Code><Message>Simulated failure.</Message></Error>",
                Encoding.UTF8,
                "application/xml")
        };
        response.Headers.TryAddWithoutValidation("x-ms-error-code", errorCode);
        response.Headers.TryAddWithoutValidation("x-ms-request-id", Guid.NewGuid().ToString("N"));
        return response;
    }

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory)
        : HttpMessageHandler
    {
        public List<HttpMethod> Methods { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Methods.Add(request.Method);
            return responseFactory(request, cancellationToken);
        }
    }
}
