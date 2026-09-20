using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebApp.Api.Models;
using WebApp.Api.Models.Entities;
using WebApp.Api.Services;
using Fixture=WebApp.Api.Tests.AiCreditConsumptionServiceTests.Fixture;

namespace WebApp.Api.Tests;

[TestClass]
public class AiUsageBillingOrchestratorTests
{
    private static async Task Seed(Fixture f,decimal budget=10m,decimal? wallet=1m,AiUsageType type=AiUsageType.ChatResponse)
    {
        await f.Seed(budget);
        await using var db=f.Db();
        var usage=await db.AiUsageRecords.SingleAsync();usage.Provider="Test";usage.Model="test-model";
        usage.InputTokens=100000;usage.OutputTokens=0;usage.TotalTokens=100000;usage.Available=true;usage.Completed=true;usage.UsageType=type;
        db.AiPricing.Add(new AiPricing{Id=Guid.NewGuid(),Provider="Test",Model="test-model",Currency="EUR",InputPricePerMillion=1m,OutputPricePerMillion=1m,EffectiveFromUtc=usage.CreatedAtUtc.AddDays(-1)});
        if(wallet.HasValue)db.CompanyWallets.Add(new CompanyWallet{CompanyId=f.CompanyId,Balance=wallet.Value,Currency="EUR"});
        await db.SaveChangesAsync();
    }

    [TestMethod]
    [DataRow("machine",AiUsageType.ChatResponse)]
    [DataRow("split",AiUsageType.VisionTool)]
    [DataRow("wallet",AiUsageType.ConversationSummary)]
    public async Task FullProcessingAndRepeat(string scenario,AiUsageType type)
    {
        await using var f=new Fixture();await Seed(f,scenario=="machine"?10m:scenario=="split"?0.04m:0m,1m,type);
        var service=new AiUsageBillingOrchestrator(f.Options);
        var result=await service.ProcessAsync(f.UsageId);
        Assert.IsTrue(result.Success);
        Assert.AreEqual(scenario=="machine"?"ProcessedByMachine":scenario=="split"?"ProcessedByMachineAndWallet":"ProcessedByWallet",result.Status);
        Assert.AreEqual(0.1m,result.RealAiCostEur);Assert.AreEqual(0.1m,result.ProviderCost);Assert.AreEqual("EUR",result.ProviderCurrency);
        Assert.AreEqual(0m,result.RemainingRealAiCostEur);
        Assert.AreEqual(scenario=="machine"?0.1m:scenario=="split"?0.04m:0m,result.MachineCoveredRealAiCostEur);
        Assert.AreEqual(scenario=="machine"?0m:scenario=="split"?0.06m:0.1m,result.WalletCoveredRealAiCostEur);
        Assert.AreEqual(scenario=="machine"?0m:scenario=="split"?0.18m:0.3m,result.CommercialCreditDebitedEur);
        await using var db=f.Db();var entries=await db.CreditLedger.CountAsync();var balance=(await db.CompanyWallets.SingleAsync()).Balance;
        var repeated=await service.ProcessAsync(f.UsageId);Assert.AreEqual("AlreadyFullyProcessed",repeated.Status);
        Assert.AreEqual(entries,await db.CreditLedger.CountAsync());Assert.AreEqual(balance,(await db.CompanyWallets.AsNoTracking().SingleAsync()).Balance);
    }

