using System.ClientModel.Primitives;
using System.Text.Json;
using System.Text.RegularExpressions;
using OpenAI.Responses;
using WebApp.Api.Models;

namespace WebApp.Api.Services;

#pragma warning disable OPENAI001
/// <summary>Request-local Vision capture and safe diagnostics. Never logs document text or arguments.</summary>
public sealed class VisionToolDiagnostics(ILogger logger)
{
    private const string Tool = "blob_page_images_analyze_page";
    private static readonly Regex TilePattern = new(@"\Ar\d{2}-c\d{2}\z", RegexOptions.CultureInvariant);
    private static readonly Regex SchemePattern = new(@"\A[a-zA-Z][a-zA-Z0-9+.-]*:", RegexOptions.CultureInvariant);
    private readonly HashSet<(string Parent, string Call)> observedCalls = [];
    private readonly Dictionary<(string Parent, string Call), VisionUsageCapture> calls = [];
    private readonly Dictionary<(string Parent, string Call), IReadOnlyList<TechnicalVisualReference>> visualsByCall = [];
    private readonly List<(string Parent, string Call)> visualCallOrder = [];
    private readonly HashSet<string> discoveredVisualAssetKeys = new(StringComparer.Ordinal);
    private readonly List<TechnicalVisualReference> pendingVisuals = [];
    public IReadOnlyCollection<VisionUsageCapture> Measurements => calls.Values;
    public IReadOnlyList<TechnicalVisualReference> Visuals => visualCallOrder
        .SelectMany(key => visualsByCall[key])
        .ToArray();
    private AiResponseUsage? parsedUsage;
    private List<TechnicalVisualReference>? parsedVisuals;
    public bool Observed { get; private set; }

    private static string? Identifier(JsonElement element, string key, int maxLength = 200)
    {
        if (!element.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.String) return null;
        var text = value.GetString();
        return text != null && text.Length <= maxLength && Regex.IsMatch(text, @"\A[a-zA-Z0-9_.:-]+\z") ? text : "[redacted]";
    }

