using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Azure.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebApp.Api.Models;
using WebApp.Api.Models.Entities;
using WebApp.Api.Services;

namespace WebApp.Api.Tests;

[TestClass]
public sealed class ClaudeConversationSummarizerTests
{
    private const string Token = "summary-test-token";
    private const string Endpoint = "https://resource.test/anthropic";
    private const string Deployment = "claude-sonnet-5";

    [TestMethod]
    public async Task SuccessfulSummary_SendsTextOnlyClaudeRequest()
    {
        var fixture = Fixture.Create(SuccessResponse("résumé généré"));
        ConversationMessage[] messages =
        [
            new() { Role = "user", Content = "Pression mesurée : 4,2 bar." },
            new() { Role = "assistant", Content = "Contrôler la référence K-17." },
        ];

        await fixture.Service.SummarizeConversationAsync("Résumé existant : code F045.", messages);

        var request = fixture.Handler.Requests.Single();
        Assert.AreEqual(new Uri($"{Endpoint}/v1/messages"), request.Uri);
        Assert.AreEqual("Bearer", request.Authorization?.Scheme);
        Assert.AreEqual(Token, request.Authorization?.Parameter);
        Assert.AreEqual("2023-06-01", request.AnthropicVersion);
        CollectionAssert.AreEqual(new[] { "https://ai.azure.com/.default" }, fixture.Credential.Scopes);

        using var document = JsonDocument.Parse(request.Body);
        var root = document.RootElement;
        Assert.AreEqual(Deployment, root.GetProperty("model").GetString());
        Assert.AreEqual(4096, root.GetProperty("max_tokens").GetInt32());
        var message = root.GetProperty("messages")[0];
        Assert.AreEqual("user", message.GetProperty("role").GetString());
        var prompt = message.GetProperty("content").GetString()!;
        StringAssert.Contains(prompt, "Résumé existant : code F045.");
        StringAssert.Contains(prompt, "user: Pression mesurée : 4,2 bar.");
        StringAssert.Contains(prompt, "assistant: Contrôler la référence K-17.");
    }

    [TestMethod]
    public async Task SummaryRequest_ContainsNoToolsOrMcp()
    {
        var fixture = Fixture.Create(SuccessResponse("résumé généré"));

        await fixture.Service.SummarizeConversationAsync(null, Messages());

        using var document = JsonDocument.Parse(fixture.Handler.Requests.Single().Body);
        var root = document.RootElement;
        CollectionAssert.AreEquivalent(
            new[] { "model", "max_tokens", "messages" },
            root.EnumerateObject().Select(property => property.Name).ToArray());
        foreach (var forbiddenProperty in new[]
                 {
                     "mcp_servers", "mcp_toolset", "tools", "tool_choice", "get_page_image",
                     "machine", "blobPrefix", "toolbox", "vectorStoreId"
                 })
        {
            Assert.IsFalse(ContainsProperty(root, forbiddenProperty), $"Unexpected JSON property: {forbiddenProperty}");
        }
    }

    [TestMethod]
    public async Task SuccessfulSummary_ReturnsTextAndUsage()
    {
        var fixture = Fixture.Create(SuccessResponse(
            "Résumé technique final.",
            id: "msg-summary-42",
            model: "claude-sonnet-5-20260929",
            inputTokens: 123,
            outputTokens: 45));

        var result = await fixture.Service.SummarizeConversationAsync(null, Messages());

        Assert.AreEqual("Résumé technique final.", result.Text);
        Assert.AreEqual(AiUsageType.ConversationSummary, result.Usage.UsageType);
        Assert.AreEqual("msg-summary-42", result.Usage.ResponseId);
        Assert.IsTrue(result.Usage.Completed);
        Assert.AreEqual(123, result.Usage.InputTokens);
        Assert.AreEqual(45, result.Usage.OutputTokens);
        Assert.AreEqual(168, result.Usage.TotalTokens);
        Assert.AreEqual("claude-sonnet-5-20260929", result.Usage.Model);
        Assert.AreEqual("response", result.Usage.ModelSource);
        Assert.AreEqual("Anthropic", result.Usage.Provider);
        Assert.AreEqual(Deployment, result.Usage.Deployment);
    }

    [TestMethod]
    public async Task SuccessfulSummary_ReturnsCacheUsageCategories()
    {
        var fixture = Fixture.Create(CacheSuccessResponse());

        var result = await fixture.Service.SummarizeConversationAsync(null, Messages());

        Assert.AreEqual(30, result.Usage.CacheReadInputTokens);
        Assert.AreEqual(50, result.Usage.CacheCreationInputTokens);
        Assert.AreEqual(20, result.Usage.CacheCreation5mInputTokens);
        Assert.AreEqual(30, result.Usage.CacheCreation1hInputTokens);
    }

    [TestMethod]
    public async Task SuccessfulSummary_PropagatesUsageCallback()
    {
        var fixture = Fixture.Create(SuccessResponse("résumé généré", inputTokens: 12, outputTokens: 3));
        var received = new List<AiResponseUsage>();

        var result = await fixture.Service.SummarizeConversationAsync(null, Messages(), onUsage: received.Add);

        Assert.HasCount(2, received);
        Assert.IsFalse(received[0].Completed);
        Assert.IsNull(received[0].ResponseId);
        Assert.IsFalse(received[0].Available);
        Assert.AreEqual(AiUsageType.ConversationSummary, received[0].UsageType);
        Assert.AreEqual("Anthropic", received[0].Provider);
        Assert.AreEqual(Deployment, received[0].Deployment);
        Assert.AreEqual(result.Usage, received[1]);
    }

