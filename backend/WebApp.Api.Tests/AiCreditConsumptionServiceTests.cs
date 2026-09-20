using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebApp.Api.Data;
using WebApp.Api.Models;
using WebApp.Api.Models.Entities;
using WebApp.Api.Services;

namespace WebApp.Api.Tests;

[TestClass]
public class AiCreditConsumptionServiceTests
{
    private static readonly DateTime At = new(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc);
    internal sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection keeper;
        public DbContextOptions<DiagLinkDbContext> Options { get; }
        public Guid CompanyId = Guid.NewGuid(), MachineId = Guid.NewGuid(), UsageId = Guid.NewGuid(), PeriodId = Guid.NewGuid();
        public AiCreditConsumptionService Service => new(Options);
        public DiagLinkDbContext Db() => new(Options);
        public Fixture()
        {
            var cs = $"Data Source=credit-{Guid.NewGuid()};Mode=Memory;Cache=Shared;Default Timeout=1";
            keeper = new SqliteConnection(cs); keeper.Open();
            Options = new DbContextOptionsBuilder<DiagLinkDbContext>().UseSqlite(cs).Options;
        }
        public async Task Seed(decimal budget = 10m, decimal used = 0m)
        {
            await using var db = Db();
            await db.Database.EnsureCreatedAsync();
            // Legacy tables are excluded from EF migrations and need a local-only schema.
            await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS Companies (Id TEXT PRIMARY KEY, Name TEXT NOT NULL, Status TEXT NOT NULL, CreatedAt TEXT NOT NULL, UpdatedAt TEXT NOT NULL)");
            await db.Database.ExecuteSqlRawAsync("CREATE TABLE IF NOT EXISTS Machines (Id TEXT PRIMARY KEY, CompanyId TEXT NOT NULL, Name TEXT NOT NULL, Status TEXT NOT NULL, Reference TEXT, FoundryAgentId TEXT, VectorStoreId TEXT, BlobPrefix TEXT, ProjectEndpoint TEXT, AgentVersion TEXT, CreatedAt TEXT NOT NULL, UpdatedAt TEXT NOT NULL)");
            db.Companies.Add(new Company { Id=CompanyId, Name="test", Status="Active" });
            db.Machines.Add(new Machine { Id=MachineId, CompanyId=CompanyId, Name="test", Status="Active" });
            db.AiUsageRecords.Add(Usage(UsageId));
            db.MachineBillingPeriods.Add(new MachineBillingPeriod { Id=PeriodId, MachineId=MachineId,
                PeriodStartUtc=At, PeriodEndUtc=At.AddMonths(1), Status="Active",
                IncludedAiBudgetRealCost=budget, IncludedAiUsedRealCost=used });
            await db.SaveChangesAsync();
        }
        public AiUsageRecord Usage(Guid id) => new() { Id=id, MachineId=MachineId, CompanyId=CompanyId, CreatedAtUtc=At };
        public async ValueTask DisposeAsync() => await keeper.DisposeAsync();
    }

    [TestMethod]
    [DataRow("10", "0", "0.093929", "0.093929", "9.906071", "0")]
    [DataRow("0.093929", "0", "0.093929", "0.093929", "0", "0")]
    [DataRow("10", "9.97", "0.093929", "0.03", "0", "0.063929")]
    [DataRow("10", "10", "0.093929", "0", "0", "0.093929")]
    [DataRow("10", "0", "0.000001", "0.000001", "9.999999", "0")]
    public async Task DebitAndLedger(string budget, string used, string cost, string debit, string remaining, string remainder)
    {
        decimal D(string s) => decimal.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
        await using var f = new Fixture(); await f.Seed(D(budget),D(used));
        var result = await f.Service.ConsumeAsync(f.UsageId,D(cost),"EUR");
        Assert.IsTrue(result.Success);
        Assert.AreEqual(D(debit),result.MachineDebitEur);
        Assert.AreEqual(D(remaining),result.RemainingIncludedBudgetEur);
        Assert.AreEqual(D(remainder),result.RemainingRealAiCostForWalletEur);
        Assert.AreEqual(D(remainder)>0 ? "WalletDebitRequired" : "Processed",result.Status);
        await using var db=f.Db();
        Assert.AreEqual(D(used)+D(debit),(await db.MachineBillingPeriods.SingleAsync()).IncludedAiUsedRealCost);
        Assert.AreEqual(0,await db.CompanyWallets.CountAsync());
        if(D(debit)==0) { Assert.AreEqual(0,await db.CreditLedger.CountAsync()); return; }
        var entry=await db.CreditLedger.SingleAsync();
        Assert.AreEqual(f.CompanyId,entry.CompanyId); Assert.AreEqual(f.MachineId,entry.MachineId);
        Assert.AreEqual(f.PeriodId,entry.MachineBillingPeriodId); Assert.AreEqual(f.UsageId,entry.AiUsageRecordId);
        Assert.AreEqual("AiUsage",entry.EntryType); Assert.AreEqual("MachineIncluded",entry.BucketType);
        Assert.AreEqual(D(debit),entry.RealAiCost); Assert.AreEqual(D(remaining),entry.BalanceAfter);
        Assert.AreEqual("EUR",entry.Currency); Assert.IsNull(entry.CommercialCreditAmount);
        Assert.IsNull(entry.ExternalEventId); Assert.IsNull(entry.Notes);
        var repeated=await f.Service.ConsumeAsync(f.UsageId,D(cost),"EUR");
        Assert.AreEqual("AlreadyProcessed",repeated.Status); Assert.AreEqual(0m,repeated.MachineDebitEur);
        Assert.AreEqual(D(remainder),repeated.RemainingRealAiCostForWalletEur);
        Assert.AreEqual(1,await db.CreditLedger.CountAsync());
    }

    [TestMethod]
    [DataRow("missing-period","BillingPeriodNotFound")]
    [DataRow("end","BillingPeriodNotFound")]
    [DataRow("before","BillingPeriodNotFound")]
    [DataRow("other-period-machine","BillingPeriodNotFound")]
    [DataRow("company","DataInconsistency")]
    [DataRow("null-company","CompanyMissing")]
    [DataRow("null-machine","MachineMissing")]
    [DataRow("missing-machine","MachineMissing")]
    [DataRow("inactive","BillingPeriodInactive")]
    [DataRow("overlap","AmbiguousBillingPeriod")]
    [DataRow("overused","DataInconsistency")]
    public async Task InvalidContextWritesNothing(string scenario,string expected)
    {
        await using var f=new Fixture(); await f.Seed();
        await using(var db=f.Db())
        {
            var period=await db.MachineBillingPeriods.SingleAsync(); var usage=await db.AiUsageRecords.SingleAsync();
            switch(scenario)
            {
                case "missing-period": db.Remove(period); break;
                case "end": usage.CreatedAtUtc=period.PeriodEndUtc; break;
                case "before": usage.CreatedAtUtc=period.PeriodStartUtc.AddTicks(-1); break;
                case "other-period-machine":
                    var other=new Machine {Id=Guid.NewGuid(),CompanyId=f.CompanyId,Name="other",Status="Active"};
                    db.Machines.Add(other); period.MachineId=other.Id; break;
                case "company": var company=new Company {Id=Guid.NewGuid(),Name="other",Status="Active"}; db.Companies.Add(company); usage.CompanyId=company.Id; break;
                case "null-company": usage.CompanyId=null; break;
                case "null-machine": usage.MachineId=null; break;
                case "missing-machine": usage.MachineId=Guid.NewGuid(); break;
                case "inactive": period.Status="Suspended"; break;
                case "overused": period.IncludedAiUsedRealCost=11m; break;
                case "overlap": db.MachineBillingPeriods.Add(new MachineBillingPeriod {Id=Guid.NewGuid(),MachineId=f.MachineId,PeriodStartUtc=At.AddDays(-1),PeriodEndUtc=At.AddDays(1),Status="Active"}); break;
            }
            await db.SaveChangesAsync();
        }
        Assert.AreEqual(expected,(await f.Service.ConsumeAsync(f.UsageId,1m,"EUR")).Status);
        await using var check=f.Db(); Assert.AreEqual(0,await check.CreditLedger.CountAsync()); Assert.AreEqual(0,await check.CompanyWallets.CountAsync());
    }

    [TestMethod]
    public async Task InvalidCostCurrencyAndUsage()
    {
        await using var f=new Fixture(); await f.Seed();
        foreach(var cost in new[]{0m,-1m,0.0000001m,decimal.MaxValue})
            Assert.AreEqual("InvalidCost",(await f.Service.ConsumeAsync(f.UsageId,cost,"EUR")).Status);
        Assert.AreEqual("InvalidCurrency",(await f.Service.ConsumeAsync(f.UsageId,1m,"USD")).Status);
        Assert.AreEqual("UsageNotFound",(await f.Service.ConsumeAsync(Guid.NewGuid(),1m,"EUR")).Status);
    }

    [TestMethod]
    [DataRow("CreditLedger","INSERT")]
    [DataRow("MachineBillingPeriods","UPDATE")]
    public async Task DatabaseFailureRollsBackBothWrites(string table,string operation)
    {
        await using var f=new Fixture(); await f.Seed();
        await using(var db=f.Db())
        {
            var sql = (table, operation) switch
            {
                ("CreditLedger", "INSERT") => "CREATE TRIGGER fail_write AFTER INSERT ON CreditLedger BEGIN SELECT RAISE(ABORT, 'test failure'); END",
                ("MachineBillingPeriods", "UPDATE") => "CREATE TRIGGER fail_write AFTER UPDATE ON MachineBillingPeriods BEGIN SELECT RAISE(ABORT, 'test failure'); END",
                _ => throw new InvalidOperationException("Unexpected test trigger")
            };
            await db.Database.ExecuteSqlRawAsync(sql);
        }
        try { await f.Service.ConsumeAsync(f.UsageId,1m,"EUR"); Assert.Fail("Expected database failure"); }
        catch(DbUpdateException) { }
        await using var check=f.Db(); Assert.AreEqual(0m,(await check.MachineBillingPeriods.SingleAsync()).IncludedAiUsedRealCost);
        Assert.AreEqual(0,await check.CreditLedger.CountAsync());
    }

    [TestMethod]
    public async Task MultipleUsagesNeverExceedBudgetAndUniqueIndexRejectsDuplicate()
    {
        await using var f=new Fixture(); await f.Seed(1m);
        var ids=new[]{f.UsageId,Guid.NewGuid(),Guid.NewGuid()};
        await using(var db=f.Db()) {db.AiUsageRecords.AddRange(ids.Skip(1).Select(f.Usage));await db.SaveChangesAsync();}
        foreach(var id in ids) await f.Service.ConsumeAsync(id,0.6m,"EUR");
        await using var check=f.Db(); Assert.AreEqual(1m,(await check.MachineBillingPeriods.SingleAsync()).IncludedAiUsedRealCost);
        Assert.AreEqual(2,await check.CreditLedger.CountAsync());
        var entry=await check.CreditLedger.AsNoTracking().FirstAsync(); entry.Id=Guid.NewGuid();check.Add(entry);
        try {await check.SaveChangesAsync(); Assert.Fail("Unique index must reject duplicate");}catch(DbUpdateException){}
    }

    [TestMethod]
    public async Task ExistingWalletIsNeverDebited()
    {
        await using var f=new Fixture(); await f.Seed(0.03m);
        await using(var db=f.Db())
        {
            db.CompanyWallets.Add(new CompanyWallet {CompanyId=f.CompanyId,Balance=50m,Currency="EUR",CreatedAtUtc=At,UpdatedAtUtc=At});
            await db.SaveChangesAsync();
        }
        var result=await f.Service.ConsumeAsync(f.UsageId,0.093929m,"EUR");
        Assert.AreEqual("WalletDebitRequired",result.Status);
        await using var check=f.Db();var wallet=await check.CompanyWallets.SingleAsync();
        Assert.AreEqual(50m,wallet.Balance);Assert.AreEqual(At,wallet.UpdatedAtUtc);
    }

    [TestMethod]
    public async Task ConcurrentSameUsageAndDistinctUsagesRemainSafe()
    {
        await using var f=new Fixture();await f.Seed(1m);
        var other=Guid.NewGuid();await using(var db=f.Db()){db.AiUsageRecords.Add(f.Usage(other));await db.SaveChangesAsync();}
        async Task Run(Guid id)
        {
            // SQLite may reject a competing writer; retry the entire rolled-back operation.
            for(var attempt=0; ; attempt++)
            {
                try {await f.Service.ConsumeAsync(id,0.7m,"EUR"); return;}
                catch(SqliteException e) when(attempt<5 && e.SqliteErrorCode is 5 or 6){await Task.Delay(20);}
                catch(DbUpdateException e) when(attempt<5 && e.InnerException is SqliteException {SqliteErrorCode:5 or 6}){await Task.Delay(20);}
            }
        }
        await Task.WhenAll(Task.Run(()=>Run(f.UsageId)),Task.Run(()=>Run(f.UsageId)),Task.Run(()=>Run(other)));
        await using var check=f.Db();Assert.AreEqual(1m,(await check.MachineBillingPeriods.SingleAsync()).IncludedAiUsedRealCost);
        var entries=await check.CreditLedger.ToListAsync();Assert.AreEqual(2,entries.Count);Assert.AreEqual(2,entries.Select(e=>e.AiUsageRecordId).Distinct().Count());
    }
}
