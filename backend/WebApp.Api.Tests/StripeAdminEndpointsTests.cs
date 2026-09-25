using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebApp.Api.Data;
using WebApp.Api.Models.Entities;
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
    [DataRow(MachineRequestProvisioningStage.SubscriptionCreated, false)]
    [DataRow(MachineRequestProvisioningStage.Completed, true)]
    public async Task AdminReadReportsActualMachineRequestProvisioningCompletion(
        MachineRequestProvisioningStage stage, bool expected)
    {
        await using var f = new Fixture(); await Prepare(f); await using var db = f.Db();
        var now = new DateTime(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc);
        var payment = new WebApp.Api.Models.Entities.MachineRequestPayment
        {
            Id = Guid.NewGuid(), Status = MachineRequestPaymentStatus.Captured, EstimatedTotalPages = 416,
            AmountCents = 13412, Currency = "EUR", StripePaymentIntentId = "pi_summary",
            AuthorizationEventId = "evt_summary", AuthorizedAtUtc = now, CapturedAtUtc = now,
            CompanyId = f.CompanyId, MachineId = f.MachineId, CreatedAtUtc = now, UpdatedAtUtc = now,
            ActivatedAtUtc = now, FirstPeriodEndUtc = now.AddDays(7), ServiceAmountCents = 1495,
            FinalCaptureAmountCents = 11917, ProvisioningStage = stage,
            ProvisioningCompletedAtUtc = stage == MachineRequestProvisioningStage.Completed ? now : null,
            RowVersion = [1]
        };
        await db.Database.ExecuteSqlInterpolatedAsync($@"INSERT INTO MachineRequestPayments
          (Id,Status,EstimatedTotalPages,AmountCents,Currency,StripePaymentIntentId,AuthorizationEventId,
           CreatedAtUtc,UpdatedAtUtc,AuthorizedAtUtc,CapturedAtUtc,ActivatedAtUtc,FirstPeriodEndUtc,
           ServiceAmountCents,FinalCaptureAmountCents,CompanyId,MachineId,ProvisioningStage,ProvisioningCompletedAtUtc,RowVersion)
          VALUES ({payment.Id},{(int)payment.Status},{payment.EstimatedTotalPages},{payment.AmountCents},{payment.Currency},
           {payment.StripePaymentIntentId},{payment.AuthorizationEventId},{payment.CreatedAtUtc},{payment.UpdatedAtUtc},
           {payment.AuthorizedAtUtc},{payment.CapturedAtUtc},{payment.ActivatedAtUtc},{payment.FirstPeriodEndUtc},
           {payment.ServiceAmountCents},{payment.FinalCaptureAmountCents},{payment.CompanyId},{payment.MachineId},
           {(int)payment.ProvisioningStage},{payment.ProvisioningCompletedAtUtc},{payment.RowVersion})");

        var result = await StripeAdminEndpoints.ReadAsync(f.CompanyId, db, Options, default);
        var summary = (StripeCompanySummary)((IValueHttpResult)result).Value!;
        Assert.AreEqual(expected, summary.MachineRequestProvisioningCompleted);
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
