using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Azure.Core;
using Microsoft.Extensions.Options;
using WebApp.Api.Models;
using WebApp.Api.Models.Entities;

namespace WebApp.Api.Services;

/// <summary>
/// Produces conversation summaries with one text-only call to the Foundry Anthropic Messages API.
/// This path intentionally has no machine, Toolbox, MCP, File Search, image, or Blob dependency.
/// </summary>
public sealed class ClaudeConversationSummarizer : IConversationSummarizer
{
    public const string HttpClientName = "ClaudeConversationSummary";
    private const string FoundryScope = "https://ai.azure.com/.default";
    private const string AnthropicVersion = "2023-06-01";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TokenCredential _credential;
    private readonly ClaudeDirectChatOptions _options;
    private readonly ILogger<ClaudeConversationSummarizer> _logger;

    public ClaudeConversationSummarizer(
        IHttpClientFactory httpClientFactory,
        TokenCredential credential,
        IOptions<ClaudeDirectChatOptions> options,
        ILogger<ClaudeConversationSummarizer> logger)
    {
        _httpClientFactory = httpClientFactory;
        _credential = credential;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<AiSummaryResult> SummarizeConversationAsync(
        string? existingSummary,
        IReadOnlyList<ConversationMessage> oldMessages,
        CancellationToken cancellationToken = default,
        Action<AiResponseUsage>? onUsage = null)
    {
        if (!Uri.TryCreate(_options.FoundryAnthropicEndpoint, UriKind.Absolute, out var configuredEndpoint) ||
            configuredEndpoint.Scheme != Uri.UriSchemeHttps ||
            string.IsNullOrWhiteSpace(_options.Deployment))
        {
            throw new InvalidOperationException("ClaudeDirectChat configuration is invalid for conversation summarization.");
        }

        var accessToken = await _credential.GetTokenAsync(
            new TokenRequestContext([FoundryScope]),
            cancellationToken);

        onUsage?.Invoke(CreateUsage(
            responseId: null,
            completed: false,
            inputTokens: null,
            outputTokens: null,
            model: _options.Deployment,
            modelSource: "configuration"));

        var endpoint = new Uri(
            new Uri(_options.FoundryAnthropicEndpoint.TrimEnd('/') + "/"),
            "v1/messages");
        var payload = new Dictionary<string, object?>
        {
            ["model"] = _options.Deployment,
            ["max_tokens"] = 4096,
            ["messages"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["role"] = "user",
                    ["content"] = BuildSummarizationPrompt(existingSummary, oldMessages)
                }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken.Token);
        request.Headers.Add("anthropic-version", AnthropicVersion);

        using var response = await _httpClientFactory.CreateClient(HttpClientName).SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Claude conversation summary request failed. StatusCode={StatusCode}",
                (int)response.StatusCode);
            throw new HttpRequestException(
                $"The Claude Messages API returned HTTP {(int)response.StatusCode} for conversation summarization.",
                inner: null,
                response.StatusCode);
        }

        JsonDocument document;
        try
        {
            await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            document = await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("The Claude summary response contains invalid JSON.", exception);
        }

        using (document)
        {
            var root = document.RootElement;
            var responseId = GetString(root, "id");
            var responseModel = GetString(root, "model");
            if (string.IsNullOrWhiteSpace(responseId) ||
                !root.TryGetProperty("usage", out var usageElement) ||
                !TryReadNonNegativeInt(usageElement, "input_tokens", out var inputTokens) ||
                !TryReadNonNegativeInt(usageElement, "output_tokens", out var outputTokens) ||
                !root.TryGetProperty("content", out var content) ||
                content.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException("The Claude summary response is missing required metadata, usage, or content.");
            }

            if (!TryReadOptionalNonNegativeInt(usageElement, "cache_read_input_tokens", out var cacheReadTokens) ||
                !TryReadOptionalNonNegativeInt(usageElement, "cache_creation_input_tokens", out var cacheCreationTokens))
            {
                throw new InvalidOperationException("The Claude summary response contains invalid cache usage.");
            }

            var cacheCreation5mTokens = 0;
            var cacheCreation1hTokens = 0;
            if (usageElement.TryGetProperty("cache_creation", out var cacheCreationDetail))
            {
                if (cacheCreationDetail.ValueKind != JsonValueKind.Object ||
                    !TryReadOptionalNonNegativeInt(cacheCreationDetail, "ephemeral_5m_input_tokens", out cacheCreation5mTokens) ||
                    !TryReadOptionalNonNegativeInt(cacheCreationDetail, "ephemeral_1h_input_tokens", out cacheCreation1hTokens) ||
                    cacheCreation5mTokens > cacheCreationTokens ||
                    cacheCreation1hTokens != cacheCreationTokens - cacheCreation5mTokens)
                {
                    throw new InvalidOperationException("The Claude summary response contains inconsistent cache creation usage.");
                }
            }
            else
            {
                cacheCreation5mTokens = cacheCreationTokens;
            }

            var text = string.Join(
                Environment.NewLine,
                content.EnumerateArray()
                    .Where(block => string.Equals(GetString(block, "type"), "text", StringComparison.Ordinal))
                    .Select(block => GetString(block, "text"))
                    .Where(value => !string.IsNullOrWhiteSpace(value)));
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new InvalidOperationException("The Claude summary response contains no usable text.");
            }

            int totalTokens;
            try
            {
                totalTokens = checked(inputTokens + outputTokens);
            }
            catch (OverflowException exception)
            {
                throw new InvalidOperationException("The Claude summary response contains invalid token usage.", exception);
            }

            var model = string.IsNullOrWhiteSpace(responseModel) ? _options.Deployment : responseModel;
            var usage = CreateUsage(
                responseId,
                completed: true,
                inputTokens,
                outputTokens,
                model,
                string.IsNullOrWhiteSpace(responseModel) ? "configuration" : "response",
                totalTokens) with
            {
                CacheReadInputTokens = cacheReadTokens,
                CacheCreationInputTokens = cacheCreationTokens,
                CacheCreation5mInputTokens = cacheCreation5mTokens,
                CacheCreation1hInputTokens = cacheCreation1hTokens
            };
            onUsage?.Invoke(usage);
            return new AiSummaryResult(text, usage);
        }
    }

    internal static string BuildSummarizationPrompt(
        string? existingSummary,
        IReadOnlyList<ConversationMessage> oldMessages)
    {
        var messagesText = new StringBuilder();
        foreach (var message in oldMessages)
        {
            messagesText.Append(message.Role).Append(": ").Append(message.Content).Append('\n');
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

    private AiResponseUsage CreateUsage(
        string? responseId,
        bool completed,
        int? inputTokens,
        int? outputTokens,
        string? model,
        string modelSource,
        int? totalTokens = null) =>
        new(
            AiUsageType.ConversationSummary,
            responseId,
            completed,
            inputTokens,
            outputTokens,
            totalTokens,
            model,
            modelSource,
            TimestampUtc: DateTimeOffset.UtcNow)
        {
            Provider = "Anthropic",
            Deployment = _options.Deployment
        };

    private static bool TryReadNonNegativeInt(JsonElement element, string propertyName, out int value)
    {
        value = 0;
        return element.TryGetProperty(propertyName, out var property) &&
               property.TryGetInt32(out value) &&
               value >= 0;
    }

    private static bool TryReadOptionalNonNegativeInt(JsonElement element, string propertyName, out int value)
    {
        value = 0;
        return !element.TryGetProperty(propertyName, out var property) ||
               (property.TryGetInt32(out value) && value >= 0);
    }

    private static string? GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
}
