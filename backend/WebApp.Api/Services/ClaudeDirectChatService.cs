using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Azure.Core;
using Microsoft.Extensions.Options;
using WebApp.Api.Models;

namespace WebApp.Api.Services;

/// <summary>
/// Non-streaming Claude Messages orchestration used by the server-selected Claude Direct runtime.
/// The SSE endpoint adapts its final result without exposing credentials or image bytes.
/// </summary>
public sealed class ClaudeDirectChatService : IClaudeDirectChatService
{
    public const string HttpClientName = "ClaudeDirectChat";
    private const string FoundryScope = "https://ai.azure.com/.default";
    private const string AnthropicVersion = "2023-06-01";
    private const string McpBeta = "mcp-client-2026-09-15";
    private const string McpServerName = "machine-manual";

    private static readonly Regex DocumentIdPattern = new(
        @"\A[A-Za-z0-9][A-Za-z0-9._-]{0,127}\z",
        RegexOptions.CultureInvariant);
    private static readonly Regex ToolboxPartPattern = new(
        @"\A[A-Za-z0-9][A-Za-z0-9._-]{0,127}\z",
        RegexOptions.CultureInvariant);
    private static readonly Regex TilePattern = new(
        @"\Ar\d{2}-c\d{2}\z",
        RegexOptions.CultureInvariant);
    private static readonly Regex DocumentIdMarkerPattern = new(
        @"\bDocument\s+ID\s*:\s*(?<id>[A-Za-z0-9][A-Za-z0-9._-]{0,127})(?=\s|$)",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TokenCredential _credential;
    private readonly ITechnicalVisualBlobReader _blobReader;
    private readonly ClaudeDirectChatOptions _options;
    private readonly ILogger<ClaudeDirectChatService> _logger;

    public ClaudeDirectChatService(
        IHttpClientFactory httpClientFactory,
        TokenCredential credential,
        ITechnicalVisualBlobReader blobReader,
        IOptions<ClaudeDirectChatOptions> options,
        ILogger<ClaudeDirectChatService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _credential = credential;
        _blobReader = blobReader;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ClaudeDirectChatResult> CompleteAsync(
        ClaudeDirectChatRequest request,
        CancellationToken cancellationToken = default)
    {
        var toolUses = new List<ClaudeDirectToolUse>();
        var mcpCalls = new List<ClaudeDirectMcpCall>();
        var visuals = new List<TechnicalVisualReference>();
        var documentResolutions = new List<ClaudeDirectDocumentResolution>();
        var calls = new List<ClaudeDirectCallUsage>();
        var allowedDocumentIds = new HashSet<string>(StringComparer.Ordinal);
        var errors = ValidateRequest(request);
        if (errors.Count > 0)
        {
            return BuildResult(null, toolUses, mcpCalls, visuals, documentResolutions, calls, null, null, errors);
        }

        AccessToken accessToken;
        try
        {
            accessToken = await _credential.GetTokenAsync(
                new TokenRequestContext([FoundryScope]),
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning("Claude direct authentication failed. ErrorType={ErrorType}", exception.GetType().Name);
            errors.Add(new("authentication_failed", "Unable to acquire the Foundry access token."));
            return BuildResult(null, toolUses, mcpCalls, visuals, documentResolutions, calls, null, null, errors);
        }

        var history = request.Messages
            .Select(message => new Dictionary<string, object?>
            {
                ["role"] = message.Role,
                ["content"] = message.Text
            })
            .ToList();

        var client = _httpClientFactory.CreateClient(HttpClientName);
        string? finalText = null;
        string? finalModel = null;
        string? finalStopReason = null;

        for (var callNumber = 1; callNumber <= _options.MaxMessageCalls; callNumber++)
        {
            using var httpRequest = BuildHttpRequest(request, history, accessToken.Token);
            HttpResponseMessage httpResponse;
            try
            {
                httpResponse = await client.SendAsync(
                    httpRequest,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogWarning(
                    "Claude direct Messages request failed. CallNumber={CallNumber}, ErrorType={ErrorType}",
                    callNumber,
                    exception.GetType().Name);
                errors.Add(new("messages_api_unavailable", "The Claude Messages API request failed.", callNumber));
                break;
            }

            using (httpResponse)
            {
                var requestId = ReadRequestId(httpResponse);
                if (!httpResponse.IsSuccessStatusCode)
                {
                    _logger.LogWarning(
                        "Claude direct Messages API returned an error. CallNumber={CallNumber}, StatusCode={StatusCode}, RequestId={RequestId}",
                        callNumber,
                        (int)httpResponse.StatusCode,
                        requestId);
                    errors.Add(new(
                        "messages_api_error",
                        $"The Claude Messages API returned HTTP {(int)httpResponse.StatusCode}.",
                        callNumber));
                    break;
                }

                JsonDocument responseDocument;
                try
                {
                    await using var responseStream = await httpResponse.Content.ReadAsStreamAsync(cancellationToken);
                    responseDocument = await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken);
                }
                catch (JsonException)
                {
                    errors.Add(new("invalid_messages_response", "The Claude Messages API returned invalid JSON.", callNumber));
                    break;
                }

                using (responseDocument)
                {
                    var root = responseDocument.RootElement;
                    if (!TryReadUsage(root, callNumber, requestId, out var usage, out var usageError))
                    {
                        errors.Add(usageError!);
                        break;
                    }

                    calls.Add(usage!);
                    finalModel = usage!.Model;
                    finalStopReason = usage.StopReason;

                    if (!root.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
                    {
                        errors.Add(new("invalid_messages_response", "The Claude response has no content array.", callNumber));
                        break;
                    }

                    ExtractAllowedDocumentIds(content, allowedDocumentIds);
                    ObserveBlocks(content, toolUses, mcpCalls);
                    var localCalls = ReadLocalToolCalls(content).ToArray();
                    if (localCalls.Length == 0)
                    {
                        finalText = ReadText(content);
                        break;
                    }

                    if (callNumber == _options.MaxMessageCalls)
                    {
                        errors.Add(new(
                            "tool_loop_limit",
                            $"The tool loop reached the configured limit of {_options.MaxMessageCalls} Messages calls.",
                            callNumber));
                        break;
                    }

                    history.Add(new Dictionary<string, object?>
                    {
                        ["role"] = "assistant",
                        ["content"] = content.Clone()
                    });

                    var toolResults = new List<object>();
                    foreach (var localCall in localCalls)
                    {
                        toolResults.Add(await ExecuteLocalToolAsync(
                            request.Machine,
                            localCall,
                            allowedDocumentIds,
                            visuals,
                            documentResolutions,
                            errors,
                            callNumber,
                            cancellationToken));
                    }

                    history.Add(new Dictionary<string, object?>
                    {
                        ["role"] = "user",
                        ["content"] = toolResults
                    });
                }
            }
        }

        return BuildResult(
            finalText,
            toolUses,
            mcpCalls,
            visuals,
            documentResolutions,
            calls,
            finalModel,
            finalStopReason,
            errors);
    }

    private HttpRequestMessage BuildHttpRequest(
        ClaudeDirectChatRequest request,
        List<Dictionary<string, object?>> history,
        string token)
    {
        var endpoint = new Uri(new Uri(_options.FoundryAnthropicEndpoint.TrimEnd('/') + "/"), "v1/messages");
        var payload = new Dictionary<string, object?>
        {
            ["model"] = _options.Deployment,
            ["max_tokens"] = 4096,
            ["system"] = BuildSystemPrompt(request),
            ["messages"] = history,
            ["tool_choice"] = new Dictionary<string, object?>
            {
                ["type"] = "auto",
                ["disable_parallel_tool_use"] = true
            },
            ["mcp_servers"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["type"] = "url",
                    ["url"] = request.Machine.McpEndpoint,
                    ["name"] = McpServerName,
                    ["authorization_token"] = token
                }
            },
            ["tools"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["type"] = "mcp_toolset",
                    ["mcp_server_name"] = McpServerName
                },
                new Dictionary<string, object?>
                {
                    ["name"] = "get_page_image",
                    ["description"] =
                        "Lit côté serveur une image PNG technique appartenant à la machine courante. " +
                        "Utilise ce tool après avoir reçu le résultat de File Search lorsqu’une inspection visuelle est nécessaire; " +
                        "ne l’appelle pas en parallèle avec File Search. document_id doit reprendre exactement l’identifiant du document " +
                        "retourné par File Search, généralement le nom de fichier sans extension, et non le nom de la machine.",
                    ["input_schema"] = new Dictionary<string, object?>
                    {
                        ["type"] = "object",
                        ["properties"] = new Dictionary<string, object?>
                        {
                            ["document_id"] = new Dictionary<string, object?>
                            {
                                ["type"] = new[] { "string", "null" },
                                ["description"] =
                                    "Identifiant exact retourné par File Search. Omettre si File Search ne l’expose pas clairement."
                            },
                            ["page"] = new Dictionary<string, object?> { ["type"] = "integer", ["minimum"] = 1 },
                            ["asset_type"] = new Dictionary<string, object?>
                            {
                                ["type"] = "string",
                                ["enum"] = new[] { "full", "tile" }
                            },
                            ["tile"] = new Dictionary<string, object?>
                            {
                                ["type"] = new[] { "string", "null" },
                                ["description"] = "Identifiant rNN-cNN requis uniquement pour asset_type=tile."
                            }
                        },
                        ["required"] = new[] { "page", "asset_type" },
                        ["additionalProperties"] = false
                    }
                }
            }
        };

        var message = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        message.Headers.Add("anthropic-version", AnthropicVersion);
        message.Headers.Add("anthropic-beta", McpBeta);
        return message;
    }

    private async Task<object> ExecuteLocalToolAsync(
        ClaudeDirectMachineContext machine,
        LocalToolCall call,
        IReadOnlySet<string> allowedDocumentIds,
        List<TechnicalVisualReference> visuals,
        List<ClaudeDirectDocumentResolution> documentResolutions,
        List<ClaudeDirectError> errors,
        int callNumber,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(call.Name, "get_page_image", StringComparison.Ordinal))
        {
            return ErrorToolResult(
                call.Id,
                AddToolError(errors, "unsupported_tool", $"Unsupported local tool '{call.Name}'.", callNumber, call.Id));
        }

        if (!TryReadImageRequest(call.Input, out var imageRequest, out var validationMessage))
        {
            documentResolutions.Add(new(
                GetString(call.Input, "document_id"),
                null,
                ClaudeDirectDocumentResolutionMode.NotFound));
            return ErrorToolResult(
                call.Id,
                AddToolError(errors, "invalid_image_request", validationMessage!, callNumber, call.Id));
        }

        ImageResolution resolution;
        try
        {
            resolution = await ResolveImageAsync(
                machine.BlobPrefix,
                imageRequest!,
                allowedDocumentIds,
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(
                "Claude direct image read failed. CallNumber={CallNumber}, ToolUseId={ToolUseId}, ErrorType={ErrorType}",
                callNumber,
                call.Id,
                exception.GetType().Name);
            documentResolutions.Add(new(
                imageRequest!.RequestedDocumentId,
                null,
                ClaudeDirectDocumentResolutionMode.NotFound));
            return ErrorToolResult(
                call.Id,
                AddToolError(errors, "blob_read_failed", "The requested page image could not be read.", callNumber, call.Id));
        }

        documentResolutions.Add(new(
            imageRequest!.RequestedDocumentId,
            resolution.Visual?.DocumentId,
            resolution.Mode));

        if (resolution.Stream is null || resolution.Visual is null)
        {
            var code = resolution.Mode == ClaudeDirectDocumentResolutionMode.Ambiguous
                ? "document_ambiguous"
                : "blob_not_found";
            var message = resolution.Mode == ClaudeDirectDocumentResolutionMode.Ambiguous
                ? "Several authorized documents contain this page image; document_id is required."
                : "The requested page image was not found in the authorized File Search documents.";
            return ErrorToolResult(
                call.Id,
                AddToolError(errors, code, message, callNumber, call.Id));
        }

        byte[] bytes;
        try
        {
            await using (resolution.Stream)
            {
                bytes = await ReadBoundedAsync(resolution.Stream, _options.MaxImageBytes, cancellationToken);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(
                "Claude direct image payload was rejected. CallNumber={CallNumber}, ToolUseId={ToolUseId}, ErrorType={ErrorType}",
                callNumber,
                call.Id,
                exception.GetType().Name);
            return ErrorToolResult(
                call.Id,
                AddToolError(errors, "invalid_image_payload", "The requested page image could not be prepared.", callNumber, call.Id));
        }

        ReadOnlySpan<byte> pngSignature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (bytes.Length < pngSignature.Length || !bytes.AsSpan(0, pngSignature.Length).SequenceEqual(pngSignature))
        {
            return ErrorToolResult(
                call.Id,
                AddToolError(errors, "invalid_png", "The requested asset is not a valid PNG image.", callNumber, call.Id));
        }

        var visual = resolution.Visual;
        visuals.Add(visual);
        MarkRecoveredImageErrors(errors);
        return new Dictionary<string, object?>
        {
            ["type"] = "tool_result",
            ["tool_use_id"] = call.Id,
            ["content"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["type"] = "text",
                    ["text"] = $"Image PNG réelle demandée : document {visual.DocumentId}, page {visual.Page}, asset {visual.AssetType}."
                },
                new Dictionary<string, object?>
                {
                    ["type"] = "image",
                    ["source"] = new Dictionary<string, object?>
                    {
                        ["type"] = "base64",
                        ["media_type"] = "image/png",
                        ["data"] = Convert.ToBase64String(bytes)
                    }
                }
            }
        };
    }

    private async Task<ImageResolution> ResolveImageAsync(
        string blobPrefix,
        ImageRequest request,
        IReadOnlySet<string> allowedDocumentIds,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.RequestedDocumentId) &&
            allowedDocumentIds.Contains(request.RequestedDocumentId))
        {
            var exactVisual = BuildVisual(request.RequestedDocumentId, request);
            var exactStream = await OpenVisualAsync(blobPrefix, exactVisual, cancellationToken);
            if (exactStream is not null)
            {
                return new(exactVisual, exactStream, ClaudeDirectDocumentResolutionMode.Exact);
            }
        }

        if (allowedDocumentIds.Count == 0)
        {
            return new(null, null, ClaudeDirectDocumentResolutionMode.NotFound);
        }

        if (allowedDocumentIds.Count == 1)
        {
            var documentId = allowedDocumentIds.Single();
            var visual = BuildVisual(documentId, request);
            var stream = await OpenVisualAsync(blobPrefix, visual, cancellationToken);
            return stream is null
                ? new(null, null, ClaudeDirectDocumentResolutionMode.NotFound)
                : new(visual, stream, ClaudeDirectDocumentResolutionMode.UniqueMcpDocument);
        }

        var matches = new List<(TechnicalVisualReference Visual, Stream Stream)>();
        foreach (var documentId in allowedDocumentIds.Order(StringComparer.Ordinal))
        {
            var visual = BuildVisual(documentId, request);
            var stream = await OpenVisualAsync(blobPrefix, visual, cancellationToken);
            if (stream is not null)
            {
                matches.Add((visual, stream));
            }
        }

        if (matches.Count == 1)
        {
            return new(
                matches[0].Visual,
                matches[0].Stream,
                ClaudeDirectDocumentResolutionMode.UniqueExistingPage);
        }

        foreach (var match in matches)
        {
            await match.Stream.DisposeAsync();
        }

        return new(
            null,
            null,
            matches.Count > 1
                ? ClaudeDirectDocumentResolutionMode.Ambiguous
                : ClaudeDirectDocumentResolutionMode.NotFound);
    }

