using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebApp.Api.Models;
using WebApp.Api.Services;
using Fixture = WebApp.Api.Tests.AiCreditConsumptionServiceTests.Fixture;

namespace WebApp.Api.Tests;

[TestClass]
public class MachineBillingPeriodServiceTests
{
    private static readonly DateTime Start = new(2026,9,15,0,0,0,DateTimeKind.Utc);
    private static async Task Prepare(Fixture f, bool initial = true)
    {
        await f.Seed(10m,2m);
        await using var db=f.Db();
        (await db.Machines.SingleAsync()).Status="active";
        if(initial) db.MachineBillingPeriods.Remove(await db.MachineBillingPeriods.SingleAsync());
        await db.SaveChangesAsync();
    }

    [TestMethod]
    [DataRow(0,28)]
    [DataRow(8,30)]
    [DataRow(-4,31)]
    public async Task InitialUsesExplicitActivationAndFullBudget(int activationOffset,int days)
    {
        await using var f=new Fixture();await Prepare(f);
        var service=new MachineBillingPeriodService(f.Options);
        var result=await service.CreateInitialPeriodAsync(f.MachineId,Start.AddDays(activationOffset),Start,Start.AddDays(days));
        Assert.IsTrue(result.Success);Assert.AreEqual("Created",result.Status);
        Assert.AreEqual(Start.AddDays(Math.Max(0,activationOffset)),result.PeriodStartUtc);
        Assert.AreEqual(Start.AddDays(days),result.PeriodEndUtc);
        Assert.AreEqual(10m,result.IncludedAiBudgetRealCost);Assert.AreEqual(0m,result.IncludedAiUsedRealCost);
        var repeated=await service.CreateInitialPeriodAsync(f.MachineId,Start.AddDays(activationOffset),Start,Start.AddDays(days));
        Assert.AreEqual("AlreadyExists",repeated.Status);Assert.AreEqual(result.BillingPeriodId,repeated.BillingPeriodId);
        await using var db=f.Db();Assert.AreEqual(1,await db.MachineBillingPeriods.CountAsync());
        Assert.AreEqual("Active",(await db.MachineBillingPeriods.SingleAsync()).Status);
        Assert.AreEqual(0,await db.CreditLedger.CountAsync());Assert.AreEqual(0,await db.CompanyWallets.CountAsync());
    }

    [TestMethod]
    public async Task RenewalClosesPreviousWithoutCarryingBudgetAndCrossesYear()
    {
        await using var f=new Fixture();await Prepare(f);
        var service=new MachineBillingPeriodService(f.Options);
        var dec=new DateTime(2026,12,15,0,0,0,DateTimeKind.Utc);
        var jan=new DateTime(2027,1,15,0,0,0,DateTimeKind.Utc);
        var feb=new DateTime(2027,2,12,0,0,0,DateTimeKind.Utc);
        var first=await service.CreateInitialPeriodAsync(f.MachineId,dec,dec,jan);
        await using(var db=f.Db()){(await db.MachineBillingPeriods.SingleAsync()).IncludedAiUsedRealCost=2m;await db.SaveChangesAsync();}
        var renewed=await service.RenewPeriodAsync(f.MachineId,jan,feb);
        Assert.IsTrue(renewed.Success);Assert.AreEqual(10m,renewed.IncludedAiBudgetRealCost);Assert.AreEqual(0m,renewed.IncludedAiUsedRealCost);
        Assert.AreEqual(jan,renewed.PeriodStartUtc);Assert.AreEqual(feb,renewed.PeriodEndUtc);
        Assert.AreEqual("AlreadyExists",(await service.RenewPeriodAsync(f.MachineId,jan,feb)).Status);
        await using var check=f.Db();var old=await check.MachineBillingPeriods.SingleAsync(p=>p.Id==first.BillingPeriodId);
        Assert.AreEqual("Closed",old.Status);Assert.AreEqual(10m,old.IncludedAiBudgetRealCost);Assert.AreEqual(2m,old.IncludedAiUsedRealCost);
        Assert.AreEqual("Active",(await check.MachineBillingPeriods.SingleAsync(p=>p.Id==renewed.BillingPeriodId)).Status);
        Assert.AreEqual(2,await check.MachineBillingPeriods.CountAsync());Assert.AreEqual(0,await check.CreditLedger.CountAsync());Assert.AreEqual(0,await check.CompanyWallets.CountAsync());
    }

    [TestMethod]
    public async Task InvalidDatesMachineAndMissingPrevious()
    {
        await using var f=new Fixture();await Prepare(f);var service=new MachineBillingPeriodService(f.Options);
        foreach(var offset in new[]{0,1})
            Assert.AreEqual("ActivationOutsideCycle",(await service.CreateInitialPeriodAsync(f.MachineId,Start.AddDays(28+offset),Start,Start.AddDays(28))).Status);
        foreach(var end in new[]{Start,Start.AddDays(-1)})
            Assert.AreEqual("InvalidCycleDates",(await service.RenewPeriodAsync(f.MachineId,Start,end)).Status);
        Assert.AreEqual("InvalidCycleDates",(await service.RenewPeriodAsync(f.MachineId,DateTime.SpecifyKind(Start,DateTimeKind.Local),Start.AddDays(28))).Status);
        Assert.AreEqual("MachineNotFound",(await service.CreateInitialPeriodAsync(Guid.NewGuid(),Start,Start,Start.AddDays(28))).Status);
        Assert.AreEqual("PreviousPeriodNotFound",(await service.RenewPeriodAsync(f.MachineId,Start,Start.AddDays(28))).Status);
        await using(var db=f.Db()){(await db.Machines.SingleAsync()).Status="inactive";await db.SaveChangesAsync();}
        Assert.AreEqual("MachineInactive",(await service.CreateInitialPeriodAsync(f.MachineId,Start,Start,Start.AddDays(28))).Status);
        await using var check=f.Db();Assert.AreEqual(0,await check.MachineBillingPeriods.CountAsync());
    }

