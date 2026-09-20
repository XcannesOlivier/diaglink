using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebApp.Api.Models;

namespace WebApp.Api.Tests;

/// <summary>
/// Exercises the exact RolesAuthorizationRequirement produced by `.RequireRole(...)` in Program.cs for
/// the TechnicianOrAbove/CompanyAdminOrAbove/SuperAdminOnly policies — RolesAuthorizationRequirement is
/// self-handling (implements IAuthorizationHandler), so this is the real evaluation logic, not a re-implementation.
/// </summary>
[TestClass]
public class RolePolicyTests
{
    private static readonly string[] CompanyAdminOrAboveRoles = [DiagLinkRoles.CompanyAdmin, DiagLinkRoles.SuperAdmin];
    private static readonly string[] SuperAdminOnlyRoles = [DiagLinkRoles.SuperAdmin];
    private static readonly string[] TechnicianOrAboveRoles =
        [DiagLinkRoles.Technician, DiagLinkRoles.CompanyAdmin, DiagLinkRoles.SuperAdmin];

    private static ClaimsPrincipal BuildPrincipal(string role)
    {
        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, role) }, "TestAuth");
        return new ClaimsPrincipal(identity);
    }

    private static async Task<bool> EvaluateAsync(string[] allowedRoles, string userRole)
    {
        var requirement = new RolesAuthorizationRequirement(allowedRoles);
        var context = new AuthorizationHandlerContext(new IAuthorizationRequirement[] { requirement }, BuildPrincipal(userRole), resource: null);
        await requirement.HandleAsync(context);
        return context.HasSucceeded;
    }

    [TestMethod]
    public async Task CompanyAdminOnly_RejectsOtherRoles()
    {
        string[] roles = [DiagLinkRoles.CompanyAdmin];
        Assert.IsTrue(await EvaluateAsync(roles, DiagLinkRoles.CompanyAdmin));
        Assert.IsFalse(await EvaluateAsync(roles, DiagLinkRoles.SuperAdmin));
        Assert.IsFalse(await EvaluateAsync(roles, DiagLinkRoles.Technician));
    }

    [TestMethod]
    public async Task CompanyAdminOrAbove_RejectsTechnician()
        => Assert.IsFalse(await EvaluateAsync(CompanyAdminOrAboveRoles, DiagLinkRoles.Technician));

    [TestMethod]
    public async Task CompanyAdminOrAbove_AcceptsCompanyAdmin()
        => Assert.IsTrue(await EvaluateAsync(CompanyAdminOrAboveRoles, DiagLinkRoles.CompanyAdmin));

    [TestMethod]
    public async Task CompanyAdminOrAbove_AcceptsSuperAdmin()
        => Assert.IsTrue(await EvaluateAsync(CompanyAdminOrAboveRoles, DiagLinkRoles.SuperAdmin));

    [TestMethod]
    public async Task SuperAdminOnly_RejectsCompanyAdmin()
        => Assert.IsFalse(await EvaluateAsync(SuperAdminOnlyRoles, DiagLinkRoles.CompanyAdmin));

    [TestMethod]
    public async Task SuperAdminOnly_AcceptsSuperAdmin()
        => Assert.IsTrue(await EvaluateAsync(SuperAdminOnlyRoles, DiagLinkRoles.SuperAdmin));

    [TestMethod]
    public async Task TechnicianOrAbove_AcceptsTechnician()
        => Assert.IsTrue(await EvaluateAsync(TechnicianOrAboveRoles, DiagLinkRoles.Technician));

    [TestMethod]
    public async Task TechnicianOrAbove_AcceptsCompanyAdmin()
        => Assert.IsTrue(await EvaluateAsync(TechnicianOrAboveRoles, DiagLinkRoles.CompanyAdmin));
}
