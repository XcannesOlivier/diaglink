using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.Json;
using WebApp.Api.Models;
using WebApp.Api.Models.Entities;
using WebApp.Api.Services;

namespace WebApp.Api.Tests;

[TestClass]
public sealed class ClaudeDirectChatRuntimeTests
{
    [TestMethod]
    public async Task UsesRequestFactoryWithResolvedMachineAndMessage()
    {
        var fixture = Fixture(Result(finalText: "answer"));

        await Collect(fixture.Runtime.StreamMessageAsync(fixture.Machine, "question"));

        Assert.AreSame(fixture.Machine, fixture.Factory.Machine);
        Assert.AreEqual("question", fixture.Factory.Messages!.Single().Text);
        Assert.AreEqual("user", fixture.Factory.Messages!.Single().Role);
    }

    [TestMethod]
    public async Task UserImagesFlowToClaudeDirectRequestWithoutToolIndirection()
    {
        var fixture = Fixture(Result(finalText: "answer"));
        var images = new[] { new ClaudeDirectUserImage("image/png", "iVBORw0KGgo=") };

        await Collect(fixture.Runtime.StreamMessageAsync(fixture.Machine, "inspect", default, images));

        var message = fixture.Factory.Messages!.Single();
        Assert.AreEqual("inspect", message.Text);
        Assert.AreSame(images, message.Images);
        Assert.AreEqual(1, fixture.Service.CallCount);
    }

    [TestMethod]
    public async Task CallsClaudeDirectServiceExactlyOnce()
    {
        var fixture = Fixture(Result(finalText: "answer"));

        await Collect(fixture.Runtime.StreamMessageAsync(fixture.Machine, "question"));

        Assert.AreEqual(1, fixture.Service.CallCount);
        Assert.AreSame(fixture.Factory.Request, fixture.Service.Request);
    }

    [TestMethod]
    public async Task FinalTextBecomesChunk()
    {
        var fixture = Fixture(Result(finalText: "final answer"));

        var chunks = await Collect(fixture.Runtime.StreamMessageAsync(fixture.Machine, "question"));

        Assert.AreEqual("final answer", chunks.Single(chunk => chunk.IsText).TextDelta);
    }

    [TestMethod]
    public async Task FileSearchMcpBecomesToolUse()
    {
        var fixture = Fixture(Result(mcpCalls: [new("mcp-1", "file_search", false)]));

        var chunks = await Collect(fixture.Runtime.StreamMessageAsync(fixture.Machine, "question"));

        Assert.IsTrue(chunks.Any(chunk => chunk.IsToolUse && chunk.ToolName == "file_search"));
    }

    [TestMethod]
    public async Task McpResultDoesNotBecomePublicToolUse()
    {
        var fixture = Fixture(Result(mcpCalls: [new("mcp-1", "mcp_tool_result", false)]));

        var chunks = await Collect(fixture.Runtime.StreamMessageAsync(fixture.Machine, "question"));

        Assert.IsFalse(chunks.Any(chunk => chunk.IsToolUse));
    }

    [TestMethod]
    public async Task GetPageImageBecomesToolUse()
    {
        var fixture = Fixture(Result(toolUses: [new("tool-1", "get_page_image", "{secret}")]));

        var chunks = await Collect(fixture.Runtime.StreamMessageAsync(fixture.Machine, "question"));

        Assert.AreEqual("get_page_image", chunks.Single(chunk => chunk.IsToolUse).ToolName);
    }

