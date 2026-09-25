using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebApp.Api.Data;
using WebApp.Api.Models;
using WebApp.Api.Models.Entities;
using WebApp.Api.Services;

namespace WebApp.Api.Tests;

[TestClass]
public sealed class AdditionalDocumentsContextResolverTests
{
    [TestMethod]
    public async Task ActiveCompanyAdminAndOwnedActiveMachineResolveReadOnlyWithoutSubscription()
    {
        await using var db = Database();
        var seed = await SeedAsync(db, subscriptionId: null);
        var counts = await CountsAsync(db);
        db.ChangeTracker.Clear();

        var result = await Resolver(db).ResolveAsync(
            Principal(seed.UserId.ToString(), seed.CompanyId.ToString(), DiagLinkRoles.CompanyAdmin),
            seed.MachineId, default);

        Assert.IsTrue(result.Success);
        Assert.IsNotNull(result.Context);
        Assert.AreEqual(seed.UserId, result.Context.UserId);
        Assert.AreEqual(seed.CompanyId, result.Context.CompanyId);
        Assert.AreEqual(seed.MachineId, result.Context.MachineId);
        Assert.AreEqual("cus_company", result.Context.StripeCustomerId);
        Assert.AreEqual(counts, await CountsAsync(db));
        Assert.IsFalse(db.ChangeTracker.HasChanges());
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("not-a-guid")]
    public async Task MissingOrInvalidUserIdClaimIsForbidden(string? userId)
    {
        await using var db = Database();
        var seed = await SeedAsync(db);

        var result = await Resolver(db).ResolveAsync(
            Principal(userId, seed.CompanyId.ToString(), DiagLinkRoles.CompanyAdmin), seed.MachineId, default);

        Assert.AreEqual(AdditionalDocumentsContextError.Forbidden, result.Error);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("not-a-guid")]
    public async Task MissingOrInvalidCompanyIdClaimIsForbidden(string? companyId)
    {
        await using var db = Database();
        var seed = await SeedAsync(db);

        var result = await Resolver(db).ResolveAsync(
            Principal(seed.UserId.ToString(), companyId, DiagLinkRoles.CompanyAdmin), seed.MachineId, default);

        Assert.AreEqual(AdditionalDocumentsContextError.Forbidden, result.Error);
    }

    [TestMethod]
    public async Task UnknownOrInactiveUserIsForbidden()
    {
        await using var db = Database();
        var seed = await SeedAsync(db, userStatus: "inactive");

        var inactive = await Resolver(db).ResolveAsync(
            Principal(seed.UserId.ToString(), seed.CompanyId.ToString(), DiagLinkRoles.CompanyAdmin),
            seed.MachineId, default);
        var unknown = await Resolver(db).ResolveAsync(
            Principal(Guid.NewGuid().ToString(), seed.CompanyId.ToString(), DiagLinkRoles.CompanyAdmin),
            seed.MachineId, default);

        Assert.AreEqual(AdditionalDocumentsContextError.Forbidden, inactive.Error);
        Assert.AreEqual(AdditionalDocumentsContextError.Forbidden, unknown.Error);
    }

    [TestMethod]
    [DataRow(DiagLinkRoles.Technician)]
    [DataRow(DiagLinkRoles.SuperAdmin)]
    public async Task NonCompanyAdminPrincipalIsForbidden(string role)
    {
        await using var db = Database();
        var seed = await SeedAsync(db, userRole: role);

        var result = await Resolver(db).ResolveAsync(
            Principal(seed.UserId.ToString(), seed.CompanyId.ToString(), role), seed.MachineId, default);

        Assert.AreEqual(AdditionalDocumentsContextError.Forbidden, result.Error);
    }

    [TestMethod]
    public async Task SqlRoleMustStillBeCompanyAdmin()
    {
        await using var db = Database();
        var seed = await SeedAsync(db, userRole: DiagLinkRoles.Technician);

        var result = await Resolver(db).ResolveAsync(
            Principal(seed.UserId.ToString(), seed.CompanyId.ToString(), DiagLinkRoles.CompanyAdmin),
            seed.MachineId, default);

        Assert.AreEqual(AdditionalDocumentsContextError.Forbidden, result.Error);
    }

    [TestMethod]
    public async Task MissingOrInactiveCompanyIsUnavailable()
    {
        await using var missingDb = Database();
        var missing = await SeedAsync(missingDb, includeCompany: false);
        var missingResult = await Resolver(missingDb).ResolveAsync(
            Principal(missing.UserId.ToString(), missing.CompanyId.ToString(), DiagLinkRoles.CompanyAdmin),
            missing.MachineId, default);

        await using var inactiveDb = Database();
        var inactive = await SeedAsync(inactiveDb, companyStatus: "inactive");
        var inactiveResult = await Resolver(inactiveDb).ResolveAsync(
            Principal(inactive.UserId.ToString(), inactive.CompanyId.ToString(), DiagLinkRoles.CompanyAdmin),
            inactive.MachineId, default);

        Assert.AreEqual(AdditionalDocumentsContextError.CompanyUnavailable, missingResult.Error);
        Assert.AreEqual(AdditionalDocumentsContextError.CompanyUnavailable, inactiveResult.Error);
    }