    [TestMethod]
    public async Task HttpFailure_ThrowsAndDoesNotEmitBillableFinalUsage()
    {
        var fixture = Fixture.Create(new HttpResponseMessage(HttpStatusCode.BadGateway));
        var received = new List<AiResponseUsage>();

        var exception = await Assert.ThrowsExactlyAsync<HttpRequestException>(() =>
            fixture.Service.SummarizeConversationAsync(null, Messages(), onUsage: received.Add));

        Assert.AreEqual(HttpStatusCode.BadGateway, exception.StatusCode);
        Assert.HasCount(1, received);
        Assert.IsFalse(received[0].Completed);
        Assert.IsNull(received[0].ResponseId);
        Assert.IsFalse(received.Any(usage => usage.Completed));
    }

    [TestMethod]
    [DataRow("{")]
    [DataRow("{\"id\":\"msg-invalid\",\"model\":\"claude-sonnet-5\",\"content\":[],\"usage\":{\"input_tokens\":1,\"output_tokens\":1}}")]
    public async Task MalformedClaudeResponse_Throws(string responseBody)
    {
        var fixture = Fixture.Create(JsonResponse(responseBody));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            fixture.Service.SummarizeConversationAsync(null, Messages()));
    }

    [TestMethod]
    public void SummarizationPrompt_PreservesLegacyFunctionalContract()
    {
        const string existingSummary = "Résumé antérieur : code F045, page 17.";
        ConversationMessage[] messages =
        [
            new() { Role = "user", Content = "Pression mesurée : 4,2 bar." },
            new() { Role = "assistant", Content = "Vérifier la référence K-17." },
        ];

        var prompt = ClaudeConversationSummarizer.BuildSummarizationPrompt(existingSummary, messages);

        StringAssert.Contains(prompt, existingSummary);
        StringAssert.Contains(prompt, "user: Pression mesurée : 4,2 bar.");
        StringAssert.Contains(prompt, "assistant: Vérifier la référence K-17.");
        StringAssert.Contains(prompt, "600 mots");
        StringAssert.Contains(prompt, "N'invente aucune information");
        StringAssert.Contains(prompt, "Préserve les valeurs, références");
        StringAssert.Contains(prompt, "codes défaut");
        StringAssert.Contains(prompt, "numéros de page");
        StringAssert.Contains(prompt, "hypothèses déjà éliminées");
        StringAssert.Contains(prompt, "URLs signées");
    }

    private static ConversationMessage[] Messages() =>
    [
        new() { Role = "user", Content = "Symptôme constaté." },
        new() { Role = "assistant", Content = "Contrôle effectué." },
    ];

    private static HttpResponseMessage SuccessResponse(
        string text,
        string id = "msg-summary",
        string model = Deployment,
        int inputTokens = 10,
        int outputTokens = 5) =>
        JsonResponse(JsonSerializer.Serialize(new
        {
            id,
            model,
            stop_reason = "end_turn",
            content = new object[] { new { type = "text", text } },
            usage = new { input_tokens = inputTokens, output_tokens = outputTokens }
        }));

    private static HttpResponseMessage CacheSuccessResponse() =>
        JsonResponse(JsonSerializer.Serialize(new
        {
            id = "msg-summary-cache",
            model = Deployment,
            stop_reason = "end_turn",
            content = new object[] { new { type = "text", text = "résumé" } },
            usage = new
            {
                input_tokens = 10,
                output_tokens = 5,
                cache_read_input_tokens = 30,
                cache_creation_input_tokens = 50,
                cache_creation = new
                {
                    ephemeral_5m_input_tokens = 20,
                    ephemeral_1h_input_tokens = 30
                }
            }
        }));

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private static bool ContainsProperty(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase) ||
                    ContainsProperty(property.Value, propertyName))
                {
                    return true;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            return element.EnumerateArray().Any(item => ContainsProperty(item, propertyName));
        }

        return false;
    }

    private sealed class Fixture
    {
        private Fixture(
            ClaudeConversationSummarizer service,
            RecordingHandler handler,
            FakeTokenCredential credential)
        {
            Service = service;
            Handler = handler;
            Credential = credential;
        }

        public ClaudeConversationSummarizer Service { get; }
        public RecordingHandler Handler { get; }
        public FakeTokenCredential Credential { get; }

        public static Fixture Create(HttpResponseMessage response)
        {
            var handler = new RecordingHandler(response);
            var credential = new FakeTokenCredential();
            var service = new ClaudeConversationSummarizer(
                new SingleClientFactory(new HttpClient(handler)),
                credential,
                Options.Create(new ClaudeDirectChatOptions
                {
                    FoundryAnthropicEndpoint = Endpoint,
                    Deployment = Deployment
                }),
                NullLogger<ClaudeConversationSummarizer>.Instance);
            return new Fixture(service, handler, credential);
        }
    }

    private sealed class RecordingHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(new CapturedRequest(
                request.RequestUri!,
                request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken),
                request.Headers.Authorization,
                request.Headers.TryGetValues("anthropic-version", out var versions) ? versions.Single() : null));
            return response;
        }
    }

    private sealed record CapturedRequest(
        Uri Uri,
        string Body,
        AuthenticationHeaderValue? Authorization,
        string? AnthropicVersion);

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            Assert.AreEqual(ClaudeConversationSummarizer.HttpClientName, name);
            return client;
        }
    }

    private sealed class FakeTokenCredential : TokenCredential
    {
        public string[] Scopes { get; private set; } = [];

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public override ValueTask<AccessToken> GetTokenAsync(
            TokenRequestContext requestContext,
            CancellationToken cancellationToken)
        {
            Scopes = requestContext.Scopes.ToArray();
            return ValueTask.FromResult(new AccessToken(Token, DateTimeOffset.UtcNow.AddHours(1)));
        }
    }
}
