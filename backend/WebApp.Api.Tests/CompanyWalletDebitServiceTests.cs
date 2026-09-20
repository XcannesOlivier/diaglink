using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebApp.Api.Models.Entities;
using WebApp.Api.Services;
using Fixture = WebApp.Api.Tests.AiCreditConsumptionServiceTests.Fixture;

namespace WebApp.Api.Tests;

[TestClass]
public class CompanyWalletDebitServiceTests
{
    private static decimal D(string s) => decimal.Parse(s, CultureInfo.InvariantCulture);
    [TestMethod]
    public async Task InsufficientThenRechargeThenPaidThenAlreadyProcessed()
    {
        await using var f=new Fixture();await f.Seed();await Wallet(f,0.02m);
        var service=new CompanyWalletDebitService(f.Options);
        var first=await service.DebitAsync(f.UsageId,0.012106m);
        Assert.AreEqual("InsufficientWalletBalance",first.Status);
        Assert.AreEqual(0.016318m,first.CommercialCreditMissing);
        Assert.AreEqual(0.012106m,first.RemainingRealAiCostUncovered);
        await using(var db=f.Db())
        {
            Assert.AreEqual(0,await db.CreditLedger.CountAsync());
            var wallet=await db.CompanyWallets.SingleAsync();Assert.AreEqual(0.02m,wallet.Balance);
            wallet.Balance=100m;await db.SaveChangesAsync(); // Fictional local recharge only.
        }
        var paid=await service.DebitAsync(f.UsageId,0.012106m);
        Assert.AreEqual("Processed",paid.Status);Assert.AreEqual(0.036318m,paid.WalletDebitEur);
        Assert.AreEqual(99.963682m,paid.WalletBalanceAfterEur);
        Assert.AreEqual(0m,paid.CommercialCreditMissing);Assert.AreEqual(0m,paid.RemainingRealAiCostUncovered);
        var repeated=await service.DebitAsync(f.UsageId,0.012106m);
        Assert.AreEqual("AlreadyProcessed",repeated.Status);Assert.AreEqual(0m,repeated.WalletDebitEur);
        Assert.AreEqual(repeated.WalletBalanceBeforeEur,repeated.WalletBalanceAfterEur);
        Assert.AreEqual(0m,repeated.CommercialCreditMissing);
        await using var check=f.Db();Assert.AreEqual(1,await check.CreditLedger.CountAsync());
        Assert.AreEqual(99.963682m,(await check.CompanyWallets.SingleAsync()).Balance);
    }
    private static async Task Wallet(Fixture f, decimal balance, string currency = "EUR")
    {
        await using var db = f.Db();
        db.CompanyWallets.Add(new CompanyWallet { CompanyId=f.CompanyId, Balance=balance, Currency=currency });
        await db.SaveChangesAsync();
    }

