using System.ClientModel.Primitives;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenAI.Responses;
using WebApp.Api.Services;
namespace WebApp.Api.Tests;
#pragma warning disable OPENAI001
[TestClass]
public class VisionToolDiagnosticsTests
{
    private static string NestedLogs(string response)
    {
        var logger = new Capture();
        var wire = System.Text.Json.JsonSerializer.Serialize(new {
            type = "openapi_call_output", id = "item_nested", call_id = "call_nested",
            name = "blob_page_images_analyze_page",
            output = System.Text.Json.JsonSerializer.Serialize(new { response })
        });
        var item = ModelReaderWriter.Read<ResponseItem>(BinaryData.FromString(wire))!;
        new VisionToolDiagnostics(logger).Observe(item, "done", "resp_parent");
        return string.Join("\n", logger.Lines);
    }

    [TestMethod]
    public void NestedResponseExtractsOnlyTechnicalFields()
    {
        var logs = NestedLogs("""
            {"usage":{"input_tokens":9715,"output_tokens":568,"total_tokens":10283},
             "model":"vision-test","provider":"provider-test","deployment":"deployment-test","version":"v1",
             "response_id":"resp_vision","ocr":"PRIVATE_OCR","images":["PRIVATE_IMAGE"],
             "document":{"text":"PRIVATE_DOCUMENT"},"sas":"https://example.test/?sig=SECRET",
             "authorization":"Bearer SECRET_TOKEN"}
            """);
        StringAssert.Contains(logs, "VisionProbe Nested");
        StringAssert.Contains(logs, "InputTokens=9715");
        StringAssert.Contains(logs, "OutputTokens=568");
        StringAssert.Contains(logs, "TotalTokens=10283");
        StringAssert.Contains(logs, "Version=v1");
        StringAssert.Contains(logs, "Model=vision-test");
        StringAssert.Contains(logs, "NestedResponseId=resp_vision");
        foreach (var secret in new[] { "PRIVATE_OCR", "PRIVATE_IMAGE", "PRIVATE_DOCUMENT", "SECRET", "https://" })
            Assert.IsFalse(logs.Contains(secret));
    }

    [TestMethod]
    public void DoubleEncodedResponseAndResponseWrapperAreDecoded()
    {
        const string payload = """{"usage":{"input_tokens":12,"output_tokens":3}}""";
        var encoded = System.Text.Json.JsonSerializer.Serialize(payload);
        StringAssert.Contains(NestedLogs(encoded), "InputTokens=12");
        var wrapped = System.Text.Json.JsonSerializer.Serialize(new { response = encoded });
        StringAssert.Contains(NestedLogs(wrapped), "OutputTokens=3");
    }

    [TestMethod]
    public void OrphanAnnouncementsNeverProducePersistableMeasurements()
    {
        var probe = new VisionToolDiagnostics(new Capture());
        Assert.IsNull(probe.Observe(Vision("A", false), "added", "parent"));
        Assert.AreEqual(0, probe.Measurements.Count);
        Assert.IsNull(probe.Observe(Vision("A", false), "done", "parent"));
        Assert.AreEqual(0, probe.Measurements.Count);
        Assert.IsNull(probe.Observe(Vision("empty_output", true, ""), "added", "parent"));
        Assert.AreEqual(0, probe.Measurements.Count);
        foreach (var call in new[] { "B", "C" })
        {
            Assert.IsNull(probe.Observe(Vision(call, false), "added", "parent"));
            var output = Vision(call, true, """{"usage":{"input_tokens":10,"output_tokens":2}}""", name: null);
            var final = probe.Observe(output, "done", "parent")!;
            Assert.IsTrue(final.Usage.Completed && final.Usage.Available);
            Assert.AreEqual(final.EventId, probe.Observe(output, "done", "parent")!.EventId);
        }
        Assert.AreEqual(2, probe.Measurements.Count);
        Assert.IsTrue(probe.Measurements.All(m => m.Usage.CallId is "B" or "C"));
    }

