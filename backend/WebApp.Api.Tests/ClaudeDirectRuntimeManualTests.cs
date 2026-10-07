using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebApp.Api.Data;
using WebApp.Api.Models;
using WebApp.Api.Models.Entities;
using WebApp.Api.Repositories;
using WebApp.Api.Services;

namespace WebApp.Api.Tests;

[TestClass]
public sealed class ClaudeDirectRuntimeManualTests
{
    private static readonly Guid Dx10zMachineId =
        Guid.Parse("8f92a818-86bb-48b5-883a-f26999510075");
    private const string Dx10zBlobPrefix = "develon/excavatrice-doosan-dx10z";
    private const string Dx10zProjectEndpoint =
        "https://diaglink-foundry-prod.services.ai.azure.com/api/projects/develon";
    private const string OfflineProjectEndpoint =
        "https://resource.test/api/projects/company";
    private const string Question =
        "Recherche dans le manuel comment remplacer le filtre hydraulique du DX10z. " +
        "Puis examine visuellement la page PDF 75 et distingue ce que dit le manuel " +
        "de ce que tu confirmes visuellement.";

    [TestMethod]
    public void ClaudeDirectManualHost_CanResolveAllServices()
    {
        using var factory = new RuntimeApplicationFactory(null, offlineValidation: true);
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;

        Assert.IsNotNull(services.GetRequiredService<MachineAssistantResolutionService>());
        Assert.IsNotNull(services.GetRequiredService<IClaudeDirectChatRuntime>());
        Assert.IsNotNull(services.GetRequiredService<IClaudeDirectChatRequestFactory>());
        Assert.IsNotNull(services.GetRequiredService<IClaudeDirectChatService>());
        Assert.IsNotNull(services.GetRequiredService<ConversationHistoryRepository>());
        Assert.IsNotNull(services.GetRequiredService<AiUsagePersistenceBillingService>());
        Assert.IsNotNull(services.GetRequiredService<MachineRequestStorageService>());
        var conversationSummarizer = services.GetRequiredService<IConversationSummarizer>();
        Assert.IsInstanceOfType<ClaudeConversationSummarizer>(conversationSummarizer);
        Assert.AreEqual(0, factory.TokenCredential.AccessAttempts);
        Assert.AreEqual(0, factory.MachineRequestBlobClient.AccessAttempts);
    }

    [TestMethod]
    public async Task ClaudeDirectManualAuth_ValidBearerAndDiagLinkReachProtectedRoute()
    {
        await using var factory = new RuntimeApplicationFactory(null, offlineValidation: true);
        var schemes = factory.Services.GetRequiredService<IAuthenticationSchemeProvider>();
        Assert.IsNotNull(await schemes.GetSchemeAsync(
            Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme));
        Assert.IsNotNull(await schemes.GetSchemeAsync(DiagLinkAuthenticationDefaults.Scheme));

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var bearerToken = factory.AddIdentity(DiagLinkRoles.SuperAdmin, companyId, userId);
        using var bearerRequest = new HttpRequestMessage(HttpMethod.Get, "/api/chat/visuals/1");
        bearerRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        using var bearerResponse = await client.SendAsync(bearerRequest);
        Assert.AreEqual(HttpStatusCode.NotFound, bearerResponse.StatusCode);

        var sessionToken = factory.AddIdentity(DiagLinkRoles.SuperAdmin, companyId, userId);
        using var sessionRequest = new HttpRequestMessage(HttpMethod.Get, "/api/chat/visuals/1");
        sessionRequest.Headers.Add(DiagLinkAuthenticationDefaults.HeaderName, sessionToken);
        using var sessionResponse = await client.SendAsync(sessionRequest);
        Assert.AreEqual(HttpStatusCode.NotFound, sessionResponse.StatusCode);

        Assert.AreEqual(0, factory.TokenCredential.AccessAttempts);
        Assert.AreEqual(0, factory.MachineRequestBlobClient.AccessAttempts);
    }

    [TestMethod]
    public async Task ClaudeDirectManualAuth_BearerWithoutScopeIsForbiddenNotServerError()
    {
        await using var factory = new RuntimeApplicationFactory(null, offlineValidation: true);
        var token = factory.AddIdentity(
            DiagLinkRoles.SuperAdmin,
            Guid.NewGuid(),
            Guid.NewGuid(),
            includeChatScope: false);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/chat/visuals/1");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.AreNotEqual(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.AreEqual(0, factory.TokenCredential.AccessAttempts);
        Assert.AreEqual(0, factory.MachineRequestBlobClient.AccessAttempts);
    }

