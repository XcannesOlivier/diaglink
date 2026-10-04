using System.Net;
using System.Text;
using System.Text.Json;
using Azure.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebApp.Api.Models;
using WebApp.Api.Services;

namespace WebApp.Api.Tests;

[TestClass]
public sealed class ClaudeDirectChatServiceTests
{
    private const string Token = "unit-test-secret-token";
    private const string ProjectEndpoint = "https://resource.test/api/projects/develon";
    private const string McpEndpoint =
        "https://resource.test/api/projects/develon/toolboxes/dx10z-toolbox/versions/1/mcp?api-version=v1";
    private const string DocumentId = "dx10z-manual";

    [TestMethod]
    public async Task CompleteAsync_ReturnsSimpleClaudeText()
    {
        var fixture = Fixture.Create(DirectResponse("bonjour", 3, 4));

        var result = await fixture.Service.CompleteAsync(Request());

        Assert.AreEqual("bonjour", result.FinalText);
        Assert.AreEqual("claude-sonnet-5", result.Model);
        Assert.AreEqual("end_turn", result.StopReason);
        Assert.HasCount(0, result.Errors);
    }

    [TestMethod]
    public async Task CompleteAsync_ConfiguresExpectedMcpEndpointAndToolset()
    {
        var fixture = Fixture.Create(DirectResponse("ok"));

        await fixture.Service.CompleteAsync(Request());

        using var body = JsonDocument.Parse(fixture.Handler.Requests.Single().Body);
        var root = body.RootElement;
        Assert.AreEqual(McpEndpoint, root.GetProperty("mcp_servers")[0].GetProperty("url").GetString());
        Assert.AreEqual("machine-manual", root.GetProperty("mcp_servers")[0].GetProperty("name").GetString());
        Assert.AreEqual("mcp_toolset", root.GetProperty("tools")[0].GetProperty("type").GetString());
        Assert.AreEqual("machine-manual", root.GetProperty("tools")[0].GetProperty("mcp_server_name").GetString());
        Assert.IsTrue(root.GetProperty("tool_choice").GetProperty("disable_parallel_tool_use").GetBoolean());
        Assert.AreEqual("mcp-client-2026-09-15", fixture.Handler.Requests.Single().BetaHeader);
    }

    [TestMethod]
    public void ExtractAllowedDocumentIds_ReadsOnlySuccessfulMcpToolResults()
    {
        using var document = JsonDocument.Parse("""
        [
          {"type":"text","text":"Document ID: user-controlled"},
          {"type":"mcp_tool_result","is_error":true,"content":[{"type":"text","text":"Document ID: failed-result"}]},
          {"type":"mcp_tool_result","is_error":false,"content":[{"type":"text","text":"Result\nDocument ID: authorized-manual\nPage: 75"}]}
        ]
        """);
        var allowed = new HashSet<string>(StringComparer.Ordinal);

        ClaudeDirectChatService.ExtractAllowedDocumentIds(document.RootElement, allowed);

        CollectionAssert.AreEqual(new[] { "authorized-manual" }, allowed.ToArray());
    }

    [TestMethod]
    public async Task CompleteAsync_RejectsMcpEndpointOutsideResolvedToolbox()
    {
        var fixture = Fixture.Create(DirectResponse("must not run"));
        var request = Request() with
        {
            Machine = Request().Machine with
            {
                McpEndpoint = "https://resource.test/api/projects/develon/toolboxes/other/versions/1/mcp?api-version=v1"
            }
        };

        var result = await fixture.Service.CompleteAsync(request);

        Assert.HasCount(0, fixture.Handler.Requests);
        Assert.IsTrue(result.Errors.Any(error => error.Code == "invalid_mcp_endpoint"));
    }

