using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebApp.Api.Data;
using WebApp.Api.Models;
using WebApp.Api.Models.Entities;
using WebApp.Api.Repositories;
using WebApp.Api.Services;

namespace WebApp.Api.Tests;

[TestClass]
public class AiPricingIdentityTests
{
    private static AiPricingIdentityResolver RealResolver() => Resolver(new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory).AddJsonFile("pricing-identity-settings.json").Build());

    [TestMethod]
    [DataRow("https://diaglink-foundry-prod.services.ai.azure.com/api/projects/entreprise-001-demo")]
    [DataRow("https://diaglink-foundry-prod.services.ai.azure.com/api/projects/entreprise-002-demo")]
    [DataRow("https://claude-assistant-techni-resource.services.ai.azure.com/api/projects/claude-assistant-technique")]
    public void RealDeploymentsResolveValidatedProvider(string endpoint)
    {
        var identity = RealResolver().Resolve(endpoint, "unused-agent", "unused-version", "claude-sonnet-5");
        Assert.AreEqual("Anthropic", identity.Provider);
        Assert.AreEqual("claude-sonnet-5", identity.Deployment);
    }

    [TestMethod]
    [DataRow("https://unknown.example/api/projects/unknown", "claude-sonnet-5")]
    [DataRow("https://diaglink-foundry-prod.services.ai.azure.com/api/projects/entreprise-001-demo", "claude-sonnet-5-2")]
    public void RealMappingsDoNotMatchUnknownProjectOrDeployment(string endpoint, string deployment)
    {
        var usage = RealResolver().Resolve(endpoint, "unused-agent", "1", deployment)
            .ApplyTo(Usage(AiUsageType.ChatResponse));
        Assert.IsNull(usage.Provider);
        Assert.IsTrue(usage.Completed);
    }

    [TestMethod]
    public async Task RealChatMappingDoesNotOverrideVisionIdentity()
    {
        var chat = RealResolver().Resolve(
            "https://diaglink-foundry-prod.services.ai.azure.com/api/projects/entreprise-001-demo",
            "agent", "1", "claude-sonnet-5").ApplyTo(Usage(AiUsageType.ChatResponse));
        var context = new AiUsageMeasurement(null, null, null, null, "conversation", null, chat);
        var vision = new VisionUsageCapture(Guid.NewGuid(), Usage(AiUsageType.VisionTool) with
            { Provider = "tool-provider", Deployment = "tool-deployment" }).WithContext(context);
        var row = await Persist(vision.Response);
        Assert.AreEqual("tool-provider", row.Provider);
        Assert.AreEqual("tool-deployment", row.Deployment);
    }

    private static IConfigurationRoot Config() => new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?> {
            ["AiPricingIdentity:Agents:0:ProjectEndpoint"] = "project",
            ["AiPricingIdentity:Agents:0:AgentId"] = "machine",
            ["AiPricingIdentity:Agents:0:AgentVersion"] = "1",
            ["AiPricingIdentity:Agents:0:Provider"] = "machine-provider",
            ["AiPricingIdentity:Agents:0:Deployment"] = "machine-deployment",
            ["AiPricingIdentity:Agents:1:ProjectEndpoint"] = "project",
            ["AiPricingIdentity:Agents:1:AgentId"] = "summary",
            ["AiPricingIdentity:Agents:1:AgentVersion"] = "2",
            ["AiPricingIdentity:Agents:1:Provider"] = "summary-provider",
            ["AiPricingIdentity:Agents:2:ProjectEndpoint"] = "project",
            ["AiPricingIdentity:Agents:2:AgentId"] = "machine",
            ["AiPricingIdentity:Agents:2:AgentVersion"] = "2",
            ["AiPricingIdentity:Agents:2:Provider"] = "new-provider"
        }).Build();

    private static AiPricingIdentityResolver Resolver(IConfiguration config) =>
        new(config, NullLogger<AiPricingIdentityResolver>.Instance);
    private static AiResponseUsage Usage(AiUsageType type) =>
        new(type, "response", true, 100, 20, 120, "model", "response", "1", DateTimeOffset.UtcNow)
        { CallId = "call", ParentResponseId = "parent" };
    private static DbContextOptions<DiagLinkDbContext> Options() =>
        new DbContextOptionsBuilder<DiagLinkDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
    private static async Task<AiUsageRecord> Persist(AiResponseUsage usage)
    {
        var options = Options();
        var measurement = new AiUsageMeasurement(null, null, null, null, "conversation", null, usage);
        var repository = new AiUsageRepository(options, NullLogger<AiUsageRepository>.Instance);
        await repository.RecordAsync(measurement, default);
        await repository.RecordAsync(measurement, default);
        await using var db = new DiagLinkDbContext(options);
        return await db.AiUsageRecords.SingleAsync();
    }

    [TestMethod]
    [DataRow("vendor", "deployment")]
    [DataRow(null, "deployment")]
    [DataRow("vendor", null)]
    [DataRow(null, null)]
    public async Task VisionPreservesIdentityAndNulls(string? provider, string? deployment)
    {
        var row = await Persist(Usage(AiUsageType.VisionTool) with { Provider = provider, Deployment = deployment });
        Assert.AreEqual(provider, row.Provider);
        Assert.AreEqual(deployment, row.Deployment);
        Assert.AreEqual("call", row.CallId);
        Assert.AreEqual("parent", row.ParentResponseId);
    }

    [TestMethod]
    public async Task ChatExactMappingIsPersistedWithoutChangingUsage()
    {
        var usage = Usage(AiUsageType.ChatResponse);
        var enriched = Resolver(Config()).Resolve("project", "machine", "1").ApplyTo(usage);
        Assert.AreEqual(usage, enriched with { Provider = null, Deployment = null });
        var row = await Persist(enriched);
        Assert.AreEqual("machine-provider", row.Provider);
        Assert.AreEqual("machine-deployment", row.Deployment);
    }

    [TestMethod]
    public async Task MissingMappingStillProducesCompletedChatUsage()
    {
        var row = await Persist(Resolver(Config()).Resolve("project", "unknown", "1").ApplyTo(Usage(AiUsageType.ChatResponse)));
        Assert.IsNull(row.Provider);
        Assert.IsNull(row.Deployment);
        Assert.IsTrue(row.Completed);
        Assert.AreEqual(120, row.TotalTokens);
    }

    [TestMethod]
    public async Task SummaryUsesGlobalIdentityIndependentlyOfMachine()
    {
        var resolver = Resolver(Config());
        var machine = resolver.Resolve("project", "machine", "1");
        var summary = resolver.Resolve("project", "summary", "2");
        var row = await Persist(summary.ApplyTo(Usage(AiUsageType.ConversationSummary)));
        Assert.AreEqual("summary-provider", row.Provider);
        Assert.AreNotEqual(machine.Provider, row.Provider);
        Assert.IsNull(row.Deployment);
    }

    [TestMethod]
    [DataRow("project", "machine-extra", "1")]
    [DataRow("project", "Machine", "1")]
    [DataRow("project", "machine", "10")]
    [DataRow("other-project", "machine", "1")]
    public void MappingIsExactAndScoped(string project, string agent, string version) =>
        Assert.IsNull(Resolver(Config()).Resolve(project, agent, version).Provider);

    [TestMethod]
    public void VersionsAreDistinctAndCapturedIdentitySurvivesConfigurationChange()
    {
        var config = Config();
        var resolver = Resolver(config);
        var original = resolver.Resolve("project", "machine", "1");
        Assert.AreEqual("new-provider", resolver.Resolve("project", "machine", "2").Provider);
        config["AiPricingIdentity:Agents:0:Provider"] = "changed";
        Assert.AreEqual("machine-provider", original.ApplyTo(Usage(AiUsageType.ChatResponse)).Provider);
    }

    [TestMethod]
    public void DuplicateOrInvalidMappingIsUnknown()
    {
        var config = Config();
        config["AiPricingIdentity:Agents:2:AgentVersion"] = "1";
        Assert.IsNull(Resolver(config).Resolve("project", "machine", "1").Provider);
        config["AiPricingIdentity:Agents:2:AgentVersion"] = "2";
        config["AiPricingIdentity:Agents:0:Provider"] = new string('x', 101);
        Assert.IsNull(Resolver(config).Resolve("project", "machine", "1").Provider);
    }

    [TestMethod]
    public void ExactDeploymentHasPriorityAndUnknownProviderRetainsDeployment()
    {
        var config = Config();
        config["AiPricingIdentity:Deployments:0:ProjectEndpoint"] = "project";
        config["AiPricingIdentity:Deployments:0:Deployment"] = "sdk-deployment";
        config["AiPricingIdentity:Deployments:0:Provider"] = "deployment-provider";
        var resolver = Resolver(config);
        Assert.AreEqual("deployment-provider", resolver.Resolve("project", "machine", "1", "sdk-deployment").Provider);
        var unknown = resolver.Resolve("project", "unknown", "1", "sdk-deployment-extra");
        Assert.IsNull(unknown.Provider);
        Assert.AreEqual("sdk-deployment-extra", unknown.Deployment);
        Assert.IsNull(resolver.Resolve("project", "machine", "1", "contradictory-deployment").Provider);
    }

    [TestMethod]
    public void AmbiguousDeploymentDoesNotFallBackToAgent()
    {
        var config = Config();
        foreach (var index in new[] { 0, 1 })
        {
            config[$"AiPricingIdentity:Deployments:{index}:ProjectEndpoint"] = "project";
            config[$"AiPricingIdentity:Deployments:{index}:Deployment"] = "machine-deployment";
            config[$"AiPricingIdentity:Deployments:{index}:Provider"] = "provider";
        }
        Assert.IsNull(Resolver(config).Resolve("project", "machine", "1", "machine-deployment").Provider);
    }

    [TestMethod]
    public async Task LegacyUsageWithoutNewPropertiesRemainsCompatible()
    {
        var row = await Persist(Usage(AiUsageType.ChatResponse));
        Assert.IsNull(row.Provider);
        Assert.IsNull(row.Deployment);
        Assert.AreEqual("model", row.Model);
        Assert.AreEqual("response", row.ModelSource);
        Assert.AreEqual("1", row.AgentVersion);
    }
}
