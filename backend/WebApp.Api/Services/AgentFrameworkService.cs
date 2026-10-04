using Azure.AI.Projects;
using Azure.AI.Projects.Agents;
using Azure.AI.Extensions.OpenAI;
using Azure.Core;
using Azure.Identity;
using OpenAI.Files;
using OpenAI.Responses;
using Microsoft.Identity.Client;
using Microsoft.Identity.Web;
using System.Runtime.CompilerServices;
using System.Text;
using WebApp.Api.Models;
using WebApp.Api.Models.Entities;

namespace WebApp.Api.Services;

#pragma warning disable OPENAI001

/// <summary>
/// Foundry Agent Service using v2 Agents API.
/// </summary>
/// <remarks>
/// Uses AIProjectClient directly (Azure.AI.Projects GA): AgentAdministrationClient for agent
/// metadata and ProjectResponsesClient for streaming (required for annotations, MCP approvals).
/// See .github/skills/researching-azure-ai-sdk/SKILL.md for SDK patterns.
/// </remarks>
public class AgentFrameworkService : IDisposable
{
    private readonly string _agentEndpoint;
    private readonly string _agentId;
    private readonly AiPricingIdentityResolver _pricingIdentityResolver;
    /// <summary>
    /// Optional concrete agent version id (e.g. "3") from <c>AI_AGENT_VERSION</c>.
    /// When set, the agent is pinned to that immutable version for both metadata
    /// (<see cref="GetAgentAsync"/>) and streaming (<c>AgentReference</c> passed to
    /// <c>ProjectResponsesClient</c>). When null, the newest published version is
    /// resolved on startup and used consistently. Foundry retains all published
    /// versions, so pinning is useful for reproducibility across deployments.
    /// </summary>
    private readonly string? _configuredAgentVersion;
    private readonly ILogger<AgentFrameworkService> _logger;
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private readonly string? _backendClientId;
    private readonly string? _tenantId;
    private readonly string? _managedIdentityClientId;
    private readonly bool _useObo;
    private readonly TokenCredential _fallbackCredential;

    // Agent metadata caches, keyed by BuildAgentCacheKey(projectEndpoint, agentId, version). Never a
    // single shared slot, so resolving Machine A's agent can never leak into a response for Machine B.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, ProjectsAgentVersion> s_cachedAgentVersions = new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, AgentMetadataResponse> s_cachedMetadataByKey = new();
    private static readonly SemaphoreSlim s_agentLock = new(1, 1);
    // MI assertion cache (static - user-independent, safe to share across requests)
    private static ManagedIdentityClientAssertion? s_miAssertion;
    // AIProjectClient cache keyed by project endpoint. MI mode shares one client per endpoint across
    // requests; OBO mode caches per endpoint for the lifetime of this scoped/per-request instance only.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, AIProjectClient> s_miProjectClientsByEndpoint = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, AIProjectClient> _oboProjectClientsByEndpoint = new();

    private readonly IHttpClientFactory _httpClientFactory;

    /// <summary>
    /// Prefix applied to image files this web app uploads to the Foundry Files API,
    /// used by the cleanup endpoint to scope deletes to files owned by this app.
    /// </summary>
    public const string WebAppUploadFilenamePrefix = "webapp-upload-";

    private bool _disposed = false;

    public AgentFrameworkService(
        IConfiguration configuration,
        ILogger<AgentFrameworkService> logger,
        IHttpClientFactory httpClientFactory,
        AiPricingIdentityResolver pricingIdentityResolver,
        IHttpContextAccessor? httpContextAccessor = null)
    {
        _logger = logger;
        _pricingIdentityResolver = pricingIdentityResolver;
        _httpClientFactory = httpClientFactory;
        _httpContextAccessor = httpContextAccessor;

        _agentEndpoint = configuration["AI_AGENT_ENDPOINT"]
            ?? throw new InvalidOperationException("AI_AGENT_ENDPOINT is not configured");

        _agentId = configuration["AI_AGENT_ID"]
            ?? throw new InvalidOperationException("AI_AGENT_ID is not configured");

        _configuredAgentVersion = string.IsNullOrWhiteSpace(configuration["AI_AGENT_VERSION"])
            ? null
            : configuration["AI_AGENT_VERSION"];

        _logger.LogDebug(
            "Initializing AgentFrameworkService: endpoint={Endpoint}, agentId={AgentId}, version={Version}", 
            _agentEndpoint, 
            _agentId,
            _configuredAgentVersion ?? "<latest>");

        _backendClientId = configuration["ENTRA_BACKEND_CLIENT_ID"];
        _tenantId = configuration["ENTRA_TENANT_ID"] ?? configuration["AzureAd:TenantId"];
        // User-assigned MI client ID — used for MI-only mode and as FIC assertion in OBO mode
        _managedIdentityClientId = configuration["MANAGED_IDENTITY_CLIENT_ID"]
            ?? configuration["OBO_MANAGED_IDENTITY_CLIENT_ID"]; // backward compat

        var environment = configuration["ASPNETCORE_ENVIRONMENT"] ?? "Production";

        // Determine if OBO is available
        _useObo = !string.IsNullOrEmpty(_backendClientId)
                  && !string.IsNullOrEmpty(_tenantId)
                  && environment != "Development";

        // Create credential for non-OBO operations (agent metadata cache, MI-only mode)
        if (environment == "Development")
        {
            _logger.LogInformation("Development: Using ChainedTokenCredential (AzureCli -> AzureDeveloperCli)");
            _fallbackCredential = new ChainedTokenCredential(
                new AzureCliCredential(),
                new AzureDeveloperCliCredential()
            );
        }
        else if (!string.IsNullOrEmpty(_managedIdentityClientId))
        {
            _logger.LogInformation("Production: Using user-assigned ManagedIdentityCredential: {MiClientId}", _managedIdentityClientId);
            _fallbackCredential = new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(_managedIdentityClientId));
        }
        else
        {
            _logger.LogInformation("Production: Using ManagedIdentityCredential (system-assigned)");
            _fallbackCredential = new ManagedIdentityCredential(ManagedIdentityId.SystemAssigned);
        }

        if (_useObo)
        {
            if (string.IsNullOrEmpty(_managedIdentityClientId))
            {
                throw new InvalidOperationException(
                    "OBO mode requires MANAGED_IDENTITY_CLIENT_ID to be set for the FIC assertion. " +
                    "This is the user-assigned managed identity that acts as the federated credential.");
            }
            _logger.LogInformation("OBO mode enabled: backendClientId={BackendClientId}. All API calls use user-delegated identity.", _backendClientId);

            // Initialize MI assertion eagerly — avoids thread-safety issues with lazy init
            // in CreateOboCredential(). Safe here because the constructor runs once per scoped instance.
            s_miAssertion ??= new ManagedIdentityClientAssertion(managedIdentityClientId: _managedIdentityClientId);

            // Project clients are created lazily per endpoint by GetProjectClient(endpoint).
        }
        else
        {
            _logger.LogInformation("MI mode: using managed identity for all API calls");
        }