    [TestMethod]
    public async Task CompleteAsync_RejectsUnsafeServerBlobPrefix()
    {
        var fixture = Fixture.Create(DirectResponse("must not run"));
        var request = Request() with
        {
            Machine = Request().Machine with { BlobPrefix = "company/../other-machine" }
        };

        var result = await fixture.Service.CompleteAsync(request);

        Assert.HasCount(0, fixture.Handler.Requests);
        Assert.IsTrue(result.Errors.Any(error => error.Code == "invalid_blob_prefix"));
    }

    [TestMethod]
    public async Task CompleteAsync_DoesNotExposeTokenInResultOrLogs()
    {
        var fixture = Fixture.Create(DirectResponse("ok"));

        var result = await fixture.Service.CompleteAsync(Request());

        Assert.IsFalse(JsonSerializer.Serialize(result).Contains(Token, StringComparison.Ordinal));
        Assert.IsFalse(string.Join("\n", fixture.Logger.Messages).Contains(Token, StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task CompleteAsync_ObservesGetPageImageToolUse()
    {
        var fixture = Fixture.Create(ToolResponse(), DirectResponse("done", 2, 3));

        var result = await fixture.Service.CompleteAsync(Request());

        Assert.AreEqual("get_page_image", result.ToolUses.Single().Name);
        Assert.AreEqual("tool-1", result.ToolUses.Single().Id);
    }

    [TestMethod]
    public async Task CompleteAsync_ResolvesBlobFromServerPrefix()
    {
        var fixture = Fixture.Create(ToolResponse(), DirectResponse("done"));

        var result = await fixture.Service.CompleteAsync(Request());

        Assert.AreEqual(
            $"company/machine/{DocumentId}/page-00075/{DocumentId}_page-00075-full.png",
            fixture.BlobReader.RequestedNames.Single());
        Assert.AreEqual(
            ClaudeDirectDocumentResolutionMode.Exact,
            result.DocumentResolutions.Single().ResolutionMode);
    }

    [TestMethod]
    public async Task CompleteAsync_MissingDocumentIdUsesUniqueMcpDocument()
    {
        var fixture = Fixture.Create(ToolResponse(documentId: null), DirectResponse("done"));

        var result = await fixture.Service.CompleteAsync(Request());

        var resolution = result.DocumentResolutions.Single();
        Assert.IsNull(resolution.RequestedDocumentId);
        Assert.AreEqual(DocumentId, resolution.ResolvedDocumentId);
        Assert.AreEqual(ClaudeDirectDocumentResolutionMode.UniqueMcpDocument, resolution.ResolutionMode);
        Assert.HasCount(2, fixture.Handler.Requests);
    }

    [TestMethod]
    public async Task CompleteAsync_MultipleMcpDocumentsSelectsOnlyExistingPage()
    {
        var fixture = Fixture.Create(
            ToolResponse(documentId: null, allowedDocumentIds: ["manual-a", "manual-b"]),
            DirectResponse("done"));
        fixture.BlobReader.StreamFactory = blobName =>
            blobName.Contains("/manual-b/", StringComparison.Ordinal)
                ? new MemoryStream([137, 80, 78, 71, 13, 10, 26, 10, 1])
                : null;

        var result = await fixture.Service.CompleteAsync(Request());

        var resolution = result.DocumentResolutions.Single();
        Assert.AreEqual("manual-b", resolution.ResolvedDocumentId);
        Assert.AreEqual(ClaudeDirectDocumentResolutionMode.UniqueExistingPage, resolution.ResolutionMode);
        Assert.AreEqual("manual-b/page-00075/manual-b_page-00075-full.png", result.Visuals.Single().AssetKey);
    }

    [TestMethod]
    public async Task CompleteAsync_MultipleExistingPagesRemainAmbiguousWithConciseError()
    {
        var fixture = Fixture.Create(
            ToolResponse(documentId: null, allowedDocumentIds: ["manual-a", "manual-b"]),
            DirectResponse("ambiguous handled"));

        var result = await fixture.Service.CompleteAsync(Request());

        Assert.AreEqual(ClaudeDirectDocumentResolutionMode.Ambiguous, result.DocumentResolutions.Single().ResolutionMode);
        Assert.HasCount(0, result.Visuals);
        var error = result.Errors.Single(item => item.Code == "document_ambiguous");
        Assert.IsTrue(error.Message.Length < 100);
        Assert.IsFalse(error.Message.Contains("company/machine", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task CompleteAsync_NoMcpDocumentReturnsNotFoundWithoutBlobProbe()
    {
        var fixture = Fixture.Create(
            ToolResponse(documentId: null, allowedDocumentIds: []),
            DirectResponse("not found handled"));

        var result = await fixture.Service.CompleteAsync(Request());

        Assert.AreEqual(ClaudeDirectDocumentResolutionMode.NotFound, result.DocumentResolutions.Single().ResolutionMode);
        Assert.HasCount(0, fixture.BlobReader.RequestedNames);
        Assert.IsTrue(result.Errors.Any(error => error.Code == "blob_not_found"));
    }

    [TestMethod]
    public async Task CompleteAsync_DocumentOutsideMcpSetIsNeverProbed()
    {
        var fixture = Fixture.Create(
            ToolResponse(documentId: "outside", allowedDocumentIds: ["manual-a", "manual-b"]),
            DirectResponse("not found handled"));
        fixture.BlobReader.StreamFactory = _ => null;

        var result = await fixture.Service.CompleteAsync(Request());

        Assert.IsFalse(fixture.BlobReader.RequestedNames.Any(name => name.Contains("/outside/", StringComparison.Ordinal)));
        CollectionAssert.AreEquivalent(
            new[] { "manual-a", "manual-b" },
            fixture.BlobReader.RequestedNames
                .Select(name => name.Split('/')[2])
                .ToArray());
        Assert.AreEqual(ClaudeDirectDocumentResolutionMode.NotFound, result.DocumentResolutions.Single().ResolutionMode);
    }

    [TestMethod]
    public async Task CompleteAsync_AllowedDocumentExtractionCannotEscapeMachineBlobPrefix()
    {
        var fixture = Fixture.Create(
            ToolResponse(documentId: "../../outside", allowedDocumentIds: ["../../outside", DocumentId]),
            DirectResponse("done"));

        var result = await fixture.Service.CompleteAsync(Request());

        Assert.IsTrue(fixture.BlobReader.RequestedNames.All(name =>
            name.StartsWith("company/machine/", StringComparison.Ordinal) &&
            !name.Contains("..", StringComparison.Ordinal)));
        Assert.AreEqual(DocumentId, result.DocumentResolutions.Single().ResolvedDocumentId);
    }

    [TestMethod]
    public async Task CompleteAsync_RejectsInvalidDocumentIdBeforeBlobRead()
    {
        var fixture = Fixture.Create(
            ToolResponse(documentId: "../other-machine", allowedDocumentIds: []),
            DirectResponse("handled"));

        var result = await fixture.Service.CompleteAsync(Request());

        Assert.HasCount(0, fixture.BlobReader.RequestedNames);
        Assert.IsTrue(result.Errors.Any(error => error.Code == "blob_not_found"));
    }

    [TestMethod]
    public async Task CompleteAsync_RejectsInvalidTileBeforeBlobRead()
    {
        var fixture = Fixture.Create(
            ToolResponse(assetType: "tile", tile: "../../secret"),
            DirectResponse("handled"));

        var result = await fixture.Service.CompleteAsync(Request());

        Assert.HasCount(0, fixture.BlobReader.RequestedNames);
        Assert.IsTrue(result.Errors.Any(error => error.Code == "invalid_image_request"));
    }

    [TestMethod]
    public async Task CompleteAsync_TransformsPngIntoImageToolResult()
    {
        var fixture = Fixture.Create(ToolResponse(), DirectResponse("done"));

        await fixture.Service.CompleteAsync(Request());

        using var secondBody = JsonDocument.Parse(fixture.Handler.Requests[1].Body);
        var resultBlock = secondBody.RootElement.GetProperty("messages")[2].GetProperty("content")[0];
        var image = resultBlock.GetProperty("content")[1];
        Assert.AreEqual("image", image.GetProperty("type").GetString());
        Assert.AreEqual("image/png", image.GetProperty("source").GetProperty("media_type").GetString());
        Assert.IsFalse(string.IsNullOrWhiteSpace(image.GetProperty("source").GetProperty("data").GetString()));
    }

    [TestMethod]
    public async Task CompleteAsync_ReusesToolUseIdInToolResult()
    {
        var fixture = Fixture.Create(ToolResponse(), DirectResponse("done"));

        await fixture.Service.CompleteAsync(Request());

        using var secondBody = JsonDocument.Parse(fixture.Handler.Requests[1].Body);
        var resultBlock = secondBody.RootElement.GetProperty("messages")[2].GetProperty("content")[0];
        Assert.AreEqual("tool-1", resultBlock.GetProperty("tool_use_id").GetString());
    }

    [TestMethod]
    public async Task CompleteAsync_ReturnsExpectedVisualMetadataAndRelativeAssetKey()
    {
        var fixture = Fixture.Create(
            ToolResponse(assetType: "tile", tile: "r02-c01"),
            DirectResponse("done"));

        var result = await fixture.Service.CompleteAsync(Request());

        var visual = result.Visuals.Single();
        Assert.AreEqual(DocumentId, visual.DocumentId);
        Assert.AreEqual(75, visual.Page);
        Assert.AreEqual("tile", visual.AssetType);
        Assert.AreEqual("r02-c01", visual.Tile);
        Assert.AreEqual($"{DocumentId}_page-00075-tile-r02-c01.png", visual.Name);
        Assert.AreEqual(
            $"{DocumentId}/page-00075/{DocumentId}_page-00075-tile-r02-c01.png",
            visual.AssetKey);
        Assert.IsFalse(visual.AssetKey.StartsWith("company/machine", StringComparison.Ordinal));
        Assert.AreEqual(
            ClaudeDirectDocumentResolutionMode.Exact,
            result.DocumentResolutions.Single().ResolutionMode);
    }

    [TestMethod]
    public async Task CompleteAsync_CallsSameClaudeDeploymentAfterToolResult()
    {
        var fixture = Fixture.Create(ToolResponse(), DirectResponse("done"));

        await fixture.Service.CompleteAsync(Request());

        Assert.HasCount(2, fixture.Handler.Requests);
        Assert.AreEqual(fixture.Handler.Requests[0].Uri, fixture.Handler.Requests[1].Uri);
        foreach (var captured in fixture.Handler.Requests)
        {
            using var body = JsonDocument.Parse(captured.Body);
            Assert.AreEqual("claude-sonnet-5", body.RootElement.GetProperty("model").GetString());
        }
        Assert.AreEqual(1, fixture.Credential.CallCount);
    }

    [TestMethod]
    public async Task CompleteAsync_AggregatesMultipleMessagesUsages()
    {
        var fixture = Fixture.Create(ToolResponse(inputTokens: 10, outputTokens: 2), DirectResponse("done", 20, 5));

        var result = await fixture.Service.CompleteAsync(Request());

        Assert.HasCount(2, result.Calls);
        Assert.AreEqual(30, result.Usage.InputTokens);
        Assert.AreEqual(7, result.Usage.OutputTokens);
        Assert.AreEqual(37, result.Usage.TotalTokens);
    }

    [TestMethod]
    public async Task CompleteAsync_DoesNotCreateSeparateVisionUsage()
    {
        var fixture = Fixture.Create(ToolResponse(), DirectResponse("done"));

        var result = await fixture.Service.CompleteAsync(Request());

        Assert.HasCount(2, result.Calls);
        Assert.IsFalse(JsonSerializer.Serialize(result.Calls).Contains("VisionTool", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task CompleteAsync_ReturnsStructuredBlobNotFoundError()
    {
        var fixture = Fixture.Create(ToolResponse(), DirectResponse("image indisponible"));
        fixture.BlobReader.StreamFactory = _ => null;

        var result = await fixture.Service.CompleteAsync(Request());

        Assert.IsTrue(result.Errors.Any(error => error.Code == "blob_not_found" && error.ToolUseId == "tool-1"));
        Assert.AreEqual("image indisponible", result.FinalText);
    }

    [TestMethod]
    public async Task CompleteAsync_WrongDocumentIdUsesUniqueMcpDocumentWithoutRetry()
    {
        var fixture = Fixture.Create(
            ToolResponse(documentId: "DX10z"),
            DirectResponse("done"));

        var result = await fixture.Service.CompleteAsync(Request());

        Assert.AreEqual("done", result.FinalText);
        Assert.HasCount(0, result.Errors);
        Assert.HasCount(1, result.Visuals);
        Assert.HasCount(2, fixture.Handler.Requests);
        Assert.AreEqual("DX10z", result.DocumentResolutions.Single().RequestedDocumentId);
        Assert.AreEqual(DocumentId, result.DocumentResolutions.Single().ResolvedDocumentId);
        Assert.AreEqual(
            ClaudeDirectDocumentResolutionMode.UniqueMcpDocument,
            result.DocumentResolutions.Single().ResolutionMode);
        Assert.AreEqual(
            $"{DocumentId}/page-00075/{DocumentId}_page-00075-full.png",
            result.Visuals.Single().AssetKey);
    }

    [TestMethod]
    public async Task CompleteAsync_ReturnsStructuredMessagesApiError()
    {
        var fixture = Fixture.Create(new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            Content = new StringContent($"server must not echo {Token}")
        });

        var result = await fixture.Service.CompleteAsync(Request());

        Assert.AreEqual("messages_api_error", result.Errors.Single().Code);
        Assert.IsFalse(JsonSerializer.Serialize(result).Contains(Token, StringComparison.Ordinal));
        Assert.IsFalse(string.Join("\n", fixture.Logger.Messages).Contains(Token, StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task CompleteAsync_BoundsToolLoop()
    {
        var fixture = Fixture.CreateWithOptions(
            new ClaudeDirectChatOptions
            {
                FoundryAnthropicEndpoint = "https://resource.test/anthropic",
                Deployment = "claude-sonnet-5",
                MaxMessageCalls = 2,
                MaxImageBytes = 1024
            },
            ToolResponse(),
            ToolResponse());

        var result = await fixture.Service.CompleteAsync(Request());

        Assert.HasCount(2, fixture.Handler.Requests);
        Assert.IsTrue(result.Errors.Any(error => error.Code == "tool_loop_limit"));
    }

    private static ClaudeDirectChatRequest Request() => new(
        new(
            ProjectEndpoint,
            "dx10z-toolbox",
            "1",
            McpEndpoint,
            "vs_unitTest123",
            "company/machine",
            "Machine DX10z; use the connected technical manual."),
        "Use File Search before inspecting technical images.",
        [new("user", "How do I replace the hydraulic filter?")]);

    private static HttpResponseMessage DirectResponse(string text, long inputTokens = 1, long outputTokens = 1) =>
        JsonResponse(JsonSerializer.Serialize(new
        {
            id = "msg-final",
            model = "claude-sonnet-5",
            stop_reason = "end_turn",
            content = new object[] { new { type = "text", text } },
            usage = new { input_tokens = inputTokens, output_tokens = outputTokens }
        }));

    private static HttpResponseMessage ToolResponse(
        string? documentId = DocumentId,
        string assetType = "full",
        string? tile = null,
        long inputTokens = 10,
        long outputTokens = 2,
        string[]? allowedDocumentIds = null)
    {
        var input = new Dictionary<string, object?>
        {
            ["page"] = 75,
            ["asset_type"] = assetType,
            ["tile"] = tile
        };
        if (documentId is not null)
        {
            input["document_id"] = documentId;
        }

        allowedDocumentIds ??= [DocumentId];
        var fileSearchText = string.Join(
            "\n",
            allowedDocumentIds.Select(id => $"Document ID: {id}"));
        return JsonResponse(JsonSerializer.Serialize(new
        {
            id = "msg-tool",
            model = "claude-sonnet-5",
            stop_reason = "tool_use",
            content = new object[]
            {
                new { type = "mcp_tool_listing", tools = Array.Empty<object>() },
                new { type = "mcp_tool_use", id = "mcp-1", name = "file_search", input = new { } },
                new
                {
                    type = "mcp_tool_result",
                    tool_use_id = "mcp-1",
                    is_error = false,
                    content = new object[] { new { type = "text", text = fileSearchText } }
                },
                new { type = "tool_use", id = "tool-1", name = "get_page_image", input }
            },
            usage = new { input_tokens = inputTokens, output_tokens = outputTokens }
        }));
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        response.Headers.Add("request-id", "request-test");
        return response;
    }

    private sealed class Fixture
    {
        private Fixture(
            ClaudeDirectChatService service,
            RecordingHandler handler,
            FakeTokenCredential credential,
            FakeBlobReader blobReader,
            CaptureLogger<ClaudeDirectChatService> logger)
        {
            Service = service;
            Handler = handler;
            Credential = credential;
            BlobReader = blobReader;
            Logger = logger;
        }

        public ClaudeDirectChatService Service { get; }
        public RecordingHandler Handler { get; }
        public FakeTokenCredential Credential { get; }
        public FakeBlobReader BlobReader { get; }
        public CaptureLogger<ClaudeDirectChatService> Logger { get; }

        public static Fixture Create(params HttpResponseMessage[] responses) =>
            CreateWithOptions(new ClaudeDirectChatOptions
            {
                FoundryAnthropicEndpoint = "https://resource.test/anthropic",
                Deployment = "claude-sonnet-5",
                MaxMessageCalls = 4,
                MaxImageBytes = 1024
            }, responses);

        public static Fixture CreateWithOptions(
            ClaudeDirectChatOptions options,
            params HttpResponseMessage[] responses)
        {
            var handler = new RecordingHandler(responses);
            var credential = new FakeTokenCredential();
            var blobReader = new FakeBlobReader();
            var logger = new CaptureLogger<ClaudeDirectChatService>();
            var service = new ClaudeDirectChatService(
                new SingleClientFactory(new HttpClient(handler)),
                credential,
                blobReader,
                Options.Create(options),
                logger);
            return new(service, handler, credential, blobReader, logger);
        }
    }

    private sealed class RecordingHandler(IEnumerable<HttpResponseMessage> responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);
        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new(
                request.RequestUri!,
                body,
                request.Headers.TryGetValues("anthropic-beta", out var beta) ? beta.Single() : null));
            return _responses.Dequeue();
        }
    }

    private sealed record CapturedRequest(Uri Uri, string Body, string? BetaHeader);

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class FakeTokenCredential : TokenCredential
    {
        public int CallCount { get; private set; }

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            CallCount++;
            return new(Token, DateTimeOffset.UtcNow.AddHours(1));
        }

        public override ValueTask<AccessToken> GetTokenAsync(
            TokenRequestContext requestContext,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return ValueTask.FromResult(new AccessToken(Token, DateTimeOffset.UtcNow.AddHours(1)));
        }
    }

    private sealed class FakeBlobReader : ITechnicalVisualBlobReader
    {
        public Func<string, Stream?> StreamFactory { get; set; } = _ =>
            new MemoryStream([137, 80, 78, 71, 13, 10, 26, 10, 1, 2, 3]);
        public List<string> RequestedNames { get; } = [];

        public Task<Stream?> OpenReadAsync(string blobName, CancellationToken cancellationToken)
        {
            RequestedNames.Add(blobName);
            return Task.FromResult(StreamFactory(blobName));
        }
    }

    private sealed class CaptureLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }
}
