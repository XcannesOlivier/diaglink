using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebApp.Api.Data;
using WebApp.Api.Models.Entities;
using WebApp.Api.Services;

namespace WebApp.Api.Tests;

/// <summary>
/// Covers CompanyOnboardingService (company creation + first company_admin onboarding). No HTTP pipeline
/// is spun up here (this project has no WebApplicationFactory scaffolding) — authorization (SuperAdminOnly
/// rejects company_admin/technician) is enforced declaratively via .RequireAuthorization("SuperAdminOnly")
/// on the minimal API routes in Program.cs, the same untested-at-HTTP-layer pattern already used by every
/// other endpoint in this project (GetCompanies, GetCompanyUsers, GetMachines, ...).
/// </summary>
[TestClass]
public class CompanyOnboardingServiceTests
{
    private static DiagLinkDbContext CreateContext(string dbName)
    {
        // OnboardCompanyAsync opens an explicit transaction (real behavior against Azure SQL); the
        // in-memory provider doesn't support transactions and would otherwise escalate that to an error.
        var options = new DbContextOptionsBuilder<DiagLinkDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new DiagLinkDbContext(options);
    }

    private static CompanyOnboardingService CreateService(DiagLinkDbContext context)
        => new(context, NullLogger<CompanyOnboardingService>.Instance);

