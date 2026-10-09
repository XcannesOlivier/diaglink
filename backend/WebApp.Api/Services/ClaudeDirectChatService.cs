using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using Azure.Core;
using Microsoft.Extensions.Options;
using WebApp.Api.Models;

namespace WebApp.Api.Services;

/// <summary>
/// Claude Messages orchestration used by the server-selected Claude Direct runtime.
/// Provider text deltas are streamed while every response is also reconstructed for tool round-trips.
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
    private static readonly string[] ValidTileIds =
    [
        "r01-c01", "r01-c02", "r01-c03",
        "r02-c01", "r02-c02", "r02-c03",
        "r03-c01", "r03-c02", "r03-c03"
    ];
    private static readonly Regex DocumentIdMarkerPattern = new(
        @"\bDocument\s+ID\s*:\s*(?<id>[A-Za-z0-9][A-Za-z0-9._-]{0,127})(?=\s|$)",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TokenCredential _credential;
    private readonly ITechnicalVisualBlobReader _blobReader;
    private readonly TechnicalSourceReferenceResolver _sourceReferenceResolver;
    private readonly ClaudeDirectChatOptions _options;
    private readonly ILogger<ClaudeDirectChatService> _logger;

    public ClaudeDirectChatService(
        IHttpClientFactory httpClientFactory,
        TokenCredential credential,
        ITechnicalVisualBlobReader blobReader,
        TechnicalSourceReferenceResolver sourceReferenceResolver,
        IOptions<ClaudeDirectChatOptions> options,
        ILogger<ClaudeDirectChatService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _credential = credential;
        _blobReader = blobReader;
        _sourceReferenceResolver = sourceReferenceResolver;
        _options = options.Value;
        _logger = logger;
    }

    public Task<ClaudeDirectChatResult> CompleteAsync(
        ClaudeDirectChatRequest request,
        CancellationToken cancellationToken = default) =>
        CompleteCoreAsync(request, null, aggregateAllStreamedText: false, cancellationToken);

    public async IAsyncEnumerable<ClaudeDirectChatStreamUpdate> StreamAsync(
        ClaudeDirectChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var channel = Channel.CreateUnbounded<ClaudeDirectChatStreamUpdate>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true
        });

        var producer = ProduceAsync();
        await foreach (var update in channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return update;
        }

        await producer;

        async Task ProduceAsync()
        {
            try
            {
                var result = await CompleteCoreAsync(
                    request,
                    (text, ct) => channel.Writer.WriteAsync(ClaudeDirectChatStreamUpdate.Text(text), ct),
                    aggregateAllStreamedText: true,
                    cancellationToken);
                await channel.Writer.WriteAsync(ClaudeDirectChatStreamUpdate.Completed(result), cancellationToken);
                channel.Writer.TryComplete();
            }
            catch (Exception exception)
            {
                channel.Writer.TryComplete(exception);
            }
        }
    }

    private async Task<ClaudeDirectChatResult> CompleteCoreAsync(
        ClaudeDirectChatRequest request,
        Func<string, CancellationToken, ValueTask>? onVisibleText,
        bool aggregateAllStreamedText,
        CancellationToken cancellationToken)
    {
        var usageTraceId = Guid.NewGuid().ToString("N")[..8];
        var toolUses = new List<ClaudeDirectToolUse>();
        var mcpCalls = new List<ClaudeDirectMcpCall>();
        var visuals = new List<TechnicalVisualReference>();
        var sources = new List<TechnicalSourceReference>();
        var documentResolutions = new List<ClaudeDirectDocumentResolution>();
        var calls = new List<ClaudeDirectCallUsage>();
        var allowedDocumentIds = new HashSet<string>(StringComparer.Ordinal);
        var fileSearchDocumentIds = new HashSet<string>(StringComparer.Ordinal);
        var webCitations = new Dictionary<string, ClaudeDirectWebCitation>(StringComparer.Ordinal);
        var errors = ValidateRequest(request);
        if (errors.Count > 0)
        {
            return BuildResult(null, toolUses, mcpCalls, visuals, sources, webCitations.Values, documentResolutions, calls, null, null, errors);
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
            return BuildResult(null, toolUses, mcpCalls, visuals, sources, webCitations.Values, documentResolutions, calls, null, null, errors);
        }

        var history = request.Messages
            .Select(message => new Dictionary<string, object?>
            {
                ["role"] = message.Role,
                ["content"] = BuildMessageContent(message)
            })
            .ToList();

        var client = _httpClientFactory.CreateClient(HttpClientName);
        string? finalText = null;
        string? finalModel = null;
        string? finalStopReason = null;
        // This is currently the absolute per-request Web Search budget. A request profile may select
        // one of the configured limits in the future without changing the cumulative enforcement below.
        var webSearchBudget = Math.Max(
            _options.WebSearch.DiagnosticMaxUses,
            _options.WebSearch.PartsMaxUses);
        long webSearchUsed = 0;
        var suggestionParser = new DiagLinkSuggestionStreamParser();
        var visibleText = new StringBuilder();

        async ValueTask AcceptTextDeltaAsync(string delta)
        {
            foreach (var part in suggestionParser.Push(delta))
            {
                visibleText.Append(part);
                if (onVisibleText is not null)
                {
                    await onVisibleText(part, cancellationToken);
                }
            }
        }

        async ValueTask CompleteVisibleTextAsync()
        {
            foreach (var part in suggestionParser.Complete())
            {
                visibleText.Append(part);
                if (onVisibleText is not null)
                {
                    await onVisibleText(part, cancellationToken);
                }
            }
        }

        for (var callNumber = 1; callNumber <= _options.MaxMessageCalls; callNumber++)
        {
            var webSearchRemaining = webSearchBudget - (int)webSearchUsed;
            using var httpRequest = BuildHttpRequest(
                request,
                history,
                accessToken.Token,
                webSearchRemaining);
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
                var streamedResponse = string.Equals(
                    httpResponse.Content.Headers.ContentType?.MediaType,
                    "text/event-stream",
                    StringComparison.OrdinalIgnoreCase);
                try
                {
                    await using var responseStream = await httpResponse.Content.ReadAsStreamAsync(cancellationToken);
                    responseDocument = streamedResponse
                        ? await ReadStreamingResponseAsync(
                            responseStream,
                            aggregateAllStreamedText ? AcceptTextDeltaAsync : static _ => ValueTask.CompletedTask,
                            cancellationToken)
                        : await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken);
                }
                catch (Exception exception) when (exception is JsonException or InvalidDataException)
                {
                    _logger.LogWarning(
                        "Claude direct Messages stream was invalid. CallNumber={CallNumber}, ErrorType={ErrorType}",
                        callNumber,
                        exception.GetType().Name);
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

                    LogUsageIterations(root, usageTraceId, callNumber);

                    var callUsage = usage!;
                    calls.Add(callUsage);
                    finalModel = callUsage.Model;
                    finalStopReason = callUsage.StopReason;

                    if (callUsage.WebSearchRequests > webSearchBudget - webSearchUsed)
                    {
                        errors.Add(new(
                            "web_search_budget_exceeded",
                            $"The cumulative Web Search usage exceeded the configured budget of {webSearchBudget} requests.",
                            callNumber));
                        break;
                    }

                    webSearchUsed += callUsage.WebSearchRequests;

                    if (!root.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
                    {
                        errors.Add(new("invalid_messages_response", "The Claude response has no content array.", callNumber));
                        break;
                    }

                    var callToolNames = ReadRequestedToolNames(content);
                    callUsage = callUsage with { Tools = callToolNames };
                    calls[^1] = callUsage;
                    _logger.LogInformation(
                        "ClaudeDirectUsage Trace={TraceId} Call={CallNumber} Input={InputTokens} Output={OutputTokens} CacheRead={CacheReadInputTokens} CacheCreate={CacheCreationInputTokens} CacheCreate5m={CacheCreation5mInputTokens} CacheCreate1h={CacheCreation1hInputTokens} WebSearchRequests={WebSearchRequests} Total={TotalTokens} Model={Model} StopReason={StopReason} Tools={Tools}",
                        usageTraceId,
                        callUsage.CallNumber,
                        callUsage.InputTokens,
                        callUsage.OutputTokens,
                        callUsage.CacheReadInputTokens,
                        callUsage.CacheCreationInputTokens,
                        callUsage.CacheCreation5mInputTokens,
                        callUsage.CacheCreation1hInputTokens,
                        callUsage.WebSearchRequests,
                        callUsage.InputTokens + callUsage.OutputTokens,
                        callUsage.Model,
                        callUsage.StopReason,
                        callToolNames.Length == 0 ? "none" : string.Join(',', callToolNames));

                    ExtractAllowedDocumentIds(content, allowedDocumentIds);
                    ExtractFileSearchDocumentIds(content, fileSearchDocumentIds);
                    ObserveBlocks(content, toolUses, mcpCalls);
                    var webSearchObservations = ObserveWebSearchBlocks(content);
                    var callWebCitations = ExtractWebCitations(content);
                    foreach (var citation in callWebCitations)
                    {
                        webCitations.TryAdd(citation.Url, citation);
                    }

                    if (webSearchObservations.Count > 0)
                    {
                        _logger.LogInformation(
                            "ClaudeDirectWebSearch Trace={TraceId} Call={CallNumber} Uses={Uses} Results={Results} Citations={Citations} UniqueCitations={UniqueCitations}",
                            usageTraceId,
                            callNumber,
                            webSearchObservations.Count(observation => observation.Type == "server_tool_use"),
                            webSearchObservations.Count(observation => observation.Type == "web_search_tool_result"),
                            callWebCitations.Count,
                            webCitations.Count);
                    }

                    var localCalls = ReadLocalToolCalls(content).ToArray();
                    if (string.Equals(callUsage.StopReason, "pause_turn", StringComparison.Ordinal))
                    {
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
                        continue;
                    }

                    if (localCalls.Length == 0)
                    {
                        if (!streamedResponse || !aggregateAllStreamedText)
                        {
                            await AcceptTextDeltaAsync(ReadText(content));
                        }

                        await CompleteVisibleTextAsync();
                        finalText = visibleText.ToString();
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

        var inputTokens = calls.Sum(call => call.InputTokens);
        var outputTokens = calls.Sum(call => call.OutputTokens);
        _logger.LogInformation(
            "ClaudeDirectUsage Trace={TraceId} FINAL Calls={CallCount} Input={InputTokens} Output={OutputTokens} Total={TotalTokens}",
            usageTraceId,
            calls.Count,
            inputTokens,
            outputTokens,
            inputTokens + outputTokens);

        try
        {
            sources.AddRange(await _sourceReferenceResolver.ResolveAsync(
                finalText,
                request.Machine.BlobPrefix,
                fileSearchDocumentIds,
                cancellationToken));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(
                exception,
                "Technical source references could not be resolved. Trace={TraceId}",
                usageTraceId);
        }

        return BuildResult(
            finalText,
            toolUses,
            mcpCalls,
            visuals,
            sources,
            webCitations.Values,
            documentResolutions,
            calls,
            finalModel,
            finalStopReason,
            errors) with
        {
            Suggestions = suggestionParser.Suggestions.ToArray()
        };
    }

    private HttpRequestMessage BuildHttpRequest(
        ClaudeDirectChatRequest request,
        List<Dictionary<string, object?>> history,
        string token,
        int webSearchRemaining)
    {
        var endpoint = new Uri(new Uri(_options.FoundryAnthropicEndpoint.TrimEnd('/') + "/"), "v1/messages");
        var tools = new List<object>
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
                    "Retourne à Claude une image PNG technique appartenant à la machine courante, après exploitation de File Search lorsqu’une inspection visuelle est nécessaire. " +
                    "Ne l’appelle pas en parallèle avec File Search ni pour reconfirmer une information déjà suffisamment établie. " +
                    "asset_type=\"full\" retourne la page complète : utilise-le pour le contexte général, l’organisation d’un schéma, la localisation d’une zone ou les relations entre éléments éloignés. " +
                    "asset_type=\"tile\" retourne une zone détaillée de la grille 3 × 3 : utilise une tuile pour lire un petit texte ou repère, examiner un connecteur, une broche ou un symbole, suivre précisément un conducteur ou un trajet, vérifier un détail graphique ou analyser une zone déjà localisée. " +
                    "Si File Search ou le contexte permet déjà de connaître précisément la zone à examiner, demande directement la tuile correspondante sans charger d’abord la page complète. " +
                    "Demande normalement une seule tuile lorsqu’elle suffit ; si un élément traverse plusieurs zones, demande uniquement les tuiles supplémentaires nécessaires. " +
                    "Les tuiles adjacentes se chevauchent de 25 % : ne demande pas automatiquement une tuile voisine pour un élément proche d’un bord. " +
                    "Lorsque File Search fournit clairement un Document ID, utilise exactement cet identifiant comme document_id et jamais le nom de la machine ; sinon, omets document_id ou utilise null afin que le backend le résolve parmi les documents autorisés.",
                ["input_schema"] = new Dictionary<string, object?>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object?>
                    {
                        ["document_id"] = new Dictionary<string, object?>
                        {
                            ["type"] = new[] { "string", "null" },
                            ["description"] =
                                "Document ID exact fourni clairement par File Search. Ne jamais utiliser le nom de la machine. Omettre ou utiliser null si File Search ne fournit pas clairement de Document ID."
                        },
                        ["page"] = new Dictionary<string, object?>
                        {
                            ["type"] = "integer",
                            ["minimum"] = 1,
                            ["maximum"] = 99999,
                            ["description"] = "Numéro de page correspondant aux assets d’image."
                        },
                        ["asset_type"] = new Dictionary<string, object?>
                        {
                            ["type"] = "string",
                            ["enum"] = new[] { "full", "tile" },
                            ["description"] = "full = image complète de la page ; tile = zone détaillée de la grille 3 × 3."
                        },
                        ["tile"] = new Dictionary<string, object?>
                        {
                            ["type"] = new[] { "string", "null" },
                            ["enum"] = ValidTileIds.Cast<object?>().Append(null).ToArray(),
                            ["description"] =
                                "Requis uniquement pour asset_type=\"tile\" ; doit être absent ou null pour asset_type=\"full\". " +
                                "r01/r02/r03 désignent les parties haute/centrale/basse et c01/c02/c03 les parties gauche/centrale/droite. " +
                                "Les zones adjacentes se chevauchent de 25 %."
                        }
                    },
                    ["required"] = new[] { "page", "asset_type" },
                    ["additionalProperties"] = false
                }
            }
        };

        if (_options.WebSearch.Enabled && webSearchRemaining > 0)
        {
            tools.Add(new Dictionary<string, object?>
            {
                ["type"] = "web_search_20250305",
                ["name"] = "web_search",
                ["max_uses"] = webSearchRemaining
            });
        }

        var payload = new Dictionary<string, object?>
        {
            ["model"] = _options.Deployment,
            ["max_tokens"] = 4096,
            ["stream"] = true,
            ["system"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["type"] = "text",
                    ["text"] = BuildSystemPrompt(request),
                    ["cache_control"] = new Dictionary<string, object?>
                    {
                        ["type"] = "ephemeral",
                        ["ttl"] = "5m"
                    }
                }
            },
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
            ["tools"] = tools
        };

        var message = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        message.Headers.Add("anthropic-version", AnthropicVersion);
        message.Headers.Add("anthropic-beta", McpBeta);
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        return message;
    }

    private static object BuildMessageContent(ClaudeDirectMessage message)
    {
        if (message.Images is not { Count: > 0 })
        {
            return message.Text;
        }

        var content = new List<object>
        {
            new Dictionary<string, object?>
            {
                ["type"] = "text",
                ["text"] = message.Text
            }
        };

        foreach (var image in message.Images)
        {
            content.Add(new Dictionary<string, object?>
            {
                ["type"] = "image",
                ["source"] = new Dictionary<string, object?>
                {
                    ["type"] = "base64",
                    ["media_type"] = image.MediaType,
                    ["data"] = image.Base64Data
                }
            });
        }

        return content;
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
            _options.MaxImageBytes is < 8 or > 20 * 1024 * 1024 ||
            _options.WebSearch is null ||
            _options.WebSearch.DiagnosticMaxUses < 0 ||
            _options.WebSearch.PartsMaxUses < 0)
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
            (assetType == "tile" && (tile is null || !ValidTileIds.Contains(tile, StringComparer.Ordinal))))
        {
            error = "tile must be null for full assets and one of r01-c01 through r03-c03 for tile assets.";
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
        var commercialInstructions = request.CommercialPolicy is { Enabled: true }
            ? request.CommercialPolicy.Instructions?.Trim() ?? string.Empty
            : string.Empty;
        var sections = new List<string>();

        if (!string.IsNullOrEmpty(prompt))
        {
            sections.Add(prompt);
        }

        if (!string.IsNullOrEmpty(machineDescription))
        {
            sections.Add($"Contexte machine fourni par le serveur :\n{machineDescription}");
        }

        if (!string.IsNullOrEmpty(commercialInstructions))
        {
            sections.Add($"Politique commerciale active fournie par le serveur :\n{commercialInstructions}");
        }

        return string.Join("\n\n", sections);
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

            ExtractDocumentIds(block, allowedDocumentIds);
        }
    }

    private static void ExtractFileSearchDocumentIds(
        JsonElement content,
        ISet<string> documentIds)
    {
        var fileSearchToolUseIds = content.EnumerateArray()
            .Where(block =>
                GetString(block, "type") == "mcp_tool_use" &&
                string.Equals(GetString(block, "name"), "file_search", StringComparison.OrdinalIgnoreCase))
            .Select(block => GetString(block, "id"))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var block in content.EnumerateArray())
        {
            if (GetString(block, "type") != "mcp_tool_result" ||
                GetBoolean(block, "is_error") == true ||
                !fileSearchToolUseIds.Contains(GetString(block, "tool_use_id")))
            {
                continue;
            }

            ExtractDocumentIds(block, documentIds);
        }
    }

    private static void ExtractDocumentIds(JsonElement element, ISet<string> documentIds) =>
        VisitStrings(element, text =>
        {
            foreach (Match match in DocumentIdMarkerPattern.Matches(text))
            {
                var documentId = match.Groups["id"].Value;
                if (DocumentIdPattern.IsMatch(documentId))
                {
                    documentIds.Add(documentId);
                }
            }
        });

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

    internal static IReadOnlyList<ClaudeDirectWebSearchObservation> ObserveWebSearchBlocks(JsonElement content)
    {
        var observations = new List<ClaudeDirectWebSearchObservation>();
        foreach (var block in content.EnumerateArray())
        {
            var type = GetString(block, "type");
            if (type == "server_tool_use" &&
                string.Equals(GetString(block, "name"), "web_search", StringComparison.Ordinal))
            {
                observations.Add(new(type, GetString(block, "id") ?? string.Empty));
            }
            else if (type == "web_search_tool_result")
            {
                observations.Add(new(type, GetString(block, "tool_use_id") ?? string.Empty));
            }
        }

        return observations;
    }

    internal static IReadOnlyList<ClaudeDirectWebCitation> ExtractWebCitations(JsonElement content)
    {
        var citations = new List<ClaudeDirectWebCitation>();
        var observedUrls = new HashSet<string>(StringComparer.Ordinal);
        foreach (var block in content.EnumerateArray())
        {
            if (GetString(block, "type") != "text" ||
                !block.TryGetProperty("citations", out var blockCitations) ||
                blockCitations.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var citation in blockCitations.EnumerateArray())
            {
                if (GetString(citation, "type") != "web_search_result_location" ||
                    !TryWebUri(GetString(citation, "url"), out var uri) ||
                    !observedUrls.Add(uri.AbsoluteUri))
                {
                    continue;
                }

                citations.Add(new(
                    uri.AbsoluteUri,
                    EmptyToNull(GetString(citation, "title")),
                    EmptyToNull(GetString(citation, "cited_text") ?? GetString(citation, "text"))));
            }
        }

        return citations;
    }

    private static string[] ReadRequestedToolNames(JsonElement content) => content
        .EnumerateArray()
        .Where(block =>
            GetString(block, "type") is "tool_use" or "mcp_tool_use" ||
            (GetString(block, "type") == "server_tool_use" &&
             string.Equals(GetString(block, "name"), "web_search", StringComparison.Ordinal)))
        .Select(block => GetString(block, "name"))
        .Where(name => !string.IsNullOrWhiteSpace(name))
        .Select(name => name!)
        .Distinct(StringComparer.Ordinal)
        .ToArray();

    private void LogUsageIterations(
    JsonElement root,
    string traceId,
    int callNumber)
{
    if (!root.TryGetProperty("usage", out var usage) ||
        !usage.TryGetProperty("iterations", out var iterations) ||
        iterations.ValueKind != JsonValueKind.Array)
    {
        _logger.LogInformation(
            "ClaudeDirectIterations Trace={TraceId} Call={CallNumber} Iterations=not_available",
            traceId,
            callNumber);
        return;
    }

    var index = 0;

    foreach (var iteration in iterations.EnumerateArray())
    {
        index++;

        var type = GetString(iteration, "type") ?? "unknown";

        var inputTokens =
            iteration.TryGetProperty("input_tokens", out var input) &&
            input.TryGetInt64(out var inputValue)
                ? inputValue
                : 0;

        var outputTokens =
            iteration.TryGetProperty("output_tokens", out var output) &&
            output.TryGetInt64(out var outputValue)
                ? outputValue
                : 0;

        var cacheReadTokens =
            iteration.TryGetProperty("cache_read_input_tokens", out var cacheRead) &&
            cacheRead.TryGetInt64(out var cacheReadValue)
                ? cacheReadValue
                : 0;

        var cacheCreationTokens =
            iteration.TryGetProperty("cache_creation_input_tokens", out var cacheCreation) &&
            cacheCreation.TryGetInt64(out var cacheCreationValue)
                ? cacheCreationValue
                : 0;

        _logger.LogInformation(
            "ClaudeDirectIteration Trace={TraceId} Call={CallNumber} Iteration={Iteration} Type={Type} Input={InputTokens} Output={OutputTokens} CacheRead={CacheReadTokens} CacheCreate={CacheCreationTokens}",
            traceId,
            callNumber,
            index,
            type,
            inputTokens,
            outputTokens,
            cacheReadTokens,
            cacheCreationTokens);
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
            !outputElement.TryGetInt64(out var outputTokens) ||
            inputTokens < 0 || outputTokens < 0 ||
            !TryReadOptionalTokenCount(usageElement, "cache_read_input_tokens", out var cacheReadTokens) ||
            !TryReadOptionalTokenCount(usageElement, "cache_creation_input_tokens", out var cacheCreationTokens) ||
            !TryReadWebSearchRequests(usageElement, out var webSearchRequests))
        {
            error = new("invalid_messages_response", "The Claude response is missing model, stop_reason, or usage.", callNumber);
            return false;
        }

        long cacheCreation5mTokens = 0;
        long cacheCreation1hTokens = 0;
        var hasCreationDetail = usageElement.TryGetProperty("cache_creation", out var cacheCreationDetail);
        if (hasCreationDetail)
        {
             if (cacheCreationDetail.ValueKind != JsonValueKind.Object ||
                 !TryReadOptionalTokenCount(
                 cacheCreationDetail,
             "ephemeral_5m_input_tokens",
            out cacheCreation5mTokens) ||
        !TryReadOptionalTokenCount(
            cacheCreationDetail,
            "ephemeral_1h_input_tokens",
            out cacheCreation1hTokens))
    {
        error = new(
            "invalid_messages_response",
            "The Claude response contains invalid cache creation usage.",
            callNumber);
        return false;
    }

    // Anthropic may expose an aggregate cache_creation_input_tokens value
    // without a matching TTL breakdown. The aggregate remains authoritative.
    if (cacheCreation5mTokens + cacheCreation1hTokens != cacheCreationTokens)
    {
        cacheCreation5mTokens = cacheCreationTokens;
        cacheCreation1hTokens = 0;
    }
}
else
{
    cacheCreation5mTokens = cacheCreationTokens;
}

        usage = new(
            callNumber,
            inputTokens,
            outputTokens,
            model,
            stopReason,
            GetString(root, "id"),
            requestId)
        {
            CacheReadInputTokens = cacheReadTokens,
            CacheCreationInputTokens = cacheCreationTokens,
            CacheCreation5mInputTokens = cacheCreation5mTokens,
            CacheCreation1hInputTokens = cacheCreation1hTokens,
            WebSearchRequests = webSearchRequests
        };
        return true;
    }

    private static bool TryReadOptionalTokenCount(JsonElement parent, string propertyName, out long value)
    {
        value = 0;
        return !parent.TryGetProperty(propertyName, out var element) ||
            (element.TryGetInt64(out value) && value >= 0);
    }

    private static bool TryReadWebSearchRequests(JsonElement usage, out long value)
    {
        value = 0;
        return !usage.TryGetProperty("server_tool_use", out var serverToolUse) ||
            (serverToolUse.ValueKind == JsonValueKind.Object &&
             TryReadOptionalTokenCount(serverToolUse, "web_search_requests", out value));
    }

    private static ClaudeDirectChatResult BuildResult(
        string? finalText,
        IReadOnlyList<ClaudeDirectToolUse> toolUses,
        IReadOnlyList<ClaudeDirectMcpCall> mcpCalls,
        IReadOnlyList<TechnicalVisualReference> visuals,
        IReadOnlyList<TechnicalSourceReference> sources,
        IEnumerable<ClaudeDirectWebCitation> webCitations,
        IReadOnlyList<ClaudeDirectDocumentResolution> documentResolutions,
        IReadOnlyList<ClaudeDirectCallUsage> calls,
        string? model,
        string? stopReason,
        IReadOnlyList<ClaudeDirectError> errors)
    {
        var inputTokens = calls.Sum(call => call.InputTokens);
        var outputTokens = calls.Sum(call => call.OutputTokens);
        var cacheReadTokens = calls.Sum(call => call.CacheReadInputTokens);
        var cacheCreationTokens = calls.Sum(call => call.CacheCreationInputTokens);
        var cacheCreation5mTokens = calls.Sum(call => call.CacheCreation5mInputTokens);
        var cacheCreation1hTokens = calls.Sum(call => call.CacheCreation1hInputTokens);
        var webSearchRequests = calls.Sum(call => call.WebSearchRequests);
        return new(
            finalText,
            toolUses.ToArray(),
            mcpCalls.ToArray(),
            visuals.DistinctBy(visual => visual.AssetKey).ToArray(),
            sources.ToArray(),
            documentResolutions.ToArray(),
            calls.ToArray(),
            new(inputTokens, outputTokens, checked(inputTokens + outputTokens))
            {
                CacheReadInputTokens = cacheReadTokens,
                CacheCreationInputTokens = cacheCreationTokens,
                CacheCreation5mInputTokens = cacheCreation5mTokens,
                CacheCreation1hInputTokens = cacheCreation1hTokens,
                WebSearchRequests = webSearchRequests
            },
            model,
            stopReason,
            errors.ToArray())
        {
            WebCitations = webCitations.ToArray()
        };
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

    private static async Task<JsonDocument> ReadStreamingResponseAsync(
        Stream responseStream,
        Func<string, ValueTask> onTextDelta,
        CancellationToken cancellationToken)
    {
        var accumulator = new AnthropicMessageStreamAccumulator();
        using var reader = new StreamReader(
            responseStream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            leaveOpen: true);
        var data = new StringBuilder();

        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                await ProcessEventAsync();
                break;
            }

            if (line.Length == 0)
            {
                await ProcessEventAsync();
                continue;
            }

            if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                if (data.Length > 0)
                {
                    data.Append('\n');
                }

                var value = line.AsSpan("data:".Length);
                if (!value.IsEmpty && value[0] == ' ')
                {
                    value = value[1..];
                }

                data.Append(value);
            }
        }

        return accumulator.BuildDocument();

        async ValueTask ProcessEventAsync()
        {
            if (data.Length == 0)
            {
                return;
            }

            using var document = JsonDocument.Parse(data.ToString());
            data.Clear();
            var textDelta = accumulator.Apply(document.RootElement);
            if (!string.IsNullOrEmpty(textDelta))
            {
                await onTextDelta(textDelta);
            }
        }
    }

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

    private static bool TryWebUri(string? value, out Uri uri) =>
        Uri.TryCreate(value, UriKind.Absolute, out uri!) &&
        (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
         string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase));

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

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

internal sealed record ClaudeDirectWebSearchObservation(string Type, string ToolUseId);