    [TestMethod]
    public async Task ConflictingDatesAndOverlapNeverChangeExistingPeriod()
    {
        await using var f=new Fixture();await Prepare(f);var service=new MachineBillingPeriodService(f.Options);
        await service.CreateInitialPeriodAsync(f.MachineId,Start,Start,Start.AddDays(30));
        Assert.AreEqual("DataInconsistency",(await service.RenewPeriodAsync(f.MachineId,Start,Start.AddDays(31))).Status);
        Assert.AreEqual("OverlappingPeriod",(await service.RenewPeriodAsync(f.MachineId,Start.AddDays(1),Start.AddDays(31))).Status);
        Assert.AreEqual("InitialPeriodAlreadyExists",(await service.CreateInitialPeriodAsync(f.MachineId,Start.AddDays(30),Start.AddDays(30),Start.AddDays(60))).Status);
        await using var db=f.Db();var period=await db.MachineBillingPeriods.SingleAsync();Assert.AreEqual("Active",period.Status);Assert.AreEqual(Start.AddDays(30),period.PeriodEndUtc);
    }

    [TestMethod]
    [DataRow("insert")]
    [DataRow("close")]
    public async Task FailedRenewalRollsBackClosureAndCreation(string failure)
    {
        await using var f=new Fixture();await Prepare(f);var service=new MachineBillingPeriodService(f.Options);
        await service.CreateInitialPeriodAsync(f.MachineId,Start,Start,Start.AddDays(28));
        await using(var db=f.Db())
        {
            var sql=failure=="insert" ? "CREATE TRIGGER fail AFTER INSERT ON MachineBillingPeriods BEGIN SELECT RAISE(ABORT,'test'); END"
                : "CREATE TRIGGER fail AFTER UPDATE ON MachineBillingPeriods BEGIN SELECT RAISE(ABORT,'test'); END";
            await db.Database.ExecuteSqlRawAsync(sql);
        }
        try{await service.RenewPeriodAsync(f.MachineId,Start.AddDays(28),Start.AddDays(56));Assert.Fail("Expected database failure");}catch(DbUpdateException){}
        await using var check=f.Db();Assert.AreEqual("Active",(await check.MachineBillingPeriods.SingleAsync()).Status);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ConcurrentCyclesAreSerialized(bool overlapping)
    {
        await using var f=new Fixture();await Prepare(f);var service=new MachineBillingPeriodService(f.Options);
        await service.CreateInitialPeriodAsync(f.MachineId,Start,Start,Start.AddDays(28));
        async Task<MachineBillingPeriodResult> Run(int offset)
        {
            for(var attempt=0;;attempt++)
            {
                try{return await service.RenewPeriodAsync(f.MachineId,Start.AddDays(28+offset),Start.AddDays(56+offset));}
                catch(SqliteException e) when(attempt<5 && e.SqliteErrorCode is 5 or 6){await Task.Delay(20);}
                catch(DbUpdateException e) when(attempt<5 && e.InnerException is SqliteException{SqliteErrorCode:5 or 6}){await Task.Delay(20);}
            }
        }
        var results=await Task.WhenAll(Task.Run(()=>Run(0)),Task.Run(()=>Run(overlapping?1:0)));
        Assert.AreEqual(1,results.Count(r=>r.Status=="Created"));
        Assert.AreEqual(1,results.Count(r=>r.Status==(overlapping?"OverlappingPeriod":"AlreadyExists")));
        await using var check=f.Db();Assert.AreEqual(2,await check.MachineBillingPeriods.CountAsync());
        Assert.AreEqual(1,await check.MachineBillingPeriods.CountAsync(p=>p.Status=="Active"));
    }

    [TestMethod]
    [DataRow("inside")]
    [DataRow("before")]
    [DataRow("end")]
    public async Task HistoricalConsumptionInClosedPeriod(string position)
    {
        await using var f=new Fixture();await f.Seed();
        await using(var db=f.Db())
        {
            var period=await db.MachineBillingPeriods.SingleAsync();period.Status="Closed";
            var usage=await db.AiUsageRecords.SingleAsync();
            usage.CreatedAtUtc=position=="end"?period.PeriodEndUtc:position=="before"?period.PeriodStartUtc.AddTicks(-1):period.PeriodStartUtc;
            await db.SaveChangesAsync();
        }
        var result=await f.Service.ConsumeAsync(f.UsageId,0.1m,"EUR");
        Assert.AreEqual(position=="inside"?"Processed":"BillingPeriodNotFound",result.Status);
        if(position=="inside")Assert.AreEqual("AlreadyProcessed",(await f.Service.ConsumeAsync(f.UsageId,0.1m,"EUR")).Status);
        await using var check=f.Db();var after=await check.MachineBillingPeriods.SingleAsync();Assert.AreEqual("Closed",after.Status);
        Assert.AreEqual(position=="inside"?0.1m:0m,after.IncludedAiUsedRealCost);
        Assert.AreEqual(position=="inside"?1:0,await check.CreditLedger.CountAsync());
    }
}
