using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebApp.Api.Data;
using WebApp.Api.Models;
using WebApp.Api.Models.Entities;
using WebApp.Api.Services;

namespace WebApp.Api.Tests;

/// <summary>
/// Covers the exact per-role machine-access rules: technician (own company AND explicit
/// UserMachineAccess grant), company_admin (own company only), diaglink_super_admin (everything).
/// </summary>
[TestClass]
public class MachineAccessServiceTests
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

    private static async Task<(Guid companyA, Guid companyB, Guid machineInA, Guid machineInB)> SeedTwoCompaniesAsync(string dbName)
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var machineInA = Guid.NewGuid();
        var machineInB = Guid.NewGuid();

        await using var context = CreateContext(dbName);
        context.Companies.AddRange(
            new Company { Id = companyA, Name = "Company A", Status = "active", CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow },
            new Company { Id = companyB, Name = "Company B", Status = "active", CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow });
        context.Machines.AddRange(
            new Machine { Id = machineInA, CompanyId = companyA, Name = "Press A1", Status = "active", CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow },
            new Machine { Id = machineInB, CompanyId = companyB, Name = "Press B1", Status = "active", CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow });
        await context.SaveChangesAsync();

        return (companyA, companyB, machineInA, machineInB);
    }

    [TestMethod]
    public async Task CanAccessMachineAsync_Technician_AssignedMachineOwnCompany_ReturnsTrue()
    {
        var dbName = Guid.NewGuid().ToString();
        var (companyA, _, machineInA, _) = await SeedTwoCompaniesAsync(dbName);
        var userId = Guid.NewGuid();

        await using (var seedContext = CreateContext(dbName))
        {
            seedContext.UserMachineAccess.Add(new UserMachineAccess { UserId = userId, MachineId = machineInA, CreatedAtUtc = DateTime.UtcNow });
            await seedContext.SaveChangesAsync();
        }

        await using var context = CreateContext(dbName);
        var service = new MachineAccessService(context);
        var principal = BuildPrincipal(DiagLinkRoles.Technician, companyA, userId);

        Assert.IsTrue(await service.CanAccessMachineAsync(principal, machineInA, CancellationToken.None));
    }

    [TestMethod]
    public async Task CanAccessMachineAsync_Technician_UnassignedMachineOwnCompany_ReturnsFalse()
    {
        var dbName = Guid.NewGuid().ToString();
        var (companyA, _, machineInA, _) = await SeedTwoCompaniesAsync(dbName);
        var userId = Guid.NewGuid();

        await using var context = CreateContext(dbName);
        var service = new MachineAccessService(context);
        var principal = BuildPrincipal(DiagLinkRoles.Technician, companyA, userId);

        Assert.IsFalse(await service.CanAccessMachineAsync(principal, machineInA, CancellationToken.None));
    }

    [TestMethod]
    public async Task CanAccessMachineAsync_Technician_MachineInOtherCompany_ReturnsFalse()
    {
        var dbName = Guid.NewGuid().ToString();
        var (companyA, _, _, machineInB) = await SeedTwoCompaniesAsync(dbName);
        var userId = Guid.NewGuid();

        await using (var seedContext = CreateContext(dbName))
        {
            // Even if a rogue grant exists, cross-company access must still be denied.
            seedContext.UserMachineAccess.Add(new UserMachineAccess { UserId = userId, MachineId = machineInB, CreatedAtUtc = DateTime.UtcNow });
            await seedContext.SaveChangesAsync();
        }

        await using var context = CreateContext(dbName);
        var service = new MachineAccessService(context);
        var principal = BuildPrincipal(DiagLinkRoles.Technician, companyA, userId);

        Assert.IsFalse(await service.CanAccessMachineAsync(principal, machineInB, CancellationToken.None));
    }

    [TestMethod]
    public async Task CanAccessMachineAsync_CompanyAdmin_OwnCompanyMachine_ReturnsTrue()
    {
        var dbName = Guid.NewGuid().ToString();
        var (companyA, _, machineInA, _) = await SeedTwoCompaniesAsync(dbName);

        await using var context = CreateContext(dbName);
        var service = new MachineAccessService(context);
        var principal = BuildPrincipal(DiagLinkRoles.CompanyAdmin, companyA);

        Assert.IsTrue(await service.CanAccessMachineAsync(principal, machineInA, CancellationToken.None));
    }

    [TestMethod]
    public async Task CanAccessMachineAsync_CompanyAdmin_OtherCompanyMachine_ReturnsFalse()
    {
        var dbName = Guid.NewGuid().ToString();
        var (companyA, _, _, machineInB) = await SeedTwoCompaniesAsync(dbName);

        await using var context = CreateContext(dbName);
        var service = new MachineAccessService(context);
        var principal = BuildPrincipal(DiagLinkRoles.CompanyAdmin, companyA);

        Assert.IsFalse(await service.CanAccessMachineAsync(principal, machineInB, CancellationToken.None));
    }

    [TestMethod]
    public async Task CanAccessMachineAsync_SuperAdmin_AnyCompanyMachine_ReturnsTrue()
    {
        var dbName = Guid.NewGuid().ToString();
        var (_, _, machineInA, machineInB) = await SeedTwoCompaniesAsync(dbName);

        await using var context = CreateContext(dbName);
        var service = new MachineAccessService(context);
        var principal = BuildPrincipal(DiagLinkRoles.SuperAdmin);

        Assert.IsTrue(await service.CanAccessMachineAsync(principal, machineInA, CancellationToken.None));
        Assert.IsTrue(await service.CanAccessMachineAsync(principal, machineInB, CancellationToken.None));
    }

    [TestMethod]
    public async Task GetAccessibleMachinesAsync_CompanyAdmin_ReturnsOnlyOwnCompanyMachines()
    {
        var dbName = Guid.NewGuid().ToString();
        var (companyA, _, machineInA, machineInB) = await SeedTwoCompaniesAsync(dbName);

        await using var context = CreateContext(dbName);
        var service = new MachineAccessService(context);
        var principal = BuildPrincipal(DiagLinkRoles.CompanyAdmin, companyA);

        var machines = await service.GetAccessibleMachinesAsync(principal, CancellationToken.None);

        CollectionAssert.AreEquivalent(new[] { machineInA }, machines.Select(m => m.Machine.Id).ToArray());
        Assert.IsFalse(machines.Any(m => m.Machine.Id == machineInB));
    }

    [TestMethod]
    public async Task GetAccessibleMachinesAsync_SuperAdmin_ReturnsAllCompaniesMachines()
    {
        var dbName = Guid.NewGuid().ToString();
        var (_, _, machineInA, machineInB) = await SeedTwoCompaniesAsync(dbName);

        await using var context = CreateContext(dbName);
        var service = new MachineAccessService(context);
        var principal = BuildPrincipal(DiagLinkRoles.SuperAdmin);

        var machines = await service.GetAccessibleMachinesAsync(principal, CancellationToken.None);

        CollectionAssert.AreEquivalent(new[] { machineInA, machineInB }, machines.Select(m => m.Machine.Id).ToArray());
    }
}
