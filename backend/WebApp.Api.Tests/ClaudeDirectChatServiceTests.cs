using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
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
    private const string PauseTurnContentJson =
        """[{"type":"text","text":"Continuation partielle."},{"type":"text","text":"Etat opaque a conserver."}]""";
    private static readonly string[] ValidTileIds =
    [
        "r01-c01", "r01-c02", "r01-c03",
        "r02-c01", "r02-c02", "r02-c03",
        "r03-c01", "r03-c02", "r03-c03"
    ];

    [TestMethod]
    public async Task CompleteAsync_ReturnsSimpleClaudeText()
    {
        var fixture = Fixture.Create(DirectResponse("bonjour", 3, 4));

        var result = await fixture.Service.CompleteAsync(Request());

        Assert.AreEqual("bonjour", result.FinalText);
        Assert.AreEqual("claude-sonnet-5", result.Model);
        Assert.AreEqual("end_turn", result.StopReason);
        Assert.HasCount(0, result.Errors);
        Assert.HasCount(0, result.WebCitations);
    }

    [TestMethod]
    public async Task StreamAsync_ForwardsProviderDeltasAndExtractsSplitSuggestions()
    {
        var fixture = Fixture.Create(EventStreamResponse(
            """{"type":"message_start","message":{"id":"msg-stream","type":"message","role":"assistant","model":"claude-sonnet-5","content":[],"stop_reason":null,"usage":{"input_tokens":3,"output_tokens":1}}}""",
            """{"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}""",
            """{"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"Avant\n<diag"}}""",
            """{"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"link_suggestion>Où est le relais"}}""",
            """{"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":" ?</diaglink_suggestion>Après"}}""",
            """{"type":"content_block_stop","index":0}""",
            """{"type":"message_delta","delta":{"stop_reason":"end_turn","stop_sequence":null},"usage":{"output_tokens":7}}""",
            """{"type":"message_stop"}"""));

        var updates = new List<ClaudeDirectChatStreamUpdate>();
        await foreach (var update in fixture.Service.StreamAsync(Request()))
        {
            updates.Add(update);
        }

        var visible = string.Concat(updates.Where(update => update.TextDelta is not null).Select(update => update.TextDelta));
        var result = updates.Single(update => update.CompletedResult is not null).CompletedResult!;
        Assert.AreEqual("Avant\nAprès", visible);
        Assert.AreEqual(visible, result.FinalText);
        CollectionAssert.AreEqual(new[] { "Où est le relais ?" }, result.Suggestions.ToArray());
        Assert.AreEqual(3, result.Usage.InputTokens);
        Assert.AreEqual(7, result.Usage.OutputTokens);
        using var request = JsonDocument.Parse(fixture.Handler.Requests.Single().Body);
        Assert.IsTrue(request.RootElement.GetProperty("stream").GetBoolean());
    }

    [TestMethod]
    public async Task StreamAsync_RebuildsSplitToolInputAndThinkingForNextMessagesCall()
    {
        var fixture = Fixture.Create(
            EventStreamResponse(
                """{"type":"message_start","message":{"id":"msg-tool","type":"message","role":"assistant","model":"claude-sonnet-5","content":[],"stop_reason":null,"usage":{"input_tokens":4,"output_tokens":1}}}""",
                """{"type":"content_block_start","index":0,"content_block":{"type":"thinking","thinking":"","signature":""}}""",
                """{"type":"content_block_delta","index":0,"delta":{"type":"thinking_delta","thinking":"private reasoning"}}""",
                """{"type":"content_block_delta","index":0,"delta":{"type":"signature_delta","signature":"signed"}}""",
                """{"type":"content_block_stop","index":0}""",
                """{"type":"content_block_start","index":1,"content_block":{"type":"tool_use","id":"tool-1","name":"get_page_image","input":{}}}""",
                """{"type":"content_block_delta","index":1,"delta":{"type":"input_json_delta","partial_json":"{\"page\":75,"}}""",
                """{"type":"content_block_delta","index":1,"delta":{"type":"input_json_delta","partial_json":"\"asset_type\":\"full\"}"}}""",
                """{"type":"content_block_stop","index":1}""",
                """{"type":"message_delta","delta":{"stop_reason":"tool_use","stop_sequence":null},"usage":{"output_tokens":8}}""",
                """{"type":"message_stop"}"""),
            EventStreamResponse(
                """{"type":"message_start","message":{"id":"msg-final","type":"message","role":"assistant","model":"claude-sonnet-5","content":[],"stop_reason":null,"usage":{"input_tokens":10,"output_tokens":1}}}""",
                """{"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}""",
                """{"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"Réponse finale"}}""",
                """{"type":"content_block_stop","index":0}""",
                """{"type":"message_delta","delta":{"stop_reason":"end_turn","stop_sequence":null},"usage":{"output_tokens":3}}""",
                """{"type":"message_stop"}"""));

        var updates = new List<ClaudeDirectChatStreamUpdate>();
        await foreach (var update in fixture.Service.StreamAsync(Request()))
        {
            updates.Add(update);
        }

        var result = updates.Single(update => update.CompletedResult is not null).CompletedResult!;
        Assert.AreEqual("Réponse finale", result.FinalText);
        Assert.IsFalse(string.Concat(updates.Select(update => update.TextDelta)).Contains("private reasoning", StringComparison.Ordinal));
        Assert.HasCount(2, fixture.Handler.Requests);
        using var secondRequest = JsonDocument.Parse(fixture.Handler.Requests[1].Body);
        var assistantBlocks = secondRequest.RootElement.GetProperty("messages")[1].GetProperty("content");
        Assert.AreEqual("private reasoning", assistantBlocks[0].GetProperty("thinking").GetString());
        Assert.AreEqual("signed", assistantBlocks[0].GetProperty("signature").GetString());
        Assert.AreEqual(75, assistantBlocks[1].GetProperty("input").GetProperty("page").GetInt32());
    }

    [TestMethod]
    public async Task StreamAsync_PreservesWebFileSearchAndImageLoopThenStreamsCleanFinalText()
    {
        const string documentId = "develon-dx10z-manuel-utilisation-maintenance-en";
        var fixture = Fixture.Create(
            EventStreamResponse(
                """{"type":"message_start","message":{"id":"msg-tools","type":"message","role":"assistant","model":"claude-sonnet-5","content":[],"stop_reason":null,"usage":{"input_tokens":10,"output_tokens":1}}}""",
                """{"type":"content_block_start","index":0,"content_block":{"type":"server_tool_use","id":"web-1","name":"web_search","input":{"query":"hydraulic filter"}}}""",
                """{"type":"content_block_stop","index":0}""",
                """{"type":"content_block_start","index":1,"content_block":{"type":"web_search_tool_result","tool_use_id":"web-1","content":[]}}""",
                """{"type":"content_block_stop","index":1}""",
                """{"type":"content_block_start","index":2,"content_block":{"type":"mcp_tool_use","id":"mcp-1","name":"file_search","input":{}}}""",
                """{"type":"content_block_stop","index":2}""",
                """{"type":"content_block_start","index":3,"content_block":{"type":"mcp_tool_result","tool_use_id":"mcp-1","is_error":false,"content":[{"type":"text","text":"Document ID: __DOCUMENT_ID__"}]}}"""
                    .Replace("__DOCUMENT_ID__", documentId, StringComparison.Ordinal),
                """{"type":"content_block_stop","index":3}""",
                """{"type":"content_block_start","index":4,"content_block":{"type":"tool_use","id":"tool-1","name":"get_page_image","input":{"document_id":"__DOCUMENT_ID__","page":75,"asset_type":"full"}}}"""
                    .Replace("__DOCUMENT_ID__", documentId, StringComparison.Ordinal),
                """{"type":"content_block_stop","index":4}""",
                """{"type":"message_delta","delta":{"stop_reason":"tool_use","stop_sequence":null},"usage":{"output_tokens":8,"server_tool_use":{"web_search_requests":1}}}""",
                """{"type":"message_stop"}"""),
            EventStreamResponse(
                """{"type":"message_start","message":{"id":"msg-final","type":"message","role":"assistant","model":"claude-sonnet-5","content":[],"stop_reason":null,"usage":{"input_tokens":12,"output_tokens":1}}}""",
                """{"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}""",
                """{"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"Source : "}}""",
                """{"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"p. 70.\n<diaglink_suggestion>Où est le relais ?</diaglink_suggestion>"}}""",
                """{"type":"content_block_stop","index":0}""",
                """{"type":"message_delta","delta":{"stop_reason":"end_turn","stop_sequence":null},"usage":{"output_tokens":4}}""",
                """{"type":"message_stop"}"""));
        fixture.BlobReader.PageMapFactory = _ => PageMap(documentId);

        var updates = new List<ClaudeDirectChatStreamUpdate>();
        await foreach (var update in fixture.Service.StreamAsync(Request()))
        {
            updates.Add(update);
        }

        var textDeltas = updates.Where(update => update.TextDelta is not null)
            .Select(update => update.TextDelta!)
            .ToArray();
        var result = updates.Single(update => update.CompletedResult is not null).CompletedResult!;
        CollectionAssert.AreEqual(new[] { "Source : ", "p. 70.\n" }, textDeltas);
        Assert.AreEqual("Source : p. 70.\n", result.FinalText);
        Assert.IsFalse(result.FinalText!.Contains("tool_use", StringComparison.Ordinal));
        CollectionAssert.AreEqual(new[] { "Où est le relais ?" }, result.Suggestions.ToArray());
        CollectionAssert.AreEqual(new[] { "web_search", "file_search", "get_page_image" }, result.Calls[0].Tools.ToArray());
        Assert.AreEqual(1, result.Usage.WebSearchRequests);
        Assert.AreEqual(22, result.Usage.InputTokens);
        Assert.AreEqual(12, result.Usage.OutputTokens);
        Assert.AreEqual("end_turn", result.StopReason);
        Assert.HasCount(2, fixture.Handler.Requests);
        var source = result.Sources.Single();
        Assert.AreEqual(result.FinalText.IndexOf(source.Label, StringComparison.Ordinal), source.StartIndex);
        Assert.AreEqual(source.StartIndex + source.Label.Length, source.EndIndex);
    }

    [TestMethod]
    public async Task StreamAsync_IncompleteProviderStreamKeepsPartialTextAndReturnsFailure()
    {
        var fixture = Fixture.Create(EventStreamResponse(
            """{"type":"message_start","message":{"id":"msg-partial","type":"message","role":"assistant","model":"claude-sonnet-5","content":[],"stop_reason":null,"usage":{"input_tokens":3,"output_tokens":1}}}""",
            """{"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}""",
            """{"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"Réponse interrompue"}}"""));

        var updates = new List<ClaudeDirectChatStreamUpdate>();
        await foreach (var update in fixture.Service.StreamAsync(Request()))
        {
            updates.Add(update);
        }

        Assert.AreEqual("Réponse interrompue", string.Concat(updates.Select(update => update.TextDelta)));
        var result = updates.Single(update => update.CompletedResult is not null).CompletedResult!;
        Assert.IsNull(result.FinalText);
        Assert.IsTrue(result.Errors.Any(error => error.Code == "invalid_messages_response"));
    }

    [TestMethod]
    public async Task StreamAsync_CancellationTokenInterruptsProviderRequest()
    {
        var fixture = Fixture.Create(EventStreamResponse());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        try
        {
            await foreach (var _ in fixture.Service.StreamAsync(Request(), cancellation.Token))
            {
            }

            Assert.Fail("The provider request should have been cancelled.");
        }
        catch (Exception exception)
        {
            Assert.IsInstanceOfType<OperationCanceledException>(exception);
        }
    }

    [TestMethod]
    public async Task CompleteAsync_EndTurnRemainsSingleMessagesCall()
    {
        var fixture = Fixture.Create(DirectResponse("final"));

        var result = await fixture.Service.CompleteAsync(Request());

        Assert.AreEqual("final", result.FinalText);
        Assert.AreEqual("end_turn", result.StopReason);
        Assert.HasCount(1, fixture.Handler.Requests);
    }

    [TestMethod]
    public async Task CompleteAsync_PauseTurnContinuesWithExactAssistantContent()
    {
        var fixture = Fixture.Create(PauseTurnResponse(), DirectResponse("continued"));

        var result = await fixture.Service.CompleteAsync(Request());

        Assert.AreEqual("continued", result.FinalText);
        Assert.AreEqual("end_turn", result.StopReason);
        Assert.HasCount(2, result.Calls);
        Assert.HasCount(2, fixture.Handler.Requests);

        using var expectedContent = JsonDocument.Parse(PauseTurnContentJson);
        using var secondBody = JsonDocument.Parse(fixture.Handler.Requests[1].Body);
        var messages = secondBody.RootElement.GetProperty("messages");
        Assert.AreEqual(2, messages.GetArrayLength());
        Assert.AreEqual("assistant", messages[1].GetProperty("role").GetString());
        Assert.IsTrue(JsonElement.DeepEquals(
            expectedContent.RootElement,
            messages[1].GetProperty("content")));
    }

    [TestMethod]
    public async Task CompleteAsync_PauseTurnHonorsMaxMessageCalls()
    {
        var fixture = Fixture.CreateWithOptions(
            new ClaudeDirectChatOptions
            {
                FoundryAnthropicEndpoint = "https://resource.test/anthropic",
                Deployment = "claude-sonnet-5",
                MaxMessageCalls = 2,
                MaxImageBytes = 1024
            },
            PauseTurnResponse(),
            PauseTurnResponse());

        var result = await fixture.Service.CompleteAsync(Request());

        Assert.HasCount(2, fixture.Handler.Requests);
        Assert.HasCount(2, result.Calls);
        Assert.IsNull(result.FinalText);
        Assert.AreEqual("pause_turn", result.StopReason);
        Assert.IsTrue(result.Errors.Any(error =>
            error.Code == "tool_loop_limit" && error.CallNumber == 2));
    }

    [TestMethod]
    public void ObserveWebSearchBlocks_RecognizesServerUseAndResult()
    {
        using var document = JsonDocument.Parse("""
        [
          {"type":"server_tool_use","id":"web-1","name":"web_search","input":{"query":"hydraulic filter"}},
          {"type":"web_search_tool_result","tool_use_id":"web-1","content":[]}
        ]
        """);

        var observations = ClaudeDirectChatService.ObserveWebSearchBlocks(document.RootElement);

        Assert.HasCount(2, observations);
        Assert.AreEqual("server_tool_use", observations[0].Type);
        Assert.AreEqual("web-1", observations[0].ToolUseId);
        Assert.AreEqual("web_search_tool_result", observations[1].Type);
        Assert.AreEqual("web-1", observations[1].ToolUseId);
    }

    [TestMethod]
    public async Task CompleteAsync_WebSearchBlocksAreNotExecutedAsLocalTools()
    {
        var fixture = Fixture.Create(WebSearchAndExistingToolsResponse(), DirectResponse("done"));

        var result = await fixture.Service.CompleteAsync(Request());

        Assert.AreEqual("done", result.FinalText);
        Assert.HasCount(2, fixture.Handler.Requests);
        Assert.HasCount(0, result.Errors);
        Assert.AreEqual("get_page_image", result.ToolUses.Single().Name);
        Assert.IsFalse(result.ToolUses.Any(tool => tool.Name == "web_search"));
        CollectionAssert.AreEqual(
            new[] { "web_search", "file_search", "get_page_image" },
            result.Calls[0].Tools.ToArray());
        Assert.IsTrue(result.McpCalls.Any(call => call.Name == "file_search"));
        Assert.IsTrue(result.McpCalls.Any(call => call.Name == "mcp_tool_result"));
        Assert.HasCount(1, result.Visuals);
    }

    [TestMethod]
    public void ExtractWebCitations_ReadsValidHttpAndHttpsCitationFields()
    {
        using var document = JsonDocument.Parse("""
        [
          {
            "type":"text",
            "text":"Answer with citations.",
            "citations":[
              {
                "type":"web_search_result_location",
                "url":"https://docs.example.test/article",
                "title":"Technical article",
                "cited_text":"Quoted technical passage"
              },
              {
                "type":"web_search_result_location",
                "url":"http://legacy.example.test/page",
                "text":"Fallback passage"
              }
            ]
          }
        ]
        """);

        var citations = ClaudeDirectChatService.ExtractWebCitations(document.RootElement);

        Assert.HasCount(2, citations);
        Assert.AreEqual("https://docs.example.test/article", citations[0].Url);
        Assert.AreEqual("Technical article", citations[0].Title);
        Assert.AreEqual("Quoted technical passage", citations[0].CitedText);
        Assert.AreEqual("http://legacy.example.test/page", citations[1].Url);
        Assert.AreEqual("Fallback passage", citations[1].CitedText);
    }

    [TestMethod]
    public void ExtractWebCitations_IgnoresInvalidSchemesAndRemovesDuplicateUrls()
    {
        using var document = JsonDocument.Parse("""
        [
          {
            "type":"text",
            "text":"Answer",
            "citations":[
              {"type":"web_search_result_location","url":"not-an-absolute-url","title":"Invalid"},
              {"type":"web_search_result_location","url":"ftp://example.test/file","title":"Forbidden"},
              {"type":"web_search_result_location","url":"https://example.test/source","title":"First"},
              {"type":"web_search_result_location","url":"https://example.test/source","title":"Duplicate"}
            ]
          }
        ]
        """);

        var citations = ClaudeDirectChatService.ExtractWebCitations(document.RootElement);

        Assert.HasCount(1, citations);
        Assert.AreEqual("https://example.test/source", citations[0].Url);
        Assert.AreEqual("First", citations[0].Title);
    }

    [TestMethod]
    public async Task CompleteAsync_WebCitationsRemainSeparateFromPdfSourcesAndFinalText()
    {
        const string documentId = "develon-dx10z-manuel-utilisation-maintenance-en";
        const string finalText = "Source : p. 70.";
        var fixture = Fixture.Create(WebCitationAndPdfSourceResponse(documentId, finalText));
        fixture.BlobReader.PageMapFactory = _ => PageMap(documentId);

        var result = await fixture.Service.CompleteAsync(Request());

        Assert.AreEqual(finalText, result.FinalText);
        var source = result.Sources.Single();
        Assert.AreEqual(documentId, source.DocumentId);
        Assert.AreEqual("70", source.DisplayPage);
        var citation = result.WebCitations.Single();
        Assert.AreEqual("https://example.test/web-source", citation.Url);
        Assert.AreEqual("Web source", citation.Title);
        Assert.AreEqual("Web passage", citation.CitedText);
    }

    [TestMethod]
    public async Task CompleteAsync_ReturnsMultipleDeduplicatedWebCitations()
    {
        var fixture = Fixture.Create(MultipleWebCitationsResponse());

        var result = await fixture.Service.CompleteAsync(Request());

        Assert.HasCount(2, result.WebCitations);
        CollectionAssert.AreEqual(
            new[] { "https://example.test/first", "https://example.test/second" },
            result.WebCitations.Select(citation => citation.Url).ToArray());
        Assert.AreEqual("First", result.WebCitations[0].Title);
        Assert.AreEqual("Second", result.WebCitations[1].Title);
        Assert.HasCount(0, result.Sources);
    }

    [TestMethod]
    public async Task CompleteAsync_SendsUserPngAsMultimodalContent()
    {
        var fixture = Fixture.Create(DirectResponse("ok"));
        var request = Request() with
        {
            Messages = [new("user", "Inspecte cette image", [new("image/png", "iVBORw0KGgo=")])]
        };

        await fixture.Service.CompleteAsync(request);

        using var body = JsonDocument.Parse(fixture.Handler.Requests.Single().Body);
        var content = body.RootElement.GetProperty("messages")[0].GetProperty("content");
        Assert.AreEqual(JsonValueKind.Array, content.ValueKind);
        Assert.AreEqual("text", content[0].GetProperty("type").GetString());
        Assert.AreEqual("Inspecte cette image", content[0].GetProperty("text").GetString());
        Assert.AreEqual("image", content[1].GetProperty("type").GetString());
        Assert.AreEqual("base64", content[1].GetProperty("source").GetProperty("type").GetString());
        Assert.AreEqual("image/png", content[1].GetProperty("source").GetProperty("media_type").GetString());
        Assert.AreEqual("iVBORw0KGgo=", content[1].GetProperty("source").GetProperty("data").GetString());
        Assert.HasCount(0, fixture.BlobReader.RequestedNames);
    }

    [TestMethod]
    public async Task CompleteAsync_SendsUserJpegWithCorrectMediaType()
    {
        var fixture = Fixture.Create(DirectResponse("ok"));
        var request = Request() with
        {
            Messages = [new("user", "Inspecte cette photo", [new("image/jpeg", "/9j/2Q==")])]
        };

        await fixture.Service.CompleteAsync(request);

        using var body = JsonDocument.Parse(fixture.Handler.Requests.Single().Body);
        var source = body.RootElement.GetProperty("messages")[0].GetProperty("content")[1].GetProperty("source");
        Assert.AreEqual("image/jpeg", source.GetProperty("media_type").GetString());
        Assert.AreEqual("/9j/2Q==", source.GetProperty("data").GetString());
    }

    [TestMethod]
    public async Task CompleteAsync_TextOnlyMessageKeepsStringContentContract()
    {
        var fixture = Fixture.Create(DirectResponse("ok"));

        await fixture.Service.CompleteAsync(Request());

        using var body = JsonDocument.Parse(fixture.Handler.Requests.Single().Body);
        var content = body.RootElement.GetProperty("messages")[0].GetProperty("content");
        Assert.AreEqual(JsonValueKind.String, content.ValueKind);
        Assert.AreEqual("How do I replace the hydraulic filter?", content.GetString());
    }

    [TestMethod]
    public async Task CompleteAsync_PlacesSingleFiveMinuteBreakpointAfterStableSystemPrefix()
    {
        var fixture = Fixture.Create(DirectResponse("ok"));

        await fixture.Service.CompleteAsync(Request());

        using var body = JsonDocument.Parse(fixture.Handler.Requests.Single().Body);
        var root = body.RootElement;
        var system = root.GetProperty("system");
        Assert.AreEqual(JsonValueKind.Array, system.ValueKind);
        Assert.AreEqual(1, system.GetArrayLength());
        var block = system[0];
        Assert.AreEqual("text", block.GetProperty("type").GetString());
        Assert.AreEqual(
            "Use File Search before inspecting technical images.\n\n" +
            "Contexte machine fourni par le serveur :\n" +
            "Machine DX10z; use the connected technical manual.",
            block.GetProperty("text").GetString());
        var cacheControl = block.GetProperty("cache_control");
        Assert.AreEqual("ephemeral", cacheControl.GetProperty("type").GetString());
        Assert.AreEqual("5m", cacheControl.GetProperty("ttl").GetString());
        Assert.IsFalse(root.TryGetProperty("cache_control", out _));
        Assert.AreEqual(1, Regex.Matches(fixture.Handler.Requests.Single().Body, "\\\"cache_control\\\"").Count);
        Assert.IsFalse(fixture.Handler.Requests.Single().Body.Contains("1h", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task CompleteAsync_AppendsActiveCommercialPolicyOnceAfterMachineContext()
    {
        const string instructions = "Instruction commerciale unique.";
        var fixture = Fixture.Create(DirectResponse("ok"));

        await fixture.Service.CompleteAsync(Request(new(true, instructions)));

        using var body = JsonDocument.Parse(fixture.Handler.Requests.Single().Body);
        var systemPrompt = body.RootElement.GetProperty("system")[0].GetProperty("text").GetString()!;
        const string technicalPrompt = "Use File Search before inspecting technical images.";
        const string machineHeader = "Contexte machine fourni par le serveur :";
        const string policyHeader = "Politique commerciale active fournie par le serveur :";
        Assert.IsTrue(systemPrompt.StartsWith(technicalPrompt, StringComparison.Ordinal));
        Assert.IsTrue(systemPrompt.IndexOf(machineHeader, StringComparison.Ordinal) >
            systemPrompt.IndexOf(technicalPrompt, StringComparison.Ordinal));
        Assert.IsTrue(systemPrompt.IndexOf(policyHeader, StringComparison.Ordinal) >
            systemPrompt.IndexOf(machineHeader, StringComparison.Ordinal));
        Assert.AreEqual(1, Regex.Matches(systemPrompt, Regex.Escape(policyHeader)).Count);
        Assert.AreEqual(1, Regex.Matches(systemPrompt, Regex.Escape(instructions)).Count);
        Assert.AreEqual(
            technicalPrompt + "\n\n" +
            machineHeader + "\nMachine DX10z; use the connected technical manual.\n\n" +
            policyHeader + "\n" + instructions,
            systemPrompt);
    }

    [TestMethod]
    [DataRow(false, "Instruction configurée")]
    [DataRow(true, "")]
    [DataRow(true, "   ")]
    public async Task CompleteAsync_OmitsInactiveOrEmptyCommercialPolicy(
        bool enabled,
        string instructions)
    {
        var fixture = Fixture.Create(DirectResponse("ok"));

        await fixture.Service.CompleteAsync(Request(new(enabled, instructions)));

        var requestBody = fixture.Handler.Requests.Single().Body;
        Assert.IsFalse(requestBody.Contains(
            "Politique commerciale active fournie par le serveur :",
            StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task CompleteAsync_CachingDoesNotChangeMcpOrLocalToolDefinitions()
    {
        var fixture = Fixture.Create(DirectResponse("ok"));

        await fixture.Service.CompleteAsync(Request());

        using var body = JsonDocument.Parse(fixture.Handler.Requests.Single().Body);
        var tools = body.RootElement.GetProperty("tools");
        Assert.AreEqual(2, tools.GetArrayLength());
        CollectionAssert.AreEquivalent(
            new[] { "type", "mcp_server_name" },
            tools[0].EnumerateObject().Select(property => property.Name).ToArray());
        Assert.AreEqual("mcp_toolset", tools[0].GetProperty("type").GetString());
        Assert.AreEqual("machine-manual", tools[0].GetProperty("mcp_server_name").GetString());
        Assert.IsFalse(tools[0].TryGetProperty("cache_control", out _));
        Assert.AreEqual("get_page_image", tools[1].GetProperty("name").GetString());
        Assert.IsTrue(tools[1].GetProperty("input_schema").GetProperty("required")
            .EnumerateArray().Select(value => value.GetString()).SequenceEqual(new[] { "page", "asset_type" }));
        Assert.IsFalse(tools[1].TryGetProperty("cache_control", out _));
    }

    [TestMethod]
    public async Task CompleteAsync_ExposesSecuredGetPageImageContract()
    {
        var fixture = Fixture.Create(DirectResponse("ok"));

        await fixture.Service.CompleteAsync(Request());

        using var body = JsonDocument.Parse(fixture.Handler.Requests.Single().Body);
        var tool = body.RootElement.GetProperty("tools")[1];
        Assert.AreEqual("get_page_image", tool.GetProperty("name").GetString());

        var description = tool.GetProperty("description").GetString()!;
        StringAssert.Contains(description, "page complète");
        StringAssert.Contains(description, "grille 3 × 3");
        StringAssert.Contains(description, "se chevauchent de 25 %");
        StringAssert.Contains(description, "demande directement la tuile correspondante");
        StringAssert.Contains(description, "une seule tuile lorsqu’elle suffit");

        var schema = tool.GetProperty("input_schema");
        CollectionAssert.AreEqual(
            new[] { "page", "asset_type" },
            schema.GetProperty("required").EnumerateArray().Select(value => value.GetString()).ToArray());
        var properties = schema.GetProperty("properties");
        Assert.IsFalse(schema.GetProperty("additionalProperties").GetBoolean());

        var page = properties.GetProperty("page");
        Assert.AreEqual(1, page.GetProperty("minimum").GetInt32());
        Assert.AreEqual(99999, page.GetProperty("maximum").GetInt32());

        var documentId = properties.GetProperty("document_id");
        CollectionAssert.AreEqual(
            new[] { "string", "null" },
            documentId.GetProperty("type").EnumerateArray().Select(value => value.GetString()).ToArray());
        Assert.IsFalse(schema.GetProperty("required").EnumerateArray()
            .Any(value => value.GetString() == "document_id"));

        var assetType = properties.GetProperty("asset_type");
        CollectionAssert.AreEqual(
            new[] { "full", "tile" },
            assetType.GetProperty("enum").EnumerateArray().Select(value => value.GetString()).ToArray());

        var tile = properties.GetProperty("tile");
        CollectionAssert.AreEqual(
            new[] { "string", "null" },
            tile.GetProperty("type").EnumerateArray().Select(value => value.GetString()).ToArray());
        CollectionAssert.AreEqual(
            ValidTileIds,
            tile.GetProperty("enum").EnumerateArray()
                .Where(value => value.ValueKind == JsonValueKind.String)
                .Select(value => value.GetString())
                .ToArray());
        Assert.AreEqual(1, tile.GetProperty("enum").EnumerateArray()
            .Count(value => value.ValueKind == JsonValueKind.Null));
        Assert.IsFalse(tile.GetProperty("enum").EnumerateArray()
            .Where(value => value.ValueKind == JsonValueKind.String)
            .Select(value => value.GetString())
            .Any(value => value is "r00-c00" or "r04-c01" or "r01-c04" or "r99-c99" or "R01-C01"));
    }

    [TestMethod]
    public void WebSearchOptions_DefaultToDisabledServerConfiguration()
    {
        var options = new ClaudeDirectChatOptions();

        Assert.IsFalse(options.WebSearch.Enabled);
        Assert.AreEqual(2, options.WebSearch.DiagnosticMaxUses);
        Assert.AreEqual(3, options.WebSearch.PartsMaxUses);
    }

    [TestMethod]
    [DataRow(-1, 3)]
    [DataRow(2, -1)]
    public async Task CompleteAsync_RejectsNegativeWebSearchLimits(
        int diagnosticMaxUses,
        int partsMaxUses)
    {
        var fixture = Fixture.CreateWithOptions(new ClaudeDirectChatOptions
        {
            FoundryAnthropicEndpoint = "https://resource.test/anthropic",
            Deployment = "claude-sonnet-5",
            MaxMessageCalls = 4,
            MaxImageBytes = 1024,
            WebSearch = new ClaudeDirectWebSearchOptions
            {
                Enabled = false,
                DiagnosticMaxUses = diagnosticMaxUses,
                PartsMaxUses = partsMaxUses
            }
        }, DirectResponse("must not be used"));

        var result = await fixture.Service.CompleteAsync(Request());

        Assert.AreEqual("invalid_configuration", result.Errors.Single().Code);
        Assert.HasCount(0, fixture.Handler.Requests);
        Assert.AreEqual(0, fixture.Credential.CallCount);
    }

    [TestMethod]
    public async Task CompleteAsync_ZeroWebSearchLimitsRemainValidAndDoNotAddClaudeTool()
    {
        var fixture = Fixture.CreateWithOptions(new ClaudeDirectChatOptions
        {
            FoundryAnthropicEndpoint = "https://resource.test/anthropic",
            Deployment = "claude-sonnet-5",
            MaxMessageCalls = 4,
            MaxImageBytes = 1024,
            WebSearch = new ClaudeDirectWebSearchOptions
            {
                Enabled = false,
                DiagnosticMaxUses = 0,
                PartsMaxUses = 0
            }
        }, DirectResponse("ok"));

        var result = await fixture.Service.CompleteAsync(Request());

        Assert.HasCount(0, result.Errors);
        using var body = JsonDocument.Parse(fixture.Handler.Requests.Single().Body);
        var tools = body.RootElement.GetProperty("tools");
        Assert.AreEqual(2, tools.GetArrayLength());
        Assert.IsFalse(fixture.Handler.Requests.Single().Body.Contains("web_search", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow(2, 3, 3)]
    [DataRow(7, 3, 7)]
    [DataRow(1, 9, 9)]
    public async Task CompleteAsync_EnabledWebSearchUsesHighestConfiguredLimitWithoutChangingExistingTools(
        int diagnosticMaxUses,
        int partsMaxUses,
        int expectedMaxUses)
    {
        var disabledFixture = Fixture.Create(DirectResponse("disabled"));
        var enabledFixture = Fixture.CreateWithOptions(new ClaudeDirectChatOptions
        {
            FoundryAnthropicEndpoint = "https://resource.test/anthropic",
            Deployment = "claude-sonnet-5",
            MaxMessageCalls = 4,
            MaxImageBytes = 1024,
            WebSearch = new ClaudeDirectWebSearchOptions
            {
                Enabled = true,
                DiagnosticMaxUses = diagnosticMaxUses,
                PartsMaxUses = partsMaxUses
            }
        }, DirectResponse("enabled"));

        await disabledFixture.Service.CompleteAsync(Request());
        await enabledFixture.Service.CompleteAsync(Request());

        using var disabledBody = JsonDocument.Parse(disabledFixture.Handler.Requests.Single().Body);
        using var enabledBody = JsonDocument.Parse(enabledFixture.Handler.Requests.Single().Body);
        var disabledTools = disabledBody.RootElement.GetProperty("tools");
        var enabledTools = enabledBody.RootElement.GetProperty("tools");

        Assert.AreEqual(2, disabledTools.GetArrayLength());
        Assert.AreEqual(3, enabledTools.GetArrayLength());
        Assert.AreEqual(disabledTools[0].GetRawText(), enabledTools[0].GetRawText());
        Assert.AreEqual(disabledTools[1].GetRawText(), enabledTools[1].GetRawText());

        var webSearch = enabledTools[2];
        CollectionAssert.AreEquivalent(
            new[] { "type", "name", "max_uses" },
            webSearch.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.AreEqual("web_search_20250305", webSearch.GetProperty("type").GetString());
        Assert.AreEqual("web_search", webSearch.GetProperty("name").GetString());
        Assert.AreEqual(expectedMaxUses, webSearch.GetProperty("max_uses").GetInt32());
    }

    [TestMethod]
    public async Task CompleteAsync_ReducesWebSearchMaxUsesToRemainingBudget()
    {
        var fixture = Fixture.CreateWithOptions(
            EnabledWebSearchOptions(),
            WebSearchUsageResponse("partial", 10, 2, webSearchRequests: 2, stopReason: "pause_turn"),
            DirectResponse("done"));

        var result = await fixture.Service.CompleteAsync(Request());

        Assert.HasCount(0, result.Errors);
        Assert.AreEqual(3, ReadWebSearchMaxUses(fixture.Handler.Requests[0].Body));
        Assert.AreEqual(1, ReadWebSearchMaxUses(fixture.Handler.Requests[1].Body));
        Assert.AreEqual(2, result.Usage.WebSearchRequests);
    }

    [TestMethod]
    public async Task CompleteAsync_RemovesWebSearchAtZeroBudgetButKeepsOtherTools()
    {
        var fixture = Fixture.CreateWithOptions(
            EnabledWebSearchOptions(),
            WebSearchUsageResponse("partial", 10, 2, webSearchRequests: 3, stopReason: "pause_turn"),
            DirectResponse("done"));

        var result = await fixture.Service.CompleteAsync(Request());

        Assert.AreEqual("done", result.FinalText);
        Assert.HasCount(0, result.Errors);
        using var secondBody = JsonDocument.Parse(fixture.Handler.Requests[1].Body);
        var tools = secondBody.RootElement.GetProperty("tools");
        Assert.AreEqual(2, tools.GetArrayLength());
        Assert.AreEqual("mcp_toolset", tools[0].GetProperty("type").GetString());
        Assert.AreEqual("get_page_image", tools[1].GetProperty("name").GetString());
        Assert.IsFalse(fixture.Handler.Requests[1].Body.Contains("web_search", StringComparison.Ordinal));
        Assert.AreEqual(3, result.Usage.WebSearchRequests);
    }

    [TestMethod]
    public async Task CompleteAsync_ConsumesWebSearchBudgetAcrossThreeMessagesCalls()
    {
        var fixture = Fixture.CreateWithOptions(
            EnabledWebSearchOptions(),
            WebSearchUsageResponse("first", 10, 2, webSearchRequests: 1, stopReason: "pause_turn"),
            WebSearchUsageResponse("second", 10, 2, webSearchRequests: 1, stopReason: "pause_turn"),
            WebSearchUsageResponse("third", 10, 2, webSearchRequests: 1, stopReason: "pause_turn"),
            DirectResponse("done"));

        var result = await fixture.Service.CompleteAsync(Request());

        Assert.AreEqual("done", result.FinalText);
        Assert.HasCount(0, result.Errors);
        CollectionAssert.AreEqual(
            new int?[] { 3, 2, 1, null },
            fixture.Handler.Requests.Select(request => ReadWebSearchMaxUses(request.Body)).ToArray());
        Assert.AreEqual(3, result.Usage.WebSearchRequests);
    }

    [TestMethod]
    public async Task CompleteAsync_ReportsDefensiveWebSearchBudgetExceededError()
    {
        var fixture = Fixture.CreateWithOptions(
            EnabledWebSearchOptions(),
            WebSearchUsageResponse("unexpected", 10, 2, webSearchRequests: 4));

        var result = await fixture.Service.CompleteAsync(Request());

        var error = result.Errors.Single();
        Assert.AreEqual("web_search_budget_exceeded", error.Code);
        Assert.AreEqual(1, error.CallNumber);
        Assert.AreEqual(4, result.Usage.WebSearchRequests);
        Assert.HasCount(1, fixture.Handler.Requests);
    }

    [TestMethod]
    public async Task CompleteAsync_WebBudgetExhaustionDoesNotDisablePageImageOrFileSearch()
    {
        var fixture = Fixture.CreateWithOptions(
            EnabledWebSearchOptions(),
            WebSearchUsageResponse("partial", 10, 2, webSearchRequests: 3, stopReason: "pause_turn"),
            ToolResponse(),
            DirectResponse("done"));

        var result = await fixture.Service.CompleteAsync(Request());

        Assert.AreEqual("done", result.FinalText);
        Assert.HasCount(0, result.Errors);
        Assert.HasCount(1, result.Visuals);
        using var secondBody = JsonDocument.Parse(fixture.Handler.Requests[1].Body);
        var tools = secondBody.RootElement.GetProperty("tools");
        Assert.AreEqual("mcp_toolset", tools[0].GetProperty("type").GetString());
        Assert.AreEqual("get_page_image", tools[1].GetProperty("name").GetString());
        Assert.IsFalse(fixture.Handler.Requests.Skip(1)
            .Any(request => request.Body.Contains("web_search", StringComparison.Ordinal)));
        Assert.AreEqual(3, result.Usage.WebSearchRequests);
    }

    [TestMethod]
    public async Task CompleteAsync_MaxUsesExceededResultDoesNotRetryOrFailConversation()
    {
        var fixture = Fixture.CreateWithOptions(
            EnabledWebSearchOptions(),
            WebSearchMaxUsesExceededResponse());

        var result = await fixture.Service.CompleteAsync(Request());

        Assert.AreEqual("recherches déjà obtenues", result.FinalText);
        Assert.HasCount(0, result.Errors);
        Assert.HasCount(1, fixture.Handler.Requests);
        Assert.AreEqual(3, result.Usage.WebSearchRequests);
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
    public async Task CompleteAsync_MapsDisplayedPageUsingFileSearchDocumentId()
    {
        const string documentId = "develon-dx10z-manuel-utilisation-maintenance-en";
        const string finalText = "Source : p. 70 (Figure 102) du manuel DX10z.";
        var response = SourceResponse("file_search", documentId, finalText);
        var fixture = Fixture.Create(response);
        fixture.BlobReader.PageMapFactory = _ => PageMap(documentId);

        var result = await fixture.Service.CompleteAsync(Request());

        Assert.AreEqual(finalText, result.FinalText);
        var source = result.Sources.Single();
        Assert.AreEqual(documentId, source.DocumentId);
        Assert.AreEqual("70", source.DisplayPage);
        Assert.AreEqual(72, source.PdfPage);
        Assert.AreEqual("p. 70", source.Label);
        Assert.AreEqual(finalText.IndexOf(source.Label, StringComparison.Ordinal), source.StartIndex);
        Assert.AreEqual(source.StartIndex + source.Label.Length, source.EndIndex);
        Assert.HasCount(1, fixture.BlobReader.RequestedPageMaps);
    }

    [TestMethod]
    public async Task CompleteAsync_RetainsFileSearchDocumentIdAcrossImageToolCall()
    {
        const string documentId = "develon-dx10z-manuel-utilisation-maintenance-en";
        const string finalText = "Source : p. 71";
        var fixture = Fixture.Create(
            ToolResponse(documentId: documentId, allowedDocumentIds: [documentId]),
            DirectResponse(finalText));
        fixture.BlobReader.PageMapFactory = _ => PageMap(documentId, "71", 73);

        var result = await fixture.Service.CompleteAsync(Request());

        Assert.AreEqual(finalText, result.FinalText);
        Assert.HasCount(2, fixture.Handler.Requests);
        var source = result.Sources.Single();
        Assert.AreEqual(documentId, source.DocumentId);
        Assert.AreEqual("71", source.DisplayPage);
        Assert.AreEqual(73, source.PdfPage);
        Assert.AreEqual("p. 71", source.Label);
        Assert.HasCount(1, fixture.BlobReader.RequestedPageMaps);
    }

    [TestMethod]
    public async Task CompleteAsync_NonFileSearchMcpResultDoesNotCreateSource()
    {
        const string documentId = "develon-dx10z-manuel-utilisation-maintenance-en";
        var fixture = Fixture.Create(SourceResponse("other_tool", documentId, "Source : p. 70."));
        fixture.BlobReader.PageMapFactory = _ => PageMap(documentId);

        var result = await fixture.Service.CompleteAsync(Request());

        Assert.IsEmpty(result.Sources);
        Assert.IsEmpty(fixture.BlobReader.RequestedPageMaps);
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
    [DataRow("r00-c00")]
    [DataRow("r04-c01")]
    [DataRow("r01-c04")]
    [DataRow("r04-c04")]
    [DataRow("r99-c99")]
    [DataRow("R01-C01")]
    [DataRow("../../secret")]
    public async Task CompleteAsync_RejectsInvalidTileBeforeBlobRead(string tile)
    {
        var fixture = Fixture.Create(
            ToolResponse(assetType: "tile", tile: tile),
            DirectResponse("handled"));

        var result = await fixture.Service.CompleteAsync(Request());

        Assert.HasCount(0, fixture.BlobReader.RequestedNames);
        Assert.IsTrue(result.Errors.Any(error => error.Code == "invalid_image_request"));
    }

    [TestMethod]
    [DynamicData(nameof(ValidTiles))]
    public async Task CompleteAsync_AcceptsEveryAvailableTile(string tile)
    {
        var fixture = Fixture.Create(
            ToolResponse(assetType: "tile", tile: tile),
            DirectResponse("done"));

        var result = await fixture.Service.CompleteAsync(Request());

        Assert.IsFalse(result.Errors.Any(error => error.Code == "invalid_image_request"));
        Assert.AreEqual(tile, result.Visuals.Single().Tile);
        StringAssert.EndsWith(
            fixture.BlobReader.RequestedNames.Single(),
            $"{DocumentId}_page-00075-tile-{tile}.png");
    }

    public static IEnumerable<object[]> ValidTiles =>
        ValidTileIds.Select(tile => new object[] { tile });

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task CompleteAsync_RejectsTileAssetWithoutTile(bool includeNullTile)
    {
        var fixture = Fixture.Create(
            ToolResponse(assetType: "tile", tile: null, includeTile: includeNullTile),
            DirectResponse("handled"));

        var result = await fixture.Service.CompleteAsync(Request());

        Assert.HasCount(0, fixture.BlobReader.RequestedNames);
        Assert.IsTrue(result.Errors.Any(error => error.Code == "invalid_image_request"));
    }

    [TestMethod]
    public async Task CompleteAsync_RejectsFullAssetWithTile()
    {
        var fixture = Fixture.Create(
            ToolResponse(assetType: "full", tile: "r01-c01"),
            DirectResponse("handled"));

        var result = await fixture.Service.CompleteAsync(Request());

        Assert.HasCount(0, fixture.BlobReader.RequestedNames);
        Assert.IsTrue(result.Errors.Any(error => error.Code == "invalid_image_request"));
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task CompleteAsync_AcceptsFullAssetWithNullOrMissingTile(bool includeNullTile)
    {
        var fixture = Fixture.Create(
            ToolResponse(assetType: "full", tile: null, includeTile: includeNullTile),
            DirectResponse("done"));

        var result = await fixture.Service.CompleteAsync(Request());

        Assert.HasCount(0, result.Errors);
        Assert.AreEqual("full", result.Visuals.Single().AssetType);
        StringAssert.EndsWith(
            fixture.BlobReader.RequestedNames.Single(),
            $"{DocumentId}_page-00075-full.png");
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
            Assert.AreEqual("5m", body.RootElement.GetProperty("system")[0]
                .GetProperty("cache_control").GetProperty("ttl").GetString());
            Assert.AreEqual(1, Regex.Matches(captured.Body, "\\\"cache_control\\\"").Count);
        }
        using var secondBody = JsonDocument.Parse(fixture.Handler.Requests[1].Body);
        Assert.AreEqual(3, secondBody.RootElement.GetProperty("messages").GetArrayLength());
        Assert.AreEqual("How do I replace the hydraulic filter?",
            secondBody.RootElement.GetProperty("messages")[0].GetProperty("content").GetString());
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
        CollectionAssert.AreEqual(
            new[] { "file_search", "get_page_image" },
            result.Calls[0].Tools.ToArray());
        Assert.HasCount(0, result.Calls[1].Tools);
    }

    [TestMethod]
    public async Task CompleteAsync_ResponseWithoutCacheReportsZeroCacheUsage()
    {
        var result = await Fixture.Create(DirectResponse("done", 20, 5)).Service.CompleteAsync(Request());

        Assert.AreEqual(0, result.Usage.CacheReadInputTokens);
        Assert.AreEqual(0, result.Usage.CacheCreationInputTokens);
        Assert.AreEqual(0, result.Usage.CacheCreation5mInputTokens);
        Assert.AreEqual(0, result.Usage.CacheCreation1hInputTokens);
    }

    [TestMethod]
    public async Task CompleteAsync_ReadsWebSearchRequestUsageWithoutChangingTokens()
    {
        var result = await Fixture.Create(
            WebSearchUsageResponse("done", 11, 4, webSearchRequests: 3)).Service.CompleteAsync(Request());

        Assert.AreEqual(3, result.Calls.Single().WebSearchRequests);
        Assert.AreEqual(3, result.Usage.WebSearchRequests);
        Assert.AreEqual(11, result.Usage.InputTokens);
        Assert.AreEqual(4, result.Usage.OutputTokens);
        Assert.AreEqual(15, result.Usage.TotalTokens);
    }

    [TestMethod]
    public async Task CompleteAsync_ResponseWithoutWebSearchUsageReportsZeroRequests()
    {
        var result = await Fixture.Create(DirectResponse("done", 20, 5)).Service.CompleteAsync(Request());

        Assert.AreEqual(0, result.Calls.Single().WebSearchRequests);
        Assert.AreEqual(0, result.Usage.WebSearchRequests);
        Assert.AreEqual(20, result.Usage.InputTokens);
        Assert.AreEqual(5, result.Usage.OutputTokens);
        Assert.AreEqual(25, result.Usage.TotalTokens);
    }

    [TestMethod]
    public async Task CompleteAsync_AggregatesWebSearchRequestsAcrossPauseTurnCalls()
    {
        var fixture = Fixture.CreateWithOptions(
            EnabledWebSearchOptions(),
            WebSearchUsageResponse("partial", 10, 2, webSearchRequests: 1, stopReason: "pause_turn"),
            WebSearchUsageResponse("done", 20, 5, webSearchRequests: 2));

        var result = await fixture.Service.CompleteAsync(Request());

        CollectionAssert.AreEqual(new long[] { 1, 2 }, result.Calls.Select(call => call.WebSearchRequests).ToArray());
        Assert.AreEqual(3, result.Usage.WebSearchRequests);
        Assert.AreEqual(30, result.Usage.InputTokens);
        Assert.AreEqual(7, result.Usage.OutputTokens);
        Assert.AreEqual(37, result.Usage.TotalTokens);
    }

    [TestMethod]
    public async Task CompleteAsync_ReadsCacheReadUsage()
    {
        var result = await Fixture.Create(CacheUsageResponse("done", 10, 2, cacheRead: 90)).Service.CompleteAsync(Request());

        Assert.AreEqual(90, result.Usage.CacheReadInputTokens);
        Assert.AreEqual(90, result.Calls.Single().CacheReadInputTokens);
    }

    [TestMethod]
    public async Task CompleteAsync_ReadsFiveMinuteCacheCreationUsage()
    {
        var result = await Fixture.Create(CacheUsageResponse("done", 10, 2, cacheCreation: 80, cacheCreation5m: 80)).Service.CompleteAsync(Request());

        Assert.AreEqual(80, result.Usage.CacheCreationInputTokens);
        Assert.AreEqual(80, result.Usage.CacheCreation5mInputTokens);
        Assert.AreEqual(0, result.Usage.CacheCreation1hInputTokens);
    }

    [TestMethod]
    public async Task CompleteAsync_ReadsOneHourCacheCreationUsage()
    {
        var result = await Fixture.Create(CacheUsageResponse("done", 10, 2, cacheCreation: 70, cacheCreation1h: 70)).Service.CompleteAsync(Request());

        Assert.AreEqual(70, result.Usage.CacheCreationInputTokens);
        Assert.AreEqual(0, result.Usage.CacheCreation5mInputTokens);
        Assert.AreEqual(70, result.Usage.CacheCreation1hInputTokens);
    }

    [TestMethod]
    public async Task CompleteAsync_AggregatesMixedCacheUsageAcrossClaudeCalls()
    {
        var fixture = Fixture.Create(
            CacheUsageResponse("first", 10, 2, cacheRead: 30, cacheCreation: 50, cacheCreation5m: 40, cacheCreation1h: 10, toolUse: true),
            CacheUsageResponse("done", 20, 5, cacheRead: 70, cacheCreation: 25, cacheCreation1h: 25));

        var result = await fixture.Service.CompleteAsync(Request());

        Assert.AreEqual(30, result.Usage.InputTokens);
        Assert.AreEqual(7, result.Usage.OutputTokens);
        Assert.AreEqual(100, result.Usage.CacheReadInputTokens);
        Assert.AreEqual(75, result.Usage.CacheCreationInputTokens);
        Assert.AreEqual(40, result.Usage.CacheCreation5mInputTokens);
        Assert.AreEqual(35, result.Usage.CacheCreation1hInputTokens);
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

    private static ClaudeDirectChatRequest Request(
        CommercialPolicySnapshot? commercialPolicy = null) => new(
        new(
            ProjectEndpoint,
            "dx10z-toolbox",
            "1",
            McpEndpoint,
            "vs_unitTest123",
            "company/machine",
            "Machine DX10z; use the connected technical manual."),
        "Use File Search before inspecting technical images.",
        [new("user", "How do I replace the hydraulic filter?")],
        commercialPolicy ?? new(false, string.Empty));

    private static HttpResponseMessage DirectResponse(string text, long inputTokens = 1, long outputTokens = 1) =>
        JsonResponse(JsonSerializer.Serialize(new
        {
            id = "msg-final",
            model = "claude-sonnet-5",
            stop_reason = "end_turn",
            content = new object[] { new { type = "text", text } },
            usage = new { input_tokens = inputTokens, output_tokens = outputTokens }
        }));

    private static HttpResponseMessage EventStreamResponse(params string[] events) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(
            string.Concat(events.Select(value => $"data: {value}\n\n")),
            Encoding.UTF8,
            "text/event-stream")
    };

    private static HttpResponseMessage PauseTurnResponse() =>
        JsonResponse($$"""
        {
            "id":"msg-paused",
            "model":"claude-sonnet-5",
            "stop_reason":"pause_turn",
            "content":{{PauseTurnContentJson}},
            "usage":{"input_tokens":4,"output_tokens":2}
        }
        """);

    private static HttpResponseMessage WebSearchAndExistingToolsResponse() =>
        JsonResponse($$$"""
        {
          "id":"msg-web-tools",
          "model":"claude-sonnet-5",
          "stop_reason":"tool_use",
          "content":[
            {"type":"server_tool_use","id":"web-1","name":"web_search","input":{"query":"hydraulic filter"}},
            {
              "type":"web_search_tool_result",
              "tool_use_id":"web-1",
              "content":[{"type":"web_search_result","url":"https://example.test/result","title":"Result"}]
            },
            {"type":"mcp_tool_use","id":"mcp-1","name":"file_search","input":{}},
            {
              "type":"mcp_tool_result",
              "tool_use_id":"mcp-1",
              "is_error":false,
              "content":[{"type":"text","text":"Document ID: {{{DocumentId}}}"}]
            },
            {
              "type":"tool_use",
              "id":"tool-1",
              "name":"get_page_image",
              "input":{"document_id":"{{{DocumentId}}}","page":75,"asset_type":"full"}
            }
          ],
          "usage":{"input_tokens":10,"output_tokens":2}
        }
        """);

    private static HttpResponseMessage WebCitationAndPdfSourceResponse(string documentId, string finalText) =>
        JsonResponse($$$"""
        {
          "id":"msg-web-citation-pdf-source",
          "model":"claude-sonnet-5",
          "stop_reason":"end_turn",
          "content":[
            {"type":"mcp_tool_use","id":"mcp-1","name":"file_search","input":{}},
            {
              "type":"mcp_tool_result",
              "tool_use_id":"mcp-1",
              "is_error":false,
              "content":[{"type":"text","text":"Document ID: {{{documentId}}}"}]
            },
            {
              "type":"text",
              "text":"{{{finalText}}}",
              "citations":[
                {
                  "type":"web_search_result_location",
                  "url":"https://example.test/web-source",
                  "title":"Web source",
                  "cited_text":"Web passage"
                }
              ]
            }
          ],
          "usage":{"input_tokens":10,"output_tokens":2}
        }
        """);

    private static HttpResponseMessage MultipleWebCitationsResponse() =>
        JsonResponse("""
        {
          "id":"msg-multiple-web-citations",
          "model":"claude-sonnet-5",
          "stop_reason":"end_turn",
          "content":[
            {
              "type":"text",
              "text":"Answer unchanged.",
              "citations":[
                {"type":"web_search_result_location","url":"https://example.test/first","title":"First"},
                {"type":"web_search_result_location","url":"https://example.test/second","title":"Second"},
                {"type":"web_search_result_location","url":"https://example.test/first","title":"Duplicate"}
              ]
            }
          ],
          "usage":{"input_tokens":10,"output_tokens":2}
        }
        """);

        private static HttpResponseMessage SourceResponse(string toolName, string documentId, string finalText) =>
                JsonResponse($$"""
                {
                    "id":"msg-source",
                    "model":"claude-sonnet-5",
                    "stop_reason":"end_turn",
                    "content":[
                        {"type":"mcp_tool_use","id":"mcp-1","name":"{{toolName}}","input":null},
                        {
                            "type":"mcp_tool_result",
                            "tool_use_id":"mcp-1",
                            "is_error":false,
                            "content":[{"type":"text","text":"Document ID: {{documentId}}"}]
                        },
                        {"type":"text","text":"{{finalText}}"}
                    ],
                    "usage":{"input_tokens":10,"output_tokens":2}
                }
                """);

        private static Stream PageMap(
            string documentId,
            string displayPage = "70",
            int pdfPage = 72) => new MemoryStream(Encoding.UTF8.GetBytes($$"""
        {
            "schemaVersion":1,
            "documentId":"{{documentId}}",
            "sourceBlob":"company/machine/DX10z.pdf",
            "pages":[{"pdfPage":{{pdfPage}},"displayPage":"{{displayPage}}","source":"ExtractedText"}]
        }
        """));

    private static HttpResponseMessage CacheUsageResponse(
        string text,
        long input,
        long output,
        long cacheRead = 0,
        long cacheCreation = 0,
        long cacheCreation5m = 0,
        long cacheCreation1h = 0,
        bool toolUse = false)
    {
        var usage = new Dictionary<string, object?>
        {
            ["input_tokens"] = input,
            ["output_tokens"] = output,
            ["cache_read_input_tokens"] = cacheRead,
            ["cache_creation_input_tokens"] = cacheCreation,
            ["cache_creation"] = new Dictionary<string, object?>
            {
                ["ephemeral_5m_input_tokens"] = cacheCreation5m,
                ["ephemeral_1h_input_tokens"] = cacheCreation1h
            }
        };
        object[] content = toolUse
            ? [new { type = "tool_use", id = "tool-cache", name = "get_page_image", input = new { document_id = DocumentId, page = 75, asset_type = "full" } }]
            : [new { type = "text", text }];
        return JsonResponse(JsonSerializer.Serialize(new
        {
            id = toolUse ? "msg-tool-cache" : "msg-cache",
            model = "claude-sonnet-5",
            stop_reason = toolUse ? "tool_use" : "end_turn",
            content,
            usage
        }));
    }

    private static HttpResponseMessage WebSearchUsageResponse(
        string text,
        long inputTokens,
        long outputTokens,
        long webSearchRequests,
        string stopReason = "end_turn") =>
        JsonResponse(JsonSerializer.Serialize(new
        {
            id = "msg-web-search-usage",
            model = "claude-sonnet-5",
            stop_reason = stopReason,
            content = new object[] { new { type = "text", text } },
            usage = new
            {
                input_tokens = inputTokens,
                output_tokens = outputTokens,
                server_tool_use = new { web_search_requests = webSearchRequests }
            }
        }));

    private static HttpResponseMessage WebSearchMaxUsesExceededResponse() =>
        JsonResponse("""
        {
          "id":"msg-web-limit",
          "model":"claude-sonnet-5",
          "stop_reason":"end_turn",
          "content":[
            {"type":"server_tool_use","id":"web-limit","name":"web_search","input":{"query":"extra search"}},
            {
              "type":"web_search_tool_result",
              "tool_use_id":"web-limit",
              "content":{"type":"web_search_tool_result_error","error_code":"max_uses_exceeded"}
            },
            {"type":"text","text":"recherches déjà obtenues"}
          ],
          "usage":{
            "input_tokens":10,
            "output_tokens":2,
            "server_tool_use":{"web_search_requests":3}
          }
        }
        """);

    private static ClaudeDirectChatOptions EnabledWebSearchOptions(int maxMessageCalls = 4) => new()
    {
        FoundryAnthropicEndpoint = "https://resource.test/anthropic",
        Deployment = "claude-sonnet-5",
        MaxMessageCalls = maxMessageCalls,
        MaxImageBytes = 1024,
        WebSearch = new ClaudeDirectWebSearchOptions
        {
            Enabled = true,
            DiagnosticMaxUses = 2,
            PartsMaxUses = 3
        }
    };

    private static int? ReadWebSearchMaxUses(string requestBody)
    {
        using var body = JsonDocument.Parse(requestBody);
        foreach (var tool in body.RootElement.GetProperty("tools").EnumerateArray())
        {
            if (tool.TryGetProperty("name", out var name) && name.GetString() == "web_search")
            {
                return tool.GetProperty("max_uses").GetInt32();
            }
        }

        return null;
    }

    private static HttpResponseMessage ToolResponse(
        string? documentId = DocumentId,
        string assetType = "full",
        string? tile = null,
        long inputTokens = 10,
        long outputTokens = 2,
        string[]? allowedDocumentIds = null,
        bool includeTile = true)
    {
        var input = new Dictionary<string, object?>
        {
            ["page"] = 75,
            ["asset_type"] = assetType
        };
        if (includeTile)
        {
            input["tile"] = tile;
        }
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
            var pageMapResolver = new TechnicalPageMapResolver(
                blobReader,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<TechnicalPageMapResolver>.Instance);
            var service = new ClaudeDirectChatService(
                new SingleClientFactory(new HttpClient(handler)),
                credential,
                blobReader,
                new TechnicalSourceReferenceResolver(pageMapResolver),
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

    private sealed class FakeBlobReader : ITechnicalVisualBlobReader, ITechnicalDocumentBlobReader
    {
        public Func<string, Stream?> StreamFactory { get; set; } = _ =>
            new MemoryStream([137, 80, 78, 71, 13, 10, 26, 10, 1, 2, 3]);
        public List<string> RequestedNames { get; } = [];
        public Func<string, Stream?> PageMapFactory { get; set; } = _ => null;
        public List<string> RequestedPageMaps { get; } = [];

        public Task<Stream?> OpenReadAsync(string blobName, CancellationToken cancellationToken)
        {
            RequestedNames.Add(blobName);
            return Task.FromResult(StreamFactory(blobName));
        }

        public Task<Stream?> OpenPageMapAsync(string blobName, CancellationToken cancellationToken)
        {
            RequestedPageMaps.Add(blobName);
            return Task.FromResult(PageMapFactory(blobName));
        }

        public Task<Stream?> OpenPdfAsync(string blobName, CancellationToken cancellationToken) =>
            Task.FromResult<Stream?>(null);
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