    private async Task<Stream?> OpenVisualAsync(
        string blobPrefix,
        TechnicalVisualReference visual,
        CancellationToken cancellationToken)
    {
        if (!TechnicalVisualAccessService.TryBuildBlobName(blobPrefix, visual.AssetKey, out var blobName))
        {
            return null;
        }

        return await _blobReader.OpenReadAsync(blobName, cancellationToken);
    }

    private List<ClaudeDirectError> ValidateRequest(ClaudeDirectChatRequest? request)
    {
        var errors = new List<ClaudeDirectError>();
        if (request?.Machine is null || request.Messages is null)
        {
            errors.Add(new("invalid_request", "Machine context and messages are required."));
            return errors;
        }

        if (!TryHttpsUri(_options.FoundryAnthropicEndpoint, out var anthropicEndpoint) ||
            string.IsNullOrWhiteSpace(_options.Deployment) ||
            _options.MaxMessageCalls is < 1 or > 12 ||
            _options.MaxImageBytes is < 8 or > 20 * 1024 * 1024)
        {
            errors.Add(new("invalid_configuration", "ClaudeDirectChat configuration is invalid."));
            return errors;
        }

        var machine = request.Machine;
        if (!TryHttpsUri(machine.ProjectEndpoint, out var projectEndpoint) ||
            !TryHttpsUri(machine.McpEndpoint, out var mcpEndpoint) ||
            !string.Equals(projectEndpoint.Host, anthropicEndpoint.Host, StringComparison.OrdinalIgnoreCase) ||
            !ToolboxPartPattern.IsMatch(machine.ToolboxName ?? string.Empty) ||
            !ToolboxPartPattern.IsMatch(machine.ToolboxVersion ?? string.Empty) ||
            string.IsNullOrWhiteSpace(machine.VectorStoreId) ||
            !machine.VectorStoreId.StartsWith("vs_", StringComparison.Ordinal))
        {
            errors.Add(new("invalid_machine_context", "The server-side Foundry machine context is invalid."));
        }
        else
        {
            var expectedMcp = BuildExpectedMcpUri(projectEndpoint, machine.ToolboxName!, machine.ToolboxVersion!);
            if (!Uri.Compare(expectedMcp, mcpEndpoint, UriComponents.HttpRequestUrl, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase).Equals(0))
            {
                errors.Add(new("invalid_mcp_endpoint", "The MCP endpoint does not match the server-side project and Toolbox context."));
            }
        }

        if (!TechnicalVisualAccessService.TryBuildBlobName(machine.BlobPrefix, "probe.png", out _))
        {
            errors.Add(new("invalid_blob_prefix", "The server-side machine BlobPrefix is invalid."));
        }

        if (request.Messages.Count == 0 || request.Messages.Any(message =>
                message is null ||
                message.Role is not ("user" or "assistant") ||
                string.IsNullOrWhiteSpace(message.Text)))
        {
            errors.Add(new("invalid_messages", "At least one valid user/assistant message is required."));
        }

        return errors;
    }

