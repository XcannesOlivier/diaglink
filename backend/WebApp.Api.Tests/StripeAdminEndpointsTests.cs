using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebApp.Api.Data;
using WebApp.Api.Services;
using Fixture = WebApp.Api.Tests.AiCreditConsumptionServiceTests.Fixture;

namespace WebApp.Api.Tests;

public partial class StripeBillingTests
{
    [TestMethod]
    public async Task AdminReadDoesNotCreateAccountAndCountsOnlyCompanyActiveMachines()
    {
        await using var f = new Fixture(); await Prepare(f); await using var db = f.Db();
        var result = await StripeAdminEndpoints.ReadAsync(f.CompanyId, db, Options, default);
        var summary = (StripeCompanySummary)((IValueHttpResult)result).Value!;
        Assert.AreEqual(1, summary.ActiveMachineCount); Assert.IsNull(summary.StripeCustomerId);
        Assert.IsNull(summary.CurrentPeriodStartUtc); Assert.IsTrue(summary.TestActionsEnabled);
        Assert.AreEqual(0, await db.BillingAccounts.CountAsync());
        Assert.AreEqual(404, ((IStatusCodeHttpResult)await StripeAdminEndpoints.ReadAsync(Guid.NewGuid(), db, Options, default)).StatusCode);
    }

    [TestMethod]
    public async Task AdminWritesUseExistingServiceAndRefreshSummaryIdempotently()
    {
        await using var f = new Fixture(); await Prepare(f); await using var db = f.Db();
        var gateway = new FakeGateway(); var billing = new StripeBillingService(f.Options, gateway, Options);
        await StripeAdminEndpoints.WriteAsync(f.CompanyId, false, db, billing, Options, default);
        await StripeAdminEndpoints.WriteAsync(f.CompanyId, false, db, billing, Options, default);
        await StripeAdminEndpoints.WriteAsync(f.CompanyId, true, db, billing, Options, default);
        var result = await StripeAdminEndpoints.WriteAsync(f.CompanyId, true, db, billing, Options, default);
        var summary = (StripeCompanySummary)((IValueHttpResult)result).Value!;
        Assert.AreEqual("cus_local", summary.StripeCustomerId); Assert.AreEqual("sub_local", summary.StripeSubscriptionId);
        Assert.AreEqual("incomplete", summary.SubscriptionStatus);
        Assert.AreEqual(DateTimeKind.Utc, summary.CurrentPeriodStartUtc!.Value.Kind);
        Assert.AreEqual(1, gateway.Customers); Assert.AreEqual(1, gateway.Subscriptions); Assert.AreEqual(1, gateway.Quantity);
        Assert.AreEqual(0, await db.CompanyWallets.CountAsync()); Assert.AreEqual(0, await db.CreditLedger.CountAsync());
        Assert.AreEqual(1, await db.MachineBillingPeriods.CountAsync());
    }

    [TestMethod]
    public async Task AdminLiveActionsRefusedBeforeDatabaseOrStripeEvenWithLiveOptIn()
    {
        await using var f = new Fixture(); await using var db = f.Db();
        var settings = new StripeBillingOptions { Enabled = true, AllowLive = true, SecretKey = "sk_live_local", PriceId = "price_local" };
        var gateway = new FakeGateway(); var billing = new StripeBillingService(f.Options, gateway, settings);
        var result = await StripeAdminEndpoints.WriteAsync(Guid.NewGuid(), true, db, billing, settings, default);
        Assert.AreEqual(409, ((IStatusCodeHttpResult)result).StatusCode);
        Assert.AreEqual(0, gateway.Customers); Assert.AreEqual(0, gateway.Subscriptions);
        Assert.IsFalse(StripeAdminEndpoints.TestActionsEnabled(new StripeBillingOptions()));
    }

    [TestMethod]
    public async Task AllStripeAdminRoutesRequireSuperAdminPolicy()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddScoped<DiagLinkDbContext>();
        builder.Services.AddScoped<AiCreditAccessService>();
        builder.Services.AddScoped<StripeBillingService>();
        builder.Services.AddScoped<StripeMachineAdditionService>();
        builder.Services.AddSingleton(Options);
        await using var app = builder.Build();
        app.MapStripeAdminEndpoints();
        var routes = ((IEndpointRouteBuilder)app).DataSources.SelectMany(s => s.Endpoints).ToList();
        Assert.AreEqual(12, routes.Count);
        foreach (var endpoint in routes)
        {
            Assert.IsTrue(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Any(a => a.Policy == "SuperAdminOnly"));
            Assert.IsNull(endpoint.Metadata.GetMetadata<IAllowAnonymous>());
        }
    }
}
