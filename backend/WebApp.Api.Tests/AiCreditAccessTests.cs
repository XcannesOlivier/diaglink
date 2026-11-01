using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebApp.Api.Data;
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

    [TestMethod]
    public async Task BatchedAdmissionMatchesLegacyDecisionAndUsesConstantQueryCount()
    {
        var counter = new CommandCounter();
        await using var f = new Fixture(counter);
        await f.Seed(0);
        await using (var setup = f.Db())
        {
            var first = await setup.AiUsageRecords.SingleAsync();
            ConfigureValuableUsage(first);
            for (var index = 1; index < 25; index++)
            {
                var usage = f.Usage(Guid.NewGuid());
                ConfigureValuableUsage(usage);
                setup.AiUsageRecords.Add(usage);
            }

            setup.AiPricing.Add(new()
            {
                Id = Guid.NewGuid(), Provider = "Test", Model = "test", Currency = "USD",
                InputPricePerMillion = 1, OutputPricePerMillion = 1,
                EffectiveFromUtc = first.CreatedAtUtc.AddDays(-1)
            });
            setup.ExchangeRates.Add(new()
            {
                Id = Guid.NewGuid(), BaseCurrency = "USD", QuoteCurrency = "EUR", Rate = 2,
                Source = "test", EffectiveFromUtc = first.CreatedAtUtc.AddDays(-1)
            });
            setup.CreditLedger.Add(new()
            {
                Id = Guid.NewGuid(), CompanyId = f.CompanyId, MachineId = f.MachineId,
                AiUsageRecordId = first.Id, EntryType = "AiUsage", BucketType = "MachineIncluded",
                RealAiCost = 0.0005m, Currency = "EUR", CreatedAtUtc = first.CreatedAtUtc
            });
            setup.CompanyWallets.Add(new()
            {
                // Exact boundary after conversion, included-quota subtraction and x3 commercial rounding.
                CompanyId = f.CompanyId, Balance = 0.1485m, Currency = "EUR"
            });
            await setup.SaveChangesAsync();
        }

        counter.Reset();
        AiCreditAccess legacy;
        await using (var legacyDb = f.Db())
            legacy = await LegacyCheckAsync(legacyDb, f.MachineId);
        var legacyQueryCount = counter.Count;

        counter.Reset();
        AiCreditAccess batched;
        await using (var batchedDb = f.Db())
            batched = await new AiCreditAccessService(batchedDb).CheckAsync(f.MachineId);
        var batchedQueryCount = counter.Count;

        Assert.AreEqual(legacy.Allowed, batched.Allowed);
        Assert.AreEqual(legacy.Status, batched.Status);
        Assert.IsFalse(batched.Allowed);
        Assert.AreEqual("AiCreditExhausted", batched.Status);
        Assert.AreEqual(79, legacyQueryCount);
        Assert.AreEqual(7, batchedQueryCount);
    }

    [TestMethod]
    public async Task MissingExchangeRateRemainsNonBlockingLikeLegacyAdmission()
    {
        await using var f = new Fixture();
        await f.Seed(0);
        await using (var setup = f.Db())
        {
            var usage = await setup.AiUsageRecords.SingleAsync();
            ConfigureValuableUsage(usage);
            setup.AiPricing.Add(new()
            {
                Id = Guid.NewGuid(), Provider = "Test", Model = "test", Currency = "USD",
                InputPricePerMillion = 1, OutputPricePerMillion = 1,
                EffectiveFromUtc = usage.CreatedAtUtc.AddDays(-1)
            });
            setup.CompanyWallets.Add(new() { CompanyId = f.CompanyId, Balance = 0.000001m, Currency = "EUR" });
            await setup.SaveChangesAsync();
        }

        AiCreditAccess legacy;
        await using (var legacyDb = f.Db())
            legacy = await LegacyCheckAsync(legacyDb, f.MachineId);
        AiCreditAccess batched;
        await using (var batchedDb = f.Db())
            batched = await new AiCreditAccessService(batchedDb).CheckAsync(f.MachineId);

        Assert.AreEqual(legacy.Allowed, batched.Allowed);
        Assert.AreEqual(legacy.Status, batched.Status);
        Assert.AreEqual("CompanyWalletAvailable", batched.Status);
    }

    private static void ConfigureValuableUsage(AiUsageRecord usage)
    {
        usage.Provider = "Test";
        usage.Model = "test";
        usage.InputTokens = 1000;
        usage.OutputTokens = 0;
        usage.TotalTokens = 1000;
        usage.Available = true;
        usage.Completed = true;
    }

    private static async Task<AiCreditAccess> LegacyCheckAsync(
        DiagLinkDbContext db,
        Guid? machineId,
        CancellationToken ct = default)
    {
        var machine = await db.Machines.AsNoTracking().SingleOrDefaultAsync(item => item.Id == machineId, ct);
        if (machine == null) return new(false, "AiCreditExhausted", AiCreditAccessService.ExhaustedMessage);
        var now = DateTime.UtcNow;
        var periods = await db.MachineBillingPeriods.AsNoTracking().Where(period => period.MachineId == machine.Id
            && period.PeriodStartUtc <= now && now < period.PeriodEndUtc).ToListAsync(ct);
        if (periods.Count == 1 && periods[0].Status == "Active"
            && periods[0].IncludedAiUsedRealCost >= 0
            && periods[0].IncludedAiBudgetRealCost > periods[0].IncludedAiUsedRealCost)
            return new(true, "MachineIncludedAvailable", "Budget IA inclus disponible.");
        var wallet = await db.CompanyWallets.AsNoTracking().SingleOrDefaultAsync(item => item.CompanyId == machine.CompanyId, ct);
        if (wallet?.Currency != "EUR" || wallet.Balance <= 0)
            return new(false, "AiCreditExhausted", AiCreditAccessService.ExhaustedMessage);

        decimal pending = 0;
        var usages = await db.AiUsageRecords.AsNoTracking().Where(usage => usage.CompanyId == machine.CompanyId
            && !db.CreditLedger.Any(entry => entry.AiUsageRecordId == usage.Id && entry.EntryType == "AiUsage"
                && entry.BucketType == "CompanyWallet")).ToListAsync(ct);
        var calculator = new AiCostCalculator(db);
        var converter = new AiCostCurrencyConverter(db);
        foreach (var usage in usages)
        {
            var cost = await calculator.CalculateAsync(usage, ct);
            if (!cost.IsValuable || cost.RealAiCost == null) continue;
            var converted = await converter.ConvertAsync(cost.RealAiCost.Value, cost.Currency, usage, ct);
            if (!converted.IsConvertible || converted.ConvertedAmount == null) continue;
            var entries = await db.CreditLedger.AsNoTracking().Where(entry => entry.AiUsageRecordId == usage.Id
                && entry.EntryType == "AiUsage" && entry.BucketType == "MachineIncluded").ToListAsync(ct);
            var remainder = converted.ConvertedAmount.Value - entries.Sum(entry => entry.RealAiCost ?? 0);
            if (remainder > 0) pending += CompanyWalletDebitService.CommercialAmount(remainder);
            if (pending >= wallet.Balance)
                return new(false, "AiCreditExhausted", AiCreditAccessService.ExhaustedMessage);
        }
        return new(true, "CompanyWalletAvailable", "CrÃ©dit entreprise disponible.");
    }

    private sealed class CommandCounter : DbCommandInterceptor
    {
        public int Count { get; private set; }
        public void Reset() => Count = 0;

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Count++;
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