    [TestMethod]
    public async Task ConfiguredMachine_ReachesClaudeDirectRuntime()
    {
        var machineId = Guid.NewGuid();
        await using var factory = new RuntimeApplicationFactory(machineId, offlineValidation: true);
        var machine = await SeedOfflineMachineAsync(factory.Services, machineId);

        var token = factory.AddIdentity(DiagLinkRoles.SuperAdmin, machine.CompanyId, Guid.NewGuid());
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat/stream")
        {
            Content = JsonContent.Create(new ChatRequest
            {
                Message = "Question de test ClaudeDirect",
                MachineId = machine.Id
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(
            HttpStatusCode.OK,
            response.StatusCode,
            $"A configured machine must reach Claude Direct. Body: {body}");
        Assert.AreEqual(1, factory.OfflineClaudeDirectChatService.CallCount);
        Assert.AreEqual(
            "company/machine/.foundry/toolbox.json",
            factory.OfflineToolboxMarkerReader.RequestedBlobNames.Single());
        StringAssert.Contains(body, "\"type\":\"done\"");
    }

    [TestMethod]
    public async Task ClaudeDirectAgentMetadata_UsesLocalMachineMetadata()
    {
        var machineId = Guid.NewGuid();
        await using var factory = new RuntimeApplicationFactory(machineId, offlineValidation: true);
        var machine = await SeedOfflineMachineAsync(factory.Services, machineId);
        var token = factory.AddIdentity(DiagLinkRoles.SuperAdmin, machine.CompanyId, Guid.NewGuid());
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/agent?machineId={machine.Id}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.SendAsync(request);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual($"claude-direct-{machine.Id:N}", body.RootElement.GetProperty("id").GetString());
        Assert.AreEqual($"Assistant-Technique-{machine.Name}", body.RootElement.GetProperty("name").GetString());
        Assert.AreEqual("claude-sonnet-5", body.RootElement.GetProperty("model").GetString());
        Assert.AreEqual("Avatar_Default.svg", body.RootElement.GetProperty("metadata").GetProperty("logo").GetString());
        var capabilities = body.RootElement.GetProperty("capabilities");
        Assert.IsTrue(capabilities.GetProperty("imageAttachments").GetBoolean());
        Assert.IsFalse(capabilities.GetProperty("fileAttachments").GetBoolean());
    }

    [TestMethod]
    public async Task ClaudeDirectAgentMetadata_DoesNotReadToolboxOrBlob()
    {
        var machineId = Guid.NewGuid();
        await using var factory = new RuntimeApplicationFactory(machineId, offlineValidation: true);
        var machine = await SeedOfflineMachineAsync(factory.Services, machineId);
        var token = factory.AddIdentity(DiagLinkRoles.SuperAdmin, machine.CompanyId, Guid.NewGuid());
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/agent?machineId={machine.Id}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.HasCount(0, factory.OfflineToolboxMarkerReader.RequestedBlobNames);
        Assert.AreEqual(0, factory.TokenCredential.AccessAttempts);
        Assert.AreEqual(0, factory.MachineRequestBlobClient.AccessAttempts);
    }

    [TestMethod]
    public async Task AgentMetadata_InaccessibleMachine_RemainsNotFound()
    {
        var machineId = Guid.NewGuid();
        await using var factory = new RuntimeApplicationFactory(machineId, offlineValidation: true);
        _ = await SeedOfflineMachineAsync(factory.Services, machineId);
        var token = factory.AddIdentity(DiagLinkRoles.CompanyAdmin, Guid.NewGuid(), Guid.NewGuid());
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/agent?machineId={machineId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task AgentMetadata_IneligibleMachine_RemainsNotConfigured()
    {
        var machineId = Guid.NewGuid();
        await using var factory = new RuntimeApplicationFactory(machineId, offlineValidation: true);
        var machine = await SeedOfflineMachineAsync(factory.Services, machineId);
        await UpdateMachineAsync(factory.Services, machineId, item => item.Status = "inactive");
        var token = factory.AddIdentity(DiagLinkRoles.SuperAdmin, machine.CompanyId, Guid.NewGuid());
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/agent?machineId={machineId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
        StringAssert.Contains(body, "assistant_not_configured");
    }

    [TestMethod]
    public async Task ClaudeDirectUserImage_ReachesRuntimeAsValidatedEphemeralImage()
    {
        var machineId = Guid.NewGuid();
        await using var factory = new RuntimeApplicationFactory(machineId, offlineValidation: true);
        var machine = await SeedOfflineMachineAsync(factory.Services, machineId);
        var token = factory.AddIdentity(DiagLinkRoles.SuperAdmin, machine.CompanyId, Guid.NewGuid());
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat/stream")
        {
            Content = JsonContent.Create(new ChatRequest
            {
                Message = "Inspecte cette image",
                MachineId = machine.Id,
                ImageDataUris = ["data:image/png;base64,iVBORw0KGgo="]
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var image = factory.OfflineClaudeDirectChatService.Request!.Messages.Single().Images!.Single();
        Assert.AreEqual("image/png", image.MediaType);
        Assert.AreEqual("iVBORw0KGgo=", image.Base64Data);
        Assert.AreEqual(0, factory.TokenCredential.AccessAttempts);
        Assert.AreEqual(0, factory.MachineRequestBlobClient.AccessAttempts);
    }

    [TestMethod]
    public async Task ClaudeDirectFileAttachment_IsRejectedBeforeClaudeCall()
    {
        var machineId = Guid.NewGuid();
        await using var factory = new RuntimeApplicationFactory(machineId, offlineValidation: true);
        var machine = await SeedOfflineMachineAsync(factory.Services, machineId);
        var token = factory.AddIdentity(DiagLinkRoles.SuperAdmin, machine.CompanyId, Guid.NewGuid());
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat/stream")
        {
            Content = JsonContent.Create(new ChatRequest
            {
                Message = "Lis ce PDF",
                MachineId = machine.Id,
                FileDataUris =
                [
                    new FileAttachment
                    {
                        DataUri = "data:application/pdf;base64,JVBERg==",
                        FileName = "manual.pdf",
                        MimeType = "application/pdf"
                    }
                ]
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        StringAssert.Contains(body, "chat_file_attachments_not_supported");
        StringAssert.Contains(body, "Les fichiers PDF et texte ne sont pas encore pris en charge dans le chat.");
        Assert.AreEqual(0, factory.OfflineClaudeDirectChatService.CallCount);
    }

    [TestMethod]
    public async Task ClaudeDirectInvalidImage_IsRejectedBeforeClaudeCall()
    {
        var machineId = Guid.NewGuid();
        await using var factory = new RuntimeApplicationFactory(machineId, offlineValidation: true);
        var machine = await SeedOfflineMachineAsync(factory.Services, machineId);
        var token = factory.AddIdentity(DiagLinkRoles.SuperAdmin, machine.CompanyId, Guid.NewGuid());
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat/stream")
        {
            Content = JsonContent.Create(new ChatRequest
            {
                Message = "Inspecte cette image",
                MachineId = machine.Id,
                ImageDataUris = ["data:image/png;base64,not-base64!"]
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        StringAssert.Contains(body, "chat_image_invalid_base64");
        Assert.AreEqual(0, factory.OfflineClaudeDirectChatService.CallCount);
    }

    [TestMethod]
    public async Task EveryEligibleMachine_UsesClaudeDirect()
    {
        var machineId = Guid.NewGuid();
        await using var factory = new RuntimeApplicationFactory(null, offlineValidation: true);
        var machine = await SeedOfflineMachineAsync(factory.Services, machineId);
        var token = factory.AddIdentity(DiagLinkRoles.SuperAdmin, machine.CompanyId, Guid.NewGuid());
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat/stream")
        {
            Content = JsonContent.Create(new ChatRequest
            {
                Message = "Question de test Claude Direct",
                MachineId = machine.Id
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, body);
        Assert.AreEqual(1, factory.OfflineClaudeDirectChatService.CallCount);
        Assert.HasCount(1, factory.OfflineToolboxMarkerReader.RequestedBlobNames);
    }

    [TestMethod]
    public async Task LegacyConversationWithoutMachineId_ReturnsConflictBeforeCreditOrRuntime()
    {
        await using var factory = new RuntimeApplicationFactory(null, offlineValidation: true);
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        const string conversationId = "legacy-conversation-without-machine";
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DiagLinkDbContext>();
            db.Conversations.Add(new Conversation
            {
                Id = Guid.NewGuid(),
                ConversationPublicId = conversationId,
                UserObjectId = userId.ToString(),
                MachineId = null,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var token = factory.AddIdentity(DiagLinkRoles.SuperAdmin, companyId, userId);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat/stream")
        {
            Content = JsonContent.Create(new ChatRequest
            {
                Message = "Tentative de reprise",
                ConversationId = conversationId
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode, body);
        StringAssert.Contains(body, "legacy_conversation_not_resumable");
        StringAssert.Contains(body, "Cette ancienne conversation ne peut plus Ãªtre poursuivie");
        Assert.AreEqual(0, factory.OfflineClaudeDirectChatService.CallCount);
        Assert.HasCount(0, factory.OfflineToolboxMarkerReader.RequestedBlobNames);
    }

    [TestMethod]
    public async Task MachineListAndDetail_ClaudeDirectConfiguration_IsConfigured()
    {
        var machineId = Guid.NewGuid();
        await using var factory = new RuntimeApplicationFactory(machineId, offlineValidation: true);
        var machine = await SeedOfflineMachineAsync(factory.Services, machineId);
        var token = factory.AddIdentity(DiagLinkRoles.SuperAdmin, machine.CompanyId, Guid.NewGuid());
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var listResponse = await client.GetAsync("/api/machines");
        listResponse.EnsureSuccessStatusCode();
        using var listDocument = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync());
        var listMachine = listDocument.RootElement.EnumerateArray()
            .Single(item => item.GetProperty("id").GetString() == machine.Id.ToString());

        using var detailResponse = await client.GetAsync($"/api/machines/{machine.Id}");
        detailResponse.EnsureSuccessStatusCode();
        using var detailDocument = JsonDocument.Parse(await detailResponse.Content.ReadAsStringAsync());

        Assert.IsTrue(listMachine.GetProperty("hasAssistantConfigured").GetBoolean());
        Assert.IsTrue(detailDocument.RootElement.GetProperty("hasAssistantConfigured").GetBoolean());
        Assert.AreEqual(0, factory.OfflineToolboxMarkerReader.RequestedBlobNames.Count);
        Assert.AreEqual(0, factory.OfflineClaudeDirectChatService.CallCount);
    }

    [TestMethod]
    [TestCategory("ClaudeDirectSqlDiscovery")]
    public async Task Dx10zMachineLookup_FindsProvisionedMachineByExactId()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("RUN_CLAUDE_DIRECT_SQL_DISCOVERY"),
                "1",
                StringComparison.Ordinal))
        {
            Assert.Inconclusive(
                "Set RUN_CLAUDE_DIRECT_SQL_DISCOVERY=1 to run the read-only SQL lookup.");
        }

        await using var factory = new RuntimeApplicationFactory(null);
        var machine = await LoadDx10zAsync(factory.Services);

        Console.WriteLine($"Id={machine.Id}");
        Console.WriteLine($"CompanyId={machine.CompanyId}");
        Console.WriteLine($"Name={machine.Name}");
        Console.WriteLine($"Reference={machine.Reference ?? "<null>"}");
        Console.WriteLine($"BlobPrefix={machine.BlobPrefix}");
        Console.WriteLine($"ProjectEndpoint={machine.ProjectEndpoint}");
        Console.WriteLine($"VectorStoreId={machine.VectorStoreId ?? "<null>"}");
        Console.WriteLine($"Status={machine.Status}");

        Assert.AreEqual(Dx10zBlobPrefix, machine.BlobPrefix);
        Assert.AreEqual(Dx10zProjectEndpoint, machine.ProjectEndpoint);
    }

    [TestMethod]
    [TestCategory("ClaudeDirectRuntimeManual")]
    public async Task Dx10z_UsesClaudeDirectThroughChatStreamAndPersistsOnce()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("RUN_CLAUDE_DIRECT_RUNTIME_MANUAL_TEST"),
                "1",
                StringComparison.Ordinal))
        {
            Assert.Inconclusive(
                "Set RUN_CLAUDE_DIRECT_RUNTIME_MANUAL_TEST=1 to run the real route validation.");
        }

        await using var discoveryFactory = new RuntimeApplicationFactory(null);
        var machine = await LoadDx10zAsync(discoveryFactory.Services);
        var before = await FinancialSnapshot.LoadAsync(discoveryFactory.Services, machine);

        await using var factory = new RuntimeApplicationFactory(machine.Id);
        var token = factory.AddIdentity(DiagLinkRoles.SuperAdmin, machine.CompanyId, Guid.NewGuid());
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        client.Timeout = TimeSpan.FromMinutes(5);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat/stream")
        {
            Content = JsonContent.Create(new ChatRequest { Message = Question, MachineId = machine.Id })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        var body = await response.Content.ReadAsStringAsync();
        response.EnsureSuccessStatusCode();
        var events = ReadEvents(body);

        Assert.AreEqual("conversationId", events.First().GetProperty("type").GetString());
        Assert.AreEqual("done", events.Last().GetProperty("type").GetString());
        Assert.IsFalse(events.Any(item => item.GetProperty("type").GetString() == "error"));
        var conversationId = events.First().GetProperty("conversationId").GetString();
        Assert.IsNotNull(conversationId);
        StringAssert.StartsWith(conversationId, "claude-direct-");

        var toolNames = events
            .Where(item => item.GetProperty("type").GetString() == "toolUse")
            .Select(item => item.GetProperty("toolName").GetString())
            .ToArray();
        CollectionAssert.Contains(toolNames, "file_search");
        CollectionAssert.Contains(toolNames, "get_page_image");

        var finalText = string.Concat(events
            .Where(item => item.GetProperty("type").GetString() == "chunk")
            .Select(item => item.GetProperty("content").GetString()));
        Assert.IsTrue(finalText.Contains("manuel", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(finalText.Contains("visuel", StringComparison.OrdinalIgnoreCase));

        var usageEvent = events.Single(item => item.GetProperty("type").GetString() == "usage");
        Assert.IsTrue(usageEvent.GetProperty("available").GetBoolean());
        Assert.IsTrue(usageEvent.GetProperty("completed").GetBoolean());

        var visualEvent = events.Single(item => item.GetProperty("type").GetString() == "visuals");
        var visual = visualEvent.GetProperty("visuals").EnumerateArray().Single();
        Assert.AreEqual(75, visual.GetProperty("page").GetInt32());
        Assert.AreEqual("full", visual.GetProperty("assetType").GetString());
        var visualId = visual.GetProperty("id").GetInt64();

        var capture = factory.Services.GetRequiredService<ClaudeResultCapture>();
        var result = capture.Result ?? throw new AssertFailedException("Claude Direct result was not captured.");
        Assert.IsTrue(result.Calls.Count <= 2, $"Expected at most two Messages calls, got {result.Calls.Count}.");
        Assert.IsTrue(result.McpCalls.Any(call => call.Name == "file_search"));
        Assert.IsTrue(result.McpCalls.Any(call => call.Name == "mcp_tool_result" && call.IsError == false));
        Assert.IsTrue(result.ToolUses.Any(call => call.Name == "get_page_image"));
        Assert.IsTrue(result.Visuals.Any(item => item.Page == 75 && item.AssetType == "full"));

        using var visualRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/chat/visuals/{visualId}");
        visualRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var visualResponse = await client.SendAsync(visualRequest);
        visualResponse.EnsureSuccessStatusCode();
        Assert.AreEqual("image/png", visualResponse.Content.Headers.ContentType?.MediaType);
        var png = await visualResponse.Content.ReadAsByteArrayAsync();
        CollectionAssert.AreEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, png.Take(8).ToArray());

        var persisted = await LoadPersistenceAsync(factory.Services, conversationId);
        Assert.AreEqual(machine.Id, persisted.Conversation.MachineId);
        Assert.AreEqual(2, persisted.Messages.Count);
        Assert.AreEqual("user", persisted.Messages[0].Role);
        Assert.AreEqual(Question, persisted.Messages[0].Content);
        Assert.AreEqual("assistant", persisted.Messages[1].Role);
        Assert.AreEqual(finalText, persisted.Messages[1].Content);
        Assert.AreEqual(1, persisted.Messages[1].Visuals.Count);
        Assert.AreEqual(75, persisted.Messages[1].Visuals.Single().Page);

        Assert.HasCount(1, persisted.Usages);
        var usage = persisted.Usages.Single();
        Assert.AreEqual(AiUsageType.ChatResponse, usage.UsageType);
        Assert.AreEqual("Anthropic", usage.Provider);
        Assert.AreEqual(result.Model, usage.Model);
        Assert.AreEqual(result.Usage.InputTokens, (long)usage.InputTokens!.Value);
        Assert.AreEqual(result.Usage.OutputTokens, (long)usage.OutputTokens!.Value);
        Assert.AreEqual(result.Usage.TotalTokens, (long)usage.TotalTokens!.Value);
        Assert.IsFalse(persisted.Usages.Any(item => item.UsageType == AiUsageType.VisionTool));

        var after = await FinancialSnapshot.LoadAsync(factory.Services, machine);
        var ledger = await LoadLedgerAsync(factory.Services, usage.Id);
        Assert.IsTrue(ledger.Count is >= 1 and <= 2);
        Assert.AreEqual(ledger.Count, ledger.Select(item => item.BucketType).Distinct().Count());
        Assert.IsTrue(ledger.Sum(item => item.RealAiCost ?? 0m) > 0m);
        var includedDebit = ledger.SingleOrDefault(item => item.BucketType == "MachineIncluded");
        if (includedDebit is not null)
        {
            Assert.IsNotNull(before.IncludedUsed);
            Assert.AreEqual(
                before.IncludedUsed!.Value + includedDebit.RealAiCost!.Value,
                after.IncludedUsed);
        }
        var walletDebit = ledger.SingleOrDefault(item => item.BucketType == "CompanyWallet");
        if (walletDebit is not null)
        {
            Assert.IsNotNull(before.WalletBalance);
            Assert.AreEqual(
                before.WalletBalance!.Value - walletDebit.CommercialCreditAmount!.Value,
                after.WalletBalance);
        }

        Console.WriteLine($"Runtime=ClaudeDirect; MachineId={machine.Id}; ConversationId={conversationId}");
        foreach (var call in result.Calls)
            Console.WriteLine($"Call {call.CallNumber}: input={call.InputTokens}, output={call.OutputTokens}, total={call.TotalTokens}, model={call.Model}");
        Console.WriteLine($"Aggregate: input={result.Usage.InputTokens}, output={result.Usage.OutputTokens}, total={result.Usage.TotalTokens}");
        foreach (var resolution in result.DocumentResolutions)
            Console.WriteLine($"Resolution: requested={resolution.RequestedDocumentId}, resolved={resolution.ResolvedDocumentId}, mode={resolution.ResolutionMode}");
        Console.WriteLine($"RecordedCostEur={ledger.Sum(item => item.RealAiCost ?? 0m)}");
        Console.WriteLine($"IncludedUsedBefore={before.IncludedUsed}; IncludedUsedAfter={after.IncludedUsed}");
        Console.WriteLine($"WalletBefore={before.WalletBalance}; WalletAfter={after.WalletBalance}");
    }

    private static async Task<Machine> LoadDx10zAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DiagLinkDbContext>();
        var machines = await db.Machines.AsNoTracking()
            .Where(item => item.Id == Dx10zMachineId)
            .ToListAsync();
        Assert.HasCount(1, machines, "Expected the exact server-side DX10z Machine.Id.");
        return machines[0];
    }

    private static async Task<Machine> SeedOfflineMachineAsync(IServiceProvider services, Guid machineId)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DiagLinkDbContext>();
        var now = DateTime.UtcNow;
        var company = new Company
        {
            Id = Guid.NewGuid(),
            Name = "Offline Company",
            Status = "active",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        var machine = new Machine
        {
            Id = machineId,
            CompanyId = company.Id,
            Name = "Offline Machine",
            Status = "active",
            ProjectEndpoint = OfflineProjectEndpoint,
            BlobPrefix = "company/machine",
            VectorStoreId = "vs_marker123",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        db.Companies.Add(company);
        db.Machines.Add(machine);
        db.CompanyWallets.Add(new CompanyWallet
        {
            CompanyId = company.Id,
            Balance = 100m,
            Currency = "EUR",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });
        await db.SaveChangesAsync();
        return machine;
    }

    private static async Task UpdateMachineAsync(
        IServiceProvider services,
        Guid machineId,
        Action<Machine> update)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DiagLinkDbContext>();
        var machine = await db.Machines.SingleAsync(item => item.Id == machineId);
        update(machine);
        machine.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    private static async Task<PersistenceSnapshot> LoadPersistenceAsync(
        IServiceProvider services,
        string conversationId)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DiagLinkDbContext>();
        var conversation = await db.Conversations.AsNoTracking()
            .SingleAsync(item => item.ConversationPublicId == conversationId);
        var messages = await db.ConversationMessages.AsNoTracking()
            .Include(item => item.Visuals)
            .Where(item => item.ConversationId == conversation.Id)
            .OrderBy(item => item.Id)
            .ToListAsync();
        var usages = await db.AiUsageRecords.AsNoTracking()
            .Where(item => item.ConversationPublicId == conversationId)
            .ToListAsync();
        return new(conversation, messages, usages);
    }

    private static async Task<List<CreditLedgerEntry>> LoadLedgerAsync(IServiceProvider services, Guid usageId)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DiagLinkDbContext>();
        return await db.CreditLedger.AsNoTracking()
            .Where(item => item.AiUsageRecordId == usageId)
            .ToListAsync();
    }

    private static List<JsonElement> ReadEvents(string body) => body
        .Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
        .Where(item => item.StartsWith("data: ", StringComparison.Ordinal))
        .Select(item => JsonDocument.Parse(item["data: ".Length..]).RootElement.Clone())
        .ToList();

    private sealed record PersistenceSnapshot(
        Conversation Conversation,
        List<ConversationMessage> Messages,
        List<AiUsageRecord> Usages);

    private sealed record FinancialSnapshot(decimal? IncludedUsed, decimal? WalletBalance)
    {
        public static async Task<FinancialSnapshot> LoadAsync(IServiceProvider services, Machine machine)
        {
            using var scope = services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DiagLinkDbContext>();
            var now = DateTime.UtcNow;
            var included = await db.MachineBillingPeriods.AsNoTracking()
                .Where(item => item.MachineId == machine.Id &&
                    item.PeriodStartUtc <= now && now < item.PeriodEndUtc && item.Status == "Active")
                .Select(item => (decimal?)item.IncludedAiUsedRealCost)
                .SingleOrDefaultAsync();
            var wallet = await db.CompanyWallets.AsNoTracking()
                .Where(item => item.CompanyId == machine.CompanyId)
                .Select(item => (decimal?)item.Balance)
                .SingleOrDefaultAsync();
            return new(included, wallet);
        }
    }

    private sealed class RuntimeApplicationFactory(
        Guid? enabledMachineId,
        bool offlineValidation = false)
        : WebApplicationFactory<BlobStorageService>
    {
        private readonly RuntimeIdentityStore identities = new();
        private readonly string offlineDatabaseName = $"claude-direct-host-validation-{Guid.NewGuid():N}";
        public NoExternalTokenCredential TokenCredential { get; } = new();
        public NoExternalMachineRequestBlobClient MachineRequestBlobClient { get; } = new();
        public OfflineToolboxMarkerReader OfflineToolboxMarkerReader { get; } = new();
        public OfflineClaudeDirectChatService OfflineClaudeDirectChatService { get; } = new();

        public string AddIdentity(
            string role,
            Guid companyId,
            Guid userId,
            bool includeChatScope = true) =>
            identities.Add(role, companyId, userId, includeChatScope);

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            if (offlineValidation)
                builder.ConfigureLogging(logging => logging.ClearProviders());
            if (enabledMachineId.HasValue || offlineValidation)
            {
                builder.ConfigureAppConfiguration((_, configuration) =>
                {
                    var overrides = new Dictionary<string, string?>();
                    if (offlineValidation)
                    {
                        overrides["ConnectionStrings:DiagLink"] = "Server=(local);Database=offline-validation";
                        overrides["AZURE_STORAGE_CONNECTION_STRING"] = string.Empty;
                    }
                    configuration.AddInMemoryCollection(overrides);
                });
            }

            builder.ConfigureTestServices(services =>
            {
                if (offlineValidation)
                {
                    services.RemoveAll<DiagLinkDbContext>();
                    services.RemoveAll<DbContextOptions<DiagLinkDbContext>>();
                    services.RemoveAll<IDbContextOptionsConfiguration<DiagLinkDbContext>>();
                    services.AddDbContext<DiagLinkDbContext>(options =>
                        options.UseInMemoryDatabase(offlineDatabaseName));
                }

                services.RemoveAll<IConfigureOptions<AuthenticationOptions>>();
                services.AddSingleton(identities);
                services.AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme;
                        options.DefaultChallengeScheme = Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme;
                    })
                    .AddScheme<AuthenticationSchemeOptions, RuntimeAuthenticationHandler>(
                        Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme,
                        _ => { })
                    .AddScheme<AuthenticationSchemeOptions, RuntimeAuthenticationHandler>(
                        DiagLinkAuthenticationDefaults.Scheme,
                        _ => { });

                TokenCredential credential = offlineValidation
                    ? TokenCredential
                    : new DefaultAzureCredential();
                var blobServiceUri = Environment.GetEnvironmentVariable("CLAUDE_DIRECT_MANUAL_BLOB_SERVICE_URI")
                    ?? "https://stknowledgeia.blob.core.windows.net/";
                services.AddSingleton(credential);
                services.AddSingleton(new BlobServiceClient(new Uri(blobServiceUri), credential));
                services.AddScoped<BlobStorageService>();
                services.AddScoped<ITechnicalVisualBlobReader>(provider => provider.GetRequiredService<BlobStorageService>());
                services.AddScoped<ITechnicalDocumentBlobReader>(provider => provider.GetRequiredService<BlobStorageService>());
                services.AddScoped<TechnicalPageMapResolver>();
                services.AddScoped<TechnicalSourceReferenceResolver>();
                services.AddScoped<TechnicalSourceAccessService>();
                services.AddScoped<IClaudeDirectToolboxMarkerReader, AzureClaudeDirectToolboxMarkerReader>();
                services.AddScoped<IClaudeDirectMachineConfigurationResolver, ClaudeDirectMachineConfigurationResolver>();
                services.AddScoped<IClaudeDirectChatRequestFactory, ClaudeDirectChatRequestFactory>();
                services.AddScoped<IClaudeDirectChatRuntime, ClaudeDirectChatRuntime>();
                services.AddScoped<IMachineRequestBlobClient>(_ => MachineRequestBlobClient);
                services.AddScoped<MachineRequestStorageService>();
                services.RemoveAll<IClaudeDirectChatService>();
                services.AddScoped<ClaudeDirectChatService>();
                services.AddSingleton<ClaudeResultCapture>();
                services.AddScoped<IClaudeDirectChatService>(provider => new CapturingClaudeDirectChatService(
                    provider.GetRequiredService<ClaudeDirectChatService>(),
                    provider.GetRequiredService<ClaudeResultCapture>()));

                if (offlineValidation)
                {
                    services.RemoveAll<IClaudeDirectToolboxMarkerReader>();
                    services.AddSingleton<IClaudeDirectToolboxMarkerReader>(_ => OfflineToolboxMarkerReader);
                    services.RemoveAll<IClaudeDirectChatService>();
                    services.AddSingleton<IClaudeDirectChatService>(_ => OfflineClaudeDirectChatService);
                }
            });
        }
    }

    private sealed class OfflineToolboxMarkerReader : IClaudeDirectToolboxMarkerReader
    {
        public List<string> RequestedBlobNames { get; } = [];

        public Task<Stream?> OpenReadAsync(string blobName, CancellationToken cancellationToken)
        {
            RequestedBlobNames.Add(blobName);
            var mcpEndpoint =
                $"{OfflineProjectEndpoint}/toolboxes/TB-machine/versions/1/mcp?api-version=v1";
            var marker = JsonSerializer.Serialize(new
            {
                toolbox_name = "TB-machine",
                toolbox_version = "1",
                vector_store_id = "vs_marker123",
                mcp_endpoint = mcpEndpoint
            });
            Stream stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(marker));
            return Task.FromResult<Stream?>(stream);
        }
    }

    private sealed class OfflineClaudeDirectChatService : IClaudeDirectChatService
    {
        public int CallCount { get; private set; }
        public ClaudeDirectChatRequest? Request { get; private set; }

        public Task<ClaudeDirectChatResult> CompleteAsync(
            ClaudeDirectChatRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            Request = request;
            return Task.FromResult(new ClaudeDirectChatResult(
                "RÃ©ponse ClaudeDirect hors ligne.",
                [],
                [],
                [],
                [],
                [],
                [new ClaudeDirectCallUsage(1, 10, 5, "claude-sonnet-5", "end_turn", "response-offline", "request-offline")],
                new ClaudeDirectAggregateUsage(10, 5, 15),
                "claude-sonnet-5",
                "end_turn",
                []));
        }
    }

    private sealed class NoExternalTokenCredential : TokenCredential
    {
        public int AccessAttempts { get; private set; }

        public override AccessToken GetToken(
            TokenRequestContext requestContext,
            CancellationToken cancellationToken)
        {
            AccessAttempts++;
            throw new AssertFailedException("Offline host validation must not request an Azure token.");
        }

        public override ValueTask<AccessToken> GetTokenAsync(
            TokenRequestContext requestContext,
            CancellationToken cancellationToken)
        {
            AccessAttempts++;
            throw new AssertFailedException("Offline host validation must not request an Azure token.");
        }
    }

    private sealed class NoExternalMachineRequestBlobClient : IMachineRequestBlobClient
    {
        public int AccessAttempts { get; private set; }

        public Task EnsurePrivateContainerAsync(CancellationToken cancellationToken = default) => Fail();
        public Task UploadAsync(string blobName, Stream content, string contentType, bool overwrite,
            CancellationToken cancellationToken = default) => Fail();
        public Task<Stream?> OpenReadAsync(string blobName, CancellationToken cancellationToken = default) => Fail<Stream?>();
        public IAsyncEnumerable<string> ListNamesAsync(string? prefix = null,
            CancellationToken cancellationToken = default) => FailAsyncEnumerable();
        public Task DeletePrefixAsync(string prefix, CancellationToken cancellationToken = default) => Fail();

        private Task Fail()
        {
            AccessAttempts++;
            throw new AssertFailedException("Offline host validation must not access Machine Request Blob storage.");
        }

        private Task<T> Fail<T>()
        {
            AccessAttempts++;
            throw new AssertFailedException("Offline host validation must not access Machine Request Blob storage.");
        }

        private async IAsyncEnumerable<string> FailAsyncEnumerable()
        {
            AccessAttempts++;
            await Task.Yield();
            throw new AssertFailedException("Offline host validation must not access Machine Request Blob storage.");
#pragma warning disable CS0162
            yield break;
#pragma warning restore CS0162
        }
    }

    private sealed class ClaudeResultCapture
    {
        public ClaudeDirectChatResult? Result { get; set; }
    }

    private sealed class CapturingClaudeDirectChatService(
        ClaudeDirectChatService inner,
        ClaudeResultCapture capture) : IClaudeDirectChatService
    {
        public async Task<ClaudeDirectChatResult> CompleteAsync(
            ClaudeDirectChatRequest request,
            CancellationToken cancellationToken = default)
        {
            var result = await inner.CompleteAsync(request, cancellationToken);
            capture.Result = result;
            return result;
        }
    }

    private sealed record RuntimeIdentity(
        string Role,
        Guid CompanyId,
        Guid UserId,
        bool IncludeChatScope);

    private sealed class RuntimeIdentityStore
    {
        private readonly ConcurrentDictionary<string, RuntimeIdentity> identities = new();

        public string Add(string role, Guid companyId, Guid userId, bool includeChatScope)
        {
            var token = Guid.NewGuid().ToString("N");
            identities[token] = new(role, companyId, userId, includeChatScope);
            return token;
        }

        public bool TryGet(string token, out RuntimeIdentity? identity) =>
            identities.TryGetValue(token, out identity);
    }

    private sealed class RuntimeAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        RuntimeIdentityStore identities)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            string? token = null;
            if (string.Equals(
                    Scheme.Name,
                    Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme,
                    StringComparison.Ordinal))
            {
                var authorization = Request.Headers.Authorization.ToString();
                if (authorization.StartsWith("Bearer ", StringComparison.Ordinal))
                    token = authorization["Bearer ".Length..];
            }
            else if (string.Equals(Scheme.Name, DiagLinkAuthenticationDefaults.Scheme, StringComparison.Ordinal) &&
                Request.Headers.TryGetValue(DiagLinkAuthenticationDefaults.HeaderName, out var sessionHeader))
            {
                token = sessionHeader.ToString();
            }

            if (string.IsNullOrWhiteSpace(token) ||
                !identities.TryGet(token, out var identity) ||
                identity is null)
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = new List<Claim>
            {
                new(DiagLinkClaimTypes.UserId, identity.UserId.ToString()),
                new(ClaimTypes.Role, identity.Role),
                new(DiagLinkClaimTypes.CompanyId, identity.CompanyId.ToString())
            };
            if (string.Equals(Scheme.Name, DiagLinkAuthenticationDefaults.Scheme, StringComparison.Ordinal))
            {
                claims.Add(new(ClaimTypes.NameIdentifier, identity.UserId.ToString()));
            }
            else
            {
                claims.Add(new("oid", identity.UserId.ToString()));
                if (identity.IncludeChatScope)
                    claims.Add(new("scp", ChatAccessRequirement.RequiredScope));
            }

            var principal = new ClaimsPrincipal(new ClaimsIdentity(
                claims,
                Scheme.Name,
                ClaimTypes.Name,
                ClaimTypes.Role));
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(principal, Scheme.Name)));
        }
    }
}
