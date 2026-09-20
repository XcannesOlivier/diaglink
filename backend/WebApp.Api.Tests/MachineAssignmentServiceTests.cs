using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebApp.Api.Data;
using WebApp.Api.Models.Entities;
using WebApp.Api.Services;

namespace WebApp.Api.Tests;

/// <summary>
/// Covers MachineAssignmentService: technician/machine cross-tenant boundaries and the replace-all
/// transaction. No HTTP pipeline is spun up here — same untested-at-HTTP-layer pattern as the rest of
/// this project (CompanyAdminOnly/SuperAdminOnly are enforced declaratively in Program.cs).
/// </summary>
[TestClass]
public class MachineAssignmentServiceTests
{
    private static DiagLinkDbContext CreateContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<DiagLinkDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new DiagLinkDbContext(options);
    }

    private static async Task<(Guid CompanyA, Guid CompanyB, Guid TechnicianA1, Guid MachineA1, Guid MachineA2, Guid MachineB1)> SeedAsync(DiagLinkDbContext context)
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var technicianA1 = Guid.NewGuid();
        var machineA1 = Guid.NewGuid();
        var machineA2 = Guid.NewGuid();
        var machineB1 = Guid.NewGuid();
        var now = DateTime.UtcNow;

        context.Companies.AddRange(
            new Company { Id = companyA, Name = "Company A", Status = "active", CreatedAtUtc = now, UpdatedAtUtc = now },
            new Company { Id = companyB, Name = "Company B", Status = "active", CreatedAtUtc = now, UpdatedAtUtc = now });

        context.Users.Add(new User { Id = technicianA1, CompanyId = companyA, Email = "tech.a1@example.com", Role = "technician", Status = "active", CreatedAt = now, UpdatedAt = now });

        context.Machines.AddRange(
            new Machine { Id = machineA1, CompanyId = companyA, Name = "Machine A1", Status = "active", CreatedAtUtc = now, UpdatedAtUtc = now },
            new Machine { Id = machineA2, CompanyId = companyA, Name = "Machine A2", Status = "active", CreatedAtUtc = now, UpdatedAtUtc = now },
            new Machine { Id = machineB1, CompanyId = companyB, Name = "Machine B1", Status = "active", CreatedAtUtc = now, UpdatedAtUtc = now });

        await context.SaveChangesAsync();

        return (companyA, companyB, technicianA1, machineA1, machineA2, machineB1);
    }

    [TestMethod]
    public async Task AssignMachineAsync_SameCompany_Succeeds()
    {
        var dbName = Guid.NewGuid().ToString();
        (Guid companyA, _, Guid technicianA1, Guid machineA1, Guid machineA2, _) = await SeedIntoAsync(dbName);
        await using var context = CreateContext(dbName);
        var service = new MachineAssignmentService(context);

        var outcome1 = await service.AssignMachineAsync(companyA, technicianA1, machineA1, CancellationToken.None);
        var outcome2 = await service.AssignMachineAsync(companyA, technicianA1, machineA2, CancellationToken.None);

        Assert.IsTrue(outcome1.Success);
        Assert.IsTrue(outcome2.Success);
        Assert.AreEqual(2, await context.UserMachineAccess.CountAsync(a => a.UserId == technicianA1));
    }

    [TestMethod]
    public async Task AssignMachineAsync_CrossTenantMachine_IsRejected()
    {
        var dbName = Guid.NewGuid().ToString();
        (Guid companyA, _, Guid technicianA1, _, _, Guid machineB1) = await SeedIntoAsync(dbName);
        await using var context = CreateContext(dbName);
        var service = new MachineAssignmentService(context);

        var outcome = await service.AssignMachineAsync(companyA, technicianA1, machineB1, CancellationToken.None);

        Assert.IsFalse(outcome.Success);
        Assert.AreEqual(MachineAssignmentErrorKind.MachineNotFound, outcome.ErrorKind);
        Assert.AreEqual(0, await context.UserMachineAccess.CountAsync());
    }

    [TestMethod]
    public async Task AssignMachineAsync_CompanyAdminB_CannotModifyTechnicianFromCompanyA()
    {
        var dbName = Guid.NewGuid().ToString();
        (_, Guid companyB, Guid technicianA1, Guid machineA1, _, _) = await SeedIntoAsync(dbName);
        await using var context = CreateContext(dbName);
        var service = new MachineAssignmentService(context);

        // company_admin B's claim resolves to companyB — technicianA1 belongs to companyA.
        var outcome = await service.AssignMachineAsync(companyB, technicianA1, machineA1, CancellationToken.None);

        Assert.IsFalse(outcome.Success);
        Assert.AreEqual(MachineAssignmentErrorKind.UserNotFound, outcome.ErrorKind);
    }

    [TestMethod]
    public async Task AssignMachineAsync_TargetNotTechnician_IsRejected()
    {
        var dbName = Guid.NewGuid().ToString();
        var companyId = Guid.NewGuid();
        var machineId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using (var seedContext = CreateContext(dbName))
        {
            seedContext.Companies.Add(new Company { Id = companyId, Name = "Acme", Status = "active", CreatedAtUtc = now, UpdatedAtUtc = now });
            seedContext.Users.Add(new User { Id = adminId, CompanyId = companyId, Email = "admin@example.com", Role = "company_admin", Status = "active", CreatedAt = now, UpdatedAt = now });
            seedContext.Machines.Add(new Machine { Id = machineId, CompanyId = companyId, Name = "Machine", Status = "active", CreatedAtUtc = now, UpdatedAtUtc = now });
            await seedContext.SaveChangesAsync();
        }

        await using var context = CreateContext(dbName);
        var service = new MachineAssignmentService(context);

        var outcome = await service.AssignMachineAsync(companyId, adminId, machineId, CancellationToken.None);

        Assert.IsFalse(outcome.Success);
        Assert.AreEqual(MachineAssignmentErrorKind.UserNotTechnician, outcome.ErrorKind);
    }

    [TestMethod]
    public async Task UnassignMachineAsync_IsIdempotent()
    {
        var dbName = Guid.NewGuid().ToString();
        (Guid companyA, _, Guid technicianA1, Guid machineA1, _, _) = await SeedIntoAsync(dbName);
        await using var context = CreateContext(dbName);
        var service = new MachineAssignmentService(context);

        var outcome = await service.UnassignMachineAsync(companyA, technicianA1, machineA1, CancellationToken.None);

        Assert.IsTrue(outcome.Success);
    }

    [TestMethod]
    public async Task ReplaceUserMachineAccessAsync_ReplacesEntireSet()
    {
        var dbName = Guid.NewGuid().ToString();
        (Guid companyA, _, Guid technicianA1, Guid machineA1, Guid machineA2, _) = await SeedIntoAsync(dbName);
        await using (var seedContext = CreateContext(dbName))
        {
            seedContext.UserMachineAccess.Add(new UserMachineAccess { UserId = technicianA1, MachineId = machineA1, CreatedAtUtc = DateTime.UtcNow });
            await seedContext.SaveChangesAsync();
        }

        await using var context = CreateContext(dbName);
        var service = new MachineAssignmentService(context);

        var outcome = await service.ReplaceUserMachineAccessAsync(companyA, technicianA1, [machineA2.ToString()], CancellationToken.None);

        Assert.IsTrue(outcome.Success);
        var access = await context.UserMachineAccess.Where(a => a.UserId == technicianA1).ToListAsync();
        Assert.AreEqual(1, access.Count);
        Assert.AreEqual(machineA2, access[0].MachineId);
    }

    [TestMethod]
    public async Task ReplaceUserMachineAccessAsync_RejectsCrossTenantMachine_NoPartialChange()
    {
        var dbName = Guid.NewGuid().ToString();
        (Guid companyA, _, Guid technicianA1, Guid machineA1, _, Guid machineB1) = await SeedIntoAsync(dbName);
        await using var context = CreateContext(dbName);
        var service = new MachineAssignmentService(context);

        var outcome = await service.ReplaceUserMachineAccessAsync(companyA, technicianA1, [machineA1.ToString(), machineB1.ToString()], CancellationToken.None);

        Assert.IsFalse(outcome.Success);
        Assert.AreEqual(MachineAssignmentErrorKind.MachineNotFound, outcome.ErrorKind);
        Assert.AreEqual(0, await context.UserMachineAccess.CountAsync());
    }

    [TestMethod]
    public async Task FullChain_TechnicianSeesAllCompanyMachinesWithAccurateAccess()
    {
        var dbName = Guid.NewGuid().ToString();
        (Guid companyA, _, Guid technicianA1, Guid machineA1, Guid machineA2, _) = await SeedIntoAsync(dbName);

        await using (var assignContext = CreateContext(dbName))
        {
            var assignmentService = new MachineAssignmentService(assignContext);
            await assignmentService.AssignMachineAsync(companyA, technicianA1, machineA1, CancellationToken.None);
        }

        await using var readContext = CreateContext(dbName);
        var machineAccessService = new MachineAccessService(readContext);
        var claims = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.Role, "technician"),
            new Claim(DiagLinkClaimTypes.CompanyId, companyA.ToString()),
            new Claim(DiagLinkClaimTypes.UserId, technicianA1.ToString()),
        ]));

        var visibleMachines = await machineAccessService.GetAccessibleMachinesAsync(claims, CancellationToken.None);

        Assert.AreEqual(2, visibleMachines.Count);
        Assert.IsTrue(visibleMachines.Single(m => m.Machine.Id == machineA1).IsAccessible);
        Assert.IsFalse(visibleMachines.Single(m => m.Machine.Id == machineA2).IsAccessible);
    }

    private static async Task<(Guid CompanyA, Guid CompanyB, Guid TechnicianA1, Guid MachineA1, Guid MachineA2, Guid MachineB1)> SeedIntoAsync(string dbName)
    {
        await using var context = CreateContext(dbName);
        return await SeedAsync(context);
    }
}