    [TestMethod]
    public void MissingInvalidEmptyAndExcessiveEncodingAreSafe()
    {
        StringAssert.Contains(NestedLogs("""{"result":["PRIVATE"]}"""), "HasUsage=False");
        Assert.IsFalse(NestedLogs("""{"result":["PRIVATE"]}""").Contains("PRIVATE"));
        StringAssert.Contains(NestedLogs("PRIVATE_INVALID"), "Diagnostic=InvalidJson");
        Assert.IsFalse(NestedLogs("PRIVATE_INVALID").Contains("PRIVATE_INVALID"));
        StringAssert.Contains(NestedLogs(""), "Diagnostic=Empty");
        var payload = """{"usage":{"input_tokens":987654}}""";
        for (var i = 0; i < 4; i++) payload = System.Text.Json.JsonSerializer.Serialize(payload);
        var logs = NestedLogs(payload);
        StringAssert.Contains(logs, "DecodeLimitReached");
        Assert.IsFalse(logs.Contains("987654"));
    }

    private static ResponseItem Vision(string call, bool output, string? payload = null, string? name = "blob_page_images_analyze_page") =>
        ModelReaderWriter.Read<ResponseItem>(BinaryData.FromString(System.Text.Json.JsonSerializer.Serialize(new {
            type = output ? "openapi_call_output" : "openapi_call", id = "item_" + call, call_id = call,
            name, status = "completed", output = payload == null ? null :
                System.Text.Json.JsonSerializer.Serialize(new { response = payload })
        })))!;

    private static VisionToolDiagnostics ObservePayload(string payload, Capture? logger = null)
    {
        var probe = new VisionToolDiagnostics(logger ?? new Capture());
        probe.Observe(Vision("visual", true, payload), "done", "parent");
        return probe;
    }

    private const string FullVisual = """
        {"document_id":"manual","page":71,"asset_type":"full","tile":null,
         "name":"manual_page-00071-full.png","asset_key":"manual/page-00071/manual_page-00071-full.png"}
        """;

    private const string TileVisual = """
        {"document_id":"manual","page":71,"asset_type":"tile","tile":"r02-c01",
         "name":"manual_page-00071-tile-r02-c01.png","asset_key":"manual/page-00071/manual_page-00071-tile-r02-c01.png"}
        """;

    [TestMethod]
    public void Visuals_ExtractsValidFull()
    {
        var visual = ObservePayload($$"""{"visuals":[{{FullVisual}}]}""").Visuals.Single();
        Assert.AreEqual("manual", visual.DocumentId);
        Assert.AreEqual(71, visual.Page);
        Assert.AreEqual("full", visual.AssetType);
        Assert.IsNull(visual.Tile);
        Assert.AreEqual("manual_page-00071-full.png", visual.Name);
        Assert.AreEqual("manual/page-00071/manual_page-00071-full.png", visual.AssetKey);
    }

    [TestMethod]
    public void Visuals_ExtractsValidTile()
    {
        var visual = ObservePayload($$"""{"visuals":[{{TileVisual}}]}""").Visuals.Single();
        Assert.AreEqual("tile", visual.AssetType);
        Assert.AreEqual("r02-c01", visual.Tile);
    }

    [TestMethod]
    public void Visuals_PreservesValidInputOrder()
    {
        var visuals = ObservePayload($$"""{"visuals":[{{TileVisual}},{{FullVisual}}]}""").Visuals;
        Assert.HasCount(2, visuals);
        Assert.AreEqual("tile", visuals[0].AssetType);
        Assert.AreEqual("full", visuals[1].AssetType);
    }

    [TestMethod]
    public void Visuals_AbsentLeavesCollectionEmpty() =>
        Assert.HasCount(0, ObservePayload("{}").Visuals);

    [TestMethod]
    public void Visuals_EmptyArrayLeavesCollectionEmpty() =>
        Assert.HasCount(0, ObservePayload("""{"visuals":[]}""").Visuals);

    [TestMethod]
    public void Visuals_MalformedEntryIsIgnored() =>
        Assert.HasCount(0, ObservePayload("""{"visuals":["invalid",{"page":71}]}""").Visuals);

