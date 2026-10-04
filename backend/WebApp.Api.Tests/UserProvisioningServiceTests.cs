using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebApp.Api.Data;
using WebApp.Api.Models;
using WebApp.Api.Models.Entities;
using WebApp.Api.Services;

namespace WebApp.Api.Tests;

/// <summary>
/// Covers UserProvisioningService (technician creation by company_admin and diaglink_super_admin).
/// No HTTP pipeline is spun up here (this project has no WebApplicationFactory scaffolding) —
/// authorization (CompanyAdminOnly / SuperAdminOnly) is enforced declaratively on the minimal API
/// routes in Program.cs, the same untested-at-HTTP-layer pattern already used throughout this project.
/// </summary>
[TestClass]
public class UserProvisioningServiceTests
{
    private static DiagLinkDbContext CreateContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<DiagLinkDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new DiagLinkDbContext(options);
    }

    private static CreateTechnicianRequest BuildRequest(string email, string role = "technician") => new()
    {
        Email = email,
        FirstName = "Jean",
        LastName = "Dupont",
        PhoneNumber = "0102030405",
        Role = role,
    };

    [TestMethod]
    public async Task CreateTechnicianAsync_CreatesTechnicianInCallerCompany()
    {
        var companyId = Guid.NewGuid();
        await using var context = CreateContext(Guid.NewGuid().ToString());
        var service = new UserProvisioningService(context);

        var outcome = await service.CreateTechnicianAsync(companyId, BuildRequest("Tech@Example.com"), CancellationToken.None);

        Assert.IsTrue(outcome.Success);
        Assert.AreEqual("technician", outcome.User!.Role);
        Assert.AreEqual("active", outcome.User.Status);
        Assert.AreEqual("Tech@Example.com", outcome.User.Email);

        var stored = await context.Users.SingleAsync(u => u.Id == Guid.Parse(outcome.User.Id));
        Assert.AreEqual(companyId, stored.CompanyId);
    }

    [TestMethod]
    public async Task CreateTechnicianAsync_RejectsInvalidEmail()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString());
        var service = new UserProvisioningService(context);

        var outcome = await service.CreateTechnicianAsync(Guid.NewGuid(), BuildRequest("not-an-email"), CancellationToken.None);

        Assert.IsFalse(outcome.Success);
        Assert.AreEqual(UserProvisioningErrorKind.InvalidEmail, outcome.ErrorKind);
    }

    [TestMethod]
    public async Task CreateTechnicianAsync_RejectsDuplicateEmailCaseInsensitive()
    {
        var dbName = Guid.NewGuid().ToString();
        var companyId = Guid.NewGuid();
        await using (var seedContext = CreateContext(dbName))
        {
            seedContext.Users.Add(new User
            {
                Id = Guid.NewGuid(),
                CompanyId = companyId,
                Email = "tech@example.com",
                Role = "technician",
                Status = "active",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });
            await seedContext.SaveChangesAsync();
        }

        await using var context = CreateContext(dbName);
        var service = new UserProvisioningService(context);

        var outcome = await service.CreateTechnicianAsync(companyId, BuildRequest("TECH@EXAMPLE.COM"), CancellationToken.None);

        Assert.IsFalse(outcome.Success);
        Assert.AreEqual(UserProvisioningErrorKind.DuplicateEmail, outcome.ErrorKind);
    }

    [TestMethod]
    public async Task CreateTechnicianAsync_TwoDifferentCompanyAdmins_CanNeverCreateInEachOthersCompany()
    {
        // Simulates: company_admin A's claim resolves to companyA; company_admin B's claim resolves to
        // companyB. Each call only ever receives its own companyId — there is no code path by which one
        // admin's request could land a user in the other's company.
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        await using var context = CreateContext(Guid.NewGuid().ToString());
        var service = new UserProvisioningService(context);

        var outcomeA = await service.CreateTechnicianAsync(companyA, BuildRequest("a@example.com"), CancellationToken.None);
        var outcomeB = await service.CreateTechnicianAsync(companyB, BuildRequest("b@example.com"), CancellationToken.None);

        var storedA = await context.Users.SingleAsync(u => u.Id == Guid.Parse(outcomeA.User!.Id));
        var storedB = await context.Users.SingleAsync(u => u.Id == Guid.Parse(outcomeB.User!.Id));
        Assert.AreEqual(companyA, storedA.CompanyId);
        Assert.AreEqual(companyB, storedB.CompanyId);
    }

    [TestMethod]
    public async Task CreateTechnicianForCompanyAsync_RejectsNonexistentCompany()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString());
        var service = new UserProvisioningService(context);

        var outcome = await service.CreateTechnicianForCompanyAsync(Guid.NewGuid(), BuildRequest("tech@example.com"), CancellationToken.None);

        Assert.IsFalse(outcome.Success);
        Assert.AreEqual(UserProvisioningErrorKind.CompanyNotFound, outcome.ErrorKind);
    }

    [TestMethod]
    public async Task CreateTechnicianForCompanyAsync_CreatesTechnicianWhenCompanyExists()
    {
        var dbName = Guid.NewGuid().ToString();
        var company = new Company { Id = Guid.NewGuid(), Name = "Acme", Status = "active", CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow };
        await using (var seedContext = CreateContext(dbName))
        {
            seedContext.Companies.Add(company);
            await seedContext.SaveChangesAsync();
        }

        await using var context = CreateContext(dbName);
        var service = new UserProvisioningService(context);

        var outcome = await service.CreateTechnicianForCompanyAsync(company.Id, BuildRequest("tech@example.com"), CancellationToken.None);

        Assert.IsTrue(outcome.Success);
        Assert.AreEqual("technician", outcome.User!.Role);
    }

    [TestMethod]
    public async Task UpdateUserProfileAsync_TrimsAndPersistsOnlyEditableFields()
    {
        var dbName = Guid.NewGuid().ToString();
        var companyId = Guid.NewGuid();
        var user = new User
        {
            Id = Guid.NewGuid(), CompanyId = companyId, Email = "tech@example.com", Role = "technician",
            Status = "active", FirstName = "Avant", LastName = "Ancien", PhoneNumber = "000",
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        await using var context = CreateContext(dbName);
        context.Users.Add(user);
        await context.SaveChangesAsync();
        var service = new UserProvisioningService(context);

        var outcome = await service.UpdateUserProfileAsync(companyId, user.Id, new UpdateUserProfileRequest
        {
            FirstName = "  Alice ", LastName = " Martin  ", PhoneNumber = "  +33 1 02 03 04 05 ",
        }, CancellationToken.None);

        Assert.IsTrue(outcome.Success);
        Assert.AreEqual("Alice", user.FirstName);
        Assert.AreEqual("Martin", user.LastName);
        Assert.AreEqual("+33 1 02 03 04 05", user.PhoneNumber);
        Assert.AreEqual("tech@example.com", user.Email);
        Assert.AreEqual("technician", user.Role);
        Assert.AreEqual("active", user.Status);
        Assert.AreEqual(companyId, user.CompanyId);

        await using var reloadedContext = CreateContext(dbName);
        var reloaded = await reloadedContext.Users.AsNoTracking().SingleAsync(candidate => candidate.Id == user.Id);
        Assert.AreEqual("Alice", reloaded.FirstName);
        Assert.AreEqual("Martin", reloaded.LastName);
        Assert.AreEqual("+33 1 02 03 04 05", reloaded.PhoneNumber);
    }

    [TestMethod]
    public async Task UpdateUserProfileAsync_CannotUpdateUserFromAnotherCompany()
    {
        var ownerCompanyId = Guid.NewGuid();
        var foreignCompanyId = Guid.NewGuid();
        var user = new User
        {
            Id = Guid.NewGuid(), CompanyId = ownerCompanyId, Email = "tech@example.com", Role = "technician",
            Status = "active", FirstName = "Avant", LastName = "Ancien", PhoneNumber = "000",
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        await using var context = CreateContext(Guid.NewGuid().ToString());
        context.Users.Add(user);
        await context.SaveChangesAsync();
        var service = new UserProvisioningService(context);

        var outcome = await service.UpdateUserProfileAsync(foreignCompanyId, user.Id, new UpdateUserProfileRequest
        {
            FirstName = "Alice", LastName = "Martin", PhoneNumber = "123",
        }, CancellationToken.None);

        Assert.IsFalse(outcome.Success);
        Assert.AreEqual(UserProfileUpdateErrorKind.UserNotFound, outcome.ErrorKind);
        Assert.AreEqual("Avant", user.FirstName);
        Assert.AreEqual("Ancien", user.LastName);
        Assert.AreEqual("000", user.PhoneNumber);
    }

    [TestMethod]
    public async Task UpdateUserProfileAsync_RejectsEmptyAndOversizedFields()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString());
        var service = new UserProvisioningService(context);
        var empty = await service.UpdateUserProfileAsync(Guid.NewGuid(), Guid.NewGuid(), new UpdateUserProfileRequest
        {
            FirstName = " ", LastName = "Martin", PhoneNumber = "123",
        }, CancellationToken.None);
        var tooLong = await service.UpdateUserProfileAsync(Guid.NewGuid(), Guid.NewGuid(), new UpdateUserProfileRequest
        {
            FirstName = "Alice", LastName = new string('x', 101), PhoneNumber = "123",
        }, CancellationToken.None);

        Assert.AreEqual(UserProfileUpdateErrorKind.MissingRequiredField, empty.ErrorKind);
        Assert.AreEqual(UserProfileUpdateErrorKind.FieldTooLong, tooLong.ErrorKind);
    }
}
