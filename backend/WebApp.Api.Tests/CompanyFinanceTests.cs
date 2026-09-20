using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebApp.Api.Data;
using WebApp.Api.Services;
using Fixture=WebApp.Api.Tests.AiCreditConsumptionServiceTests.Fixture;
namespace WebApp.Api.Tests;
[TestClass]
public class CompanyFinanceTests
{
    private sealed class InvoiceGateway(string status,string url,string subscription="sub_owned") : IStripeSubscriptionPaymentGateway
    {
        public Task<SubscriptionInvoice> ReadAsync(WebApp.Api.Models.Entities.BillingAccount account,string? invoiceId,CancellationToken ct)
        {
            Assert.AreEqual("cus_owned",account.StripeCustomerId);Assert.AreEqual("in_owned",invoiceId);
            return Task.FromResult(new SubscriptionInvoice("in_owned",subscription,status,"subscription_cycle",DateTime.UtcNow,DateTime.UtcNow.AddMonths(1),1,0,null,null,url,"past_due"));
        }
    }
    [TestMethod]
    [DataRow("open","https://invoice.stripe.com/i/test","sub_owned",200)]
    [DataRow("paid","https://invoice.stripe.com/i/test","sub_owned",409)]
    [DataRow("void","https://invoice.stripe.com/i/test","sub_owned",409)]
    [DataRow("open","https://evil.example/test","sub_owned",409)]
    [DataRow("open","https://invoice.stripe.com/i/test","sub_other",409)]
    public async Task InvoicePaymentUsesOnlyCompanyOwnedInvoiceWithoutWrites(string status,string url,string subscription,int expected)
    {
        await using var f=new Fixture();await f.Seed();await using var db=f.Db();
        db.BillingAccounts.Add(new(){Id=Guid.NewGuid(),CompanyId=f.CompanyId,StripeCustomerId="cus_owned",StripeSubscriptionId="sub_owned",LatestInvoiceId="in_owned"});await db.SaveChangesAsync();
        var result=await CompanyFinanceEndpoints.InvoicePaymentAsync(Context(f.CompanyId),db,new InvoiceGateway(status,url,subscription),default);
        Assert.AreEqual(expected,((IStatusCodeHttpResult)result).StatusCode);
        Assert.AreEqual(409,((IStatusCodeHttpResult)await CompanyFinanceEndpoints.InvoicePaymentAsync(Context(Guid.NewGuid()),db,new InvoiceGateway(status,url),default)).StatusCode);
        Assert.AreEqual(0,await db.CreditLedger.CountAsync());Assert.AreEqual(0,await db.CompanyWallets.CountAsync());Assert.AreEqual(1,await db.MachineBillingPeriods.CountAsync());
    }
    private static HttpContext Context(Guid company)=>new DefaultHttpContext {User=new ClaimsPrincipal(new ClaimsIdentity(new[]{new Claim(DiagLinkClaimTypes.CompanyId,company.ToString()),new Claim(ClaimTypes.Role,"company_admin")},"test"))};
    [TestMethod]
    public async Task SummaryIsTenantScopedAndContainsNoTechnicalFinanceData()
    {
        await using var f=new Fixture();await f.Seed();await using var db=f.Db();
        var p=await db.MachineBillingPeriods.SingleAsync();p.PeriodStartUtc=DateTime.UtcNow.AddDays(-1);p.PeriodEndUtc=DateTime.UtcNow.AddDays(1);p.IncludedAiUsedRealCost=2;
        db.CompanyWallets.Add(new(){CompanyId=f.CompanyId,Balance=20,Currency="EUR"});
        db.BillingAccounts.Add(new(){Id=Guid.NewGuid(),CompanyId=f.CompanyId,StripeCustomerId="secret_customer",StripeSubscriptionId="secret_subscription",SubscriptionStatus="past_due",CurrentPeriodEndUtc=DateTime.UtcNow.AddDays(10),LatestInvoiceStatus="open",AmountRemainingCents=2990,CancelAtPeriodEnd=true});
        await db.SaveChangesAsync();
        var result=await CompanyFinanceEndpoints.ReadAsync(f.MachineId,Context(f.CompanyId),db,new(db),new(),default);
        var summary=(CompanyFinanceSummary)((IValueHttpResult)result).Value!;
        Assert.AreEqual(p.PeriodEndUtc,summary.MachineCreditResetUtc);
        Assert.AreEqual(DateTimeKind.Utc,summary.MachineCreditResetUtc!.Value.Kind);
        Assert.AreNotEqual(summary.NextDueUtc,summary.MachineCreditResetUtc);
        Assert.AreEqual(8m,summary.MachineCreditRemaining);Assert.AreEqual(20m,summary.WalletBalance);Assert.AreEqual(29.90m,summary.UnpaidInvoiceAmount);Assert.IsTrue(summary.CancelAtPeriodEnd);
        var json=JsonSerializer.Serialize(summary);Assert.IsFalse(json.Contains("secret_"));Assert.IsFalse(json.Contains("Tokens"));Assert.IsFalse(json.Contains("Ledger"));
        var other=await CompanyFinanceEndpoints.ReadAsync(f.MachineId,Context(Guid.NewGuid()),db,new(db),new(),default);
        Assert.AreEqual(404,((IStatusCodeHttpResult)other).StatusCode);
        Assert.IsInstanceOfType<Microsoft.AspNetCore.Http.HttpResults.ForbidHttpResult>(await CompanyFinanceEndpoints.ReadAsync(null,new DefaultHttpContext(),db,new(db),new(),default));
        Assert.AreEqual(0,await db.CreditLedger.CountAsync());
    }
    [TestMethod]
    [DataRow("expired")]
    [DataRow("future")]
    [DataRow("closed")]
    [DataRow("missing")]
    [DataRow("unselected")]
    public async Task NoActivePeriodReturnsNoResetDate(string scenario)
    {
        await using var f=new Fixture();await f.Seed();await using var db=f.Db();
        var p=await db.MachineBillingPeriods.SingleAsync();var now=DateTime.UtcNow;
        p.PeriodStartUtc=now.AddDays(-1);p.PeriodEndUtc=now.AddDays(1);
        if(scenario=="expired"){p.PeriodStartUtc=now.AddDays(-2);p.PeriodEndUtc=now.AddDays(-1);}
        if(scenario=="future"){p.PeriodStartUtc=now.AddDays(1);p.PeriodEndUtc=now.AddDays(2);}
        if(scenario=="closed")p.Status="Closed";
        if(scenario=="missing")db.MachineBillingPeriods.Remove(p);
        await db.SaveChangesAsync();
        var result=await CompanyFinanceEndpoints.ReadAsync(scenario=="unselected"?null:f.MachineId,Context(f.CompanyId),db,new(db),new(),default);
        var summary=(CompanyFinanceSummary)((IValueHttpResult)result).Value!;
        Assert.IsNull(summary.MachineCreditResetUtc);
        if(scenario!="unselected"){Assert.AreEqual(0m,summary.MachineCreditRemaining);Assert.AreEqual("AiCreditExhausted",summary.CreditStatus);}
    }
    [TestMethod]
    public async Task RoutesRequireCompanyAdminAndNoClientCompanyId()
    {
        var b=WebApplication.CreateBuilder();b.Services.AddScoped<DiagLinkDbContext>();b.Services.AddScoped<AiCreditAccessService>();b.Services.AddScoped<CompanyWalletTopUpService>();b.Services.AddScoped<IStripeSubscriptionPaymentGateway,StripeBillingGateway>();b.Services.AddSingleton(new StripeBillingOptions());
        await using var app=b.Build();app.MapCompanyFinance();
        var endpoints=((IEndpointRouteBuilder)app).DataSources.SelectMany(d=>d.Endpoints).ToArray();Assert.AreEqual(4,endpoints.Length);
        foreach(var endpoint in endpoints)Assert.IsTrue(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Any(a=>a.Policy=="CompanyAdminOnly"));
    }
    [TestMethod]
    public void TopUpProjectionHidesProviderAndLedgerIdentifiers()
    {
        var row=new WalletTopUpResult(Guid.NewGuid(),"Completed","Completed",20,"https://checkout.stripe.com/test","cs_secret","pi_secret",Guid.NewGuid(),"evt_secret",DateTime.UtcNow,DateTime.UtcNow,DateTime.UtcNow);
        var projected=CompanyFinanceEndpoints.Project(row);Assert.IsNull(projected.PaymentUrl);Assert.AreEqual("Completed",projected.Status);
        Assert.IsFalse(JsonSerializer.Serialize(projected).Contains("secret"));
    }
}