    [TestMethod]
    [DataRow("10", "0.012106", "0.036318", "9.963682", "0.012106", "0", "Processed")]
    [DataRow("0.036318", "0.012106", "0.036318", "0", "0.012106", "0", "Processed")]
    [DataRow("0.02", "0.012106", "0", "0.02", "0", "0.012106", "InsufficientWalletBalance")]
    [DataRow("0.036317", "0.012106", "0", "0.036317", "0", "0.012106", "InsufficientWalletBalance")]
    [DataRow("0", "0.012106", "0", "0", "0", "0.012106", "InsufficientWalletBalance")]
    [DataRow("1", "0.000001", "0.000003", "0.999997", "0.000001", "0", "Processed")]
    [DataRow("0.000001", "0.000001", "0", "0.000001", "0", "0.000001", "InsufficientWalletBalance")]
    [DataRow("100", "0.012106", "0.036318", "99.963682", "0.012106", "0", "Processed")]
    public async Task AllOrNothingDebitAndLedger(string balance,string cost,string debit,string after,string covered,string uncovered,string status)
    {
        await using var f=new Fixture();await f.Seed();await Wallet(f,D(balance));
        var service=new CompanyWalletDebitService(f.Options);
        var result=await service.DebitAsync(f.UsageId,D(cost));
        Assert.AreEqual(status=="Processed",result.Success);Assert.AreEqual(status,result.Status);
        Assert.AreEqual(D(balance),result.WalletBalanceBeforeEur);
        Assert.AreEqual(Math.Max(0,D(cost)*3m-D(balance)),result.CommercialCreditMissing);
        Assert.AreEqual(D(cost)*3m,result.CommercialDebitRequired);
        Assert.AreEqual(D(debit),result.WalletDebitEur);Assert.AreEqual(D(after),result.WalletBalanceAfterEur);
        Assert.AreEqual(D(uncovered),result.RemainingRealAiCostUncovered);
        Assert.AreEqual(D(cost)*3m-D(debit),result.RemainingCommercialCreditRequired);
        await using var db=f.Db();Assert.AreEqual(D(after),(await db.CompanyWallets.SingleAsync()).Balance);
        Assert.AreEqual(0m,(await db.MachineBillingPeriods.SingleAsync()).IncludedAiUsedRealCost);
        if(D(debit)==0){Assert.AreEqual(0,await db.CreditLedger.CountAsync());Assert.IsNull(result.WalletLedgerEntryId);Assert.AreEqual(default(DateTime),(await db.CompanyWallets.SingleAsync()).UpdatedAtUtc);return;}
        var entry=await db.CreditLedger.SingleAsync();
        Assert.AreEqual(f.CompanyId,entry.CompanyId);Assert.AreEqual(f.MachineId,entry.MachineId);
        Assert.AreEqual(f.UsageId,entry.AiUsageRecordId);Assert.IsNull(entry.MachineBillingPeriodId);
        Assert.AreEqual("CompanyWallet",entry.BucketType);Assert.AreEqual("AiUsage",entry.EntryType);
        Assert.AreEqual(D(covered),entry.RealAiCost);Assert.AreEqual(D(debit),entry.CommercialCreditAmount);
        Assert.AreEqual(D(after),entry.BalanceAfter);Assert.AreEqual("EUR",entry.Currency);
        Assert.IsNull(entry.ExternalEventId);Assert.IsNull(entry.Notes);
        Assert.AreEqual(D(cost),entry.RealAiCost+result.RemainingRealAiCostUncovered);
        var repeated=await service.DebitAsync(f.UsageId,D(cost));
        Assert.AreEqual("AlreadyProcessed",repeated.Status);Assert.AreEqual(0m,repeated.WalletDebitEur);
        Assert.AreEqual(result.RemainingRealAiCostUncovered,repeated.RemainingRealAiCostUncovered);
        Assert.AreEqual(result.RemainingCommercialCreditRequired,repeated.RemainingCommercialCreditRequired);
        Assert.AreEqual(1,await db.CreditLedger.CountAsync());
        Assert.AreEqual(D(after),(await db.CompanyWallets.AsNoTracking().SingleAsync()).Balance);
    }

    [TestMethod]
    public async Task InvalidInputsAndMissingRecordsWriteNothing()
    {
        await using var f=new Fixture();await f.Seed();var service=new CompanyWalletDebitService(f.Options);
        foreach(var cost in new[]{0m,-1m,decimal.MaxValue,400000000000m,0.0000001m})
            Assert.AreEqual("InvalidCost",(await service.DebitAsync(f.UsageId,cost)).Status);
        Assert.AreEqual("InvalidCurrency",(await service.DebitAsync(f.UsageId,1m,"USD")).Status);
        Assert.AreEqual("UsageNotFound",(await service.DebitAsync(Guid.NewGuid(),1m)).Status);
        Assert.AreEqual("WalletNotFound",(await service.DebitAsync(f.UsageId,1m)).Status);
        await using var db=f.Db();Assert.AreEqual(0,await db.CompanyWallets.CountAsync());Assert.AreEqual(0,await db.CreditLedger.CountAsync());
    }

    [TestMethod]
    [DataRow("currency","InvalidCurrency")]
    [DataRow("negative","DataInconsistency")]
    [DataRow("company","CompanyMissing")]
    [DataRow("machine","DataInconsistency")]
    public async Task InvalidContext(string scenario,string expected)
    {
        await using var f=new Fixture();await f.Seed();await Wallet(f,scenario=="negative" ? -1m:1m,scenario=="currency"?"USD":"EUR");
        await using(var db=f.Db())
        {
            var usage=await db.AiUsageRecords.SingleAsync();
            if(scenario=="company")usage.CompanyId=null;
            if(scenario=="machine")usage.MachineId=Guid.NewGuid();
            await db.SaveChangesAsync();
        }
        Assert.AreEqual(expected,(await new CompanyWalletDebitService(f.Options).DebitAsync(f.UsageId,0.1m)).Status);
        await using var check=f.Db();Assert.AreEqual(0,await check.CreditLedger.CountAsync());
    }

