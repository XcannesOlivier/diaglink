using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebApp.Api.Data;
using WebApp.Api.Models;
using WebApp.Api.Models.Entities;
using WebApp.Api.Services;

namespace WebApp.Api.Tests;

[TestClass]
public class MachineAssistantResolutionServiceTests
{
    private static DiagLinkDbContext CreateContext(string dbName) => new(
        new DbContextOptionsBuilder<DiagLinkDbContext>().UseInMemoryDatabase(dbName).Options);

    private static ClaimsPrincipal Principal(string role, Guid? companyId = null, Guid? userId = null)
    {
        var claims = new List<Claim> { new(ClaimTypes.Role, role) };
        if (companyId.HasValue) claims.Add(new(DiagLinkClaimTypes.CompanyId, companyId.Value.ToString()));
        if (userId.HasValue) claims.Add(new(DiagLinkClaimTypes.UserId, userId.Value.ToString()));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    private static async Task<(Guid CompanyId, Guid MachineId)> SeedAsync(string dbName)
    {
        var companyId = Guid.NewGuid();
        var machineId = Guid.NewGuid();
        await using var db = CreateContext(dbName);
        db.Companies.Add(new Company { Id = companyId, Name = "Company", Status = "active", CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow });
        db.Machines.Add(new Machine
        {
            Id = machineId,
            CompanyId = companyId,
            Name = "Machine",
            Status = "active",
            ProjectEndpoint = "https://resource.test/api/projects/company",
            BlobPrefix = "company/machine",
            VectorStoreId = "vs_marker123",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        return (companyId, machineId);
    }

    [TestMethod]
    public async Task ResolveMachineAsync_MachineInaccessible_ReturnsMachineNotAccessible()
    {
        var dbName = Guid.NewGuid().ToString();
        var (companyId, machineId) = await SeedAsync(dbName);
        await using var db = CreateContext(dbName);

        var result = await new MachineAssistantResolutionService(db, new MachineAccessService(db))
            .ResolveMachineAsync(Principal(DiagLinkRoles.Technician, companyId, Guid.NewGuid()), machineId, default);

        Assert.AreEqual(MachineResolutionKind.MachineNotAccessible, result.Kind);
        Assert.IsNull(result.Machine);
    }

    [TestMethod]
    public async Task ResolveMachineAsync_MissingMachine_ReturnsMachineNotAccessible()
    {
        await using var db = CreateContext(Guid.NewGuid().ToString());
        var result = await new MachineAssistantResolutionService(db, new MachineAccessService(db))
            .ResolveMachineAsync(Principal(DiagLinkRoles.SuperAdmin), Guid.NewGuid(), default);

        Assert.AreEqual(MachineResolutionKind.MachineNotAccessible, result.Kind);
    }

    [TestMethod]
    public async Task ResolveMachineAsync_IneligibleMachine_ReturnsMachineDisabled()
    {
        var dbName = Guid.NewGuid().ToString();
        var (companyId, machineId) = await SeedAsync(dbName);
        await using (var seed = CreateContext(dbName))
        {
            (await seed.Machines.SingleAsync()).Status = "disabled";
            await seed.SaveChangesAsync();
        }

        await using var db = CreateContext(dbName);
        var result = await new MachineAssistantResolutionService(db, new MachineAccessService(db))
            .ResolveMachineAsync(Principal(DiagLinkRoles.CompanyAdmin, companyId), machineId, default);

        Assert.AreEqual(MachineResolutionKind.MachineDisabled, result.Kind);
    }

    [TestMethod]
    public async Task ResolveMachineAsync_ConfiguredClaudeDirectMachine_ReturnsMachine()
    {
        var dbName = Guid.NewGuid().ToString();
        var (companyId, machineId) = await SeedAsync(dbName);
        await using var db = CreateContext(dbName);

        var result = await new MachineAssistantResolutionService(db, new MachineAccessService(db))
            .ResolveMachineAsync(Principal(DiagLinkRoles.CompanyAdmin, companyId), machineId, default);

        Assert.AreEqual(MachineResolutionKind.Resolved, result.Kind);
        Assert.AreEqual(machineId, result.Machine!.Id);
    }
}