    [TestMethod]
    public async Task ToolUseEventsExposeNoArgumentsOrSecrets()
    {
        var fixture = Fixture(Result(toolUses: [new("tool-1", "get_page_image", "data:image/png;base64,SECRET")]));

        var chunks = await Collect(fixture.Runtime.StreamMessageAsync(fixture.Machine, "question"));
        var toolChunk = chunks.Single(chunk => chunk.IsToolUse);

        Assert.AreEqual("get_page_image", toolChunk.ToolName);
        Assert.IsFalse(toolChunk.ToString()!.Contains("SECRET", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task DuplicateToolsProduceOneEventPerName()
    {
        var fixture = Fixture(Result(
            toolUses: [new("1", "get_page_image", "{}"), new("2", "get_page_image", "{}")],
            mcpCalls: [new("3", "file_search", false), new("4", "file_search", false)]));

        var chunks = await Collect(fixture.Runtime.StreamMessageAsync(fixture.Machine, "question"));

        CollectionAssert.AreEquivalent(
            new[] { "file_search", "get_page_image" },
            chunks.Where(chunk => chunk.IsToolUse).Select(chunk => chunk.ToolName).ToArray());
    }

    [TestMethod]
    public async Task VisualsUseExistingStreamChunkContract()
    {
        var visual = new TechnicalVisualReference("manual", 75, "full", null, "page.png", "manual/page.png");
        var fixture = Fixture(Result(visuals: [visual]));

        var chunks = await Collect(fixture.Runtime.StreamMessageAsync(fixture.Machine, "question"));

        Assert.AreSame(visual, chunks.Single(chunk => chunk.HasVisuals).Visuals!.Single());
    }

    [TestMethod]
    public async Task SourcesUseInternalStreamChunkContract()
    {
        var source = new TechnicalSourceReference("manual", 72, "70", "p. 70", 10, 15, 0);
        var fixture = Fixture(Result(sources: [source]));

        var chunks = await Collect(fixture.Runtime.StreamMessageAsync(fixture.Machine, "question"));

        Assert.AreSame(source, chunks.Single(chunk => chunk.HasSources).Sources!.Single());
    }

    [TestMethod]
    public async Task WebCitationUsesExistingAnnotationStreamContract()
    {
        var citation = new ClaudeDirectWebCitation(
            "https://example.test/technical-article",
            "Technical article",
            "Quoted technical passage");
        var fixture = Fixture(Result(webCitations: [citation]));

        var chunks = await Collect(fixture.Runtime.StreamMessageAsync(fixture.Machine, "question"));

        var annotation = chunks.Single(chunk => chunk.HasAnnotations).Annotations!.Single();
        Assert.AreEqual("uri_citation", annotation.Type);
        Assert.AreEqual("Technical article", annotation.Label);
        Assert.AreEqual("https://example.test/technical-article", annotation.Url);
        Assert.AreEqual("Quoted technical passage", annotation.Quote);
        Assert.IsNull(annotation.FileId);
        Assert.IsFalse(chunks.Any(chunk => chunk.HasSources));
    }

    [TestMethod]
    public async Task ResponseWithoutWebCitationProducesNoAnnotationChunk()
    {
        var fixture = Fixture(Result());

        var chunks = await Collect(fixture.Runtime.StreamMessageAsync(fixture.Machine, "question"));

        Assert.IsFalse(chunks.Any(chunk => chunk.HasAnnotations));
    }

    [TestMethod]
    public async Task PdfSourcesRemainSeparateFromWebCitationAnnotations()
    {
        var source = new TechnicalSourceReference("manual", 72, "70", "p. 70", 10, 15, 0);
        var citation = new ClaudeDirectWebCitation(
            "https://example.test/web-source",
            "Web source",
            "Web passage");
        var fixture = Fixture(Result(sources: [source], webCitations: [citation]));

        var chunks = await Collect(fixture.Runtime.StreamMessageAsync(fixture.Machine, "question"));

        var sourceChunk = chunks.Single(chunk => chunk.HasSources);
        var annotationChunk = chunks.Single(chunk => chunk.HasAnnotations);
        Assert.AreSame(source, sourceChunk.Sources!.Single());
        Assert.HasCount(1, sourceChunk.Sources!);
        Assert.HasCount(1, annotationChunk.Annotations!);
        Assert.AreEqual("https://example.test/web-source", annotationChunk.Annotations![0].Url);
        Assert.IsNull(sourceChunk.Annotations);
        Assert.IsNull(annotationChunk.Sources);
    }

    [TestMethod]
    public async Task AggregateUsageBecomesSingleChatResponse()
    {
        var fixture = Fixture(Result(input: 100, output: 25));

        var chunks = await Collect(fixture.Runtime.StreamMessageAsync(fixture.Machine, "question"));
        var usages = chunks.Where(chunk => chunk.Usage is not null).Select(chunk => chunk.Usage!).ToArray();

        Assert.HasCount(1, usages);
        Assert.AreEqual(AiUsageType.ChatResponse, usages[0].UsageType);
        Assert.AreEqual(100, usages[0].InputTokens);
        Assert.AreEqual(25, usages[0].OutputTokens);
        Assert.AreEqual(125, usages[0].TotalTokens);
    }

    [TestMethod]
    public async Task UsageContainsOnlyAllowedPerCallBreakdownFieldsWithoutChangingTotals()
    {
        var calls = new ClaudeDirectCallUsage[]
        {
            new(1, 10, 2, "claude-returned", "tool_use", "response-secret", "request-secret")
            {
                CacheReadInputTokens = 3,
                CacheCreationInputTokens = 5,
                CacheCreation5mInputTokens = 4,
                CacheCreation1hInputTokens = 1,
                Tools = ["file_search"]
            },
            new(2, 20, 5, "claude-returned", "end_turn", "response-final", "request-final")
            {
                Tools = []
            }
        };
        var fixture = Fixture(Result(input: 30, output: 7, calls: calls));

        var chunks = await Collect(fixture.Runtime.StreamMessageAsync(fixture.Machine, "question"));
        var usage = chunks.Single(chunk => chunk.Usage is not null).Usage!;

        Assert.AreEqual(30, usage.InputTokens);
        Assert.AreEqual(7, usage.OutputTokens);
        Assert.AreEqual(37, usage.TotalTokens);
        Assert.IsNotNull(usage.CallBreakdownJson);
        using var document = JsonDocument.Parse(usage.CallBreakdownJson);
        var serializedCalls = document.RootElement.EnumerateArray().ToArray();
        Assert.HasCount(2, serializedCalls);
        CollectionAssert.AreEquivalent(
            new[] { "callNumber", "inputTokens", "outputTokens", "cacheReadInputTokens", "cacheCreationInputTokens", "cacheCreation5mInputTokens", "cacheCreation1hInputTokens", "totalTokens", "model", "stopReason", "tools" },
            serializedCalls[0].EnumerateObject().Select(property => property.Name).ToArray());
        Assert.AreEqual(12, serializedCalls[0].GetProperty("totalTokens").GetInt64());
        Assert.AreEqual("file_search", serializedCalls[0].GetProperty("tools")[0].GetString());
        Assert.AreEqual(3, serializedCalls[0].GetProperty("cacheReadInputTokens").GetInt64());
        Assert.IsFalse(usage.CallBreakdownJson.Contains("response-secret", StringComparison.Ordinal));
        Assert.IsFalse(usage.CallBreakdownJson.Contains("request-secret", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task UsageHasAnthropicReturnedModelAndConfiguredDeployment()
    {
        var fixture = Fixture(Result(model: "claude-returned"), deployment: "claude-configured");

        var chunks = await Collect(fixture.Runtime.StreamMessageAsync(fixture.Machine, "question"));
        var usage = chunks.Single(chunk => chunk.Usage is not null).Usage!;

        Assert.AreEqual("Anthropic", usage.Provider);
        Assert.AreEqual("claude-returned", usage.Model);
        Assert.AreEqual("claude-configured", usage.Deployment);
    }

    [TestMethod]
    public async Task UsageCarriesAggregateCacheTokenCategories()
    {
        var fixture = Fixture(Result(input: 10, output: 2, cacheRead: 30, cacheCreation5m: 40, cacheCreation1h: 50));

        var chunks = await Collect(fixture.Runtime.StreamMessageAsync(fixture.Machine, "question"));
        var usage = chunks.Single(chunk => chunk.Usage is not null).Usage!;

        Assert.AreEqual(30, usage.CacheReadInputTokens);
        Assert.AreEqual(90, usage.CacheCreationInputTokens);
        Assert.AreEqual(40, usage.CacheCreation5mInputTokens);
        Assert.AreEqual(50, usage.CacheCreation1hInputTokens);
    }

    [TestMethod]
    public async Task WebSearchRequestsFlowIntoInternalResponseUsage()
    {
        var fixture = Fixture(Result(webSearchRequests: 3));

        var chunks = await Collect(fixture.Runtime.StreamMessageAsync(fixture.Machine, "question"));

        Assert.AreEqual(3, chunks.Single(chunk => chunk.Usage is not null).Usage!.WebSearchRequests);
    }

    [TestMethod]
    public async Task ClaudeDirectNeverProducesSeparateVisionUsage()
    {
        var fixture = Fixture(Result(visuals: [new("manual", 75, "full", null, "page.png", "manual/page.png")]));

        var chunks = await Collect(fixture.Runtime.StreamMessageAsync(fixture.Machine, "question"));

        Assert.IsFalse(chunks.Any(chunk => chunk.Usage?.UsageType == AiUsageType.VisionTool));
    }

    [TestMethod]
    public async Task FileSearchDoesNotProduceSecondUsageMeasurement()
    {
        var fixture = Fixture(Result(mcpCalls: [new("mcp", "file_search", false)]));

        var chunks = await Collect(fixture.Runtime.StreamMessageAsync(fixture.Machine, "question"));

        Assert.AreEqual(1, chunks.Count(chunk => chunk.Usage is not null));
    }

    [TestMethod]
    public async Task DoesNotInventAnnotationsFromMcpObservations()
    {
        var fixture = Fixture(Result(mcpCalls: [new("mcp", "file_search", false)]));

        var chunks = await Collect(fixture.Runtime.StreamMessageAsync(fixture.Machine, "question"));

        Assert.IsFalse(chunks.Any(chunk => chunk.HasAnnotations));
    }

    [TestMethod]
    public async Task UnrecoveredErrorYieldsUsageThenThrowsForSseErrorMapping()
    {
        var fixture = Fixture(Result(finalText: null, errors: [new("mcp_error", "File Search failed.")]));
        var chunks = new List<StreamChunk>();

        var exception = await Assert.ThrowsExactlyAsync<ClaudeDirectRuntimeException>(async () =>
        {
            await foreach (var chunk in fixture.Runtime.StreamMessageAsync(fixture.Machine, "question"))
                chunks.Add(chunk);
        });

        Assert.AreEqual("File Search failed.", exception.Message);
        Assert.AreEqual(1, chunks.Count(chunk => chunk.Usage is not null));
        Assert.IsFalse(chunks.Single(chunk => chunk.Usage is not null).Usage!.Completed);
    }

    [TestMethod]
    public async Task RecoveredErrorDoesNotFailSuccessfulResponse()
    {
        var fixture = Fixture(Result(finalText: "answer", errors: [new("blob", "recovered", Recovered: true)]));

        var chunks = await Collect(fixture.Runtime.StreamMessageAsync(fixture.Machine, "question"));

        Assert.AreEqual("answer", chunks.Single(chunk => chunk.IsText).TextDelta);
        Assert.IsTrue(chunks.Single(chunk => chunk.Usage is not null).Usage!.Completed);
    }

    [TestMethod]
    public async Task CancellationTokenFlowsThroughFactoryAndService()
    {
        var fixture = Fixture(Result());
        using var source = new CancellationTokenSource();

        await Collect(fixture.Runtime.StreamMessageAsync(fixture.Machine, "question", source.Token));

        Assert.AreEqual(source.Token, fixture.Factory.CancellationToken);
        Assert.AreEqual(source.Token, fixture.Service.CancellationToken);
    }

    private static RuntimeFixture Fixture(ClaudeDirectChatResult result, string deployment = "claude-sonnet-5")
    {
        var machine = new Machine
        {
            Id = Guid.NewGuid(), CompanyId = Guid.NewGuid(), Name = "DX10z", Status = "active",
            CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
        };
        var request = new ClaudeDirectChatRequest(
            new("project", "toolbox", "1", "mcp", "vector", "prefix", "machine"),
            "prompt",
            [],
            new(false, string.Empty));
        var factory = new RequestFactoryStub(request);
        var service = new ChatServiceStub(result);
        var runtime = new ClaudeDirectChatRuntime(factory, service, Options.Create(new ClaudeDirectChatOptions
        {
            Deployment = deployment
        }));
        return new(runtime, factory, service, machine);
    }

    private static ClaudeDirectChatResult Result(
        string? finalText = "answer",
        IReadOnlyList<ClaudeDirectToolUse>? toolUses = null,
        IReadOnlyList<ClaudeDirectMcpCall>? mcpCalls = null,
        IReadOnlyList<TechnicalVisualReference>? visuals = null,
        IReadOnlyList<TechnicalSourceReference>? sources = null,
        IReadOnlyList<ClaudeDirectWebCitation>? webCitations = null,
        IReadOnlyList<ClaudeDirectError>? errors = null,
        long input = 10,
        long output = 5,
        long cacheRead = 0,
        long cacheCreation5m = 0,
        long cacheCreation1h = 0,
        long webSearchRequests = 0,
        string model = "claude-returned",
        IReadOnlyList<ClaudeDirectCallUsage>? calls = null) => new(
            finalText,
            toolUses ?? [],
            mcpCalls ?? [],
            visuals ?? [],
            sources ?? [],
            [],
            calls ?? [new(1, input, output, model, "end_turn", "response-1", "request-1")
            {
                CacheReadInputTokens = cacheRead,
                CacheCreationInputTokens = cacheCreation5m + cacheCreation1h,
                CacheCreation5mInputTokens = cacheCreation5m,
                CacheCreation1hInputTokens = cacheCreation1h
            }],
            new(input, output, input + output)
            {
                CacheReadInputTokens = cacheRead,
                CacheCreationInputTokens = cacheCreation5m + cacheCreation1h,
                CacheCreation5mInputTokens = cacheCreation5m,
                CacheCreation1hInputTokens = cacheCreation1h,
                WebSearchRequests = webSearchRequests
            },
            model,
            "end_turn",
            errors ?? [])
        {
            WebCitations = webCitations ?? []
        };

    private static async Task<List<StreamChunk>> Collect(IAsyncEnumerable<StreamChunk> stream)
    {
        var chunks = new List<StreamChunk>();
        await foreach (var chunk in stream)
            chunks.Add(chunk);
        return chunks;
    }

    private sealed record RuntimeFixture(
        ClaudeDirectChatRuntime Runtime,
        RequestFactoryStub Factory,
        ChatServiceStub Service,
        Machine Machine);

    private sealed class RequestFactoryStub(ClaudeDirectChatRequest request) : IClaudeDirectChatRequestFactory
    {
        public ClaudeDirectChatRequest Request { get; } = request;
        public Machine? Machine { get; private set; }
        public IReadOnlyList<ClaudeDirectMessage>? Messages { get; private set; }
        public CancellationToken CancellationToken { get; private set; }

        public Task<ClaudeDirectChatRequest> CreateAsync(
            Machine machine,
            IReadOnlyList<ClaudeDirectMessage> messages,
            CancellationToken cancellationToken = default)
        {
            Machine = machine;
            Messages = messages;
            CancellationToken = cancellationToken;
            return Task.FromResult(Request);
        }
    }

    private sealed class ChatServiceStub(ClaudeDirectChatResult result) : IClaudeDirectChatService
    {
        public int CallCount { get; private set; }
        public ClaudeDirectChatRequest? Request { get; private set; }
        public CancellationToken CancellationToken { get; private set; }

        public Task<ClaudeDirectChatResult> CompleteAsync(
            ClaudeDirectChatRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            Request = request;
            CancellationToken = cancellationToken;
            return Task.FromResult(result);
        }
    }
}