    [TestMethod]
    public async Task CreateCompanyAsync_CreatesActiveCompanyWithServerGeneratedFields()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString());
        var service = CreateService(context);

        var outcome = await service.CreateCompanyAsync("Acme Corp", CancellationToken.None);

        Assert.IsTrue(outcome.Success);
        Assert.AreEqual("Acme Corp", outcome.Company!.Name);
        Assert.AreEqual("active", outcome.Company.Status);
        Assert.IsTrue(Guid.TryParse(outcome.Company.Id, out _));
    }

    [TestMethod]
    public async Task CreateCompanyAsync_RejectsEmptyName()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString());
        var service = CreateService(context);

        var outcome = await service.CreateCompanyAsync("   ", CancellationToken.None);

        Assert.IsFalse(outcome.Success);
        Assert.AreEqual(CompanyOnboardingErrorKind.InvalidCompanyName, outcome.ErrorKind);
    }

    [TestMethod]
    public async Task CreateCompanyAsync_AllowsNormalizedDuplicateName()
    {
        var dbName = Guid.NewGuid().ToString();
        await using (var seedContext = CreateContext(dbName))
        {
            seedContext.Companies.Add(new Company { Id = Guid.NewGuid(), Name = "Acme Corp", Status = "active", CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow });
            await seedContext.SaveChangesAsync();
        }

        await using var context = CreateContext(dbName);
        var service = CreateService(context);

        var outcome = await service.CreateCompanyAsync("  acme corp  ", CancellationToken.None);

        Assert.IsTrue(outcome.Success);
    }

    [TestMethod]
    public async Task AddCompanyAdminAsync_ForcesCompanyAdminRoleAndActiveStatus()
    {
        var dbName = Guid.NewGuid().ToString();
        var companyId = Guid.NewGuid();
        await using (var seedContext = CreateContext(dbName))
        {
            seedContext.Companies.Add(new Company { Id = companyId, Name = "Acme Corp", Status = "active", CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow });
            await seedContext.SaveChangesAsync();
        }

        await using var context = CreateContext(dbName);
        var service = CreateService(context);

        var outcome = await service.AddCompanyAdminAsync(companyId, "admin@acme.test", CancellationToken.None);

        Assert.IsTrue(outcome.Success);
        Assert.AreEqual("company_admin", outcome.Admin!.Role);
        Assert.AreEqual("active", outcome.Admin.Status);
        Assert.AreEqual("admin@acme.test", outcome.Admin.Email);

        var stored = await context.Users.FirstAsync(u => u.Email == "admin@acme.test");
        Assert.AreEqual(companyId, stored.CompanyId);
        Assert.AreEqual("company_admin", stored.Role);
        Assert.IsNull(stored.EntraObjectId);
    }

    [TestMethod]
    public async Task AddCompanyAdminAsync_RejectsUnknownCompany()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString());
        var service = CreateService(context);

        var outcome = await service.AddCompanyAdminAsync(Guid.NewGuid(), "admin@acme.test", CancellationToken.None);

        Assert.IsFalse(outcome.Success);
        Assert.AreEqual(CompanyOnboardingErrorKind.CompanyNotFound, outcome.ErrorKind);
    }

    [TestMethod]
    public async Task AddCompanyAdminAsync_RejectsDuplicateEmailCaseInsensitive()
    {
        var dbName = Guid.NewGuid().ToString();
        var companyId = Guid.NewGuid();
        await using (var seedContext = CreateContext(dbName))
        {
            seedContext.Companies.Add(new Company { Id = companyId, Name = "Acme Corp", Status = "active", CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow });
            seedContext.Users.Add(new User { Id = Guid.NewGuid(), CompanyId = companyId, Email = "Admin@Acme.test", Role = "company_admin", Status = "active", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
            await seedContext.SaveChangesAsync();
        }

        await using var context = CreateContext(dbName);
        var service = CreateService(context);

        var outcome = await service.AddCompanyAdminAsync(companyId, "admin@acme.test", CancellationToken.None);

        Assert.IsFalse(outcome.Success);
        Assert.AreEqual(CompanyOnboardingErrorKind.DuplicateEmail, outcome.ErrorKind);
    }

    [TestMethod]
    public async Task OnboardCompanyAsync_CreatesCompanyAndAdminTogether()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString());
        var service = CreateService(context);

        var outcome = await service.OnboardCompanyAsync("Globex", "admin@globex.test", "John", "Doe", "0123456789", CancellationToken.None);

        Assert.IsTrue(outcome.Success);
        Assert.AreEqual("Globex", outcome.Result!.Company.Name);
        Assert.AreEqual("admin@globex.test", outcome.Result.Admin.Email);
        Assert.AreEqual("company_admin", outcome.Result.Admin.Role);
        Assert.AreEqual("active", outcome.Result.Admin.Status);

        var storedAdmin = await context.Users.FirstAsync(u => u.Email == "admin@globex.test");
        Assert.AreEqual(Guid.Parse(outcome.Result.Company.Id), storedAdmin.CompanyId);
    }

    [TestMethod]
    public async Task OnboardCompanyAsync_DoesNotCreateOrphanedCompanyWhenAdminEmailIsDuplicate()
    {
        var dbName = Guid.NewGuid().ToString();
        await using (var seedContext = CreateContext(dbName))
        {
            seedContext.Companies.Add(new Company { Id = Guid.NewGuid(), Name = "Other Co", Status = "active", CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow });
            seedContext.Users.Add(new User { Id = Guid.NewGuid(), CompanyId = Guid.NewGuid(), Email = "admin@globex.test", Role = "company_admin", Status = "active", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
            await seedContext.SaveChangesAsync();
        }

        await using var context = CreateContext(dbName);
        var service = CreateService(context);

        var outcome = await service.OnboardCompanyAsync("Globex", "admin@globex.test", "John", "Doe", "0123456789", CancellationToken.None);

        Assert.IsFalse(outcome.Success);
        Assert.AreEqual(CompanyOnboardingErrorKind.DuplicateEmail, outcome.ErrorKind);
        Assert.AreEqual("Cette adresse e-mail est déjà associée à un utilisateur.", outcome.ErrorMessage);

        var companyCount = await context.Companies.CountAsync(c => c.Name == "Globex");
        Assert.AreEqual(0, companyCount);
    }

    [TestMethod]
    public async Task OnboardCompanyAsync_RejectsInvalidEmail()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString());
        var service = CreateService(context);
        var outcome = await service.OnboardCompanyAsync("Globex", "not-an-email", "John", "Doe", "0123456789", CancellationToken.None);

        Assert.IsFalse(outcome.Success);
        Assert.AreEqual(CompanyOnboardingErrorKind.InvalidEmail, outcome.ErrorKind);
    }

    [TestMethod]
    public async Task OnboardCompanyAsync_TrimsContactInfoAndStores()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString());
        var service = CreateService(context);

        var outcome = await service.OnboardCompanyAsync(" TrimCo ", " admin@trim.test ", "  Alice  ", "  Smith  ", "  0123  ", CancellationToken.None);

        Assert.IsTrue(outcome.Success);
        var storedAdmin = await context.Users.FirstAsync(u => u.Email == "admin@trim.test");
        Assert.AreEqual("Alice", storedAdmin.FirstName);
        Assert.AreEqual("Smith", storedAdmin.LastName);
        Assert.AreEqual("0123", storedAdmin.PhoneNumber);
    }

    [TestMethod]
    public async Task OnboardCompanyAsync_RejectsEmptyFirstName()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString());
        var service = CreateService(context);

        var outcome = await service.OnboardCompanyAsync("Co", "admin@co.test", "   ", "Doe", "0123", CancellationToken.None);
        Assert.IsFalse(outcome.Success);
        Assert.AreEqual(CompanyOnboardingErrorKind.InvalidFirstName, outcome.ErrorKind);
    }

    [TestMethod]
    public async Task OnboardCompanyAsync_RejectsEmptyLastName()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString());
        var service = CreateService(context);

        var outcome = await service.OnboardCompanyAsync("Co", "admin@co.test", "John", "   ", "0123", CancellationToken.None);
        Assert.IsFalse(outcome.Success);
        Assert.AreEqual(CompanyOnboardingErrorKind.InvalidLastName, outcome.ErrorKind);
    }

    [TestMethod]
    public async Task OnboardCompanyAsync_RejectsEmptyPhoneNumber()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString());
        var service = CreateService(context);

        var outcome = await service.OnboardCompanyAsync("Co", "admin@co.test", "John", "Doe", "   ", CancellationToken.None);
        Assert.IsFalse(outcome.Success);
        Assert.AreEqual(CompanyOnboardingErrorKind.InvalidPhoneNumber, outcome.ErrorKind);
    }

    [TestMethod]
    public async Task OnboardCompanyAsync_RejectsTooLongContactValues()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString());
        var service = CreateService(context);

        var longFirst = new string('A', 101);
        var longLast = new string('B', 101);
        var longPhone = new string('9', 31);

        var outcome1 = await service.OnboardCompanyAsync("Co", "admin@co.test", longFirst, "Doe", "0123", CancellationToken.None);
        Assert.IsFalse(outcome1.Success);
        Assert.AreEqual(CompanyOnboardingErrorKind.InvalidFirstName, outcome1.ErrorKind);

        var outcome2 = await service.OnboardCompanyAsync("Co", "admin@co.test", "John", longLast, "0123", CancellationToken.None);
        Assert.IsFalse(outcome2.Success);
        Assert.AreEqual(CompanyOnboardingErrorKind.InvalidLastName, outcome2.ErrorKind);

        var outcome3 = await service.OnboardCompanyAsync("Co", "admin@co.test", "John", "Doe", longPhone, CancellationToken.None);
        Assert.IsFalse(outcome3.Success);
        Assert.AreEqual(CompanyOnboardingErrorKind.InvalidPhoneNumber, outcome3.ErrorKind);
    }
}
