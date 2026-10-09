using Azure.Core;
using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebApp.Api.Models;
using WebApp.Api.Models.Entities;
using WebApp.Api.Services;

namespace WebApp.Api.Tests;

/// <summary>
/// Explicitly opt-in integration test. It calls only Claude Messages, the already-provisioned
/// Toolbox MCP endpoint, and Blob Storage. It never starts WebApp.Api or touches SQL/billing.
///
/// PowerShell:
/// $env:RUN_CLAUDE_DIRECT_MANUAL_TEST = '1'
/// $env:CLAUDE_DIRECT_MANUAL_PROJECT_ENDPOINT = 'https://.../api/projects/...'
/// $env:CLAUDE_DIRECT_MANUAL_BLOB_PREFIX = 'company/machine'
/// dotnet test backend/WebApp.Api.Tests -- --filter "TestCategory=ClaudeDirectManual"
/// </summary>
[TestClass]
public sealed class ClaudeDirectChatServiceManualTests
{
    [TestMethod]
    [TestCategory("ClaudeDirectManual")]
    public async Task Dx10z_ReproducesMcpAndPage75ImageFlow()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("RUN_CLAUDE_DIRECT_MANUAL_TEST"),
                "1",
                StringComparison.Ordinal))
        {
            Assert.Inconclusive("Set RUN_CLAUDE_DIRECT_MANUAL_TEST=1 to run this real-resource test.");
        }

        TokenCredential credential = new DefaultAzureCredential();
        var blobServiceClient = new BlobServiceClient(
            new Uri(Environment.GetEnvironmentVariable("CLAUDE_DIRECT_MANUAL_BLOB_SERVICE_URI")
                ?? "https://stknowledgeia.blob.core.windows.net/"),
            credential);
        var blobReader = new BlobStorageService(blobServiceClient, new PdfPigPageCounter());
        var markerReader = new AzureClaudeDirectToolboxMarkerReader(blobServiceClient);
        var promptProvider = new TechnicalAssistantPromptProvider(AppContext.BaseDirectory);
        var resolver = new ClaudeDirectMachineConfigurationResolver(
            markerReader,
            promptProvider,
            NullLogger<ClaudeDirectMachineConfigurationResolver>.Instance);
        var requestFactory = new ClaudeDirectChatRequestFactory(
            resolver,
            new GlobalCommercialPolicyProvider(Options.Create(new CommercialPolicyOptions())));
        var pageMapResolver = new TechnicalPageMapResolver(
            blobReader,
            NullLogger<TechnicalPageMapResolver>.Instance);
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        var service = new ClaudeDirectChatService(
            new ManualHttpClientFactory(httpClient),
            credential,
            blobReader,
            new TechnicalSourceReferenceResolver(pageMapResolver),
            Options.Create(new ClaudeDirectChatOptions
            {
                FoundryAnthropicEndpoint = "https://diaglink-foundry-prod.services.ai.azure.com/anthropic",
                Deployment = "claude-sonnet-5",
                MaxMessageCalls = 4,
                MaxImageBytes = 5 * 1024 * 1024
            }),
            NullLogger<ClaudeDirectChatService>.Instance);

        var request = await requestFactory.CreateAsync(
            LoadServerResolvedMachine(),
            [new ClaudeDirectMessage(
                "user",
                "Recherche dans le manuel comment remplacer le filtre hydraulique du DX10z. " +
                "Puis examine visuellement la page PDF 75 et distingue ce que dit le manuel " +
                "de ce que tu confirmes visuellement.")]);
        var result = await service.CompleteAsync(request);

        Console.WriteLine(result.FinalText);
        foreach (var usage in result.Calls)
        {
            Console.WriteLine(
                $"Call {usage.CallNumber}: input={usage.InputTokens}, output={usage.OutputTokens}, " +
                $"total={usage.TotalTokens}, model={usage.Model}, stop={usage.StopReason}, " +
                $"responseId={usage.ResponseId}, requestId={usage.RequestId}");
        }
        Console.WriteLine(
            $"Aggregate: input={result.Usage.InputTokens}, output={result.Usage.OutputTokens}, total={result.Usage.TotalTokens}");
        foreach (var toolUse in result.ToolUses)
        {
            Console.WriteLine($"ToolUse: id={toolUse.Id}, name={toolUse.Name}, input={toolUse.InputJson}");
        }
        foreach (var error in result.Errors)
        {
            Console.WriteLine(
                $"Error: code={error.Code}, recovered={error.Recovered}, call={error.CallNumber}, " +
                $"toolUseId={error.ToolUseId}, message={error.Message}");
        }
        foreach (var resolution in result.DocumentResolutions)
        {
            Console.WriteLine(
                $"DocumentResolution: requested={resolution.RequestedDocumentId ?? "<absent>"}, " +
                $"resolved={resolution.ResolvedDocumentId ?? "<none>"}, mode={resolution.ResolutionMode}");
        }

        Assert.IsFalse(string.IsNullOrWhiteSpace(result.FinalText));
        Assert.IsTrue(result.McpCalls.Any(call => call.Name == "file_search"));
        Assert.IsTrue(result.McpCalls.Any(call => call.Name == "mcp_tool_result" && call.IsError == false));
        Assert.IsTrue(result.ToolUses.Any(call => call.Name == "get_page_image"));
        Assert.IsTrue(result.Visuals.Any(visual => visual.Page == 75 && visual.AssetType == "full"));
        Assert.AreEqual(2, result.Calls.Count);
        var documentResolution = result.DocumentResolutions.Single();
        Assert.AreNotEqual(documentResolution.RequestedDocumentId, documentResolution.ResolvedDocumentId);
        Assert.IsTrue(documentResolution.ResolutionMode is
            ClaudeDirectDocumentResolutionMode.UniqueMcpDocument or
            ClaudeDirectDocumentResolutionMode.UniqueExistingPage);
        Assert.IsFalse(result.Errors.Any(error => !error.Recovered));
    }

    private static Machine LoadServerResolvedMachine()
    {
        var projectEndpoint = RequiredEnvironment("CLAUDE_DIRECT_MANUAL_PROJECT_ENDPOINT");
        var blobPrefix = RequiredEnvironment("CLAUDE_DIRECT_MANUAL_BLOB_PREFIX");
        var machineName = Environment.GetEnvironmentVariable("CLAUDE_DIRECT_MANUAL_MACHINE_NAME") ?? "DX10z";
        var reference = Environment.GetEnvironmentVariable("CLAUDE_DIRECT_MANUAL_MACHINE_REFERENCE") ?? machineName;
        var machineIdText = Environment.GetEnvironmentVariable("CLAUDE_DIRECT_MANUAL_MACHINE_ID");

        return new Machine
        {
            Id = Guid.TryParse(machineIdText, out var machineId) ? machineId : Guid.Empty,
            CompanyId = Guid.Empty,
            Name = machineName,
            Reference = reference,
            Status = "active",
            ProjectEndpoint = projectEndpoint,
            BlobPrefix = blobPrefix,
            VectorStoreId = Environment.GetEnvironmentVariable("CLAUDE_DIRECT_MANUAL_SQL_VECTOR_STORE_ID")
        };
    }

    private static string RequiredEnvironment(string name) =>
        Environment.GetEnvironmentVariable(name)
        ?? throw new AssertInconclusiveException($"Set {name} from the server-resolved Machine before running the manual test.");

    private sealed class ManualHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
}