    private static bool TryReadImageRequest(
        JsonElement input,
        out ImageRequest? request,
        out string? error)
    {
        request = null;
        error = null;

        var documentId = GetString(input, "document_id");
        var assetType = GetString(input, "asset_type");
        var tile = GetString(input, "tile");
        if (!input.TryGetProperty("page", out var pageElement) ||
            !pageElement.TryGetInt32(out var page) ||
            page is < 1 or > 99999)
        {
            error = "page must be an integer between 1 and 99999.";
            return false;
        }

        if (assetType is not ("full" or "tile"))
        {
            error = "asset_type must be full or tile.";
            return false;
        }

        if ((assetType == "full" && tile is not null) ||
            (assetType == "tile" && (tile is null || !TilePattern.IsMatch(tile))))
        {
            error = "tile must be null for full assets and match rNN-cNN for tile assets.";
            return false;
        }

        request = new(documentId, page, assetType, tile);
        return true;
    }

    private static TechnicalVisualReference BuildVisual(string documentId, ImageRequest request)
    {
        var pageSegment = $"page-{request.Page:00000}";
        var suffix = request.AssetType == "full" ? "full" : $"tile-{request.Tile}";
        var name = $"{documentId}_{pageSegment}-{suffix}.png";
        var assetKey = $"{documentId}/{pageSegment}/{name}";
        return new(documentId, request.Page, request.AssetType, request.Tile, name, assetKey);
    }