    [TestMethod]
    public async Task NullableMachineIsSupported()
    {
        await using var f=new Fixture();await f.Seed();await Wallet(f,1m);
        await using(var db=f.Db()){(await db.AiUsageRecords.SingleAsync()).MachineId=null;await db.SaveChangesAsync();}
        Assert.AreEqual("Processed",(await new CompanyWalletDebitService(f.Options).DebitAsync(f.UsageId,0.1m)).Status);
        await using var check=f.Db();Assert.IsNull((await check.CreditLedger.SingleAsync()).MachineId);
    }

    [TestMethod]
    [DataRow("ledger")]
    [DataRow("wallet")]
    public async Task FailureRollsBackBothWrites(string target)
    {
        await using var f=new Fixture();await f.Seed();await Wallet(f,1m);
        await using(var db=f.Db())
        {
            var sql=target=="ledger"
                ? "CREATE TRIGGER fail AFTER INSERT ON CreditLedger BEGIN SELECT RAISE(ABORT,'test'); END"
                : "CREATE TRIGGER fail AFTER UPDATE ON CompanyWallets BEGIN SELECT RAISE(ABORT,'test'); END";
            await db.Database.ExecuteSqlRawAsync(sql);
        }
        try{await new CompanyWalletDebitService(f.Options).DebitAsync(f.UsageId,0.1m);Assert.Fail("Expected failure");}catch(DbUpdateException){}
        await using var check=f.Db();Assert.AreEqual(1m,(await check.CompanyWallets.SingleAsync()).Balance);Assert.AreEqual(0,await check.CreditLedger.CountAsync());
    }

    [TestMethod]
    public async Task MachineIncludedDoesNotBlockWalletAndDuplicateWalletIsRejected()
    {
        await using var f=new Fixture();await f.Seed(0.01m);await Wallet(f,1m);
        var included=await f.Service.ConsumeAsync(f.UsageId,0.022106m,"EUR");
        Assert.AreEqual(0.012106m,included.RemainingRealAiCostForWalletEur);
        var wallet=await new CompanyWalletDebitService(f.Options).DebitAsync(f.UsageId,included.RemainingRealAiCostForWalletEur);
        Assert.AreEqual(0.036318m,wallet.WalletDebitEur);
        await using var db=f.Db();Assert.AreEqual(2,await db.CreditLedger.CountAsync());
        var entry=await db.CreditLedger.AsNoTracking().SingleAsync(e=>e.BucketType=="CompanyWallet");
        entry.Id=Guid.NewGuid();db.Add(entry);
        try{await db.SaveChangesAsync();Assert.Fail("Unique index must reject duplicate");}catch(DbUpdateException){}
    }

    [TestMethod]
    public async Task ConcurrentSameAndDifferentUsagesCannotOverdraw()
    {
        await using var f=new Fixture();await f.Seed();await Wallet(f,1m);
        var other=Guid.NewGuid();await using(var db=f.Db()){db.AiUsageRecords.Add(f.Usage(other));await db.SaveChangesAsync();}
        async Task Run(Guid id)
        {
            for(int attempt=0;;attempt++)
            {
                try{await new CompanyWalletDebitService(f.Options).DebitAsync(id,0.2m);return;}
                catch(SqliteException e) when(attempt<5 && e.SqliteErrorCode is 5 or 6){await Task.Delay(20);}
                catch(DbUpdateException e) when(attempt<5 && e.InnerException is SqliteException{SqliteErrorCode:5 or 6}){await Task.Delay(20);}
            }
        }
        await Task.WhenAll(Task.Run(()=>Run(f.UsageId)),Task.Run(()=>Run(f.UsageId)),Task.Run(()=>Run(other)));
        await using var check=f.Db();Assert.AreEqual(0.4m,(await check.CompanyWallets.SingleAsync()).Balance);
        var entries=await check.CreditLedger.ToListAsync();Assert.AreEqual(1,entries.Count);
        Assert.AreEqual(1,entries.Select(e=>e.AiUsageRecordId).Distinct().Count());
        Assert.AreEqual(0.6m,entries.Sum(e=>e.CommercialCreditAmount));
    }
}