    [TestMethod]
    public async Task InsufficientThenRechargeResumesOnlyWallet()
    {
        await using var f=new Fixture();await Seed(f,0.04m,0.01m);var service=new AiUsageBillingOrchestrator(f.Options);
        var first=await service.ProcessAsync(f.UsageId);Assert.AreEqual("AwaitingWalletCredit",first.Status);
        Assert.IsTrue(first.WalletCreditRequired);Assert.AreEqual(0.06m,first.RemainingRealAiCostEur);
        Assert.AreEqual(0.04m,first.MachineCoveredRealAiCostEur);
        await using(var db=f.Db())
        {
            Assert.AreEqual(1,await db.CreditLedger.CountAsync());var wallet=await db.CompanyWallets.SingleAsync();
            Assert.AreEqual(0.01m,wallet.Balance);wallet.Balance=1m;await db.SaveChangesAsync();
        }
        var retry=await service.ProcessAsync(f.UsageId);Assert.AreEqual("ProcessedByMachineAndWallet",retry.Status);
        Assert.AreEqual(0.06m,retry.WalletCoveredRealAiCostEur);Assert.AreEqual(0.18m,retry.CommercialCreditDebitedEur);
        await using var check=f.Db();Assert.AreEqual(0.04m,(await check.MachineBillingPeriods.SingleAsync()).IncludedAiUsedRealCost);
        Assert.AreEqual(2,await check.CreditLedger.CountAsync());Assert.AreEqual(0.82m,(await check.CompanyWallets.SingleAsync()).Balance);
    }

    [TestMethod]
    public async Task MachineAlreadyProcessedResumesOutstandingWallet()
    {
        await using var f=new Fixture();await Seed(f,0.04m);
        await f.Service.ConsumeAsync(f.UsageId,0.1m,"EUR");
        var result=await new AiUsageBillingOrchestrator(f.Options).ProcessAsync(f.UsageId);
        Assert.AreEqual("ProcessedByMachineAndWallet",result.Status);Assert.AreEqual(0.18m,result.CommercialCreditDebitedEur);
    }

    [TestMethod]
    [DataRow("period","BillingPeriodNotFound")]
    [DataRow("wallet","WalletNotFound")]
    [DataRow("provider","UsageNotValuable")]
    [DataRow("pricing","UsageNotValuable")]
    [DataRow("ambiguous","UsageNotValuable")]
    [DataRow("conversion","CurrencyConversionFailed")]
    public async Task FailuresDoNotCreateMissingResources(string scenario,string expected)
    {
        await using var f=new Fixture();await Seed(f,0.04m,null);
        await using(var db=f.Db())
        {
            if(scenario=="period")db.Remove(await db.MachineBillingPeriods.SingleAsync());
            if(scenario=="provider")(await db.AiUsageRecords.SingleAsync()).Provider=null;
            if(scenario=="pricing")db.Remove(await db.AiPricing.SingleAsync());
            if(scenario=="conversion")(await db.AiPricing.SingleAsync()).Currency="USD";
            if(scenario=="ambiguous")
            {
                var row=await db.AiPricing.AsNoTracking().SingleAsync();row.Id=Guid.NewGuid();db.Add(row);
            }
            await db.SaveChangesAsync();
        }
        var result=await new AiUsageBillingOrchestrator(f.Options).ProcessAsync(f.UsageId);
        Assert.AreEqual(expected,result.Status);Assert.IsFalse(result.Success);
        if(scenario=="provider")Assert.AreEqual("ProviderMissing",result.FailureReason);
        if(scenario=="pricing")Assert.AreEqual("PricingNotFound",result.FailureReason);
        if(scenario=="ambiguous")Assert.AreEqual("AmbiguousPricing",result.FailureReason);
        if(scenario=="conversion")Assert.AreEqual("ExchangeRateNotFound",result.FailureReason);
        Assert.AreEqual(scenario=="period",result.BillingPeriodRequired);
        await using var check=f.Db();Assert.AreEqual(0,await check.CompanyWallets.CountAsync());
        Assert.AreEqual(scenario=="period"?0:1,await check.MachineBillingPeriods.CountAsync());
        Assert.AreEqual(scenario=="wallet"?1:0,await check.CreditLedger.CountAsync());
    }

