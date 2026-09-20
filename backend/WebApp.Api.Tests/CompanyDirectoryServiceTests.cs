using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebApp.Api.Data;
using WebApp.Api.Models.Entities;
using WebApp.Api.Services;

namespace WebApp.Api.Tests;

/// <summary>Confirms company-user isolation: GetCompanyUsersAsync never leaks another company's users.</summary>
[TestClass]
public class CompanyDirectoryServiceTests
{
    private static DiagLinkDbContext CreateContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<DiagLinkDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new DiagLinkDbContext(options);
    }

    [TestMethod]
    public async Task GetCompanyUsersAsync_ReturnsOnlyRequestedCompanysUsers()
    {
        var dbName = Guid.NewGuid().ToString();
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();

        await using (var seedContext = CreateContext(dbName))
        {
            seedContext.Users.AddRange(
                new User { Id = Guid.NewGuid(), CompanyId = companyA, Email = "a1@companyA.test", Role = "technician", Status = "active", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new User { Id = Guid.NewGuid(), CompanyId = companyA, Email = "a2@companyA.test", Role = "company_admin", Status = "active", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new User { Id = Guid.NewGuid(), CompanyId = companyB, Email = "b1@companyB.test", Role = "technician", Status = "active", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
            await seedContext.SaveChangesAsync();
        }

        await using var context = CreateContext(dbName);
        var service = new CompanyDirectoryService(context);

        var users = await service.GetCompanyUsersAsync(companyA, CancellationToken.None);

        Assert.AreEqual(2, users.Count);
        Assert.IsTrue(users.All(u => u.CompanyId == companyA));
    }
}