    public VisionUsageCapture? Observe(ResponseItem item, string phase, string? parent)
    {
        try
        {
            using var json = JsonDocument.Parse(ModelReaderWriter.Write(item).ToMemory());
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object || string.IsNullOrWhiteSpace(parent)) return null;
            var call = Identifier(root, "call_id");
            if (call is null or "[redacted]") return null;
            var key = (parent, call);
            calls.TryGetValue(key, out var previous);
            if (Identifier(root, "name") != Tool && !observedCalls.Contains(key)) return null;
            observedCalls.Add(key);
            Observed = true;
            var hasOutput = root.TryGetProperty("output", out var eventOutput) && eventOutput.ValueKind != JsonValueKind.Null;
            if (logger.IsEnabled(LogLevel.Debug))
            {
                var shape = OutputPresence(eventOutput);
                logger.LogDebug("VisionProbe Event Phase={Phase} RuntimeType={RuntimeType} WireType={WireType} ParentResponseId={ParentResponseId} CallId={CallId} Status={Status} HasOutput={HasOutput} OutputKind={OutputKind} HasNestedResponse={HasNestedResponse} HasUsage={HasUsage} PreviousAvailable={PreviousAvailable} PreviousCompleted={PreviousCompleted}",
                    phase, item.GetType().FullName, Identifier(root, "type"), parent, call, Identifier(root, "status"),
                    hasOutput, hasOutput ? eventOutput.ValueKind.ToString() : null, shape.NestedResponse, shape.Usage,
                    previous?.Usage.Available, previous?.Usage.Completed);
            }
            // Announcements only establish correlation; only a final output can become consumption.
            var final = phase == "done" && Identifier(root, "type") == "openapi_call_output";
            if (!final) return null;
            // Only a completed, known result is definitive. Unknown finals may be enriched.
            if (previous?.Usage is { Completed: true, Available: true }) return previous;
            var usage = new AiResponseUsage(AiUsageType.VisionTool, null, final,
                null, null, null, null, null, null, DateTimeOffset.UtcNow)
                { CallId = call, ParentResponseId = parent };
            parsedUsage = usage;
            parsedVisuals = [];
            if (final && root.TryGetProperty("output", out var output)) InspectOutput(output, parent, call);
            foreach (var visual in parsedVisuals)
            {
                if (discoveredVisualAssetKeys.Add(visual.AssetKey))
                    pendingVisuals.Add(visual);
            }
            if (!visualsByCall.ContainsKey(key))
            {
                visualCallOrder.Add(key);
                visualsByCall[key] = parsedVisuals;
            }
            else if (parsedVisuals.Count > 0)
            {
                visualsByCall[key] = parsedVisuals;
            }
            var capture = new VisionUsageCapture(previous?.EventId ?? Guid.NewGuid(), parsedUsage ?? usage);
            // Late added/invalid outputs must not regress an already completed unknown capture.
            if (previous?.Usage.Completed == true && !capture.Usage.Available) return previous;
            calls[key] = capture;
            logger.LogDebug("VisionProbe Call ParentResponseId={ParentResponseId} CallId={CallId} Completed={Completed} Available={Available}",
                parent, call, capture.Usage.Completed, capture.Usage.Available);
            return capture;
        }
        catch (Exception)
        {
            logger.LogDebug("VisionProbe Diagnostic=UnreadableItem");
            return null;
        }
    }

    internal IReadOnlyList<TechnicalVisualReference> TakeNewVisuals()
    {
        if (pendingVisuals.Count == 0) return Array.Empty<TechnicalVisualReference>();
        var result = pendingVisuals.ToArray();
        pendingVisuals.Clear();
        return result;
    }

    // Inspect presence only, with the same bounded JSON decoding budget (Output + three levels).
    private static (bool NestedResponse, bool Usage) OutputPresence(JsonElement value)
    {
        var documents = new List<JsonDocument>();
        var nested = false;
        try
        {
            for (var level = 0; level <= 3; level++)
            {
                if (value.ValueKind == JsonValueKind.String)
                {
                    var document = JsonDocument.Parse(value.GetString()!);
                    documents.Add(document);
                    value = document.RootElement;
                }
                if (value.ValueKind == JsonValueKind.Object)
                {
                    var hasResponse = value.TryGetProperty("response", out var response);
                    nested |= hasResponse;
                    if (value.TryGetProperty("usage", out _)) return (nested, true);
                    if (!hasResponse) break;
                    value = response;
                }
                else if (value.ValueKind != JsonValueKind.String) break;
            }
        }
        catch (JsonException) { /* Never include the payload or parsing exception in logs. */ }
        finally { foreach (var document in documents) document.Dispose(); }
        return (nested, false);
    }

    private void ExtractUsage(JsonElement output)
    {
        if (parsedUsage is null) return;
        string? Technical(string key) => Identifier(output, key, key == "version" ? 100 : key == "model" ? 256 : 200) is { } v && v != "[redacted]" ? v : null;
        var hasUsage = output.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object;
        int? Number(string key) => hasUsage && usage.TryGetProperty(key, out var v) &&
            v.TryGetInt64Safe(out var n) && n >= 0 && n <= int.MaxValue ? (int)n : null;
        var input = Number("input_tokens");
        var result = Number("output_tokens");
        var total = Number("total_tokens");
        var sum = (long?)input + result;
        if (hasUsage && !usage.TryGetProperty("total_tokens", out _) && sum <= int.MaxValue) total = (int?)sum;
        var valid = input.HasValue && result.HasValue && total.HasValue && total == sum;
        var model = Technical("model");
        parsedUsage = parsedUsage with {
            InputTokens = valid ? input : null, OutputTokens = valid ? result : null, TotalTokens = valid ? total : null,
            ResponseId = Technical("response_id"), Model = model, ModelSource = model == null ? null : "response",
            AgentVersion = Technical("version"), Provider = Technical("provider"), Deployment = Technical("deployment")
        };
    }

    private void ExtractVisuals(JsonElement output)
    {
        if (parsedVisuals is null ||
            !output.TryGetProperty("visuals", out var visuals) ||
            visuals.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var visual in visuals.EnumerateArray())
        {
            if (TryParseVisual(visual, out var reference))
            {
                parsedVisuals.Add(reference);
            }
        }
    }

    private static bool TryParseVisual(JsonElement visual, out TechnicalVisualReference reference)
    {
        reference = null!;
        if (visual.ValueKind != JsonValueKind.Object ||
            !RequiredString(visual, "document_id", out var documentId) ||
            !visual.TryGetProperty("page", out var pageValue) ||
            !pageValue.TryGetInt32(out var page) || page <= 0 ||
            !RequiredString(visual, "asset_type", out var assetType) ||
            assetType is not ("full" or "tile") ||
            !RequiredString(visual, "name", out var name) ||
            !name.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
            !RequiredString(visual, "asset_key", out var assetKey) ||
            !IsSafeRelativeAssetKey(assetKey))
        {
            return false;
        }

        string? tile = null;
        if (visual.TryGetProperty("tile", out var tileValue) && tileValue.ValueKind != JsonValueKind.Null)
        {
            if (tileValue.ValueKind != JsonValueKind.String) return false;
            tile = tileValue.GetString();
        }

        if ((assetType == "full" && tile is not null) ||
            (assetType == "tile" && (tile is null || !TilePattern.IsMatch(tile))) ||
            ContainsHttpScheme(documentId) || ContainsHttpScheme(name) ||
            (tile is not null && ContainsHttpScheme(tile)))
        {
            return false;
        }

        reference = new TechnicalVisualReference(documentId, page, assetType, tile, name, assetKey);
        return true;
    }

    private static bool RequiredString(JsonElement element, string propertyName, out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
            return false;
        value = property.GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool IsSafeRelativeAssetKey(string assetKey) =>
        !assetKey.StartsWith('/') &&
        !assetKey.StartsWith('\\') &&
        !assetKey.Contains('\\') &&
        !assetKey.Contains("..", StringComparison.Ordinal) &&
        !ContainsHttpScheme(assetKey) &&
        !SchemePattern.IsMatch(assetKey) &&
        !Uri.TryCreate(assetKey, UriKind.Absolute, out _);

    private static bool ContainsHttpScheme(string value) =>
        value.Contains("http://", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("https://", StringComparison.OrdinalIgnoreCase);

    private void InspectOutput(JsonElement output, string? parent, string? call)
    {
        JsonDocument? decoded = null;
        try
        {
            if (output.ValueKind == JsonValueKind.String)
            {
                decoded = JsonDocument.Parse(output.GetString()!);
                output = decoded.RootElement;
            }
            if (output.ValueKind != JsonValueKind.Object)
            {
                logger.LogDebug("VisionProbe Output ParentResponseId={ParentResponseId} CallId={CallId} JsonKind={Kind}", parent, call, output.ValueKind);
                return;
            }
            ExtractUsage(output);
            ExtractVisuals(output);
            // Only property names and kinds; arbitrary names are restricted and capped.
            var shape = string.Join(",", output.EnumerateObject().Take(40).Select(p =>
                (Regex.IsMatch(p.Name, @"\A[a-zA-Z_][a-zA-Z0-9_]{0,63}\z") ? p.Name : "[redacted]") + ":" + p.Value.ValueKind));
            var hasUsage = output.TryGetProperty("usage", out var usage);
            long? Number(string key) => hasUsage && usage.ValueKind == JsonValueKind.Object &&
                usage.TryGetProperty(key, out var v) && v.TryGetInt64Safe(out var n) && n >= 0 ? n : null;
            logger.LogDebug("VisionProbe Output ParentResponseId={ParentResponseId} CallId={CallId} Shape={Shape} HasUsage={HasUsage} UsageKind={UsageKind} InputTokens={InputTokens} OutputTokens={OutputTokens} TotalTokens={TotalTokens} Model={Model} Deployment={Deployment} Provider={Provider}",
                parent, call, shape, hasUsage, hasUsage ? usage.ValueKind.ToString() : null,
                Number("input_tokens"), Number("output_tokens"), Number("total_tokens"),
                Identifier(output, "model"), Identifier(output, "deployment"), Identifier(output, "provider"));
            if (output.TryGetProperty("response", out var nested))
                InspectNested(nested, parent, call);
        }
        catch (JsonException)
        {
            logger.LogDebug("VisionProbe Output ParentResponseId={ParentResponseId} CallId={CallId} JsonValid=false", parent, call);
        }
        finally { decoded?.Dispose(); }
    }

    // At most three additional JSON parses after the existing Output decoding.
    private void InspectNested(JsonElement nested, string? parent, string? call)
    {
        var documents = new List<JsonDocument>();
        try
        {
            for (var level = 1; level <= 3; level++)
            {
                if (nested.ValueKind == JsonValueKind.String)
                {
                    var text = nested.GetString();
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        logger.LogDebug("VisionProbe Nested ParentResponseId={ParentResponseId} CallId={CallId} Level={Level} Diagnostic=Empty", parent, call, level);
                        return;
                    }
                    JsonDocument document;
                    try { document = JsonDocument.Parse(text); }
                    catch (JsonException)
                    {
                        logger.LogDebug("VisionProbe Nested ParentResponseId={ParentResponseId} CallId={CallId} Level={Level} Diagnostic=InvalidJson", parent, call, level);
                        return;
                    }
                    documents.Add(document);
                    nested = document.RootElement;
                }
                if (nested.ValueKind == JsonValueKind.Object)
                {
                    ExtractUsage(nested);
                    ExtractVisuals(nested);
                    var shape = string.Join(",", nested.EnumerateObject().Take(40).Select(p =>
                        Regex.IsMatch(p.Name, @"\A[a-zA-Z_][a-zA-Z0-9_]{0,63}\z") ? p.Name : "[redacted]"));
                    var hasUsage = nested.TryGetProperty("usage", out var usage);
                    long? Number(string key) => hasUsage && usage.ValueKind == JsonValueKind.Object &&
                        usage.TryGetProperty(key, out var value) && value.TryGetInt64Safe(out var number) && number >= 0 ? number : null;
                    logger.LogDebug("VisionProbe Nested ParentResponseId={ParentResponseId} CallId={CallId} Level={Level} Properties={Properties} HasUsage={HasUsage} UsageKind={UsageKind} InputTokens={InputTokens} OutputTokens={OutputTokens} TotalTokens={TotalTokens} Model={Model} Provider={Provider} Deployment={Deployment} Version={Version} NestedResponseId={NestedResponseId}",
                        parent, call, level, shape, hasUsage, hasUsage ? usage.ValueKind.ToString() : null,
                        Number("input_tokens"), Number("output_tokens"), Number("total_tokens"),
                        Identifier(nested, "model"), Identifier(nested, "provider"), Identifier(nested, "deployment"),
                        Identifier(nested, "version"), Identifier(nested, "response_id"));
                    if (hasUsage || !nested.TryGetProperty("response", out var next)) return;
                    nested = next;
                }
                else if (nested.ValueKind != JsonValueKind.String)
                {
                    logger.LogDebug("VisionProbe Nested ParentResponseId={ParentResponseId} CallId={CallId} Level={Level} JsonKind={Kind}", parent, call, level, nested.ValueKind);
                    return;
                }
            }
            logger.LogDebug("VisionProbe Nested ParentResponseId={ParentResponseId} CallId={CallId} Diagnostic=DecodeLimitReached", parent, call);
        }
        finally { foreach (var document in documents) document.Dispose(); }
    }


    public void Complete(ResponseResult response)
    {
        if (!Observed) return;
        logger.LogDebug("VisionProbe Main ParentResponseId={ParentResponseId} DistinctVisionCallIds={CallCount} InputTokens={InputTokens} OutputTokens={OutputTokens} TotalTokens={TotalTokens} Model={Model}",
            response.Id, calls.Count, response.Usage?.InputTokenCount, response.Usage?.OutputTokenCount,
            response.Usage?.TotalTokenCount, response.Model);
    }
}

internal static class VisionJsonNumber
{
    public static bool TryGetInt64Safe(this JsonElement value, out long number)
    {
        number = 0;
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out number);
    }
}
