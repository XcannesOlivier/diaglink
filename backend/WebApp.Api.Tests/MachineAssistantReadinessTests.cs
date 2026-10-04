using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebApp.Api.Models.Entities;
using WebApp.Api.Services;

namespace WebApp.Api.Tests;

[TestClass]
public sealed class MachineAssistantReadinessTests
{
    [TestMethod]
    public void ClaudeDirectEligibleWithoutHostedAgentIdentity_IsConfigured()
    {
        var machine = Machine();
        var readiness = Readiness(machine.Id);

        Assert.IsTrue(readiness.IsAssistantConfigured(machine, eligible: true));
    }

    [TestMethod]
    public void HostedAgentEligibleWithoutFoundryAgentId_IsNotConfigured()
    {
        var machine = Machine();
        var readiness = Readiness();

        Assert.IsFalse(readiness.IsAssistantConfigured(machine, eligible: true));
    }

    [TestMethod]
    public void ClaudeDirectEligibleWithMissingVectorStoreId_IsNotConfigured()
    {
        var machine = Machine();
        machine.VectorStoreId = null;
        var readiness = Readiness(machine.Id);

        Assert.IsFalse(readiness.IsAssistantConfigured(machine, eligible: true));
    }

    [TestMethod]
    public void ClaudeDirectIneligibleWithCompleteConfiguration_IsNotConfigured()
    {
        var machine = Machine();
        var readiness = Readiness(machine.Id);

        Assert.IsFalse(readiness.IsAssistantConfigured(machine, eligible: false));
    }

    private static MachineAssistantReadiness Readiness(params Guid[] enabledMachineIds) =>
        new(new AiChatRuntimeSelector(Options.Create(new ClaudeDirectChatOptions
        {
            EnabledMachineIds = enabledMachineIds
        })));

    private static Machine Machine() => new()
    {
        Id = Guid.NewGuid(),
        CompanyId = Guid.NewGuid(),
        Name = "Machine",
        Status = "active",
        ProjectEndpoint = "https://resource.test/api/projects/company",
        BlobPrefix = "company/machine",
        VectorStoreId = "vs_marker123",
        FoundryAgentId = null,
        AgentVersion = null,
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow
    };
}