    private static Uri BuildExpectedMcpUri(Uri projectEndpoint, string toolboxName, string toolboxVersion)
    {
        var baseUri = new Uri(projectEndpoint.AbsoluteUri.TrimEnd('/') + "/");
        return new Uri(
            baseUri,
            $"toolboxes/{Uri.EscapeDataString(toolboxName)}/versions/{Uri.EscapeDataString(toolboxVersion)}/mcp?api-version=v1");
    }

    private static string BuildSystemPrompt(ClaudeDirectChatRequest request)
    {
        var prompt = request.SystemPrompt?.Trim() ?? string.Empty;
        var machineDescription = request.Machine.Description?.Trim() ?? string.Empty;
        return string.IsNullOrEmpty(machineDescription)
            ? prompt
            : $"{prompt}\n\nContexte machine fourni par le serveur :\n{machineDescription}";
    }

    internal static void ExtractAllowedDocumentIds(
        JsonElement content,
        ISet<string> allowedDocumentIds)
    {
        foreach (var block in content.EnumerateArray())
        {
            if (GetString(block, "type") != "mcp_tool_result" || GetBoolean(block, "is_error") == true)
            {
                continue;
            }

            VisitStrings(block, text =>
            {
                foreach (Match match in DocumentIdMarkerPattern.Matches(text))
                {
                    var documentId = match.Groups["id"].Value;
                    if (DocumentIdPattern.IsMatch(documentId))
                    {
                        allowedDocumentIds.Add(documentId);
                    }
                }
            });
        }
    }