        _logger.LogInformation("AIProjectClient initialized successfully");
    }

    /// <summary>
    /// Get AIProjectClient for the given project endpoint — OBO mode creates per-request with the
    /// user's identity, MI mode uses a client cached per endpoint. Keyed by endpoint (not a single
    /// slot) so different machines' MachineAssistantConfiguration.ProjectEndpoint never collide.
    /// </summary>
    private AIProjectClient GetProjectClient(string projectEndpoint)
    {
        if (!_useObo)
        {
            return s_miProjectClientsByEndpoint.GetOrAdd(
                projectEndpoint,
                endpoint => new AIProjectClient(new Uri(endpoint), _fallbackCredential));
        }

        return _oboProjectClientsByEndpoint.GetOrAdd(projectEndpoint, endpoint =>
        {
            var userToken = ExtractBearerToken();
            if (string.IsNullOrEmpty(userToken))
            {
                throw new InvalidOperationException(
                    "OBO mode requires a bearer token but none was found in the request. " +
                    "Ensure the frontend is sending an Authorization header with a valid token.");
            }

            var oboCredential = CreateOboCredential(userToken);
            _logger.LogDebug("Created OBO credential for request (endpoint={Endpoint})", endpoint);
            return new AIProjectClient(new Uri(endpoint), oboCredential);
        });
    }

    /// <summary>Legacy overload — resolves the client for the globally-configured (env var) agent endpoint.</summary>
    private AIProjectClient GetProjectClient() => GetProjectClient(_agentEndpoint);

    /// <summary>
    /// Create OBO credential using the user's JWT and managed identity FIC assertion.
    /// </summary>
    private OnBehalfOfCredential CreateOboCredential(string userToken)
    {
        // s_miAssertion is initialized eagerly in the constructor (OBO branch)
        Func<CancellationToken, Task<string>> assertionCallback =
            async (ct) => await s_miAssertion!.GetSignedAssertionAsync(
                new AssertionRequestOptions { CancellationToken = ct });

        return new OnBehalfOfCredential(
            _tenantId!,
            _backendClientId!,
            assertionCallback,
            userToken,
            new OnBehalfOfCredentialOptions());
    }

    /// <summary>
    /// Extract bearer token from the current HTTP request.
    /// </summary>
    private string? ExtractBearerToken()
    {
        var authHeader = _httpContextAccessor?.HttpContext?.Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return null;

        return authHeader["Bearer ".Length..].Trim();
    }

    /// <summary>
    /// Load the agent version metadata via AgentAdministrationClient (v2 Agents API).
    /// When <paramref name="assistantConfig"/> is supplied (machine-scoped chat), resolves that
    /// machine's ProjectEndpoint/AgentId/AgentVersion; otherwise falls back to the legacy
    /// globally-configured (env var) agent for backward compatibility with pre-existing conversations.
    /// Cached per (endpoint, agentId, version) key — never a single shared slot — so resolving
    /// Machine A's agent can never be returned for a Machine B request.
    /// </summary>
    private async Task<ProjectsAgentVersion> GetAgentAsync(ResolvedAssistantConfiguration? assistantConfig, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var effectiveEndpoint = assistantConfig?.ProjectEndpoint ?? _agentEndpoint;
        var effectiveAgentId = assistantConfig?.AgentId ?? _agentId;
        var effectiveVersion = assistantConfig?.AgentVersion ?? _configuredAgentVersion;
        var cacheKey = BuildAgentCacheKey(effectiveEndpoint, effectiveAgentId, effectiveVersion);

        if (s_cachedAgentVersions.TryGetValue(cacheKey, out var cachedVersion))
            return cachedVersion;

        await s_agentLock.WaitAsync(cancellationToken);
        try
        {
            if (s_cachedAgentVersions.TryGetValue(cacheKey, out cachedVersion))
                return cachedVersion;

            // Use the same credential path as all other operations (MI or OBO)
            var client = GetProjectClient(effectiveEndpoint);

            ProjectsAgentVersion? loaded;
            if (!string.IsNullOrWhiteSpace(effectiveVersion))
            {
                _logger.LogInformation("Loading agent: {AgentId} version={Version}", effectiveAgentId, effectiveVersion);
                var response = await client.AgentAdministrationClient.GetAgentVersionAsync(
                    effectiveAgentId,
                    effectiveVersion!,
                    cancellationToken);
                loaded = response.Value;
            }
            else
            {
                _logger.LogInformation("Loading agent: {AgentId} version=<latest>", effectiveAgentId);
                loaded = null;
                await foreach (var v in client.AgentAdministrationClient.GetAgentVersionsAsync(
                    agentName: effectiveAgentId,
                    limit: 1,
                    order: AgentListOrder.Descending,
                    after: null,
                    before: null,
                    cancellationToken: cancellationToken))
                {
                    loaded = v;
                    break;
                }

                if (loaded is null)
                {
                    throw new InvalidOperationException(
                        $"Agent '{effectiveAgentId}' has no versions. Create at least one version in AI Foundry.");
                }
            }

            s_cachedAgentVersions[cacheKey] = loaded;

            var definition = loaded.Definition as DeclarativeAgentDefinition;

            _logger.LogInformation(
                "Loaded agent: name={AgentName}, model={Model}, version={Version} (pinned={Pinned})",
                loaded.Name ?? effectiveAgentId,
                definition?.Model ?? "unknown",
                loaded.Version ?? "<unknown>",
                !string.IsNullOrWhiteSpace(effectiveVersion));

            // Log StructuredInputs at debug level for troubleshooting
            if (definition?.StructuredInputs != null && definition.StructuredInputs.Count > 0)
            {
                _logger.LogDebug("Agent has {Count} StructuredInputs: {Keys}",
                    definition.StructuredInputs.Count,
                    string.Join(", ", definition.StructuredInputs.Keys));
            }

            return loaded;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load agent: {AgentId}", effectiveAgentId);
            throw;
        }
        finally
        {
            s_agentLock.Release();
        }
    }

    /// <summary>Cache key for the per-(endpoint,agentId,version) agent/metadata caches.</summary>
    private static string BuildAgentCacheKey(string projectEndpoint, string agentId, string? version)
        => $"{projectEndpoint}|{agentId}|{version ?? "latest"}";

    /// <summary>
    /// Streams agent response for a message using ProjectResponsesClient (Responses API).
    /// Returns StreamChunk objects containing text deltas, annotations, or MCP approval requests.
    /// </summary>
    /// <remarks>
    /// Stateless by design: SQL (see <see cref="WebApp.Api.Services.ConversationContextBuilder"/> and
    /// <see cref="WebApp.Api.Program"/>) is the sole source of conversational memory. No
    /// ProjectConversation is created or bound here — <paramref name="conversationId"/> is kept only
    /// for log correlation with the SQL-side conversation, never passed to the Responses API.
    /// Uses direct ProjectResponsesClient instead of IChatClient because we need access to:
    /// - McpToolCallApprovalRequestItem for MCP approval flows
    /// - FileSearchCallResponseItem for file search quotes  
    /// - MessageResponseItem.OutputTextAnnotations for citations
    /// The IChatClient abstraction doesn't expose these specialized response types.
    /// </remarks>
    public async IAsyncEnumerable<StreamChunk> StreamMessageAsync(
        string conversationId,
        string message,
        List<string>? imageDataUris = null,
        List<FileAttachment>? fileDataUris = null,
        string? previousResponseId = null,
        McpApprovalResponse? mcpApproval = null,
        ResolvedAssistantConfiguration? assistantConfig = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _logger.LogInformation(
            "Streaming message to conversation: {ConversationId}, ImageCount: {ImageCount}, FileCount: {FileCount}, HasApproval: {HasApproval}",
            conversationId,
            imageDataUris?.Count ?? 0,
            fileDataUris?.Count ?? 0,
            mcpApproval != null);

        CreateResponseOptions options = new() { StreamingEnabled = true };

        var effectiveAgentId = assistantConfig?.AgentId ?? _agentId;

        // Resolve the concrete agent version up front so streaming and metadata use the same version.
        var resolvedAgent = await GetAgentAsync(assistantConfig, cancellationToken);
        var resolvedVersion = (assistantConfig?.AgentVersion ?? _configuredAgentVersion) ?? resolvedAgent.Version;

        // Stateless call — no conversation binding. SQL is the memory; Foundry only sees what's in
        // options.InputItems for this single call.
        ProjectResponsesClient responsesClient
            = GetProjectClient(assistantConfig?.ProjectEndpoint ?? _agentEndpoint).ProjectOpenAIClient.GetProjectResponsesClientForAgent(
                new AgentReference(effectiveAgentId, resolvedVersion));

        // If continuing from MCP approval, add approval response items.
        // PreviousResponseId here is a short-lived pointer to the SAME tool/MCP exchange only —
        // not a substitute for conversational memory, which lives entirely in SQL.
        if (!string.IsNullOrEmpty(previousResponseId) && mcpApproval != null)
        {
            options.PreviousResponseId = previousResponseId;
            options.InputItems.Add(ResponseItem.CreateMcpApprovalResponseItem(
                mcpApproval.ApprovalRequestId,
                mcpApproval.Approved));
            
            _logger.LogInformation(
                "Resuming with MCP approval: RequestId={RequestId}, Approved={Approved}",
                mcpApproval.ApprovalRequestId,
                mcpApproval.Approved);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                _logger.LogWarning("Attempted to stream empty message to conversation {ConversationId}", conversationId);
                throw new ArgumentException("Message cannot be null or whitespace", nameof(message));
            }

            // Build user message with optional images and files
            ResponseItem userMessage = await BuildUserMessageAsync(message, imageDataUris, fileDataUris, cancellationToken);
            options.InputItems.Add(userMessage);
        }

        // Dictionary to collect file search results for quote extraction
        var fileSearchQuotes = new Dictionary<string, string>();
        // Track the current response ID for MCP approval resume flow
        string? currentResponseId = null;
        var visionDiagnostics = new VisionToolDiagnostics(_logger);
        var definitionModel = (resolvedAgent.Definition as DeclarativeAgentDefinition)?.Model;
        var pricingIdentity = _pricingIdentityResolver.Resolve(
            assistantConfig?.ProjectEndpoint ?? _agentEndpoint, effectiveAgentId, resolvedVersion, definitionModel);
        // Initial event makes interrupted streams explicitly unknown, while retaining agent identity.
        yield return new StreamChunk { Usage = pricingIdentity.ApplyTo(new AiResponseUsage(AiUsageType.ChatResponse, null, false,
            null, null, null, definitionModel, "agentDefinition", resolvedVersion, DateTimeOffset.UtcNow)) };

        await foreach (StreamingResponseUpdate update
            in responsesClient.CreateResponseStreamingAsync(
                options: options,
                cancellationToken: cancellationToken))
        {
            // Capture response ID from created event (needed for MCP approval resume)
            if (update is StreamingResponseCreatedUpdate createdUpdate)
            {
                currentResponseId = createdUpdate.Response.Id;
                yield return new StreamChunk { Usage = pricingIdentity.ApplyTo(new AiResponseUsage(AiUsageType.ChatResponse, currentResponseId, false,
                    null, null, null, definitionModel, "agentDefinition", resolvedVersion, DateTimeOffset.UtcNow)) };
                _logger.LogDebug("Response created: {ResponseId}", currentResponseId);
                continue;
            }

            if (update is StreamingResponseOutputTextDeltaUpdate deltaUpdate)
            {
                yield return StreamChunk.Text(deltaUpdate.Delta);
            }
            else if (update is StreamingResponseOutputItemDoneUpdate itemDoneUpdate)
            {
                foreach (var visionChunk in CreateVisionChunks(visionDiagnostics, itemDoneUpdate.Item, "done", currentResponseId))
                    yield return visionChunk;
                // Check for MCP tool approval request
                if (itemDoneUpdate.Item is McpToolCallApprovalRequestItem mcpApprovalItem)
                {
                    _logger.LogInformation(
                        "MCP tool approval requested: Id={Id}, Tool={Tool}, Server={Server}",
                        mcpApprovalItem.Id,
                        mcpApprovalItem.ToolName,
                        mcpApprovalItem.ServerLabel);
                    
                    // Parse tool arguments from BinaryData to string (JSON)
                    string? argumentsJson = mcpApprovalItem.ToolArguments?.ToString();
                    
                    yield return StreamChunk.McpApproval(new McpApprovalRequest
                    {
                        Id = mcpApprovalItem.Id,
                        ToolName = mcpApprovalItem.ToolName ?? "Unknown tool",
                        ServerLabel = mcpApprovalItem.ServerLabel ?? "MCP Server",
                        Arguments = argumentsJson,
                        PreviousResponseId = currentResponseId
                    });
                    continue;
                }
                
                // Capture file search results for quote extraction
                if (itemDoneUpdate.Item is FileSearchCallResponseItem fileSearchItem)
                {
                    foreach (var result in fileSearchItem.Results)
                    {
                        if (!string.IsNullOrEmpty(result.FileId) && !string.IsNullOrEmpty(result.Text))
                        {
                            fileSearchQuotes[result.FileId] = result.Text;
                            _logger.LogDebug(
                                "Captured file search quote for FileId={FileId}, QuoteLength={Length}", 
                                result.FileId, 
                                result.Text.Length);
                        }
                    }
                    continue;
                }
                
                // Extract annotations/citations from completed output items
                var annotations = ExtractAnnotations(itemDoneUpdate.Item, fileSearchQuotes);
                if (annotations.Count > 0)
                {
                    _logger.LogInformation("Extracted {Count} annotations from response", annotations.Count);
                    yield return StreamChunk.WithAnnotations(annotations);
                }
            }
            else if (update is StreamingResponseOutputItemAddedUpdate itemAddedUpdate)
            {
                if (itemAddedUpdate.Item != null)
                {
                    foreach (var visionChunk in CreateVisionChunks(visionDiagnostics, itemAddedUpdate.Item, "added", currentResponseId))
                        yield return visionChunk;
                }
                // Detect tool-use steps and signal the frontend for progress indicators
                string? toolName = itemAddedUpdate.Item switch
                {
                    FileSearchCallResponseItem => "file_search",
                    CodeInterpreterCallResponseItem => "code_interpreter",
                    _ when itemAddedUpdate.Item?.GetType().Name.Contains("ToolCall") == true => "function_call",
                    _ => null
                };

                if (toolName != null)
                {
                    _logger.LogDebug("Tool use detected: {ToolName}", toolName);
                    yield return StreamChunk.ToolUse(toolName);
                }
            }
            else if (update is StreamingResponseCompletedUpdate completedUpdate)
            {
                var response = completedUpdate.Response;
                visionDiagnostics.Complete(response);
                yield return new StreamChunk { Usage = pricingIdentity.ApplyTo(new AiResponseUsage(AiUsageType.ChatResponse, response.Id, true,
                    response.Usage?.InputTokenCount, response.Usage?.OutputTokenCount, response.Usage?.TotalTokenCount,
                    string.IsNullOrWhiteSpace(response.Model) ? definitionModel : response.Model,
                    string.IsNullOrWhiteSpace(response.Model) ? "agentDefinition" : "response",
                    resolvedVersion, DateTimeOffset.UtcNow)) };
            }
            else if (update is StreamingResponseErrorUpdate errorUpdate)
            {
                _logger.LogError("Stream error: {Error}", errorUpdate.Message);
                throw new InvalidOperationException($"Stream error: {errorUpdate.Message}");
            }
            else
            {
                _logger.LogDebug("Unhandled stream update type: {Type}", update.GetType().Name);
            }
        }

        _logger.LogInformation("Completed streaming for conversation: {ConversationId}", conversationId);
    }

    internal static IReadOnlyList<StreamChunk> CreateVisionChunks(
        VisionToolDiagnostics diagnostics,
        ResponseItem item,
        string phase,
        string? parentResponseId)
    {
        var chunks = new List<StreamChunk>(2);
        if (diagnostics.Observe(item, phase, parentResponseId) is { } usage)
            chunks.Add(new StreamChunk { VisionUsage = usage });

        var newVisuals = diagnostics.TakeNewVisuals();
        if (newVisuals.Count > 0)
            chunks.Add(StreamChunk.WithVisuals(newVisuals));

        return chunks;
    }

    /// <summary>
    /// Supported image MIME types for vision capabilities.
    /// </summary>
    private static readonly HashSet<string> AllowedImageTypes = 
        ["image/png", "image/jpeg", "image/jpg", "image/gif", "image/webp"];

    /// <summary>
    /// Supported document MIME types for file input.
    /// Note: Office documents (docx, pptx, xlsx) are NOT supported - they cannot be parsed.
    /// </summary>
    private static readonly HashSet<string> AllowedDocumentTypes = 
        [
            "application/pdf",
            "text/plain",
            "text/markdown",
            "text/csv",
            "application/json",
            "text/html",
            "application/xml",
            "text/xml"
        ];

    /// <summary>
    /// Text-based document MIME types that should be inlined as text rather than sent as file input.
    /// The Responses API only supports PDF for CreateInputFilePart.
    /// </summary>
    private static readonly HashSet<string> TextBasedDocumentTypes = 
        [
            "text/plain",
            "text/markdown",
            "text/csv",
            "application/json",
            "text/html",
            "application/xml",
            "text/xml"
        ];

    /// <summary>
    /// MIME types that can be sent as file input (only PDF is currently supported by Responses API).
    /// </summary>
    private static readonly HashSet<string> FileInputTypes = 
        [
            "application/pdf"
        ];

    /// <summary>
    /// Maximum number of images per message.
    /// </summary>
    private const int MaxImageCount = 5;

    /// <summary>
    /// Maximum number of files per message.
    /// </summary>
    private const int MaxFileCount = 10;

    /// <summary>
    /// Maximum size per image in bytes (5MB).
    /// </summary>
    private const long MaxImageSizeBytes = 5 * 1024 * 1024;

    /// <summary>
    /// Maximum size per document file in bytes (20MB).
    /// </summary>
    private const long MaxFileSizeBytes = 20 * 1024 * 1024;

    /// <summary>
    /// Builds a ResponseItem for the user message with optional image and file attachments.
    /// Validates count, size, MIME type, and Base64 format. Image bytes are uploaded to the
    /// Foundry Files API (purpose: assistants) and referenced by file id.
    /// </summary>
    private async Task<ResponseItem> BuildUserMessageAsync(
        string message,
        List<string>? imageDataUris,
        List<FileAttachment>? fileDataUris,
        CancellationToken cancellationToken)
    {
        if ((imageDataUris == null || imageDataUris.Count == 0) && 
            (fileDataUris == null || fileDataUris.Count == 0))
        {
            return ResponseItem.CreateUserMessageItem(message);
        }

        var contentParts = new List<ResponseContentPart>
        {
            ResponseContentPart.CreateInputTextPart(message)
        };

        var errors = new List<string>();

        // Process images
        if (imageDataUris != null && imageDataUris.Count > 0)
        {
            // Enforce maximum image count
            if (imageDataUris.Count > MaxImageCount)
            {
                throw new ArgumentException(
                    $"Invalid image attachments: Too many images ({imageDataUris.Count}), maximum {MaxImageCount} allowed");
            }

            for (int i = 0; i < imageDataUris.Count; i++)
            {
                var label = $"Image {i + 1}";

                if (!TryParseDataUri(imageDataUris[i], out var mediaType, out var bytes, out var parseError))
                {
                    errors.Add($"{label}: {parseError}");
                    continue;
                }

                if (!AllowedImageTypes.Contains(mediaType))
                {
                    errors.Add($"{label}: Unsupported type '{mediaType}'. Allowed: PNG, JPEG, GIF, WebP");
                    continue;
                }

                if (bytes.Length > MaxImageSizeBytes)
                {
                    var sizeMB = bytes.Length / (1024.0 * 1024.0);
                    errors.Add($"{label}: Size {sizeMB:F1}MB exceeds maximum 5MB");
                    continue;
                }

                // Upload image bytes via the OpenAI Files API and reference the returned file id.
                // Foundry's Files proxy rejects purpose=vision/user_data with "Invalid file ContentType";
                // purpose=assistants is the accepted path and the resulting file id works with
                // CreateInputImagePart on the Responses API.
                var fileClient = GetProjectClient().ProjectOpenAIClient.GetOpenAIFileClient();
                var extension = mediaType switch
                {
                    "image/png" => ".png",
                    "image/jpeg" => ".jpg",
                    "image/gif" => ".gif",
                    "image/webp" => ".webp",
                    _ => ".bin",
                };
                // Prefix uploaded filenames so the cleanup endpoint can identify files uploaded
                // by this web app versus other files in the shared Foundry project.
                var imageFileName = $"{WebAppUploadFilenamePrefix}{Guid.NewGuid():N}{extension}";
                using var imageStream = new MemoryStream(bytes);
                // Azure Foundry Files API only accepts purpose = assistants | batch | fine-tune | evals.
                // Use purpose=assistants per Azure Responses API docs.
                // See: learn.microsoft.com/azure/foundry/openai/how-to/responses#file-input
                var uploaded = await fileClient.UploadFileAsync(
                    imageStream,
                    imageFileName,
                    FileUploadPurpose.Assistants,
                    cancellationToken);
                contentParts.Add(ResponseContentPart.CreateInputImagePart(uploaded.Value.Id));
            }
        }

        // Process file attachments
        if (fileDataUris != null && fileDataUris.Count > 0)
        {
            // Enforce maximum file count
            if (fileDataUris.Count > MaxFileCount)
            {
                throw new ArgumentException(
                    $"Invalid file attachments: Too many files ({fileDataUris.Count}), maximum {MaxFileCount} allowed");
            }

            for (int i = 0; i < fileDataUris.Count; i++)
            {
                var file = fileDataUris[i];
                var label = $"File {i + 1} ({file.FileName})";

                if (!TryParseDataUri(file.DataUri, out var mediaType, out var bytes, out var parseError))
                {
                    errors.Add($"{label}: {parseError}");
                    continue;
                }

                if (!AllowedDocumentTypes.Contains(mediaType))
                {
                    errors.Add($"{label}: Unsupported type '{mediaType}'");
                    continue;
                }

                // Verify MIME type matches what was declared
                if (!string.Equals(mediaType, file.MimeType.ToLowerInvariant(), StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add($"{label}: MIME type mismatch (declared: {file.MimeType}, detected: {mediaType})");
                    continue;
                }

                if (bytes.Length > MaxFileSizeBytes)
                {
                    var sizeMB = bytes.Length / (1024.0 * 1024.0);
                    errors.Add($"{label}: Size {sizeMB:F1}MB exceeds maximum 20MB");
                    continue;
                }

                // Handle text-based files by inlining their content
                // The Responses API only supports PDF for CreateInputFilePart
                if (TextBasedDocumentTypes.Contains(mediaType))
                {
                    var textContent = System.Text.Encoding.UTF8.GetString(bytes);
                    var inlineText = $"\n\n--- Content of {file.FileName} ---\n{textContent}\n--- End of {file.FileName} ---\n";
                    contentParts.Add(ResponseContentPart.CreateInputTextPart(inlineText));
                }
                else if (FileInputTypes.Contains(mediaType))
                {
                    contentParts.Add(ResponseContentPart.CreateInputFilePart(
                        BinaryData.FromBytes(bytes),
                        mediaType,
                        file.FileName));
                }
            }
        }

        if (errors.Count > 0)
        {
            throw new ArgumentException($"Invalid attachments: {string.Join("; ", errors)}");
        }

        return ResponseItem.CreateUserMessageItem(contentParts);
    }

    /// <summary>
    /// Parses a data URI into its media type and decoded bytes.
    /// </summary>
    /// <returns>true if parsing succeeded; false with an error message otherwise.</returns>
    private static bool TryParseDataUri(string dataUri, out string mediaType, out byte[] bytes, out string error)
    {
        mediaType = string.Empty;
        bytes = Array.Empty<byte>();
        error = string.Empty;

        if (!dataUri.StartsWith("data:"))
        {
            error = "Invalid format (must be data URI)";
            return false;
        }

        var semiIndex = dataUri.IndexOf(';');
        var commaIndex = dataUri.IndexOf(',');

        if (semiIndex < 0 || commaIndex < 0 || commaIndex < semiIndex)
        {
            error = "Malformed data URI";
            return false;
        }

        mediaType = dataUri[5..semiIndex].ToLowerInvariant();

        var base64Data = dataUri[(commaIndex + 1)..];
        try
        {
            bytes = Convert.FromBase64String(base64Data);
        }
        catch (FormatException)
        {
            error = "Invalid Base64 encoding";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Extracts annotation information from a completed response item.
    /// </summary>
    private List<AnnotationInfo> ExtractAnnotations(
        ResponseItem? item, 
        Dictionary<string, string>? fileSearchQuotes = null)
    {
        var annotations = new List<AnnotationInfo>();
        
        if (item is not MessageResponseItem messageItem)
            return annotations;

        foreach (var content in messageItem.Content)
        {
            if (content.OutputTextAnnotations == null) continue;
            
            foreach (var annotation in content.OutputTextAnnotations)
            {
                var annotationInfo = annotation switch
                {
                    UriCitationMessageAnnotation uriAnnotation => new AnnotationInfo
                    {
                        Type = "uri_citation",
                        Label = uriAnnotation.Title ?? "Source",
                        Url = uriAnnotation.Uri?.ToString(),
                        StartIndex = uriAnnotation.StartIndex,
                        EndIndex = uriAnnotation.EndIndex
                    },
                    
                    FileCitationMessageAnnotation fileCitation => new AnnotationInfo
                    {
                        Type = "file_citation",
                        Label = fileCitation.Filename ?? fileCitation.FileId ?? "File",
                        FileId = fileCitation.FileId,
                        StartIndex = fileCitation.Index,
                        EndIndex = fileCitation.Index,
                        Quote = fileSearchQuotes?.TryGetValue(fileCitation.FileId ?? string.Empty, out var quote) == true 
                            ? quote : null
                    },
                    
                    FilePathMessageAnnotation filePath => new AnnotationInfo
                    {
                        Type = "file_path",
                        Label = filePath.FileId?.Split('/').LastOrDefault() ?? "Generated File",
                        FileId = filePath.FileId,
                        StartIndex = filePath.Index,
                        EndIndex = filePath.Index
                    },
                    
                    ContainerFileCitationMessageAnnotation containerCitation => new AnnotationInfo
                    {
                        Type = "container_file_citation",
                        Label = containerCitation.Filename ?? "Container File",
                        FileId = containerCitation.FileId,
                        ContainerId = containerCitation.ContainerId,
                        StartIndex = containerCitation.StartIndex,
                        EndIndex = containerCitation.EndIndex,
                        Quote = fileSearchQuotes?.TryGetValue(containerCitation.FileId ?? string.Empty, out var containerQuote) == true 
                            ? containerQuote : null
                    },
                    
                    _ => null
                };
                
                if (annotationInfo != null)
                    annotations.Add(annotationInfo);
            }
        }

        return annotations;
    }

    /// <summary>
    /// Create a new conversation for the agent.
    /// Uses ProjectConversation from Azure.AI.Projects for server-managed state.
    /// </summary>
    public async Task<string> CreateConversationAsync(string? firstMessage = null, string? projectEndpointOverride = null, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        try
        {
            _logger.LogInformation("Creating new conversation");
            
            ProjectConversationCreationOptions conversationOptions = new();

            if (!string.IsNullOrEmpty(firstMessage))
            {
                // Store title in metadata (truncate to 50 chars)
                var title = firstMessage.Length > 50 
                    ? firstMessage[..50] + "..."
                    : firstMessage;
                conversationOptions.Metadata["title"] = title;
            }

            ProjectConversation conversation
                = await GetProjectClient(projectEndpointOverride ?? _agentEndpoint).ProjectOpenAIClient.GetProjectConversationsClient().CreateProjectConversationAsync(
                    conversationOptions,
                    cancellationToken);

            _logger.LogInformation(
                "Created conversation: {ConversationId}", 
                conversation.Id);
            return conversation.Id;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Conversation creation was cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create conversation");
            throw;
        }
    }

    /// <summary>
    /// List conversations for the current agent.
    /// </summary>
    public async Task<List<ConversationSummary>> ListConversationsAsync(int limit = 20, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        try
        {
            _logger.LogInformation("Listing conversations (limit={Limit})", limit);

            var conversations = new List<ConversationSummary>();
            // Fetch limit+1 to detect if more conversations exist beyond the requested page
            var fetchLimit = limit + 1;
            // No agent filter: ProjectConversationCreationOptions doesn't support tagging a conversation
            // with an agent at creation time, so GetProjectConversationsAsync(AgentReference) never matches
            // conversations created by this app — list all conversations in the project instead.
            await foreach (var conv in GetProjectClient().ProjectOpenAIClient.GetProjectConversationsClient().GetProjectConversationsAsync(
                limit: fetchLimit, cancellationToken: cancellationToken))
            {
                conversations.Add(new ConversationSummary
                {
                    Id = conv.Id,
                    Title = conv.Metadata?.TryGetValue("title", out var title) == true ? title : null,
                    CreatedAt = conv.CreatedAt.ToUnixTimeSeconds()
                });

                if (conversations.Count >= fetchLimit)
                    break;
            }

            _logger.LogInformation("Found {Count} conversations", conversations.Count);
            return conversations;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list conversations");
            throw;
        }
    }

    /// <summary>
    /// Get messages for a specific conversation.
    /// </summary>
    public async Task<List<ConversationMessageInfo>> GetConversationMessagesAsync(
        string conversationId,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        try
        {
            _logger.LogInformation("Getting messages for conversation: {ConversationId}", conversationId);

            var messages = new List<ConversationMessageInfo>();

            // Filter to message items only
            await foreach (var item in GetProjectClient().ProjectOpenAIClient.GetProjectConversationsClient().GetProjectConversationItemsAsync(
                conversationId, itemKind: AgentResponseItemKind.Message, cancellationToken: cancellationToken))
            {
                var responseItem = item.AsResponseResultItem();
                if (responseItem is MessageResponseItem messageItem)
                {
                    var content = string.Join("", messageItem.Content
                        .Where(c => c.Text != null)
                        .Select(c => c.Text));

                    messages.Add(new ConversationMessageInfo
                    {
                        Role = messageItem.Role.ToString().ToLowerInvariant(),
                        Content = content
                    });
                }
            }

            _logger.LogInformation("Found {Count} messages in conversation {ConversationId}", messages.Count, conversationId);
            messages.Reverse();
            return messages;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get messages for conversation: {ConversationId}", conversationId);
            throw;
        }
    }

    /// <summary>
    /// Delete a conversation.
    /// </summary>
    /// <remarks>
    /// TODO: The Azure.AI.Projects SDK does not expose a delete conversation API.
    /// This method is a stub that will need to be updated when the SDK adds delete support.
    /// </remarks>
    public Task DeleteConversationAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _logger.LogWarning(
            "DeleteConversationAsync is not yet supported by the SDK. ConversationId: {ConversationId}",
            conversationId);

        // TODO: Replace with actual SDK call when available.
        // The ProjectConversationsClient currently only supports Create, Get, List, and Update.
        throw new NotSupportedException(
            "Conversation deletion is not yet supported by the Azure.AI.Projects SDK.");
    }

    /// <summary>
    /// Generates a condensed technical summary from older messages, merged with any existing summary.
    /// Runs on a disposable, throwaway Foundry conversation created solely for this call — never stored,
    /// never reused, and never affecting the user's live conversation.
    /// </summary>
    public async Task<AiSummaryResult> SummarizeConversationAsync(
        string? existingSummary,
        IReadOnlyList<ConversationMessage> oldMessages,
        CancellationToken cancellationToken = default,
        Action<AiResponseUsage>? onUsage = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var scratchConversationId = await CreateConversationAsync(firstMessage: null, projectEndpointOverride: null, cancellationToken: cancellationToken);

        var resolvedAgent = await GetAgentAsync(assistantConfig: null, cancellationToken);
        var resolvedVersion = _configuredAgentVersion ?? resolvedAgent.Version;
        var pricingIdentity = _pricingIdentityResolver.Resolve(_agentEndpoint, _agentId, resolvedVersion,
            (resolvedAgent.Definition as DeclarativeAgentDefinition)?.Model);

        ProjectResponsesClient responsesClient
            = GetProjectClient().ProjectOpenAIClient.GetProjectResponsesClientForAgent(
                new AgentReference(_agentId, resolvedVersion),
                scratchConversationId);

        var prompt = BuildSummarizationPrompt(existingSummary, oldMessages);

        CreateResponseOptions options = new() { StreamingEnabled = false };
        options.InputItems.Add(ResponseItem.CreateUserMessageItem(prompt));

        onUsage?.Invoke(pricingIdentity.ApplyTo(new AiResponseUsage(AiUsageType.ConversationSummary, null, false,
            null, null, null, (resolvedAgent.Definition as DeclarativeAgentDefinition)?.Model,
            "agentDefinition", resolvedVersion, DateTimeOffset.UtcNow)));
        var result = await responsesClient.CreateResponseAsync(options, cancellationToken);
        var response = result.Value;
        var usage = pricingIdentity.ApplyTo(new AiResponseUsage(
            AiUsageType.ConversationSummary, response.Id, true,
            response.Usage?.InputTokenCount, response.Usage?.OutputTokenCount, response.Usage?.TotalTokenCount,
            string.IsNullOrWhiteSpace(response.Model) ? (resolvedAgent.Definition as DeclarativeAgentDefinition)?.Model : response.Model,
            string.IsNullOrWhiteSpace(response.Model) ? "agentDefinition" : "response",
            resolvedVersion, DateTimeOffset.UtcNow));
        onUsage?.Invoke(usage);
        return new AiSummaryResult(ExtractOutputText(response), usage);
    }

    /// <summary>
    /// Builds the strict, maintenance-focused summarization prompt merging the existing technical
    /// summary with the older messages being folded into it.
    /// </summary>
    private static string BuildSummarizationPrompt(string? existingSummary, IReadOnlyList<ConversationMessage> oldMessages)
    {
        var messagesText = new StringBuilder();
        foreach (var m in oldMessages)
        {
            messagesText.Append(m.Role).Append(": ").Append(m.Content).Append('\n');
        }

        return $$"""
            Tu es un assistant de maintenance industrielle. Ta seule tâche ici est de produire un résumé technique
            compact d'une conversation de diagnostic, destiné à servir de mémoire de contexte pour la suite de
            l'intervention. Tu ne dois pas répondre à l'utilisateur, ni poursuivre le diagnostic : uniquement résumer.

            CONSERVER UNIQUEMENT si présent dans le résumé existant ou les messages ci-dessous :
            - problème initial signalé
            - symptômes constatés
            - codes défaut (conserver le code exact, sans le reformuler)
            - machine ou document concerné (nom/référence exacte si mentionné)
            - pages importantes (numéros exacts)
            - composants et repères identifiés (références exactes)
            - mesures effectuées avec leurs valeurs exactes
            - contrôles déjà réalisés
            - résultats obtenus
            - hypothèses déjà éliminées (préciser qu'il s'agit d'hypothèses écartées, pas de faits)
            - diagnostic actuel (en l'état, sans le présenter comme définitif s'il ne l'est pas)
            - prochaines actions prévues
            - informations importantes données explicitement par l'utilisateur

            NE JAMAIS INCLURE :
            - formules de politesse
            - répétitions
            - explications longues déjà comprises
            - raisonnement interne ou étapes de réflexion
            - sorties brutes d'outils
            - contenu intégral de recherches de fichiers ou de résultats Vision
            - métadonnées inutiles
            - URLs signées (SAS) ou tout lien temporaire
            - secrets, clés, tokens ou identifiants

            RÈGLES STRICTES :
            - N'invente aucune information absente des messages ou de l'ancien résumé.
            - Ne transforme jamais une hypothèse ou une supposition en fait confirmé.
            - Préserve les valeurs, références, repères, codes défaut et numéros de page exactement tels qu'exprimés
              (ne pas arrondir, reformuler ou approximer).
            - Si l'ancien résumé technique contient une information toujours utile, conserve-la ; sinon, ne la répète pas.
            - Considère l'ancien résumé comme une mémoire persistante : ne supprime une information existante que si
              les nouveaux échanges la corrigent explicitement, la rendent fausse ou montrent clairement qu'elle n'est
              plus utile au diagnostic.
            - En cas de contradiction entre l'ancien résumé et les nouveaux échanges, privilégie l'information la plus
              récente et indique brièvement qu'elle remplace/corrige l'information précédente.
            - Fusionne l'ancien résumé et les nouveaux échanges en un seul résumé cohérent, sans doublons.
            - Reste sous 600 mots.
            - Le résultat doit être directement exploitable comme contexte technique par un agent qui reprendra la
              conversation sans avoir vu les échanges originaux.

            FORMAT DE SORTIE :
            Texte structuré par sections courtes, une section par rubrique ci-dessus UNIQUEMENT si elle contient une
            information réelle (omettre les sections vides). Style télégraphique, phrases courtes, pas de prose.
            Pas d'introduction ni de conclusion, uniquement le résumé.

            --- RÉSUMÉ TECHNIQUE EXISTANT (peut être vide) ---
            {{existingSummary ?? "(aucun résumé existant)"}}

            --- NOUVEAUX ÉCHANGES À INTÉGRER AU RÉSUMÉ ---
            {{messagesText}}
            """;
    }

    /// <summary>
    /// Extracts the concatenated output text from a non-streaming Responses API result.
    /// </summary>
    private static string ExtractOutputText(ResponseResult response) => response.GetOutputText() ?? string.Empty;

    /// <summary>
    /// Download a file generated by code interpreter or other tools.

    /// Container files (with containerId) use the REST API: GET /openai/v1/containers/{containerId}/files/{fileId}/content.
    /// Standard files use the OpenAI FileClient.
    /// </summary>
    public async Task<(BinaryData Content, string FileName)> DownloadFileAsync(
        string fileId,
        string? containerId = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        try
        {
            if (!string.IsNullOrEmpty(containerId))
            {
                return await DownloadContainerFileAsync(fileId, containerId, cancellationToken);
            }

            _logger.LogInformation("Downloading standard file: {FileId}", fileId);
            var fileClient = GetProjectClient().ProjectOpenAIClient.GetOpenAIFileClient();
            var fileContent = await fileClient.DownloadFileAsync(fileId, cancellationToken);
            var fileInfo = await fileClient.GetFileAsync(fileId, cancellationToken);
            var fileName = fileInfo.Value?.Filename ?? $"{fileId}.bin";
            _logger.LogInformation("Downloaded file: {FileId}, Name: {FileName}, Size: {Size} bytes",
                fileId, fileName, fileContent.Value.ToMemory().Length);
            return (fileContent.Value, fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download file {FileId}. Error: {Error}", fileId, ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Download a container file via REST API.
    /// Endpoint: GET {projectEndpoint}/openai/v1/containers/{containerId}/files/{fileId}/content
    /// </summary>
    private async Task<(BinaryData Content, string FileName)> DownloadContainerFileAsync(
        string fileId,
        string containerId,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Downloading container file: {FileId} from container: {ContainerId}", fileId, containerId);

        // Reuse the same credential as the project client (MI or OBO)
        TokenCredential credential;
        if (_useObo)
        {
            var userToken = ExtractBearerToken();
            credential = CreateOboCredential(userToken ?? throw new InvalidOperationException("OBO requires bearer token"));
        }
        else
        {
            credential = _fallbackCredential;
        }

        var tokenRequestContext = new TokenRequestContext(["https://ai.azure.com/.default"]);
        var accessToken = await credential.GetTokenAsync(tokenRequestContext, cancellationToken);

        var requestUrl = $"{_agentEndpoint.TrimEnd('/')}/openai/v1/containers/{Uri.EscapeDataString(containerId)}/files/{Uri.EscapeDataString(fileId)}/content";
        using var httpClient = _httpClientFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken.Token);

        var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);

        // Try to extract filename from Content-Disposition header, fall back to fileId
        var fileName = $"{fileId}.bin";
        if (response.Content.Headers.ContentDisposition?.FileName is { } headerFileName)
        {
            fileName = headerFileName.Trim('"');
        }

        _logger.LogInformation("Downloaded container file: {FileId}, Name: {FileName}, Size: {Size} bytes",
            fileId, fileName, bytes.Length);
        return (BinaryData.FromBytes(bytes), fileName);
    }

    /// <summary>
    /// Get the agent metadata (name, description, etc.) for display in UI.
    /// Reads directly from the cached ProjectsAgentVersion. When <paramref name="assistantConfig"/> is
    /// supplied, returns metadata for that machine's agent; otherwise falls back to the legacy
    /// globally-configured agent.
    /// </summary>
    public async Task<AgentMetadataResponse> GetAgentMetadataAsync(ResolvedAssistantConfiguration? assistantConfig = null, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var effectiveAgentId = assistantConfig?.AgentId ?? _agentId;
        var effectiveVersion = assistantConfig?.AgentVersion ?? _configuredAgentVersion;
        var cacheKey = BuildAgentCacheKey(assistantConfig?.ProjectEndpoint ?? _agentEndpoint, effectiveAgentId, effectiveVersion);

        var agentVersion = await GetAgentAsync(assistantConfig, cancellationToken);

        if (s_cachedMetadataByKey.TryGetValue(cacheKey, out var cachedMetadata))
            return cachedMetadata;

        var definition = agentVersion.Definition as DeclarativeAgentDefinition;
        var metadata = agentVersion.Metadata?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        // Log metadata keys at debug level for troubleshooting
        if (metadata != null && metadata.Count > 0)
        {
            _logger.LogDebug("Agent metadata keys: {Keys}", string.Join(", ", metadata.Keys));
        }

        // Parse starter prompts from metadata
        List<string>? starterPrompts = ParseStarterPrompts(metadata);

        var result = new AgentMetadataResponse
        {
            Id = effectiveAgentId,
            Object = "agent",
            CreatedAt = agentVersion.CreatedAt.ToUnixTimeSeconds(),
            Name = effectiveAgentId,
            Description = agentVersion.Description,
            Model = definition?.Model ?? string.Empty,
            Instructions = definition?.Instructions ?? string.Empty,
            Metadata = metadata,
            StarterPrompts = starterPrompts
        };

        s_cachedMetadataByKey[cacheKey] = result;
        return result;
    }

    /// <summary>
    /// Parse starter prompts from agent metadata.
    /// Microsoft Foundry stores starter prompts as newline-separated text in the "starterPrompts" metadata key.
    /// Example: "How's the weather?\nIs your fridge running?\nTell me a joke"
    /// </summary>
    private List<string>? ParseStarterPrompts(Dictionary<string, string>? metadata)
    {
        if (metadata == null)
            return null;

        // Microsoft Foundry uses camelCase "starterPrompts" key with newline-separated values
        if (!metadata.TryGetValue("starterPrompts", out var starterPromptsValue))
            return null;

        if (string.IsNullOrWhiteSpace(starterPromptsValue))
            return null;

        // Split by newlines and filter out empty entries
        var prompts = starterPromptsValue
            .Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .Where(p => !string.IsNullOrEmpty(p))
            .ToList();

        if (prompts.Count > 0)
        {
            _logger.LogDebug("Parsed {Count} starter prompts from agent metadata", prompts.Count);
            return prompts;
        }

        return null;
    }

    /// <summary>
    /// Get basic agent info string (for debugging).
    /// </summary>
    public async Task<string> GetAgentInfoAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var agentVersion = await GetAgentAsync(assistantConfig: null, cancellationToken);
        return agentVersion.Name ?? _agentId;
    }

    /// <summary>
    /// Returns a count and total byte size of files uploaded by this web app (identified by
    /// filename prefix <see cref="WebAppUploadFilenamePrefix"/>) that are still stored in the
    /// Foundry project. Uses <see cref="FilePurpose.Assistants"/> because that is the purpose
    /// under which <see cref="BuildUserMessageAsync"/> stores image uploads.
    /// </summary>
    public async Task<UploadedFilesInfo> ListUploadedFilesAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var fileClient = GetProjectClient().ProjectOpenAIClient.GetOpenAIFileClient();
        var result = await fileClient.GetFilesAsync(FilePurpose.Assistants, cancellationToken);

        int count = 0;
        long totalBytes = 0;
        foreach (var file in result.Value)
        {
            if (file.Filename != null && file.Filename.StartsWith(WebAppUploadFilenamePrefix, StringComparison.Ordinal))
            {
                count++;
                totalBytes += file.SizeInBytesLong ?? file.SizeInBytes ?? 0;
            }
        }

        _logger.LogInformation("ListUploadedFiles: {Count} files, {TotalBytes} bytes", count, totalBytes);
        return new UploadedFilesInfo(count, totalBytes);
    }

    /// <summary>
    /// Deletes every file in the Foundry project whose filename begins with
    /// <see cref="WebAppUploadFilenamePrefix"/>. Intended as a user-triggered cleanup
    /// because the GA Files API does not expose <c>expires_after</c> on upload — see README
    /// "Known limitations". Returns counts of successful and failed deletions; failures are
    /// logged but do not abort the loop.
    /// </summary>
    public async Task<UploadedFilesCleanupResult> CleanupUploadedFilesAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var fileClient = GetProjectClient().ProjectOpenAIClient.GetOpenAIFileClient();
        var result = await fileClient.GetFilesAsync(FilePurpose.Assistants, cancellationToken);

        int deleted = 0;
        int failed = 0;
        foreach (var file in result.Value)
        {
            if (file.Filename == null || !file.Filename.StartsWith(WebAppUploadFilenamePrefix, StringComparison.Ordinal))
            {
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await fileClient.DeleteFileAsync(file.Id, cancellationToken);
                deleted++;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                failed++;
                _logger.LogWarning(ex, "Failed to delete uploaded file {FileId} ({FileName})", file.Id, file.Filename);
            }
        }

        _logger.LogInformation("CleanupUploadedFiles: deleted={Deleted} failed={Failed}", deleted, failed);
        return new UploadedFilesCleanupResult(deleted, failed);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            // AIProjectClient does not implement IDisposable (verified via reflection on
            // Azure.AI.Projects assembly). No cleanup needed for the cached project clients.
            _logger.LogDebug("AgentFrameworkService disposed");
        }
    }
}
