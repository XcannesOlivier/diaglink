using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Azure.Storage.Blobs;

return await PocProgram.RunAsync(args);

internal static class PocProgram
{
    private const string ClaudeMessagesEndpoint =
        "https://diaglink-foundry-prod.services.ai.azure.com/anthropic/v1/messages";
    private const string Deployment = "claude-sonnet-5";
    private const string FoundryProject =
        "https://diaglink-foundry-prod.services.ai.azure.com/api/projects/develon";
    private const string ToolboxName = "TB-excavatrice-doosan-dx10z";
    private const string ToolboxVersion = "1";
    private const string McpEndpoint =
        "https://diaglink-foundry-prod.services.ai.azure.com/api/projects/develon/toolboxes/TB-excavatrice-doosan-dx10z/versions/1/mcp?api-version=v1";
    private const string McpServerName = "dx10z-manual";
    private const string ExpectedVectorStore = "vs_sFC58t8D4992ObuiXEo9xjDM";
    private const string McpBeta = "mcp-client-2026-09-15";
    private const string Scope = "https://ai.azure.com/.default";
    private const string DefaultBlobServiceUri = "https://stknowledgeia.blob.core.windows.net/";
    private const string BlobPath =
        "develon/excavatrice-doosan-dx10z/develon-dx10z-manuel-utilisation-maintenance-en/page-00075/develon-dx10z-manuel-utilisation-maintenance-en_page-00075-full.png";
    private const string FullQuestion =
        "Recherche dans le manuel comment remplacer le filtre hydraulique du DX10z. " +
        "Utilise d’abord File Search, puis examine visuellement la page PDF 75 avec get_page_image. " +
        "Distingue ce que dit le manuel de ce que tu confirmes visuellement.";

