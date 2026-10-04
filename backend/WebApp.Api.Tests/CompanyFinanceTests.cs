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
using WebApp.Api.Models;
using WebApp.Api.Models.Entities;
using WebApp.Api.Services;
using Fixture=WebApp.Api.Tests.AiCreditConsumptionServiceTests.Fixture;
namespace WebApp.Api.Tests;
[TestClass]
public class CompanyFinanceTests
{
    private sealed class MachineStatusGateway : IStripeLifecycleGateway
    {
        public StripeLifecycleSnapshot State { get; private set; } = new("active",DateTime.UtcNow.AddDays(-1),DateTime.UtcNow.AddDays(29),false,"in_test","open",2990,"si_test",1);
        public int Writes { get; private set; }
        public Task<StripeLifecycleSnapshot> ReadAsync(BillingAccount account,CancellationToken ct)=>Task.FromResult(State);
        public Task SetQuantityAsync(BillingAccount account,StripeLifecycleSnapshot state,int quantity,string idempotencyKey,CancellationToken ct)
        {Writes++;State=State with{Quantity=quantity};return Task.CompletedTask;}
    }
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
    public async Task ConsumptionIsTenantScopedProjectedAndMatchesSharedReader()
    {
        await using var f=new Fixture();await f.Seed();await using var db=f.Db();
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE Users (Id TEXT PRIMARY KEY, CompanyId TEXT, EntraObjectId TEXT, Email TEXT, Role TEXT, Status TEXT, CreatedAt TEXT, UpdatedAt TEXT, FirstName TEXT, LastName TEXT, PhoneNumber TEXT)");
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE UserMachines (UserId TEXT NOT NULL, MachineId TEXT NOT NULL, CreatedAt TEXT NOT NULL, PRIMARY KEY (UserId, MachineId))");
        var companyB=Guid.NewGuid();var machineB=Guid.NewGuid();var userA=Guid.NewGuid();var userB=Guid.NewGuid();
        db.Companies.Add(new(){Id=companyB,Name="Other tenant",Status="active"});
        db.Machines.Add(new(){Id=machineB,CompanyId=companyB,Name="Other machine",Status="active"});
        db.Users.AddRange(
            new(){Id=userA,CompanyId=f.CompanyId,Email="a@example.test",FirstName="Alice",LastName="Admin",Role="company_admin",Status="active"},
            new(){Id=userB,CompanyId=companyB,Email="b@example.test",FirstName="Bob",LastName="Other",Role="company_admin",Status="active"});
        var ownUsage=await db.AiUsageRecords.SingleAsync();ownUsage.UserId=userA;ownUsage.UsageType=AiUsageType.ChatResponse;
        var outsideUsage=new AiUsageRecord{Id=Guid.NewGuid(),CompanyId=f.CompanyId,MachineId=f.MachineId,UserId=userA,CreatedAtUtc=ownUsage.CreatedAtUtc.AddDays(-10),UsageType=AiUsageType.ChatResponse};
        var otherUsage=new AiUsageRecord{Id=Guid.NewGuid(),CompanyId=companyB,MachineId=machineB,UserId=userB,CreatedAtUtc=ownUsage.CreatedAtUtc,UsageType=AiUsageType.ChatResponse};
        db.AiUsageRecords.AddRange(outsideUsage,otherUsage);
        db.CreditLedger.AddRange(
            new(){Id=Guid.NewGuid(),CompanyId=f.CompanyId,MachineId=f.MachineId,AiUsageRecordId=ownUsage.Id,EntryType="AiUsage",BucketType="CompanyWallet",Currency="EUR",RealAiCost=1,CommercialCreditAmount=2.5m,BalanceAfter=0},
            new(){Id=Guid.NewGuid(),CompanyId=f.CompanyId,MachineId=f.MachineId,AiUsageRecordId=outsideUsage.Id,EntryType="AiUsage",BucketType="CompanyWallet",Currency="EUR",RealAiCost=1,CommercialCreditAmount=8m,BalanceAfter=0},
            new(){Id=Guid.NewGuid(),CompanyId=companyB,MachineId=machineB,AiUsageRecordId=otherUsage.Id,EntryType="AiUsage",BucketType="CompanyWallet",Currency="EUR",RealAiCost=1,CommercialCreditAmount=99m,BalanceAfter=0});
        await db.SaveChangesAsync();db.ChangeTracker.Clear();
        var usageTime=DateTime.SpecifyKind(ownUsage.CreatedAtUtc,DateTimeKind.Utc);
        var from=usageTime.AddMinutes(-1).ToString("O");var to=usageTime.AddMinutes(1).ToString("O");

        var result=await CompanyFinanceEndpoints.ReadConsumptionAsync(Context(f.CompanyId),from,to,null,db,default);
        var projected=(CompanyConsumptionReport)((IValueHttpResult)result).Value!;
        Assert.AreEqual(1,projected.Machines.Length);Assert.AreEqual(f.MachineId,projected.Machines[0].Id);
        Assert.AreEqual(2.5m,projected.Machines[0].CommercialCredit);
        Assert.IsTrue(projected.Machines[0].Users.All(user=>user.Id==null||user.Id==userA));
        Assert.IsFalse(JsonSerializer.Serialize(projected).Contains("Other tenant"));
        Assert.IsFalse(JsonSerializer.Serialize(projected).Contains("Providers"));
        Assert.IsFalse(JsonSerializer.Serialize(projected).Contains("RealCost"));
        Assert.IsTrue(AiUsageFilter.TryParse(from,to,null,out var filter));
        var shared=await AdminConsumptionReader.ReadReportAsync(f.CompanyId,filter,db,default);
        Assert.AreEqual(shared!.Machines.Single(machine=>machine.Id==f.MachineId).Metrics.CommercialCredit,projected.Machines[0].CommercialCredit);
    }
    [TestMethod]
    public async Task ConsumptionRejectsClientTenantAndInvalidWindows()
    {
        await using var f=new Fixture();await f.Seed();await using var db=f.Db();
        var forged=Context(f.CompanyId);forged.Request.QueryString=new QueryString($"?companyId={Guid.NewGuid()}");
        Assert.AreEqual(400,((IStatusCodeHttpResult)await CompanyFinanceEndpoints.ReadConsumptionAsync(forged,null,null,null,db,default)).StatusCode);
        Assert.AreEqual(400,((IStatusCodeHttpResult)await CompanyFinanceEndpoints.ReadConsumptionAsync(Context(f.CompanyId),"2026-10-02T00:00:00Z","2026-10-01T00:00:00Z",null,db,default)).StatusCode);
        Assert.IsInstanceOfType<Microsoft.AspNetCore.Http.HttpResults.ForbidHttpResult>(await CompanyFinanceEndpoints.ReadConsumptionAsync(new DefaultHttpContext(),null,null,null,db,default));
    }
    [TestMethod]
    public async Task MachineStatusUsesClaimTenantAndExistingIdempotentService()
    {
        await using var f=new Fixture();await f.Seed();await using var db=f.Db();
        db.BillingAccounts.Add(new(){Id=Guid.NewGuid(),CompanyId=f.CompanyId,StripeCustomerId="cus_test",StripeSubscriptionId="sub_test",SubscriptionStatus="active"});
        await db.SaveChangesAsync();
        var gateway=new MachineStatusGateway();var service=new StripeMachineStatusService(f.Options,gateway,null!);
        var settings=new StripeBillingOptions{Enabled=true,SecretKey="sk_test_local",PriceId="price_local"};
        var deactivateId=Guid.NewGuid();
        var request=new StripeAdminEndpoints.MachineStatusRequest(false,deactivateId);
        Assert.AreEqual(200,((IStatusCodeHttpResult)await CompanyFinanceEndpoints.SetMachineStatusAsync(f.MachineId,request,Context(f.CompanyId),db,service,settings,default)).StatusCode);
        Assert.AreEqual(200,((IStatusCodeHttpResult)await CompanyFinanceEndpoints.SetMachineStatusAsync(f.MachineId,request,Context(f.CompanyId),db,service,settings,default)).StatusCode);
        await using(var afterDeactivate=f.Db())
        {
            Assert.AreEqual("inactive",(await afterDeactivate.Machines.SingleAsync()).Status);
            Assert.IsTrue(await MachineEntitlements.Eligible(afterDeactivate,DateTime.UtcNow).AnyAsync());
            Assert.AreEqual(1,await afterDeactivate.MachineBillingPeriods.CountAsync());
            Assert.AreEqual(0,await afterDeactivate.CreditLedger.CountAsync());
        }
        Assert.AreEqual(200,((IStatusCodeHttpResult)await CompanyFinanceEndpoints.SetMachineStatusAsync(f.MachineId,
            new(true,Guid.NewGuid()),Context(f.CompanyId),db,service,settings,default)).StatusCode);
        await using var afterReactivate=f.Db();
        Assert.AreEqual("active",(await afterReactivate.Machines.SingleAsync()).Status);
        Assert.AreEqual(1,await afterReactivate.MachineBillingPeriods.CountAsync());
        Assert.AreEqual(0,await afterReactivate.StripeMachineAdditions.CountAsync());
        Assert.AreEqual(0,await afterReactivate.CreditLedger.CountAsync());
        Assert.AreEqual(2,gateway.Writes);
    }
    [TestMethod]
    public async Task MachineStatusRejectsForeignMachineClientTenantAndMissingClaimBeforeStripe()
    {
        await using var f=new Fixture();await f.Seed();await using var db=f.Db();
        var companyB=Guid.NewGuid();var machineB=Guid.NewGuid();
        db.Companies.Add(new(){Id=companyB,Name="Other",Status="active"});
        db.Machines.Add(new(){Id=machineB,CompanyId=companyB,Name="Foreign",Status="active"});
        await db.SaveChangesAsync();
        var request=new StripeAdminEndpoints.MachineStatusRequest(false,Guid.NewGuid());
        Assert.AreEqual(404,((IStatusCodeHttpResult)await CompanyFinanceEndpoints.SetMachineStatusAsync(machineB,request,Context(f.CompanyId),db,null!,new(),default)).StatusCode);
        var forged=Context(f.CompanyId);forged.Request.QueryString=new QueryString($"?companyId={companyB}");
        Assert.AreEqual(400,((IStatusCodeHttpResult)await CompanyFinanceEndpoints.SetMachineStatusAsync(f.MachineId,request,forged,db,null!,new(),default)).StatusCode);
        Assert.IsInstanceOfType<Microsoft.AspNetCore.Http.HttpResults.ForbidHttpResult>(await CompanyFinanceEndpoints.SetMachineStatusAsync(f.MachineId,request,new DefaultHttpContext(),db,null!,new(),default));
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
        var endpoints=((IEndpointRouteBuilder)app).DataSources.SelectMany(d=>d.Endpoints).ToArray();Assert.AreEqual(6,endpoints.Length);
        foreach(var endpoint in endpoints)Assert.IsTrue(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Any(a=>a.Policy=="CompanyAdminOnly"));
        var consumption=endpoints.OfType<RouteEndpoint>().Single(endpoint=>endpoint.RoutePattern.RawText=="/api/company/finance/consumption");
        Assert.IsFalse(consumption.Metadata.GetOrderedMetadata<IAllowAnonymous>().Any());
        var machineStatus=endpoints.OfType<RouteEndpoint>().Single(endpoint=>endpoint.RoutePattern.RawText=="/api/company/stripe/machines/{machineId:guid}/status");
        Assert.IsFalse(machineStatus.Metadata.GetOrderedMetadata<IAllowAnonymous>().Any());
    }
    [TestMethod]
    public void TopUpProjectionHidesProviderAndLedgerIdentifiers()
    {
        var row=new WalletTopUpResult(Guid.NewGuid(),"Completed","Completed",20,"https://checkout.stripe.com/test","cs_secret","pi_secret",Guid.NewGuid(),"evt_secret",DateTime.UtcNow,DateTime.UtcNow,DateTime.UtcNow);
        var projected=CompanyFinanceEndpoints.Project(row);Assert.IsNull(projected.PaymentUrl);Assert.AreEqual("Completed",projected.Status);
        Assert.IsFalse(JsonSerializer.Serialize(projected).Contains("secret"));
    }
}
