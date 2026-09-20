using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebApp.Api.Models.Entities;
using WebApp.Api.Services;
using Fixture=WebApp.Api.Tests.AiCreditConsumptionServiceTests.Fixture;
namespace WebApp.Api.Tests;
[TestClass]
public class AiCreditAccessTests
{
    [TestMethod]
    public async Task ClosedPeriodCannotAuthorizeIncludedCreditButWalletRemainsAvailable()
    {
        await using var f=new Fixture();await f.Seed();await using var db=f.Db();
        var period=await db.MachineBillingPeriods.SingleAsync();
        period.Status="Closed";period.PeriodStartUtc=DateTime.UtcNow.AddDays(-1);period.PeriodEndUtc=DateTime.UtcNow.AddDays(1);
        await db.SaveChangesAsync();var service=new AiCreditAccessService(db);
        Assert.IsFalse((await service.CheckAsync(f.MachineId)).Allowed);
        db.CompanyWallets.Add(new(){CompanyId=f.CompanyId,Balance=10,Currency="EUR"});await db.SaveChangesAsync();
        Assert.AreEqual("CompanyWalletAvailable",(await service.CheckAsync(f.MachineId)).Status);
        Assert.AreEqual(10m,(await db.CompanyWallets.SingleAsync()).Balance);
        Assert.AreEqual(0,await db.CreditLedger.CountAsync());
    }
    [TestMethod]
    public async Task FuturePeriodAndAnotherCompanyWalletDoNotAuthorize()
    {
        await using var f=new Fixture();await f.Seed();await using var db=f.Db();
        var p=await db.MachineBillingPeriods.SingleAsync();p.PeriodStartUtc=DateTime.UtcNow.AddDays(1);p.PeriodEndUtc=DateTime.UtcNow.AddDays(30);
        var other=Guid.NewGuid();db.Companies.Add(new(){Id=other,Name="other",Status="active"});
        db.CompanyWallets.Add(new(){CompanyId=other,Balance=100,Currency="EUR"});await db.SaveChangesAsync();
        Assert.IsFalse((await new AiCreditAccessService(db).CheckAsync(f.MachineId)).Allowed);
    }
    [TestMethod]
    [DataRow(0,0,false)]
    [DataRow(1,0,true)]
    [DataRow(0,1,true)]
    public async Task UsesCurrentIncludedBudgetThenCompanyWallet(int included,int wallet,bool allowed)
    {
        await using var f=new Fixture();await f.Seed(included);
        await using var db=f.Db();var p=await db.MachineBillingPeriods.SingleAsync();
        p.PeriodStartUtc=DateTime.UtcNow.AddDays(-1);p.PeriodEndUtc=DateTime.UtcNow.AddDays(1);
        if(wallet>0)db.CompanyWallets.Add(new(){CompanyId=f.CompanyId,Balance=wallet,Currency="EUR"});
        await db.SaveChangesAsync();
        var result=await new AiCreditAccessService(db).CheckAsync(f.MachineId);
        Assert.AreEqual(allowed,result.Allowed);
        if(!allowed)Assert.AreEqual("AiCreditExhausted",result.Status);
        Assert.AreEqual(0,await db.CreditLedger.CountAsync());
        Assert.IsFalse((await new AiCreditAccessService(db).CheckAsync(null)).Allowed);
    }
    [TestMethod]
    public async Task ExpiredBudgetIgnoredAndRechargeIsImmediatelyVisible()
    {
        await using var f=new Fixture();await f.Seed();await using var db=f.Db();
        var p=await db.MachineBillingPeriods.SingleAsync();p.PeriodStartUtc=DateTime.UtcNow.AddDays(-2);p.PeriodEndUtc=DateTime.UtcNow.AddDays(-1);await db.SaveChangesAsync();
        var service=new AiCreditAccessService(db);Assert.IsFalse((await service.CheckAsync(f.MachineId)).Allowed);
        db.CompanyWallets.Add(new(){CompanyId=f.CompanyId,Balance=10,Currency="EUR"});await db.SaveChangesAsync();
        Assert.IsTrue((await service.CheckAsync(f.MachineId)).Allowed);
        Assert.AreEqual(10m,p.IncludedAiBudgetRealCost);Assert.AreEqual(0,await db.CreditLedger.CountAsync());
    }
    [TestMethod]
    public async Task LastCallOverrunBlocksPositiveButInsufficientWalletWithoutWritingAnything()
    {
        await using var f=new Fixture();await f.Seed(0);await using var db=f.Db();
        var u=await db.AiUsageRecords.SingleAsync();u.Provider="Test";u.Model="test";u.InputTokens=100000;u.OutputTokens=0;u.TotalTokens=100000;u.Available=true;u.Completed=true;
        db.AiPricing.Add(new(){Id=Guid.NewGuid(),Provider="Test",Model="test",Currency="EUR",InputPricePerMillion=1,OutputPricePerMillion=1,EffectiveFromUtc=u.CreatedAtUtc.AddDays(-1)});
        var wallet=new CompanyWallet{CompanyId=f.CompanyId,Balance=0.01m,Currency="EUR"};db.CompanyWallets.Add(wallet);await db.SaveChangesAsync();
        var service=new AiCreditAccessService(db);Assert.IsFalse((await service.CheckAsync(f.MachineId)).Allowed);
        Assert.AreEqual(0.01m,(await db.CompanyWallets.AsNoTracking().SingleAsync()).Balance);
        wallet.Balance=10;await db.SaveChangesAsync();Assert.IsTrue((await service.CheckAsync(f.MachineId)).Allowed);
        Assert.AreEqual(0,await db.CreditLedger.CountAsync());Assert.AreEqual(1,await db.AiUsageRecords.CountAsync());
    }
}