    private static void VisitStrings(JsonElement element, Action<string> visitor)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                visitor(element.GetString() ?? string.Empty);
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    VisitStrings(item, visitor);
                }
                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    VisitStrings(property.Value, visitor);
                }
                break;
        }
    }

    private static void ObserveBlocks(
        JsonElement content,
        List<ClaudeDirectToolUse> toolUses,
        List<ClaudeDirectMcpCall> mcpCalls)
    {
        foreach (var block in content.EnumerateArray())
        {
            var type = GetString(block, "type");
            if (type == "tool_use")
            {
                toolUses.Add(new(
                    GetString(block, "id") ?? string.Empty,
                    GetString(block, "name") ?? string.Empty,
                    block.TryGetProperty("input", out var input) ? input.GetRawText() : "{}"));
            }
            else if (type == "mcp_tool_use")
            {
                mcpCalls.Add(new(
                    GetString(block, "id") ?? string.Empty,
                    GetString(block, "name") ?? string.Empty,
                    null));
            }
            else if (type == "mcp_tool_result")
            {
                mcpCalls.Add(new(
                    GetString(block, "tool_use_id") ?? string.Empty,
                    "mcp_tool_result",
                    GetBoolean(block, "is_error")));
            }
        }
    }

    private static IEnumerable<LocalToolCall> ReadLocalToolCalls(JsonElement content)
    {
        foreach (var block in content.EnumerateArray())
        {
            if (GetString(block, "type") == "tool_use" &&
                block.TryGetProperty("input", out var input))
            {
                yield return new(
                    GetString(block, "id") ?? string.Empty,
                    GetString(block, "name") ?? string.Empty,
                    input.Clone());
            }
        }
    }

    private static bool TryReadUsage(
        JsonElement root,
        int callNumber,
        string? requestId,
        out ClaudeDirectCallUsage? usage,
        out ClaudeDirectError? error)
    {
        usage = null;
        error = null;
        var model = GetString(root, "model");
        var stopReason = GetString(root, "stop_reason");
        if (string.IsNullOrWhiteSpace(model) ||
            string.IsNullOrWhiteSpace(stopReason) ||
            !root.TryGetProperty("usage", out var usageElement) ||
            !usageElement.TryGetProperty("input_tokens", out var inputElement) ||
            !inputElement.TryGetInt64(out var inputTokens) ||
            !usageElement.TryGetProperty("output_tokens", out var outputElement) ||
            !outputElement.TryGetInt64(out var outputTokens))
        {
            error = new("invalid_messages_response", "The Claude response is missing model, stop_reason, or usage.", callNumber);
            return false;
        }

        usage = new(
            callNumber,
            inputTokens,
            outputTokens,
            model,
            stopReason,
            GetString(root, "id"),
            requestId);
        return true;
    }

    private static ClaudeDirectChatResult BuildResult(
        string? finalText,
        IReadOnlyList<ClaudeDirectToolUse> toolUses,
        IReadOnlyList<ClaudeDirectMcpCall> mcpCalls,
        IReadOnlyList<TechnicalVisualReference> visuals,
        IReadOnlyList<ClaudeDirectDocumentResolution> documentResolutions,
        IReadOnlyList<ClaudeDirectCallUsage> calls,
        string? model,
        string? stopReason,
        IReadOnlyList<ClaudeDirectError> errors)
    {
        var inputTokens = calls.Sum(call => call.InputTokens);
        var outputTokens = calls.Sum(call => call.OutputTokens);
        return new(
            finalText,
            toolUses.ToArray(),
            mcpCalls.ToArray(),
            visuals.DistinctBy(visual => visual.AssetKey).ToArray(),
            documentResolutions.ToArray(),
            calls.ToArray(),
            new(inputTokens, outputTokens, checked(inputTokens + outputTokens)),
            model,
            stopReason,
            errors.ToArray());
    }

    private static object ErrorToolResult(string toolUseId, ClaudeDirectError error) =>
        new Dictionary<string, object?>
        {
            ["type"] = "tool_result",
            ["tool_use_id"] = toolUseId,
            ["is_error"] = true,
            ["content"] = error.Message
        };

    private static ClaudeDirectError AddToolError(
        List<ClaudeDirectError> errors,
        string code,
        string message,
        int callNumber,
        string toolUseId)
    {
        var error = new ClaudeDirectError(code, message, callNumber, toolUseId);
        errors.Add(error);
        return error;
    }

    private static void MarkRecoveredImageErrors(List<ClaudeDirectError> errors)
    {
        for (var index = 0; index < errors.Count; index++)
        {
            if (!errors[index].Recovered && errors[index].Code is
                "invalid_image_request" or
                "blob_not_found" or
                "blob_read_failed" or
                "invalid_png" or
                "invalid_image_payload")
            {
                errors[index] = errors[index] with { Recovered = true };
            }
        }
    }

    private static async Task<byte[]> ReadBoundedAsync(
        Stream stream,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        while (true)
        {
            var read = await stream.ReadAsync(chunk.AsMemory(), cancellationToken);
            if (read == 0)
            {
                return buffer.ToArray();
            }

            if (buffer.Length + read > maxBytes)
            {
                throw new InvalidDataException($"The PNG exceeds the configured limit of {maxBytes} bytes.");
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }
    }

    private static string ReadText(JsonElement content) =>
        string.Join(
            Environment.NewLine,
            content.EnumerateArray()
                .Where(block => GetString(block, "type") == "text")
                .Select(block => GetString(block, "text"))
                .Where(text => !string.IsNullOrWhiteSpace(text)));

    private static string? ReadRequestId(HttpResponseMessage response)
    {
        foreach (var header in new[] { "request-id", "x-request-id", "apim-request-id" })
        {
            if (response.Headers.TryGetValues(header, out var values))
            {
                return values.FirstOrDefault();
            }
        }

        return null;
    }

    private static bool TryHttpsUri(string? value, out Uri uri) =>
        Uri.TryCreate(value, UriKind.Absolute, out uri!) && uri.Scheme == Uri.UriSchemeHttps;

    private static string? GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static bool? GetBoolean(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) &&
        property.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? property.GetBoolean()
            : null;

    private sealed record LocalToolCall(string Id, string Name, JsonElement Input);
    private sealed record ImageRequest(
        string? RequestedDocumentId,
        int Page,
        string AssetType,
        string? Tile);
    private sealed record ImageResolution(
        TechnicalVisualReference? Visual,
        Stream? Stream,
        ClaudeDirectDocumentResolutionMode Mode);
}
