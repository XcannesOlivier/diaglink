using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebApp.Api.Services;
using Fixture=WebApp.Api.Tests.AiCreditConsumptionServiceTests.Fixture;
namespace WebApp.Api.Tests;
[TestClass]
public class AdminFinanceReaderTests
{
    [TestMethod]
    public async Task GlobalTotalUsesPostedTopUpsAcrossCompaniesNotWalletBalances()
    {
        await using var f=new Fixture();await f.Seed();await using var db=f.Db();
        var empty=(AdminFinanceReader.GlobalTopUps)((IValueHttpResult)await AdminFinanceReader.GlobalTopUpsAsync(db,default)).Value!;
        Assert.AreEqual(0m,empty.TotalAddedEur);
        var other=Guid.NewGuid();db.Companies.Add(new(){Id=other,Name="Other",Status="active"});
        db.CompanyWallets.Add(new(){CompanyId=f.CompanyId,Balance=1,Currency="EUR"});
        db.CreditLedger.AddRange(
            new(){Id=Guid.NewGuid(),CompanyId=f.CompanyId,EntryType="TopUp",BucketType="CompanyWallet",CommercialCreditAmount=20,Currency="EUR"},
            new(){Id=Guid.NewGuid(),CompanyId=other,EntryType="TopUp",BucketType="CompanyWallet",CommercialCreditAmount=50.25m,Currency="EUR"},
            new(){Id=Guid.NewGuid(),CompanyId=f.CompanyId,EntryType="AiUsage",BucketType="CompanyWallet",CommercialCreditAmount=3,Currency="EUR"},
            new(){Id=Guid.NewGuid(),CompanyId=f.CompanyId,EntryType="AiUsage",BucketType="MachineIncluded",RealAiCost=10,Currency="EUR"});
        foreach(var stage in new[]{WebApp.Api.Models.Entities.StripeWalletTopUpStage.AwaitingPayment,WebApp.Api.Models.Entities.StripeWalletTopUpStage.PaymentConfirmed})
            db.StripeWalletTopUps.Add(new(){Id=Guid.NewGuid(),CompanyId=f.CompanyId,StripeCustomerId="cus_test",ReturnUrl="https://example.com",AmountCents=10000,Stage=stage,
                StripeSessionId=$"cs_{stage}",StripePaymentIntentId=stage==WebApp.Api.Models.Entities.StripeWalletTopUpStage.PaymentConfirmed?"pi_test":null,
                PaymentConfirmedAtUtc=stage==WebApp.Api.Models.Entities.StripeWalletTopUpStage.PaymentConfirmed?DateTime.UtcNow:null,
                ExternalEventId=stage==WebApp.Api.Models.Entities.StripeWalletTopUpStage.PaymentConfirmed?"evt_test":null});
        await db.SaveChangesAsync();
        var result=(AdminFinanceReader.GlobalTopUps)((IValueHttpResult)await AdminFinanceReader.GlobalTopUpsAsync(db,default)).Value!;
        Assert.AreEqual(70.25m,result.TotalAddedEur);Assert.AreEqual(1m,(await db.CompanyWallets.SingleAsync()).Balance);
        Assert.AreEqual(4,await db.CreditLedger.CountAsync());
    }
    [TestMethod]
    public async Task ClosedCurrentPeriodAndFuturePaidRightsRemainVisibleForInactiveMachine()
    {
        await using var f=new Fixture();await f.Seed(10,3);await using var db=f.Db();
        var now=DateTime.UtcNow;
        var p=await db.MachineBillingPeriods.SingleAsync();p.PeriodStartUtc=now.AddDays(-1);p.PeriodEndUtc=now.AddDays(10);p.Status="Closed";
        (await db.Machines.SingleAsync()).Status="inactive";
        db.MachineBillingPeriods.Add(new(){Id=Guid.NewGuid(),MachineId=f.MachineId,PeriodStartUtc=p.PeriodEndUtc,PeriodEndUtc=now.AddDays(40),IncludedAiBudgetRealCost=10,Status="Active"});
        await db.SaveChangesAsync();
        var result=(AdminFinanceOverview)((IValueHttpResult)await AdminFinanceReader.OverviewAsync(f.CompanyId,db,new(db),default)).Value!;
        var row=result.Machines.Single();
        Assert.IsFalse(row.Billable);Assert.IsTrue(row.HasCurrentPaidRights);Assert.IsTrue(row.CreditBlocked);
        Assert.IsNull(row.ResetUtc);Assert.AreEqual(now.AddDays(40),row.PaidRightsEndUtc);Assert.IsNull(row.Used);Assert.AreEqual(0m,row.Remaining);
        p.PeriodStartUtc=now.AddDays(-20);p.PeriodEndUtc=now.AddDays(-10);await db.SaveChangesAsync();
        result=(AdminFinanceOverview)((IValueHttpResult)await AdminFinanceReader.OverviewAsync(f.CompanyId,db,new(db),default)).Value!;
        Assert.IsFalse(result.Machines.Single().HasCurrentPaidRights);Assert.IsNull(result.Machines.Single().ResetUtc);
        Assert.AreEqual(0,await db.CreditLedger.CountAsync());
    }
    [TestMethod]
    public async Task OverviewUsesActualPeriodAndCreditGateWithoutWrites()
    {
        await using var f=new Fixture();await f.Seed(10,3);await using var db=f.Db();
        var p=await db.MachineBillingPeriods.SingleAsync();p.PeriodStartUtc=DateTime.UtcNow.AddDays(-1);p.PeriodEndUtc=DateTime.UtcNow.AddDays(1);await db.SaveChangesAsync();
        var result=(AdminFinanceOverview)((IValueHttpResult)await AdminFinanceReader.OverviewAsync(f.CompanyId,db,new(db),default)).Value!;
        Assert.AreEqual(3m,result.Machines.Single().Used);Assert.AreEqual(7m,result.Machines.Single().Remaining);Assert.AreEqual(p.PeriodEndUtc,result.Machines.Single().ResetUtc);Assert.IsFalse(result.Machines.Single().CreditBlocked);
        Assert.AreEqual(404,((IStatusCodeHttpResult)await AdminFinanceReader.OverviewAsync(Guid.NewGuid(),db,new(db),default)).StatusCode);
        Assert.AreEqual(0,await db.CreditLedger.CountAsync());
    }
    [TestMethod]
    public async Task TechnicalUsesExistingValuationAndDoesNotInventUnknownCosts()
    {
        await using var f=new Fixture();await f.Seed();await using var db=f.Db();
        var u=await db.AiUsageRecords.SingleAsync();u.CreatedAtUtc=DateTime.UtcNow.AddMinutes(-1);await db.SaveChangesAsync();
        var result=(AdminFinanceTechnical)((IValueHttpResult)await AdminFinanceReader.TechnicalAsync(f.CompanyId,db,default)).Value!;
        Assert.AreEqual(1,result.UnvaluedCount);Assert.AreEqual(0m,result.RealCostEur);Assert.AreEqual(0m,result.EstimatedWalletMarginEur);
        u.Provider="Test";u.Model="test";u.Available=true;u.Completed=true;u.InputTokens=100000;u.OutputTokens=0;u.TotalTokens=100000;
        db.AiPricing.Add(new(){Id=Guid.NewGuid(),Provider="Test",Model="test",Currency="EUR",InputPricePerMillion=1,OutputPricePerMillion=1,EffectiveFromUtc=u.CreatedAtUtc.AddDays(-1)});await db.SaveChangesAsync();
        result=(AdminFinanceTechnical)((IValueHttpResult)await AdminFinanceReader.TechnicalAsync(f.CompanyId,db,default)).Value!;
        Assert.AreEqual(0.1m,result.RealCostEur);Assert.AreEqual(0,result.UnvaluedCount);Assert.AreEqual(100000,result.InputTokens);
        Assert.AreEqual(0,await db.CreditLedger.CountAsync());Assert.AreEqual(0,await db.CompanyWallets.CountAsync());
        var p=await db.MachineBillingPeriods.SingleAsync();p.PeriodStartUtc=u.CreatedAtUtc.AddDays(-1);p.PeriodEndUtc=u.CreatedAtUtc.AddDays(1);p.IncludedAiUsedRealCost=10;
        db.CompanyWallets.Add(new(){CompanyId=f.CompanyId,Balance=1,Currency="EUR"});await db.SaveChangesAsync();
        var billed=await new AiUsageBillingOrchestrator(f.Options).ProcessAsync(u.Id);Assert.IsTrue(billed.Success);
        result=(AdminFinanceTechnical)((IValueHttpResult)await AdminFinanceReader.TechnicalAsync(f.CompanyId,db,default)).Value!;
        Assert.AreEqual(0.3m,result.CommercialConsumedEur);Assert.AreEqual(0.1m,result.WalletCoveredRealCostEur);Assert.AreEqual(0.2m,result.EstimatedWalletMarginEur);
    }
}