    public static async Task<int> RunAsync(string[] args)
    {
        var mode = args.FirstOrDefault()?.Trim().ToLowerInvariant() ?? "all";
        if (mode is not ("direct" or "mcp" or "full" or "all"))
        {
            Console.Error.WriteLine("Usage: dotnet run -- [direct|mcp|full|all]");
            return 2;
        }

        Console.WriteLine("=== POC .NET ISOLÉ : Claude Foundry + Toolbox MCP + image Blob ===");
        Console.WriteLine($"Deployment : {Deployment}");
        Console.WriteLine($"Projet     : {FoundryProject}");
        Console.WriteLine($"Toolbox    : {ToolboxName}, version {ToolboxVersion}");
        Console.WriteLine("Aucun endpoint de production, Hosted Agent, SQL, quota ou wallet n’est utilisé.");

        try
        {
            var credential = new DefaultAzureCredential();
            var accessToken = await credential.GetTokenAsync(
                new TokenRequestContext([Scope]), CancellationToken.None);

            using var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            var runner = new ClaudePocRunner(httpClient, credential, accessToken.Token);

            if (mode is "direct" or "all")
            {
                await runner.RunDirectAsync();
            }

            if (mode is "mcp" or "all")
            {
                await runner.RunMcpAsync();
            }

            if (mode is "full" or "all")
            {
                await runner.RunFullAsync();
            }

            runner.PrintGlobalUsage();
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"POC ÉCHOUÉ — {exception.GetType().Name}: {exception.Message}");
            return 1;
        }
    }

    private sealed class ClaudePocRunner(HttpClient httpClient, TokenCredential credential, string entraToken)
    {
        private readonly List<CallUsage> _usages = [];
        private int _callNumber;

        public async Task RunDirectAsync()
        {
            Console.WriteLine("\n--- Test 1 : Claude direct ---");
            var messages = NewMessages("Réponds en une phrase : quel est ton rôle dans ce test technique ?");
            using var response = await SendAsync(messages, useMcp: false, useImageTool: false, "direct");
            PrintBlocks(response.RootElement);

            RequireText(response.RootElement, "L’appel direct n’a retourné aucun texte.");
            Console.WriteLine("Validation : HTTP succès, texte, model, stop_reason et usage présents.");
        }

        public async Task RunMcpAsync()
        {
            Console.WriteLine("\n--- Test 2 : File Search via Toolbox MCP ---");
            var messages = NewMessages(
                "Comment remplacer le filtre hydraulique du DX10z ? Utilise obligatoirement le File Search du manuel connecté avant de répondre.");
            using var response = await SendAsync(messages, useMcp: true, useImageTool: false, "mcp-file-search");
            PrintBlocks(response.RootElement);

            RequireText(response.RootElement, "Le test MCP n’a retourné aucun texte final.");
            if (!HasBlock(response.RootElement, "mcp_tool_use"))
            {
                throw new InvalidOperationException("Aucun bloc mcp_tool_use n’a été observé.");
            }

            if (!HasBlock(response.RootElement, "mcp_tool_result"))
            {
                throw new InvalidOperationException("Aucun bloc mcp_tool_result n’a été observé.");
            }

            var rawResponse = response.RootElement.GetRawText();
            Console.WriteLine(rawResponse.Contains(ExpectedVectorStore, StringComparison.Ordinal)
                ? $"Vector store attendu observé dans la réponse : {ExpectedVectorStore}"
                : $"Vector store non exposé dans les blocs de réponse ; attendu côté Toolbox : {ExpectedVectorStore}");
            Console.WriteLine("Validation : le connecteur a exécuté un outil MCP et retourné son résultat au modèle.");
        }

        public async Task RunFullAsync()
        {
            Console.WriteLine("\n--- Test 3 : orchestration MCP + get_page_image + même Claude ---");
            var messages = NewMessages(FullQuestion);
            var sawMcpUse = false;
            var sawMcpResult = false;
            var imageToolResults = 0;
            string? finalText = null;

            for (var turn = 1; turn <= 4; turn++)
            {
                using var response = await SendAsync(messages, useMcp: true, useImageTool: true, $"full-turn-{turn}");
                PrintBlocks(response.RootElement);
                sawMcpUse |= HasBlock(response.RootElement, "mcp_tool_use");
                sawMcpResult |= HasBlock(response.RootElement, "mcp_tool_result");

                var localCalls = GetLocalImageToolCalls(response.RootElement).ToArray();
                if (localCalls.Length == 0)
                {
                    finalText = GetText(response.RootElement);
                    break;
                }

                var assistantContent = response.RootElement.GetProperty("content").Clone();
                messages.Add(new Dictionary<string, object?>
                {
                    ["role"] = "assistant",
                    ["content"] = assistantContent
                });

                var results = new List<object>();
                foreach (var localCall in localCalls)
                {
                    if (localCall.Page != 75)
                    {
                        throw new InvalidOperationException(
                            $"get_page_image a demandé la page {localCall.Page}; ce POC est verrouillé sur la page 75.");
                    }

                    var image = await ReadPage75Async(credential);
                    imageToolResults++;
                    Console.WriteLine(
                        $"Tool local exécuté : page=75, bytes={image.Bytes.Length}, sha256={image.Sha256}, blob={BlobPath}");

                    results.Add(new Dictionary<string, object?>
                    {
                        ["type"] = "tool_result",
                        ["tool_use_id"] = localCall.Id,
                        ["content"] = new object[]
                        {
                            new Dictionary<string, object?>
                            {
                                ["type"] = "text",
                                ["text"] = "Image PNG réelle de la page PDF 75 demandée, lue directement depuis Azure Blob Storage."
                            },
                            new Dictionary<string, object?>
                            {
                                ["type"] = "image",
                                ["source"] = new Dictionary<string, object?>
                                {
                                    ["type"] = "base64",
                                    ["media_type"] = "image/png",
                                    ["data"] = Convert.ToBase64String(image.Bytes)
                                }
                            }
                        }
                    });
                }

                messages.Add(new Dictionary<string, object?>
                {
                    ["role"] = "user",
                    ["content"] = results
                });
                Console.WriteLine("Historique conservé et tool_result image rattaché au tool_use_id avant le rappel du même deployment.");
            }

            if (!sawMcpUse || !sawMcpResult)
            {
                throw new InvalidOperationException("L’orchestration complète n’a pas produit les blocs MCP attendus.");
            }

            if (imageToolResults == 0)
            {
                throw new InvalidOperationException("Claude n’a pas demandé get_page_image.");
            }

            if (string.IsNullOrWhiteSpace(finalText))
            {
                throw new InvalidOperationException("Claude n’a pas produit de réponse finale après réception de l’image.");
            }

            Console.WriteLine("\nRÉPONSE FINALE COMPLÈTE :");
            Console.WriteLine(finalText);
            Console.WriteLine($"\nPreuve de continuité : {imageToolResults} tool_result image envoyé au deployment {Deployment} dans le même historique Messages.");
        }

        public void PrintGlobalUsage()
        {
            Console.WriteLine("\n=== USAGE GLOBAL DU POC ===");
            foreach (var usage in _usages)
            {
                Console.WriteLine(
                    $"#{usage.Number} {usage.Label}: input={usage.InputTokens}, output={usage.OutputTokens}, total={usage.Total}, " +
                    $"model={usage.Model}, stop_reason={usage.StopReason}");
            }

            Console.WriteLine(
                $"TOTAL: input={_usages.Sum(item => item.InputTokens)}, " +
                $"output={_usages.Sum(item => item.OutputTokens)}, " +
                $"total={_usages.Sum(item => item.Total)}");
        }

        private async Task<JsonDocument> SendAsync(
            List<Dictionary<string, object?>> messages,
            bool useMcp,
            bool useImageTool,
            string label)
        {
            var payload = new Dictionary<string, object?>
            {
                ["model"] = Deployment,
                ["max_tokens"] = 4096,
                ["messages"] = messages
            };

            if (useMcp)
            {
                payload["mcp_servers"] = new object[]
                {
                    new Dictionary<string, object?>
                    {
                        ["type"] = "url",
                        ["url"] = McpEndpoint,
                        ["name"] = McpServerName,
                        ["authorization_token"] = entraToken
                    }
                };

                var tools = new List<object>
                {
                    new Dictionary<string, object?>
                    {
                        ["type"] = "mcp_toolset",
                        ["mcp_server_name"] = McpServerName
                    }
                };

                if (useImageTool)
                {
                    tools.Add(new Dictionary<string, object?>
                    {
                        ["name"] = "get_page_image",
                        ["description"] =
                            "Récupère l’image PNG réelle d’une page du document technique afin que Claude puisse l’examiner directement. " +
                            "Dans ce POC, appelle cet outil avec la page 75 après avoir utilisé File Search.",
                        ["input_schema"] = new Dictionary<string, object?>
                        {
                            ["type"] = "object",
                            ["properties"] = new Dictionary<string, object?>
                            {
                                ["page"] = new Dictionary<string, object?>
                                {
                                    ["type"] = "integer",
                                    ["description"] = "Numéro de page PDF à examiner (75 pour ce POC)."
                                }
                            },
                            ["required"] = new[] { "page" },
                            ["additionalProperties"] = false
                        }
                    });
                }

                payload["tools"] = tools;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, ClaudeMessagesEndpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", entraToken);
            request.Headers.Add("anthropic-version", "2023-06-01");
            if (useMcp)
            {
                request.Headers.Add("anthropic-beta", McpBeta);
            }

            request.Content = new StringContent(
                JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            var responseBody = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"Messages API a répondu {(int)response.StatusCode} ({response.StatusCode}). " +
                    $"Erreur: {SafeErrorSummary(responseBody)}",
                    null,
                    response.StatusCode);
            }

            var document = JsonDocument.Parse(responseBody);
            var usage = ReadUsage(document.RootElement, ++_callNumber, label);
            _usages.Add(usage);
            Console.WriteLine(
                $"Messages #{usage.Number} [{label}] HTTP={(int)response.StatusCode}; model={usage.Model}; " +
                $"stop_reason={usage.StopReason}; input={usage.InputTokens}; output={usage.OutputTokens}; total={usage.Total}");
            return document;
        }

        private static async Task<ImagePayload> ReadPage75Async(TokenCredential credential)
        {
            var serviceUri = ResolveBlobServiceUri();
            var container = Environment.GetEnvironmentVariable("POC_BLOB_CONTAINER")?.Trim();
            if (string.IsNullOrWhiteSpace(container))
            {
                container = "documents";
            }

            var serviceClient = new BlobServiceClient(serviceUri, credential);
            var blob = serviceClient.GetBlobContainerClient(container).GetBlobClient(BlobPath);
            var download = await blob.DownloadContentAsync();
            var bytes = download.Value.Content.ToArray();

            ReadOnlySpan<byte> pngSignature = [137, 80, 78, 71, 13, 10, 26, 10];
            if (bytes.Length < pngSignature.Length || !bytes.AsSpan(0, pngSignature.Length).SequenceEqual(pngSignature))
            {
                throw new InvalidDataException("Le Blob page 75 n’est pas un PNG valide.");
            }

            if (bytes.Length > 5 * 1024 * 1024)
            {
                throw new InvalidDataException("Le PNG dépasse 5 Mio et n’est pas envoyé automatiquement au modèle.");
            }

            return new ImagePayload(bytes, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        }

        private static Uri ResolveBlobServiceUri()
        {
            var explicitUri = Environment.GetEnvironmentVariable("POC_BLOB_SERVICE_URI")?.Trim();
            if (Uri.TryCreate(explicitUri, UriKind.Absolute, out var parsedUri) && parsedUri.Scheme == Uri.UriSchemeHttps)
            {
                return parsedUri;
            }

            var connectionString = Environment.GetEnvironmentVariable("AZURE_STORAGE_CONNECTION_STRING");
            if (!string.IsNullOrWhiteSpace(connectionString))
            {
                var parts = connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries)
                    .Select(part => part.Split('=', 2))
                    .Where(part => part.Length == 2)
                    .ToDictionary(part => part[0].Trim(), part => part[1].Trim(), StringComparer.OrdinalIgnoreCase);

                if (parts.TryGetValue("BlobEndpoint", out var blobEndpoint) &&
                    Uri.TryCreate(blobEndpoint, UriKind.Absolute, out parsedUri))
                {
                    return parsedUri;
                }

                if (parts.TryGetValue("AccountName", out var accountName))
                {
                    var endpointSuffix = parts.GetValueOrDefault("EndpointSuffix", "core.windows.net");
                    return new Uri($"https://{accountName}.blob.{endpointSuffix}");
                }
            }

            return new Uri(DefaultBlobServiceUri);
        }

        private static List<Dictionary<string, object?>> NewMessages(string question) =>
        [
            new Dictionary<string, object?>
            {
                ["role"] = "user",
                ["content"] = question
            }
        ];

        private static IEnumerable<LocalToolCall> GetLocalImageToolCalls(JsonElement root)
        {
            if (!root.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            {
                yield break;
            }

            foreach (var block in content.EnumerateArray())
            {
                if (GetString(block, "type") != "tool_use" || GetString(block, "name") != "get_page_image")
                {
                    continue;
                }

                var id = GetString(block, "id");
                if (string.IsNullOrWhiteSpace(id) ||
                    !block.TryGetProperty("input", out var input) ||
                    !input.TryGetProperty("page", out var pageElement) ||
                    !pageElement.TryGetInt32(out var page))
                {
                    throw new InvalidDataException("Bloc get_page_image invalide : id ou page manquant.");
                }

                yield return new LocalToolCall(id, page);
            }
        }

        private static bool HasBlock(JsonElement root, string type) =>
            root.TryGetProperty("content", out var content) &&
            content.ValueKind == JsonValueKind.Array &&
            content.EnumerateArray().Any(block => GetString(block, "type") == type);

        private static string GetText(JsonElement root)
        {
            if (!root.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            {
                return string.Empty;
            }

            return string.Join(
                Environment.NewLine,
                content.EnumerateArray()
                    .Where(block => GetString(block, "type") == "text")
                    .Select(block => GetString(block, "text"))
                    .Where(text => !string.IsNullOrWhiteSpace(text)));
        }

        private static void RequireText(JsonElement root, string message)
        {
            if (string.IsNullOrWhiteSpace(GetText(root)))
            {
                throw new InvalidDataException(message);
            }
        }

        private static void PrintBlocks(JsonElement root)
        {
            if (!root.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            {
                Console.WriteLine("Aucun bloc content.");
                return;
            }

            foreach (var block in content.EnumerateArray())
            {
                var type = GetString(block, "type") ?? "inconnu";
                switch (type)
                {
                    case "text":
                        Console.WriteLine("[text]");
                        Console.WriteLine(GetString(block, "text"));
                        break;
                    case "mcp_tool_listing":
                    case "mcp_tool_use":
                    case "tool_use":
                        Console.WriteLine($"[{type}] name={GetString(block, "name") ?? "n/a"}, id={GetString(block, "id") ?? "n/a"}");
                        break;
                    case "mcp_tool_result":
                        Console.WriteLine(
                            $"[mcp_tool_result] tool_use_id={GetString(block, "tool_use_id") ?? "n/a"}, " +
                            $"is_error={GetBoolean(block, "is_error")?.ToString() ?? "n/a"}");
                        break;
                    default:
                        Console.WriteLine($"[{type}]");
                        break;
                }
            }
        }

        private static CallUsage ReadUsage(JsonElement root, int number, string label)
        {
            var model = GetString(root, "model")
                ?? throw new InvalidDataException("Champ model absent de la réponse.");
            var stopReason = GetString(root, "stop_reason")
                ?? throw new InvalidDataException("Champ stop_reason absent de la réponse.");
            if (!root.TryGetProperty("usage", out var usage) ||
                !usage.TryGetProperty("input_tokens", out var inputElement) ||
                !inputElement.TryGetInt64(out var inputTokens) ||
                !usage.TryGetProperty("output_tokens", out var outputElement) ||
                !outputElement.TryGetInt64(out var outputTokens))
            {
                throw new InvalidDataException("Usage input_tokens/output_tokens absent de la réponse.");
            }

            return new CallUsage(number, label, model, stopReason, inputTokens, outputTokens);
        }

        private string SafeErrorSummary(string responseBody)
        {
            const int maxLength = 1_500;
            var sanitized = responseBody.Replace(entraToken, "[TOKEN REDACTED]", StringComparison.Ordinal);
            return sanitized.Length <= maxLength ? sanitized : sanitized[..maxLength] + "…";
        }

        private static string? GetString(JsonElement element, string propertyName) =>
            element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : null;

        private static bool? GetBoolean(JsonElement element, string propertyName) =>
            element.TryGetProperty(propertyName, out var property) &&
            property.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? property.GetBoolean()
                : null;

        private sealed record CallUsage(
            int Number,
            string Label,
            string Model,
            string StopReason,
            long InputTokens,
            long OutputTokens)
        {
            public long Total => InputTokens + OutputTokens;
        }

        private sealed record LocalToolCall(string Id, int Page);
        private sealed record ImagePayload(byte[] Bytes, string Sha256);
    }
}
