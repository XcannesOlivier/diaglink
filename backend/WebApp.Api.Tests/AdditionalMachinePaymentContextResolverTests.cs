using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebApp.Api.Data;
using WebApp.Api.Models;
using WebApp.Api.Models.Entities;
using WebApp.Api.Services;

namespace WebApp.Api.Tests;

[TestClass]
public class AdditionalMachinePaymentContextResolverTests
{
    [TestMethod]
    public async Task CompanyAdminWithActiveCompanyAndBillingAccount_ResolvesServerContextReadOnly()
    {
        await using var db = Database();
        var seed = await SeedAsync(db);
        var counts = await CountsAsync(db);
        db.ChangeTracker.Clear();

        var result = await Resolver(db).ResolveAsync(
            Principal(seed.UserId, seed.CompanyId, DiagLinkRoles.CompanyAdmin), default);

        Assert.IsTrue(result.Success);
        Assert.IsNotNull(result.Context);
        Assert.AreEqual(seed.UserId, result.Context.UserId);
        Assert.AreEqual(seed.CompanyId, result.Context.CompanyId);
        Assert.AreEqual("cus_company", result.Context.StripeCustomerId);
        Assert.AreEqual("sub_company", result.Context.StripeSubscriptionId);
        Assert.AreEqual(counts, await CountsAsync(db));
        Assert.IsFalse(db.ChangeTracker.HasChanges());
    }

    [TestMethod]
    public async Task Technician_IsForbiddenBeforeAnyWrite()
    {
        await using var db = Database();
        var seed = await SeedAsync(db, role: DiagLinkRoles.Technician);
        var counts = await CountsAsync(db);
        db.ChangeTracker.Clear();

        var result = await Resolver(db).ResolveAsync(
            Principal(seed.UserId, seed.CompanyId, DiagLinkRoles.Technician), default);

        Assert.AreEqual(AdditionalMachinePaymentContextError.Forbidden, result.Error);
        Assert.AreEqual(counts, await CountsAsync(db));
        Assert.IsFalse(db.ChangeTracker.HasChanges());
    }

    [TestMethod]
    public async Task InactiveUser_IsForbidden()
    {
        await using var db = Database();
        var seed = await SeedAsync(db, userStatus: "inactive");

        var result = await Resolver(db).ResolveAsync(
            Principal(seed.UserId, seed.CompanyId, DiagLinkRoles.CompanyAdmin), default);

        Assert.AreEqual(AdditionalMachinePaymentContextError.Forbidden, result.Error);
    }

    [TestMethod]
    public async Task InactiveCompany_IsRejected()
    {
        await using var db = Database();
        var seed = await SeedAsync(db, companyStatus: "inactive");

        var result = await Resolver(db).ResolveAsync(
            Principal(seed.UserId, seed.CompanyId, DiagLinkRoles.CompanyAdmin), default);

        Assert.AreEqual(AdditionalMachinePaymentContextError.CompanyUnavailable, result.Error);
    }

    [TestMethod]
    public async Task MissingBillingAccount_IsRejectedCleanly()
    {
        await using var db = Database();
        var seed = await SeedAsync(db, includeBillingAccount: false);

        var result = await Resolver(db).ResolveAsync(
            Principal(seed.UserId, seed.CompanyId, DiagLinkRoles.CompanyAdmin), default);

        Assert.AreEqual(AdditionalMachinePaymentContextError.BillingAccountMissing, result.Error);
    }

    [TestMethod]
    [DataRow(null, "sub_company", AdditionalMachinePaymentContextError.StripeCustomerMissing)]
    [DataRow("   ", "sub_company", AdditionalMachinePaymentContextError.StripeCustomerMissing)]
    [DataRow("cus_company", null, AdditionalMachinePaymentContextError.StripeSubscriptionMissing)]
    [DataRow("cus_company", "   ", AdditionalMachinePaymentContextError.StripeSubscriptionMissing)]
    public async Task IncompleteStripeIdentity_IsRejectedCleanly(
        string? customerId,
        string? subscriptionId,
        AdditionalMachinePaymentContextError expected)
    {
        await using var db = Database();
        var seed = await SeedAsync(db, customerId: customerId, subscriptionId: subscriptionId);

        var result = await Resolver(db).ResolveAsync(
            Principal(seed.UserId, seed.CompanyId, DiagLinkRoles.CompanyAdmin), default);

        Assert.AreEqual(expected, result.Error);
    }

    [TestMethod]
    public async Task MismatchedCompanyClaim_CannotSelectAnotherCompany()
    {
        await using var db = Database();
        var seed = await SeedAsync(db);
        var otherCompanyId = Guid.NewGuid();
        db.Companies.Add(new Company
        {
            Id = otherCompanyId, Name = "Other", Status = "active",
            CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
        });
        db.BillingAccounts.Add(Account(otherCompanyId, "cus_other", "sub_other"));
        await db.SaveChangesAsync();

        var result = await Resolver(db).ResolveAsync(
            Principal(seed.UserId, otherCompanyId, DiagLinkRoles.CompanyAdmin), default);

        Assert.AreEqual(AdditionalMachinePaymentContextError.Forbidden, result.Error);
        Assert.IsNull(result.Context);
    }

    private static AdditionalMachinePaymentContextResolver Resolver(DiagLinkDbContext db) =>
        new(db, new DiagLinkUserLookupService(db));

    private static DiagLinkDbContext Database() => new(new DbContextOptionsBuilder<DiagLinkDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static ClaimsPrincipal Principal(Guid userId, Guid companyId, string role) => new(
        new ClaimsIdentity(
        [
            new Claim(DiagLinkClaimTypes.UserId, userId.ToString()),
            new Claim(DiagLinkClaimTypes.CompanyId, companyId.ToString()),
            new Claim(ClaimTypes.Role, role)
        ], "Test"));

    private static async Task<(Guid UserId, Guid CompanyId)> SeedAsync(
        DiagLinkDbContext db,
        string role = DiagLinkRoles.CompanyAdmin,
        string userStatus = "active",
        string companyStatus = "active",
        bool includeBillingAccount = true,
        string? customerId = "cus_company",
        string? subscriptionId = "sub_company")
    {
        var now = DateTime.UtcNow;
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        db.Companies.Add(new Company
        {
            Id = companyId, Name = "Company", Status = companyStatus,
            CreatedAtUtc = now, UpdatedAtUtc = now
        });
        db.Users.Add(new User
        {
            Id = userId, CompanyId = companyId, Email = "admin@example.com", Role = role,
            Status = userStatus, CreatedAt = now, UpdatedAt = now
        });
        if (includeBillingAccount) db.BillingAccounts.Add(Account(companyId, customerId, subscriptionId));
        await db.SaveChangesAsync();
        return (userId, companyId);
    }

    private static BillingAccount Account(Guid companyId, string? customerId, string? subscriptionId) => new()
    {
        Id = Guid.NewGuid(), CompanyId = companyId, StripeCustomerId = customerId,
        StripeSubscriptionId = subscriptionId, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
    };

    private static async Task<(int Users, int Companies, int BillingAccounts, int Payments, int Machines)> CountsAsync(
        DiagLinkDbContext db) => (
        await db.Users.CountAsync(),
        await db.Companies.CountAsync(),
        await db.BillingAccounts.CountAsync(),
        await db.MachineRequestPayments.CountAsync(),
        await db.Machines.CountAsync());
}
