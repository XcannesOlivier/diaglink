using System.ClientModel.Primitives;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenAI.Responses;
using WebApp.Api.Models;
using WebApp.Api.Services;

namespace WebApp.Api.Tests;

#pragma warning disable OPENAI001

[TestClass]
public class TechnicalVisualTransportTests
{
    private static readonly TechnicalVisualReference First = new(
        "manual", 71, "full", null, "first.png", "manual/page-00071/first.png");
    private static readonly TechnicalVisualReference Second = new(
        "manual", 71, "tile", "r02-c01", "second.png", "manual/page-00071/second.png");

    [TestMethod]
    public void NoVisuals_PreservesUsageOnlyBehavior()
    {
        var chunks = Process("call_1", """{"usage":{"input_tokens":4,"output_tokens":2}}""");
        Assert.HasCount(1, chunks);
        Assert.IsNotNull(chunks[0].VisionUsage);
        Assert.IsFalse(chunks[0].HasVisuals);
    }

    [TestMethod]
    public void ValidVisual_TraversesAgentFrameworkAsTypedChunk()
    {
        var chunks = Process("call_1", Payload(First));
        var visual = chunks.Single(chunk => chunk.HasVisuals).Visuals!.Single();
        Assert.AreEqual(First, visual);
    }

    [TestMethod]
    public void MultipleVisuals_PreserveDiscoveryOrder()
    {
        var chunk = Process("call_1", Payload(First, Second)).Single(item => item.HasVisuals);
        CollectionAssert.AreEqual(new[] { First, Second }, chunk.Visuals!);
    }

    [TestMethod]
    public void SuccessiveOutputs_EmitOnlyNewReferences()
    {
        var diagnostics = new VisionToolDiagnostics(new Capture());
        var first = AgentFrameworkService.CreateVisionChunks(diagnostics, Item("call_1", Payload(First)), "done", "parent");
        var second = AgentFrameworkService.CreateVisionChunks(diagnostics, Item("call_2", Payload(First, Second)), "done", "parent");

        CollectionAssert.AreEqual(new[] { First }, first.Single(chunk => chunk.HasVisuals).Visuals!);
        CollectionAssert.AreEqual(new[] { Second }, second.Single(chunk => chunk.HasVisuals).Visuals!);
    }

    [TestMethod]
    public void MessageAccumulator_DuplicateAssetKeyUsesFirstOccurrence()
    {
        var replacement = First with { DocumentId = "other", Name = "replacement.png" };
        var accumulator = new TechnicalVisualAccumulator();
        accumulator.AddRange([First, replacement]);
        Assert.HasCount(1, accumulator.Items);
        Assert.AreEqual(First, accumulator.Items[0]);
    }

    [TestMethod]
    public void MessageAccumulator_DifferentAssetKeysRemainDistinctAndOrdered()
    {
        var accumulator = new TechnicalVisualAccumulator();
        accumulator.AddRange([Second, First]);
        CollectionAssert.AreEqual(new[] { Second, First }, accumulator.Items.ToArray());
    }

    [TestMethod]
    public void MessageAccumulator_UsesOrdinalCaseSensitiveAssetKeys()
    {
        var caseVariant = First with { AssetKey = First.AssetKey.ToUpperInvariant() };
        var accumulator = new TechnicalVisualAccumulator();
        accumulator.AddRange([First, caseVariant]);
        CollectionAssert.AreEqual(new[] { First, caseVariant }, accumulator.Items.ToArray());
    }

    [TestMethod]
    public void TextChunkBehaviorIsUnchanged()
    {
        var chunk = StreamChunk.Text("texte");
        Assert.AreEqual("texte", chunk.TextDelta);
        Assert.IsTrue(chunk.IsText);
        Assert.IsFalse(chunk.HasVisuals);
    }

    [TestMethod]
    public void VisionUsageBehaviorIsUnchangedWhenVisualsArePresent()
    {
        var usage = Process("call_1", PayloadWithUsage(First)).Single(chunk => chunk.VisionUsage != null).VisionUsage!.Usage;
        Assert.AreEqual(4, usage.InputTokens);
        Assert.AreEqual(2, usage.OutputTokens);
        Assert.AreEqual(6, usage.TotalTokens);
    }

    [TestMethod]
    public void TransportDoesNotLogVisualValues()
    {
        var logger = new Capture();
        var diagnostics = new VisionToolDiagnostics(logger);
        AgentFrameworkService.CreateVisionChunks(diagnostics, Item("call_1", Payload(First)), "done", "parent");
        var logs = string.Join("\n", logger.Lines);
        foreach (var sensitive in new[] { First.DocumentId, First.Name, First.AssetKey })
            Assert.IsFalse(logs.Contains(sensitive));
    }

    private static IReadOnlyList<StreamChunk> Process(string call, string payload) =>
        AgentFrameworkService.CreateVisionChunks(new VisionToolDiagnostics(new Capture()), Item(call, payload), "done", "parent");

    private static ResponseItem Item(string call, string payload) =>
        ModelReaderWriter.Read<ResponseItem>(BinaryData.FromString(System.Text.Json.JsonSerializer.Serialize(new
        {
            type = "openapi_call_output",
            id = "item_" + call,
            call_id = call,
            name = "blob_page_images_analyze_page",
            status = "completed",
            output = System.Text.Json.JsonSerializer.Serialize(new { response = payload })
        })))!;

    private static string Payload(params TechnicalVisualReference[] visuals) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            visuals = visuals.Select(visual => new
            {
                document_id = visual.DocumentId,
                page = visual.Page,
                asset_type = visual.AssetType,
                tile = visual.Tile,
                name = visual.Name,
                asset_key = visual.AssetKey
            })
        });

    private static string PayloadWithUsage(params TechnicalVisualReference[] visuals) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            visuals = visuals.Select(visual => new
            {
                document_id = visual.DocumentId,
                page = visual.Page,
                asset_type = visual.AssetType,
                tile = visual.Tile,
                name = visual.Name,
                asset_key = visual.AssetKey
            }),
            usage = new { input_tokens = 4, output_tokens = 2 }
        });

    private sealed class Capture : ILogger
    {
        public List<string> Lines { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? error, Func<TState, Exception?, string> format) =>
            Lines.Add(format(state, error));
    }
}