    [TestMethod]
    public void Visuals_UnknownAssetTypeIsIgnored() =>
        Assert.HasCount(0, ObservePayload("""{"visuals":[{"document_id":"manual","page":71,"asset_type":"preview","tile":null,"name":"preview.png","asset_key":"manual/page-00071/preview.png"}]}""").Visuals);

    [TestMethod]
    public void Visuals_InvalidTileIsIgnored() =>
        Assert.HasCount(0, ObservePayload("""{"visuals":[{"document_id":"manual","page":71,"asset_type":"tile","tile":"r2-c1","name":"tile.png","asset_key":"manual/page-00071/tile.png"}]}""").Visuals);

    [TestMethod]
    public void Visuals_FullWithTileIsIgnored() =>
        Assert.HasCount(0, ObservePayload("""{"visuals":[{"document_id":"manual","page":71,"asset_type":"full","tile":"r02-c01","name":"full.png","asset_key":"manual/page-00071/full.png"}]}""").Visuals);

    [TestMethod]
    public void Visuals_MissingRequiredValuesAndNonPngNamesAreIgnored()
    {
        Assert.HasCount(0, ObservePayload("""{"visuals":[{"document_id":"","page":71,"asset_type":"full","tile":null,"name":"full.png","asset_key":"manual/full.png"}]}""").Visuals);
        Assert.HasCount(0, ObservePayload("""{"visuals":[{"document_id":"manual","page":71,"asset_type":"full","tile":null,"name":"full.jpg","asset_key":"manual/full.jpg"}]}""").Visuals);
    }

    [TestMethod]
    public void Visuals_NonPositivePageIsIgnored()
    {
        Assert.HasCount(0, ObservePayload("""{"visuals":[{"document_id":"manual","page":0,"asset_type":"full","tile":null,"name":"full.png","asset_key":"manual/page-00000/full.png"}]}""").Visuals);
        Assert.HasCount(0, ObservePayload("""{"visuals":[{"document_id":"manual","page":-1,"asset_type":"full","tile":null,"name":"full.png","asset_key":"manual/page-00001/full.png"}]}""").Visuals);
    }

    [TestMethod]
    public void Visuals_ParentTraversalAssetKeyIsIgnored() =>
        Assert.HasCount(0, ObservePayload("""{"visuals":[{"document_id":"manual","page":71,"asset_type":"full","tile":null,"name":"full.png","asset_key":"manual/../full.png"}]}""").Visuals);

    [TestMethod]
    public void Visuals_AbsoluteAssetKeyIsIgnored()
    {
        Assert.HasCount(0, ObservePayload("""{"visuals":[{"document_id":"manual","page":71,"asset_type":"full","tile":null,"name":"full.png","asset_key":"/manual/full.png"}]}""").Visuals);
        Assert.HasCount(0, ObservePayload("""{"visuals":[{"document_id":"manual","page":71,"asset_type":"full","tile":null,"name":"full.png","asset_key":"\\manual\\full.png"}]}""").Visuals);
    }

    [TestMethod]
    public void Visuals_UrlAssetKeyIsIgnored() =>
        Assert.HasCount(0, ObservePayload("""{"visuals":[{"document_id":"manual","page":71,"asset_type":"full","tile":null,"name":"full.png","asset_key":"https://storage.test/manual/full.png"}]}""").Visuals);

    [TestMethod]
    public void Visuals_UrlsInOtherStringFieldsAreIgnored()
    {
        Assert.HasCount(0, ObservePayload("""{"visuals":[{"document_id":"https://storage.test/manual","page":71,"asset_type":"full","tile":null,"name":"full.png","asset_key":"manual/full.png"}]}""").Visuals);
        Assert.HasCount(0, ObservePayload("""{"visuals":[{"document_id":"manual","page":71,"asset_type":"full","tile":null,"name":"https://storage.test/full.png","asset_key":"manual/full.png"}]}""").Visuals);
    }

