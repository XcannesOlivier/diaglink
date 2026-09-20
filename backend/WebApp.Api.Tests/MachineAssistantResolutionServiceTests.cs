using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebApp.Api.Data;
using WebApp.Api.Models;
using WebApp.Api.Models.Entities;
using WebApp.Api.Services;

namespace WebApp.Api.Tests;

/// <summary>
/// Covers MachineAssistantResolutionService's access-then-configuration resolution chain: a caller
/// must pass MachineAccessService's rules before a MachineAssistantConfiguration row is ever read,
/// and every non-Configured outcome must be distinguishable enough for /api/chat/stream to map to
/// the correct HTTP status (404 for inaccessible, 409 for not-configured/disabled).
/// </summary>
[TestClass]
public class MachineAssistantResolutionServiceTests
{
    private static DiagLinkDbContext CreateContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<DiagLinkDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new DiagLinkDbContext(options);
    }

    private static ClaimsPrincipal BuildPrincipal(string role, Guid? companyId = null, Guid? userId = null)
    {
        var claims = new List<Claim> { new(ClaimTypes.Role, role) };
        if (companyId is not null)
        {
            claims.Add(new Claim(DiagLinkClaimTypes.CompanyId, companyId.Value.ToString()));
        }
        if (userId is not null)
        {
            claims.Add(new Claim(DiagLinkClaimTypes.UserId, userId.Value.ToString()));
        }
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    private static async Task<(Guid companyId, Guid machineId)> SeedCompanyWithMachineAsync(string dbName)
    {
        var companyId = Guid.NewGuid();
        var machineId = Guid.NewGuid();

        await using var context = CreateContext(dbName);
        context.Companies.Add(new Company { Id = companyId, Name = "Company A", Status = "active", CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow });
        context.Machines.Add(new Machine { Id = machineId, CompanyId = companyId, Name = "Press A1", Status = "active", CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow });
        await context.SaveChangesAsync();

        return (companyId, machineId);
    }

    [TestMethod]
    public async Task ResolveAsync_MachineInaccessible_ReturnsMachineNotAccessible()
    {
        var dbName = Guid.NewGuid().ToString();
        var (companyId, machineId) = await SeedCompanyWithMachineAsync(dbName);
        var userId = Guid.NewGuid();

        await using var context = CreateContext(dbName);
        var service = new MachineAssistantResolutionService(context, new MachineAccessService(context));
        // Technician with no UserMachineAccess grant for this machine.
        var principal = BuildPrincipal(DiagLinkRoles.Technician, companyId, userId);

        var resolution = await service.ResolveAsync(principal, machineId, CancellationToken.None);

        Assert.AreEqual(MachineAssistantResolutionKind.MachineNotAccessible, resolution.Kind);
        Assert.IsNull(resolution.Configuration);
    }

    [TestMethod]
    public async Task ResolveAsync_AccessibleNoConfigurationRow_ReturnsAssistantNotConfigured()
    {
        var dbName = Guid.NewGuid().ToString();
        var (companyId, machineId) = await SeedCompanyWithMachineAsync(dbName);

        await using var context = CreateContext(dbName);
        var service = new MachineAssistantResolutionService(context, new MachineAccessService(context));
        var principal = BuildPrincipal(DiagLinkRoles.CompanyAdmin, companyId);

        var resolution = await service.ResolveAsync(principal, machineId, CancellationToken.None);

        Assert.AreEqual(MachineAssistantResolutionKind.AssistantNotConfigured, resolution.Kind);
        Assert.IsNull(resolution.Configuration);
    }

    [TestMethod]
    public async Task ResolveAsync_ConfigurationDisabled_ReturnsAssistantDisabled()
    {
        var dbName = Guid.NewGuid().ToString();
        var (companyId, machineId) = await SeedCompanyWithMachineAsync(dbName);

        await using (var seedContext = CreateContext(dbName))
        {
            var machine = await seedContext.Machines.FirstAsync(m => m.Id == machineId);
            machine.FoundryAgentId = "agent-123";
            machine.ProjectEndpoint = "https://example-project.services.ai.azure.com/api/projects/proj";
            machine.AgentVersion = "1";
            machine.VectorStoreId = "vs-disabled";
            machine.BlobPrefix = "bp-disabled";
            machine.Status = "disabled";
            machine.UpdatedAtUtc = DateTime.UtcNow;
            await seedContext.SaveChangesAsync();
        }

        await using var context = CreateContext(dbName);
        var service = new MachineAssistantResolutionService(context, new MachineAccessService(context));
        var principal = BuildPrincipal(DiagLinkRoles.CompanyAdmin, companyId);

        var resolution = await service.ResolveAsync(principal, machineId, CancellationToken.None);

        Assert.AreEqual(MachineAssistantResolutionKind.AssistantDisabled, resolution.Kind);
        Assert.IsNull(resolution.Configuration);
    }

    [TestMethod]
    public async Task ResolveAsync_ConfiguredAndAccessible_ReturnsConfiguredWithoutLeakingEntity()
    {
        var dbName = Guid.NewGuid().ToString();
        var (companyId, machineId) = await SeedCompanyWithMachineAsync(dbName);

        await using (var seedContext = CreateContext(dbName))
        {
            var machine = await seedContext.Machines.FirstAsync(m => m.Id == machineId);
            machine.FoundryAgentId = "agent-123";
            machine.ProjectEndpoint = "https://example-project.services.ai.azure.com/api/projects/proj";
            machine.AgentVersion = "3";
            machine.VectorStoreId = "vs-abc";
            machine.BlobPrefix = "bp-abc";
            machine.Status = "active";
            machine.UpdatedAtUtc = DateTime.UtcNow;
            await seedContext.SaveChangesAsync();
        }

        await using var context = CreateContext(dbName);
        var service = new MachineAssistantResolutionService(context, new MachineAccessService(context));
        var principal = BuildPrincipal(DiagLinkRoles.CompanyAdmin, companyId);

        var resolution = await service.ResolveAsync(principal, machineId, CancellationToken.None);

        Assert.AreEqual(MachineAssistantResolutionKind.Configured, resolution.Kind);
        Assert.IsNotNull(resolution.Configuration);
        Assert.AreEqual("https://example-project.services.ai.azure.com/api/projects/proj", resolution.Configuration!.ProjectEndpoint);
        Assert.AreEqual("agent-123", resolution.Configuration.AgentId);
        Assert.AreEqual("3", resolution.Configuration.AgentVersion);
        Assert.IsNull(resolution.Configuration.AgentName);
        // Configuration is a plain ResolvedAssistantConfiguration record, never the EF entity — no
        // VectorStoreId/Id/CreatedAtUtc/Machine navigation is exposed through it.
        Assert.IsInstanceOfType(resolution.Configuration, typeof(ResolvedAssistantConfiguration));
    }

    [TestMethod]
    public async Task ResolveAsync_SuperAdmin_ConfiguredMachineInAnyCompany_ReturnsConfigured()
    {
        var dbName = Guid.NewGuid().ToString();
        var (machineCompanyId, machineId) = await SeedCompanyWithMachineAsync(dbName);

        await using (var seedContext = CreateContext(dbName))
        {
            var machine = await seedContext.Machines.FirstAsync(m => m.Id == machineId);
            machine.FoundryAgentId = "agent-123";
            machine.ProjectEndpoint = "https://example-project.services.ai.azure.com/api/projects/proj";
            machine.AgentVersion = "1";
            machine.VectorStoreId = "vs-super";
            machine.BlobPrefix = "bp-super";
            machine.Status = "active";
            machine.UpdatedAtUtc = DateTime.UtcNow;
            await seedContext.SaveChangesAsync();
        }

        await using var context = CreateContext(dbName);
        var service = new MachineAssistantResolutionService(context, new MachineAccessService(context));
        var principal = BuildPrincipal(DiagLinkRoles.SuperAdmin, companyId: Guid.NewGuid());

        var resolution = await service.ResolveAsync(principal, machineId, CancellationToken.None);

        Assert.AreEqual(MachineAssistantResolutionKind.Configured, resolution.Kind);
        Assert.AreEqual(machineCompanyId, resolution.Configuration!.CompanyId);
    }

    [TestMethod]
    public async Task ResolveAsync_AccessRevokedAfterInitialGrant_ReturnsMachineNotAccessible()
    {
        // Simulates "access revoked mid-conversation": resolution always re-queries UserMachineAccess
        // fresh, so removing the grant between two calls must flip the outcome.
        var dbName = Guid.NewGuid().ToString();
        var (companyId, machineId) = await SeedCompanyWithMachineAsync(dbName);
        var userId = Guid.NewGuid();

        await using (var seedContext = CreateContext(dbName))
        {
            seedContext.UserMachineAccess.Add(new UserMachineAccess { UserId = userId, MachineId = machineId, CreatedAtUtc = DateTime.UtcNow });
            var machine = await seedContext.Machines.FirstAsync(m => m.Id == machineId);
            machine.FoundryAgentId = "agent-123";
            machine.ProjectEndpoint = "https://example-project.services.ai.azure.com/api/projects/proj";
            machine.AgentVersion = "1";
            machine.VectorStoreId = "vs-access";
            machine.BlobPrefix = "bp-access";
            machine.Status = "active";
            machine.UpdatedAtUtc = DateTime.UtcNow;
            await seedContext.SaveChangesAsync();
        }

        var principal = BuildPrincipal(DiagLinkRoles.Technician, companyId, userId);

        await using (var firstCallContext = CreateContext(dbName))
        {
            var firstResolution = await new MachineAssistantResolutionService(firstCallContext, new MachineAccessService(firstCallContext))
                .ResolveAsync(principal, machineId, CancellationToken.None);
            Assert.AreEqual(MachineAssistantResolutionKind.Configured, firstResolution.Kind);
        }

        await using (var revokeContext = CreateContext(dbName))
        {
            var grant = await revokeContext.UserMachineAccess.FirstAsync(a => a.UserId == userId && a.MachineId == machineId);
            revokeContext.UserMachineAccess.Remove(grant);
            await revokeContext.SaveChangesAsync();
        }

        await using var secondCallContext = CreateContext(dbName);
        var secondResolution = await new MachineAssistantResolutionService(secondCallContext, new MachineAccessService(secondCallContext))
            .ResolveAsync(principal, machineId, CancellationToken.None);

        Assert.AreEqual(MachineAssistantResolutionKind.MachineNotAccessible, secondResolution.Kind);
    }
}
