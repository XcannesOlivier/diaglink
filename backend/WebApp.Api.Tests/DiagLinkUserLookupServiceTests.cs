using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebApp.Api.Data;
using WebApp.Api.Models;
using WebApp.Api.Models.Entities;
using WebApp.Api.Services;

namespace WebApp.Api.Tests;

/// <summary>
/// Covers the dbo.Users resolution shared by DiagLinkSessionAuthenticationHandler (session -> role/company)
/// and DiagLinkUserClaimsTransformation (Microsoft oid -> role/company). No HTTP pipeline is spun up here
/// (this project has no WebApplicationFactory scaffolding) — these tests instead pin down the exact rules
/// both authentication paths rely on: active status, non-empty role, and a real CompanyId.
/// </summary>
[TestClass]
public class DiagLinkUserLookupServiceTests
{
    private static DiagLinkDbContext CreateContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<DiagLinkDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new DiagLinkDbContext(options);
    }

    private static User BuildUser(Guid id, Guid companyId, string role, string status, string? entraObjectId = null) => new()
    {
        Id = id,
        Email = $"{id}@example.com",
        CompanyId = companyId,
        Role = role,
        Status = status,
        EntraObjectId = entraObjectId,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
    };

    [TestMethod]
    public async Task FindActiveUserByIdAsync_ActiveTechnician_ReturnsTechnicianRoleAndCompany()
    {
        var dbName = Guid.NewGuid().ToString();
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();

        await using (var seed = CreateContext(dbName))
        {
            seed.Users.Add(BuildUser(userId, companyId, DiagLinkRoles.Technician, "active"));
            await seed.SaveChangesAsync();
        }

        await using var context = CreateContext(dbName);
        var result = await new DiagLinkUserLookupService(context).FindActiveUserByIdAsync(userId, CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.AreEqual(DiagLinkRoles.Technician, result!.Role);
        Assert.AreEqual(companyId, result.CompanyId);
    }

    [TestMethod]
    public async Task FindActiveUserByIdAsync_ActiveCompanyAdmin_ReturnsCompanyAdminRole()
    {
        var dbName = Guid.NewGuid().ToString();
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();

        await using (var seed = CreateContext(dbName))
        {
            seed.Users.Add(BuildUser(userId, companyId, DiagLinkRoles.CompanyAdmin, "active"));
            await seed.SaveChangesAsync();
        }

        await using var context = CreateContext(dbName);
        var result = await new DiagLinkUserLookupService(context).FindActiveUserByIdAsync(userId, CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.AreEqual(DiagLinkRoles.CompanyAdmin, result!.Role);
        Assert.AreEqual(companyId, result.CompanyId);
    }

    [TestMethod]
    public async Task FindActiveUserByIdAsync_ActiveSuperAdmin_ReturnsSuperAdminRole()
    {
        var dbName = Guid.NewGuid().ToString();
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();

        await using (var seed = CreateContext(dbName))
        {
            seed.Users.Add(BuildUser(userId, companyId, DiagLinkRoles.SuperAdmin, "active"));
            await seed.SaveChangesAsync();
        }

        await using var context = CreateContext(dbName);
        var result = await new DiagLinkUserLookupService(context).FindActiveUserByIdAsync(userId, CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.AreEqual(DiagLinkRoles.SuperAdmin, result!.Role);
        Assert.AreEqual(companyId, result.CompanyId);
    }

    [TestMethod]
    public async Task FindActiveUserByIdAsync_InactiveUser_ReturnsNull()
    {
        var dbName = Guid.NewGuid().ToString();
        var userId = Guid.NewGuid();

        await using (var seed = CreateContext(dbName))
        {
            seed.Users.Add(BuildUser(userId, Guid.NewGuid(), DiagLinkRoles.Technician, "inactive"));
            await seed.SaveChangesAsync();
        }

        await using var context = CreateContext(dbName);
        var result = await new DiagLinkUserLookupService(context).FindActiveUserByIdAsync(userId, CancellationToken.None);

        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task FindActiveUserByIdAsync_UnknownUser_ReturnsNull()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString());
        var result = await new DiagLinkUserLookupService(context).FindActiveUserByIdAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task FindActiveUserByEntraObjectIdAsync_ActiveUser_ReturnsUser()
    {
        var dbName = Guid.NewGuid().ToString();
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        const string entraObjectId = "11111111-1111-1111-1111-111111111111";

        await using (var seed = CreateContext(dbName))
        {
            seed.Users.Add(BuildUser(userId, companyId, DiagLinkRoles.CompanyAdmin, "active", entraObjectId));
            await seed.SaveChangesAsync();
        }

        await using var context = CreateContext(dbName);
        var result = await new DiagLinkUserLookupService(context)
            .FindActiveUserByEntraObjectIdAsync(entraObjectId, CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.AreEqual(userId, result!.Id);
        Assert.AreEqual(DiagLinkRoles.CompanyAdmin, result.Role);
    }
}