    [TestMethod]
    public void Visuals_PreserveExistingUsageAndTechnicalMetadataExtraction()
    {
        var probe = ObservePayload($$"""
            {"visuals":[{{FullVisual}}],"usage":{"input_tokens":4,"output_tokens":2},
             "model":"vision","provider":"azure","deployment":"vision-prod"}
            """);
        var usage = probe.Measurements.Single().Usage;
        Assert.AreEqual(4, usage.InputTokens);
        Assert.AreEqual(2, usage.OutputTokens);
        Assert.AreEqual(6, usage.TotalTokens);
        Assert.AreEqual("vision", usage.Model);
        Assert.AreEqual("azure", usage.Provider);
        Assert.AreEqual("vision-prod", usage.Deployment);
        Assert.HasCount(1, probe.Visuals);
    }

    [TestMethod]
    public void Visuals_ExtractThroughSupportedNestedJsonEnvelopes()
    {
        var payload = $$"""{"visuals":[{{TileVisual}}]}""";
        var encoded = System.Text.Json.JsonSerializer.Serialize(payload);
        var wrapped = System.Text.Json.JsonSerializer.Serialize(new { response = encoded });
        Assert.AreEqual("r02-c01", ObservePayload(wrapped).Visuals.Single().Tile);
    }

    [TestMethod]
    public void Visuals_AreNeverWrittenToLogs()
    {
        var logger = new Capture();
        ObservePayload("""
            {"visuals":[{"document_id":"PRIVATE_DOCUMENT_ID","page":71,"asset_type":"tile","tile":"r02-c01",
             "name":"PRIVATE_NAME.png","asset_key":"PRIVATE/KEY/PRIVATE_NAME.png"}]}
            """, logger);
        var logs = string.Join("\n", logger.Lines);
        foreach (var sensitive in new[] { "PRIVATE_DOCUMENT_ID", "PRIVATE_NAME", "PRIVATE/KEY" })
            Assert.IsFalse(logs.Contains(sensitive));
    }

    [TestMethod]
    public void Visuals_DeltaContainsOnlyNewAssetKeysAndIsDrainedAfterRead()
    {
        var probe = new VisionToolDiagnostics(new Capture());
        probe.Observe(Vision("first", true, $$"""{"visuals":[{{FullVisual}}]}"""), "done", "parent");
        Assert.AreEqual(
            "manual/page-00071/manual_page-00071-full.png",
            probe.TakeNewVisuals().Single().AssetKey);
        Assert.HasCount(0, probe.TakeNewVisuals());

        probe.Observe(Vision("second", true, $$"""{"visuals":[{{FullVisual}},{{TileVisual}}]}"""), "done", "parent");
        var delta = probe.TakeNewVisuals();
        Assert.HasCount(1, delta);
        Assert.AreEqual("manual/page-00071/manual_page-00071-tile-r02-c01.png", delta[0].AssetKey);
    }

    [TestMethod]
    public void ZeroOneAndThreeCallsAreDistinctAndRepeatedEventsAreDeduplicated()
    {
        var probe = new VisionToolDiagnostics(new Capture());
        probe.Observe(Vision("search", false, name: "myfiles_browser.msearch"), "done", "parent");
        Assert.AreEqual(0, probe.Measurements.Count);
        for (var i = 0; i < 3; i++)
        {
            Assert.IsNull(probe.Observe(Vision("call_" + i, false), "added", "parent"));
            // A call-item done is not the final tool output.
            Assert.IsNull(probe.Observe(Vision("call_" + i, false), "done", "parent"));
            var done = Vision("call_" + i, true, """{"usage":{"input_tokens":12,"output_tokens":3}}""", name: null);
            var final = probe.Observe(done, "done", "parent")!;
            Assert.IsTrue(final.Usage.Completed && final.Usage.Available);
            Assert.AreEqual(15, final.Usage.TotalTokens);
            Assert.IsNull(final.Usage.Model);
            Assert.IsNull(final.Usage.ResponseId);
            Assert.AreEqual("parent", final.Usage.ParentResponseId);
            Assert.AreEqual("call_" + i, final.Usage.CallId);
            Assert.AreEqual(final, probe.Observe(done, "done", "parent"));
            Assert.IsNull(probe.Observe(Vision("call_" + i, false), "added", "parent"));
            Assert.AreEqual(i + 1, probe.Measurements.Count);
        }
        Assert.AreEqual(3, probe.Measurements.Select(m => m.EventId).Distinct().Count());
        probe.Observe(Vision("call_0", false), "added", "another_parent");
        Assert.AreEqual(3, probe.Measurements.Count);
    }