    [TestMethod]
    public async Task MissingForeignOrInactiveMachineIsUnavailable()
    {
        await using var db = Database();
        var seed = await SeedAsync(db);
        var otherCompanyId = Guid.NewGuid();
        var otherMachineId = Guid.NewGuid();
        db.Companies.Add(Company(otherCompanyId, "active"));
        db.Machines.Add(new Machine
        {
            Id = otherMachineId, CompanyId = otherCompanyId, Name = "Other", Status = "active",
            CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
        });
        var inactiveMachineId = Guid.NewGuid();
        db.Machines.Add(new Machine
        {
            Id = inactiveMachineId, CompanyId = seed.CompanyId, Name = "Inactive", Status = "inactive",
            CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var resolver = Resolver(db);
        var principal = Principal(seed.UserId.ToString(), seed.CompanyId.ToString(), DiagLinkRoles.CompanyAdmin);

        var missing = await resolver.ResolveAsync(principal, Guid.NewGuid(), default);
        var foreign = await resolver.ResolveAsync(principal, otherMachineId, default);
        var inactive = await resolver.ResolveAsync(principal, inactiveMachineId, default);

        Assert.AreEqual(AdditionalDocumentsContextError.MachineUnavailable, missing.Error);
        Assert.AreEqual(AdditionalDocumentsContextError.MachineUnavailable, foreign.Error);
        Assert.AreEqual(AdditionalDocumentsContextError.MachineUnavailable, inactive.Error);
    }

    [TestMethod]
    public async Task CompanyClaimCannotSelectACompanyDifferentFromTheSqlUserCompany()
    {
        await using var db = Database();
        var seed = await SeedAsync(db);
        var browserCompanyId = Guid.NewGuid();

        var result = await Resolver(db).ResolveAsync(
            Principal(seed.UserId.ToString(), browserCompanyId.ToString(), DiagLinkRoles.CompanyAdmin),
            seed.MachineId, default);

        Assert.AreEqual(AdditionalDocumentsContextError.Forbidden, result.Error);
    }

    [TestMethod]
    public async Task MissingBillingAccountOrStripeCustomerIsRejected()
    {
        await using var missingDb = Database();
        var missing = await SeedAsync(missingDb, includeBillingAccount: false);
        var missingResult = await Resolver(missingDb).ResolveAsync(
            Principal(missing.UserId.ToString(), missing.CompanyId.ToString(), DiagLinkRoles.CompanyAdmin),
            missing.MachineId, default);

        await using var customerDb = Database();
        var customer = await SeedAsync(customerDb, customerId: "   ", subscriptionId: null);
        var customerResult = await Resolver(customerDb).ResolveAsync(
            Principal(customer.UserId.ToString(), customer.CompanyId.ToString(), DiagLinkRoles.CompanyAdmin),
            customer.MachineId, default);

        Assert.AreEqual(AdditionalDocumentsContextError.BillingAccountMissing, missingResult.Error);
        Assert.AreEqual(AdditionalDocumentsContextError.StripeCustomerMissing, customerResult.Error);
    }

    private static AdditionalDocumentsContextResolver Resolver(DiagLinkDbContext db) =>
        new(db, new DiagLinkUserLookupService(db));

    private static DiagLinkDbContext Database() => new(new DbContextOptionsBuilder<DiagLinkDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static ClaimsPrincipal Principal(string? userId, string? companyId, string role)
    {
        var claims = new List<Claim> { new(ClaimTypes.Role, role) };
        if (userId is not null) claims.Add(new(DiagLinkClaimTypes.UserId, userId));
        if (companyId is not null) claims.Add(new(DiagLinkClaimTypes.CompanyId, companyId));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    private static async Task<(Guid UserId, Guid CompanyId, Guid MachineId)> SeedAsync(
        DiagLinkDbContext db,
        string userRole = DiagLinkRoles.CompanyAdmin,
        string userStatus = "active",
        bool includeCompany = true,
        string companyStatus = "active",
        bool includeBillingAccount = true,
        string? customerId = "cus_company",
        string? subscriptionId = "sub_company")
    {
        var now = DateTime.UtcNow;
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var machineId = Guid.NewGuid();
        if (includeCompany) db.Companies.Add(Company(companyId, companyStatus));
        db.Users.Add(new User
        {
            Id = userId, CompanyId = companyId, Email = "admin@example.com", Role = userRole,
            Status = userStatus, CreatedAt = now, UpdatedAt = now
        });
        db.Machines.Add(new Machine
        {
            Id = machineId, CompanyId = companyId, Name = "Compresseur", Status = "active",
            CreatedAtUtc = now, UpdatedAtUtc = now
        });
        if (includeBillingAccount) db.BillingAccounts.Add(new BillingAccount
        {
            Id = Guid.NewGuid(), CompanyId = companyId, StripeCustomerId = customerId,
            StripeSubscriptionId = subscriptionId, CreatedAtUtc = now, UpdatedAtUtc = now
        });
        await db.SaveChangesAsync();
        return (userId, companyId, machineId);
    }

    private static Company Company(Guid id, string status) => new()
    {
        Id = id, Name = "Company", Status = status,
        CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
    };

    private static async Task<(int Users, int Companies, int Machines, int BillingAccounts, int Payments)> CountsAsync(
        DiagLinkDbContext db) => (
        await db.Users.CountAsync(), await db.Companies.CountAsync(), await db.Machines.CountAsync(),
        await db.BillingAccounts.CountAsync(), await db.MachineRequestPayments.CountAsync());
}