    [TestMethod]
    public async Task OvercoveredLedgerStopsBeforeDebit()
    {
        await using var f=new Fixture();await Seed(f,0.04m);
        await f.Service.ConsumeAsync(f.UsageId,0.1m,"EUR");
        await new CompanyWalletDebitService(f.Options).DebitAsync(f.UsageId,0.1m);
        var result=await new AiUsageBillingOrchestrator(f.Options).ProcessAsync(f.UsageId);
        Assert.AreEqual("DataInconsistency",result.Status);
        await using var check=f.Db();Assert.AreEqual(2,await check.CreditLedger.CountAsync());
        Assert.AreEqual(0.7m,(await check.CompanyWallets.SingleAsync()).Balance);
    }

    [TestMethod]
    public async Task WalletAlreadyPaidWithNoMachineEntryDoesNotConsumeRestoredBudget()
    {
        await using var f=new Fixture();await Seed(f,0m);
        await new CompanyWalletDebitService(f.Options).DebitAsync(f.UsageId,0.1m);
        await using(var db=f.Db()){(await db.MachineBillingPeriods.SingleAsync()).IncludedAiBudgetRealCost=10m;await db.SaveChangesAsync();}
        Assert.AreEqual("AlreadyFullyProcessed",(await new AiUsageBillingOrchestrator(f.Options).ProcessAsync(f.UsageId)).Status);
        await using var check=f.Db();Assert.AreEqual(0m,(await check.MachineBillingPeriods.SingleAsync()).IncludedAiUsedRealCost);
        Assert.AreEqual(1,await check.CreditLedger.CountAsync());
    }

    [TestMethod]
    public async Task WalletFailureAfterMachineCommitCanResume()
    {
        await using var f=new Fixture();await Seed(f,0.04m);
        await using(var db=f.Db())
            await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER fail_wallet AFTER UPDATE ON CompanyWallets BEGIN SELECT RAISE(ABORT,'test'); END");
        var service=new AiUsageBillingOrchestrator(f.Options);
        try{await service.ProcessAsync(f.UsageId);Assert.Fail("Expected wallet failure");}catch(DbUpdateException){}
        await using(var db=f.Db())
        {
            Assert.AreEqual(1,await db.CreditLedger.CountAsync());
            Assert.AreEqual(0.04m,(await db.MachineBillingPeriods.SingleAsync()).IncludedAiUsedRealCost);
            Assert.AreEqual(1m,(await db.CompanyWallets.SingleAsync()).Balance);
            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER fail_wallet");
        }
        var result=await service.ProcessAsync(f.UsageId);Assert.AreEqual("ProcessedByMachineAndWallet",result.Status);
        await using var check=f.Db();Assert.AreEqual(2,await check.CreditLedger.CountAsync());
        Assert.AreEqual(0.82m,(await check.CompanyWallets.SingleAsync()).Balance);
    }

    [TestMethod]
    public async Task ConcurrentProcessesSettleOnlyOnce()
    {
        await using var f=new Fixture();await Seed(f,0.04m);
        async Task<AiUsageBillingResult> Run()
        {
            for(var attempt=0;;attempt++)
            {
                try{return await new AiUsageBillingOrchestrator(f.Options).ProcessAsync(f.UsageId);}
                catch(SqliteException e) when(attempt<5 && e.SqliteErrorCode is 5 or 6){await Task.Delay(20);}
                catch(DbUpdateException e) when(attempt<5 && e.InnerException is SqliteException{SqliteErrorCode:5 or 6}){await Task.Delay(20);}
            }
        }
        var results=await Task.WhenAll(Task.Run(Run),Task.Run(Run));
        Assert.IsTrue(results.All(r=>r.Success && r.RemainingRealAiCostEur==0));
        await using var check=f.Db();Assert.AreEqual(2,await check.CreditLedger.CountAsync());
        Assert.AreEqual(0.82m,(await check.CompanyWallets.SingleAsync()).Balance);
        Assert.AreEqual(0.04m,(await check.MachineBillingPeriods.SingleAsync()).IncludedAiUsedRealCost);
    }
}