    [TestMethod]
    public void MissingInvalidOverflowAndInconsistentUsageRemainUnknown()
    {
        foreach (var payload in new[] { "{}", "invalid", "", 
            """{"usage":{"input_tokens":1}}""",
            """{"usage":{"input_tokens":-1,"output_tokens":2}}""",
            """{"usage":{"input_tokens":2147483647,"output_tokens":1}}""",
            """{"usage":{"input_tokens":1,"output_tokens":2,"total_tokens":4}}""",
            """{"usage":{"input_tokens":1,"output_tokens":2,"total_tokens":null}}""" })
        {
            var result = new VisionToolDiagnostics(new Capture()).Observe(Vision("call", true, payload), "done", "parent")!;
            Assert.IsTrue(result.Usage.Completed);
            Assert.IsFalse(result.Usage.Available);
            Assert.IsNull(result.Usage.InputTokens);
            Assert.IsNull(result.Usage.OutputTokens);
            Assert.IsNull(result.Usage.TotalTokens);
        }
    }

    [TestMethod]
    public void TechnicalMetadataComesOnlyFromVisionPayload()
    {
        var result = new VisionToolDiagnostics(new Capture()).Observe(Vision("call", true,
            """{"usage":{"input_tokens":0,"output_tokens":0},"model":"vision","provider":"azure","deployment":"vision-prod","version":"v2","response_id":"vision_response"}"""), "done", "parent")!;
        Assert.IsTrue(result.Usage.Available);
        Assert.AreEqual("vision", result.Usage.Model);
        Assert.AreEqual("azure", result.Usage.Provider);
        Assert.AreEqual("vision-prod", result.Usage.Deployment);
        Assert.AreEqual("v2", result.Usage.AgentVersion);
        Assert.AreEqual("vision_response", result.Usage.ResponseId);
    }

    [TestMethod]
    public void UnknownFinalIsEnrichedWithoutChangingIdentityOrOtherCalls()
    {
        var probe = new VisionToolDiagnostics(new Capture());
        var unknown = probe.Observe(Vision("A", true, "{}"), "done", "parent")!;
        Assert.IsTrue(unknown.Usage.Completed);
        Assert.IsFalse(unknown.Usage.Available);
        var other = probe.Observe(Vision("B", true, """{"usage":{"input_tokens":4,"output_tokens":2}}"""), "done", "parent")!;
        Assert.IsNull(probe.Observe(Vision("A", false), "added", "parent"));
        Assert.AreEqual(unknown, probe.Observe(Vision("A", true, "invalid"), "done", "parent"));
        var enriched = probe.Observe(Vision("A", true, """{"usage":{"input_tokens":9734,"output_tokens":787}}"""), "done", "parent")!;
        Assert.AreEqual(unknown.EventId, enriched.EventId);
        Assert.IsTrue(enriched.Usage.Completed && enriched.Usage.Available);
        Assert.AreEqual(9734, enriched.Usage.InputTokens);
        Assert.AreEqual(787, enriched.Usage.OutputTokens);
        Assert.AreEqual(10521, enriched.Usage.TotalTokens);
        Assert.AreEqual("A", enriched.Usage.CallId);
        Assert.AreEqual("parent", enriched.Usage.ParentResponseId);
        Assert.AreEqual(2, probe.Measurements.Count);
        Assert.AreEqual(other, probe.Measurements.Single(m => m.Usage.CallId == "B"));
        Assert.IsNull(probe.Observe(Vision("A", false), "added", "parent"));
        Assert.AreEqual(enriched, probe.Observe(Vision("A", true, "{}"), "done", "parent"));
    }

