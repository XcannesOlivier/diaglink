using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebApp.Api.Models;
using WebApp.Api.Models.Entities;
using WebApp.Api.Services;

namespace WebApp.Api.Tests;

[TestClass]
public sealed class AiChatRuntimeSelectorTests
{
    [TestMethod]
    public void EmptyConfiguration_SelectsHostedAgent()
    {
        var selector = Selector();

        Assert.AreEqual(AiChatRuntime.HostedAgent, selector.Select(Machine()));
    }

    [TestMethod]
    public void NonEnabledMachine_SelectsHostedAgent()
    {
        var selector = Selector(Guid.NewGuid());

        Assert.AreEqual(AiChatRuntime.HostedAgent, selector.Select(Machine()));
    }

    [TestMethod]
    public void EnabledMachine_SelectsClaudeDirect()
    {
        var machine = Machine();
        var selector = Selector(machine.Id);

        Assert.AreEqual(AiChatRuntime.ClaudeDirect, selector.Select(machine));
    }

    [TestMethod]
    public void MissingResolvedMachine_SelectsHostedAgent()
    {
        var selector = Selector(Guid.NewGuid());

        Assert.AreEqual(AiChatRuntime.HostedAgent, selector.Select(null));
    }

    [TestMethod]
    public void MachineNameCannotEnableClaudeDirect()
    {
        var enabled = Machine(name: "DX10z");
        var sameNameDifferentId = Machine(name: "DX10z");
        var selector = Selector(enabled.Id);

        Assert.AreEqual(AiChatRuntime.HostedAgent, selector.Select(sameNameDifferentId));
    }

    [TestMethod]
    public void FrontendContractCannotChooseRuntime()
    {
        Assert.IsNull(typeof(ChatRequest).GetProperty("Runtime"));
        Assert.IsNull(typeof(ChatRequest).GetProperty("AiChatRuntime"));
        Assert.AreEqual(typeof(Machine), typeof(IAiChatRuntimeSelector)
            .GetMethod(nameof(IAiChatRuntimeSelector.Select))!.GetParameters().Single().ParameterType);
    }

    [TestMethod]
    public void HostedDispatch_DoesNotInvokeClaudeDirect()
    {
        var hosted = 0;
        var direct = 0;

        _ = AiChatRuntimeDispatch.SelectStream(
            AiChatRuntime.HostedAgent,
            () => Counted(() => hosted++),
            () => Counted(() => direct++));

        Assert.AreEqual(1, hosted);
        Assert.AreEqual(0, direct);
    }

    [TestMethod]
    public void ClaudeDirectDispatch_DoesNotInvokeHostedAgent()
    {
        var hosted = 0;
        var direct = 0;

        _ = AiChatRuntimeDispatch.SelectStream(
            AiChatRuntime.ClaudeDirect,
            () => Counted(() => hosted++),
            () => Counted(() => direct++));

        Assert.AreEqual(0, hosted);
        Assert.AreEqual(1, direct);
    }

    [TestMethod]
    public async Task ClaudeDirectFailure_DoesNotFallbackToHostedAgent()
    {
        var hosted = 0;
        var stream = AiChatRuntimeDispatch.SelectStream(
            AiChatRuntime.ClaudeDirect,
            () => Counted(() => hosted++),
            Failing);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () => await Collect(stream));
        Assert.AreEqual(0, hosted);
    }

    private static AiChatRuntimeSelector Selector(params Guid[] enabled) =>
        new(Options.Create(new ClaudeDirectChatOptions { EnabledMachineIds = enabled }));

    private static Machine Machine(string name = "Machine") => new()
    {
        Id = Guid.NewGuid(),
        CompanyId = Guid.NewGuid(),
        Name = name,
        Status = "active",
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow
    };

    private static IAsyncEnumerable<StreamChunk> Counted(Action count)
    {
        count();
        return One();

        static async IAsyncEnumerable<StreamChunk> One()
        {
            await Task.Yield();
            yield return StreamChunk.Text("ok");
        }
    }

    private static async IAsyncEnumerable<StreamChunk> Failing()
    {
        await Task.Yield();
        throw new InvalidOperationException("direct failed");
#pragma warning disable CS0162
        yield break;
#pragma warning restore CS0162
    }

    private static async Task<List<StreamChunk>> Collect(IAsyncEnumerable<StreamChunk> stream)
    {
        var chunks = new List<StreamChunk>();
        await foreach (var chunk in stream)
            chunks.Add(chunk);
        return chunks;
    }
}

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
    public async Task ClaudeDirectNeverProducesVisionUsage()
    {
        var fixture = Fixture(Result(visuals: [new("manual", 75, "full", null, "page.png", "manual/page.png")]));

        var chunks = await Collect(fixture.Runtime.StreamMessageAsync(fixture.Machine, "question"));

        Assert.IsFalse(chunks.Any(chunk => chunk.VisionUsage is not null));
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
            []);
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
        IReadOnlyList<ClaudeDirectError>? errors = null,
        long input = 10,
        long output = 5,
        string model = "claude-returned") => new(
            finalText,
            toolUses ?? [],
            mcpCalls ?? [],
            visuals ?? [],
            [],
            [new(1, input, output, model, "end_turn", "response-1", "request-1")],
            new(input, output, input + output),
            model,
            "end_turn",
            errors ?? []);

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
