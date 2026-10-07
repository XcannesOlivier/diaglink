using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebApp.Api.Models;
using WebApp.Api.Services;

namespace WebApp.Api.Tests;

[TestClass]
public class TechnicalVisualSseTests
{
    private static readonly ConversationMessageVisualInfo Full = new()
    {
        Id = 101,
        DocumentId = "manual",
        Page = 71,
        AssetType = "full",
        Tile = null,
        Name = "manual_page-00071-full.png",
        DisplayOrder = 0
    };

    private static readonly ConversationMessageVisualInfo Tile = new()
    {
        Id = 102,
        DocumentId = "manual",
        Page = 71,
        AssetType = "tile",
        Tile = "r02-c01",
        Name = "manual_page-00071-tile-r02-c01.png",
        DisplayOrder = 1
    };

    private static readonly ConversationMessageSourceReferenceInfo Source = new()
    {
        Id = 921,
        PdfPage = 72,
        DisplayPage = "70",
        Label = "p. 70",
        StartIndex = 123,
        EndIndex = 128,
        DisplayOrder = 0
    };

    [TestMethod]
    public async Task NoVisuals_WritesNoVisualsEventAndPreservesDone()
    {
        var response = Response();

        await TechnicalVisualSseWriter.WriteBeforeDoneAsync(response, [], default, WriteDoneAsync);

        CollectionAssert.AreEqual(new[] { "done" }, EventTypes(response));
    }

    [TestMethod]
    public async Task OneVisual_WritesSingleEventBeforeDone()
    {
        var response = Response();

        await TechnicalVisualSseWriter.WriteBeforeDoneAsync(response, [Full], default, WriteDoneAsync);

        CollectionAssert.AreEqual(new[] { "visuals", "done" }, EventTypes(response));
        using var json = JsonDocument.Parse(Events(response)[0]);
        var visuals = json.RootElement.GetProperty("visuals");
        Assert.AreEqual(1, visuals.GetArrayLength());
        Assert.AreEqual(101, visuals[0].GetProperty("id").GetInt64());
    }

    [TestMethod]
    public async Task MultipleVisuals_WriteOneOrderedEvent()
    {
        var response = Response();

        await TechnicalVisualSseWriter.WriteBeforeDoneAsync(response, [Full, Tile], default, WriteDoneAsync);

        var events = Events(response);
        Assert.HasCount(2, events);
        using var json = JsonDocument.Parse(events[0]);
        var visuals = json.RootElement.GetProperty("visuals");
        CollectionAssert.AreEqual(new long[] { 101, 102 }, visuals.EnumerateArray().Select(item => item.GetProperty("id").GetInt64()).ToArray());
        CollectionAssert.AreEqual(new[] { 0, 1 }, visuals.EnumerateArray().Select(item => item.GetProperty("displayOrder").GetInt32()).ToArray());
    }

    [TestMethod]
    public async Task VisualsEvent_UsesPublicHistoryDtoAndContainsNoPrivateFields()
    {
        var response = Response();

        await TechnicalVisualSseWriter.WriteBeforeDoneAsync(response, [Tile], default, WriteDoneAsync);

        var payload = Events(response)[0];
        using var json = JsonDocument.Parse(payload);
        var visual = json.RootElement.GetProperty("visuals")[0];
        CollectionAssert.AreEquivalent(
            new[] { "id", "documentId", "page", "assetType", "tile", "name", "displayOrder" },
            visual.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.IsNull(typeof(ConversationMessageVisualInfo).GetProperty("AssetKey"));
        foreach (var forbidden in new[] { "assetKey", "blobPrefix", "https://", "sig=", "sas" })
        {
            Assert.IsFalse(payload.Contains(forbidden, StringComparison.OrdinalIgnoreCase));
        }
    }

    [TestMethod]
    public async Task ExistingChunkAndUsageRemainBeforeVisualsAndDone()
    {
        var response = Response();
        await WriteRawEventAsync(response, """{"type":"chunk","content":"texte"}""");
        await WriteRawEventAsync(response, """{"type":"usage","duration":12}""");

        await TechnicalVisualSseWriter.WriteBeforeDoneAsync(response, [Full], default, WriteDoneAsync);

        CollectionAssert.AreEqual(new[] { "chunk", "usage", "visuals", "done" }, EventTypes(response));
        using var chunk = JsonDocument.Parse(Events(response)[0]);
        Assert.AreEqual("texte", chunk.RootElement.GetProperty("content").GetString());
        using var usage = JsonDocument.Parse(Events(response)[1]);
        Assert.AreEqual(12, usage.RootElement.GetProperty("duration").GetInt32());
    }

    [TestMethod]
    public async Task SourcesEvent_IsAfterVisualsAndBeforeDone()
    {
        var response = Response();

        await TechnicalVisualSseWriter.WriteBeforeDoneAsync(
            response,
            [Full],
            [Source],
            default,
            WriteDoneAsync);

        CollectionAssert.AreEqual(new[] { "visuals", "sources", "done" }, EventTypes(response));
        using var json = JsonDocument.Parse(Events(response)[1]);
        var source = json.RootElement.GetProperty("sources")[0];
        Assert.AreEqual(921, source.GetProperty("id").GetInt64());
        Assert.AreEqual(72, source.GetProperty("pdfPage").GetInt32());
        Assert.AreEqual("70", source.GetProperty("displayPage").GetString());
        Assert.AreEqual("p. 70", source.GetProperty("label").GetString());
        Assert.AreEqual(123, source.GetProperty("startIndex").GetInt32());
        Assert.AreEqual(128, source.GetProperty("endIndex").GetInt32());
        Assert.AreEqual(0, source.GetProperty("displayOrder").GetInt32());
    }

    [TestMethod]
    public async Task SourcesEvent_ContainsNoPrivateDocumentOrStorageData()
    {
        var response = Response();

        await TechnicalVisualSseWriter.WriteBeforeDoneAsync(
            response,
            [],
            [Source],
            default,
            WriteDoneAsync);

        var payload = Events(response).Single(item => item.Contains("\"type\":\"sources\"", StringComparison.Ordinal));
        using var json = JsonDocument.Parse(payload);
        var source = json.RootElement.GetProperty("sources")[0];
        CollectionAssert.AreEquivalent(
            new[] { "id", "pdfPage", "displayPage", "label", "startIndex", "endIndex", "displayOrder" },
            source.EnumerateObject().Select(property => property.Name).ToArray());
        foreach (var forbidden in new[] { "documentId", "sourceBlob", "blob", "https://", "sig=", "sas", "foundry" })
        {
            Assert.IsFalse(payload.Contains(forbidden, StringComparison.OrdinalIgnoreCase));
        }
    }

    private static HttpResponse Response()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        return context.Response;
    }

    private static async Task WriteDoneAsync(HttpResponse response, CancellationToken cancellationToken) =>
        await WriteRawEventAsync(response, """{"type":"done"}""", cancellationToken);

    private static async Task WriteRawEventAsync(
        HttpResponse response,
        string json,
        CancellationToken cancellationToken = default)
    {
        await response.WriteAsync($"data: {json}\n\n", cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }

    private static List<string> Events(HttpResponse response)
    {
        var body = Encoding.UTF8.GetString(((MemoryStream)response.Body).ToArray());
        return body.Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .Select(item => item["data: ".Length..])
            .ToList();
    }

    private static string[] EventTypes(HttpResponse response) => Events(response)
        .Select(payload =>
        {
            using var json = JsonDocument.Parse(payload);
            return json.RootElement.GetProperty("type").GetString()!;
        })
        .ToArray();
}