    [TestMethod]
    public void DefinitiveCaptureIsLoggedBeforeReturnWithoutLeakingPayload()
    {
        var logger = new Capture();
        var probe = new VisionToolDiagnostics(logger);
        var known = probe.Observe(Vision("A", true, """{"usage":{"input_tokens":1,"output_tokens":2}}"""), "done", "parent")!;
        logger.Lines.Clear();
        var incoming = Vision("A", true, """{"usage":{"input_tokens":99,"output_tokens":22},"document":"PRIVATE_DOCUMENT","url":"https://example.test/?sig=SECRET"}""");
        Assert.AreEqual(known, probe.Observe(incoming, "done", "parent"));
        var log = logger.Lines.Single();
        foreach (var field in new[] { "VisionProbe Event", "Phase=done", "RuntimeType=", "WireType=openapi_call_output",
            "ParentResponseId=parent", "CallId=A", "Status=completed", "HasOutput=True", "OutputKind=String",
            "HasNestedResponse=True", "HasUsage=True", "PreviousAvailable=True", "PreviousCompleted=True" })
            StringAssert.Contains(log, field);
        foreach (var secret in new[] { "PRIVATE_DOCUMENT", "https://", "SECRET", "input_tokens", "99" })
            Assert.IsFalse(log.Contains(secret));
        logger.Lines.Clear();
        Assert.IsNull(probe.Observe(Vision("A", false), "added", "parent"));
        StringAssert.Contains(logger.Lines.Single(), "HasOutput=False");
        logger.Lines.Clear();
        Assert.AreEqual(known, probe.Observe(Vision("A", true, "PRIVATE_INVALID"), "done", "parent"));
        Assert.IsFalse(string.Join("", logger.Lines).Contains("PRIVATE_INVALID"));
    }

    private sealed class Capture : ILogger
    {
        public List<string> Lines { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? error, Func<TState, Exception?, string> format)
            => Lines.Add(format(state, error));
    }
    [TestMethod]
    public void WireOutputIsInspectedWithoutLoggingDocumentOrArguments()
    {
        var logger = new Capture();
        var probe = new VisionToolDiagnostics(logger);
        var item = ModelReaderWriter.Read<ResponseItem>(BinaryData.FromString("""
            {"type":"openapi_call_output","id":"item_1","call_id":"call_1",
             "name":"blob_page_images_analyze_page","status":"completed",
             "arguments":"SECRET_ARGUMENT","output":{"text":"PRIVATE_DOCUMENT",
             "usage":{"input_tokens":9715,"output_tokens":568},"model":"vision-test"}}
            """))!;
        probe.Observe(item, "done", "resp_1");
        Assert.IsTrue(probe.Observed);
        var logs = string.Join("\n", logger.Lines);
        StringAssert.Contains(logs, "9715");
        StringAssert.Contains(logs, "568");
        StringAssert.Contains(logs, "call_1");
        Assert.IsFalse(logs.Contains("PRIVATE_DOCUMENT"));
        Assert.IsFalse(logs.Contains("SECRET_ARGUMENT"));
    }
    [TestMethod]
    public void OtherToolsAreSilentAndInvalidUsageDoesNotBreakStream()
    {
        var logger = new Capture();
        var probe = new VisionToolDiagnostics(logger);
        var other = ModelReaderWriter.Read<ResponseItem>(BinaryData.FromString("""
            {"type":"function_call","id":"item_2","call_id":"call_2","name":"other","arguments":"{}"}
            """))!;
        probe.Observe(other, "done", "resp_1");
        Assert.AreEqual(0, logger.Lines.Count);
        var invalid = ModelReaderWriter.Read<ResponseItem>(BinaryData.FromString("""
            {"type":"openapi_call_output","id":"item_3","call_id":"call_3",
             "name":"blob_page_images_analyze_page","output":{"usage":{"input_tokens":"invalid"}}}
            """))!;
        probe.Observe(invalid, "done", "resp_1");
        Assert.IsTrue(probe.Observed);
        Assert.IsTrue(logger.Lines.Any(l => l.Contains("HasUsage=True")));
    }
}
