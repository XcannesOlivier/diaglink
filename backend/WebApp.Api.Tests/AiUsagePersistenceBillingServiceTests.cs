using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebApp.Api.Data;
using WebApp.Api.Models;
using WebApp.Api.Repositories;
using WebApp.Api.Services;

namespace WebApp.Api.Tests;

[TestClass]
public class AiUsagePersistenceBillingServiceTests
{
    private static DbContextOptions<DiagLinkDbContext> Options() => new DbContextOptionsBuilder<DiagLinkDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
    private static AiUsageMeasurement Measurement(AiUsageType type=AiUsageType.ChatResponse) => new(null,null,null,null,"test",null,
        new AiResponseUsage(type,"response",true,1,1,2,"model","response",null,DateTimeOffset.UtcNow));
    private sealed class Billing(Func<Guid,CancellationToken,Task<AiUsageBillingResult>> action) : IAiUsageBillingOrchestrator
    {
        public List<Guid> Calls {get;}=[];
        public Task<AiUsageBillingResult> ProcessAsync(Guid id,CancellationToken ct=default){Calls.Add(id);return action(id,ct);}
    }
    private sealed class Logs : ILogger<AiUsagePersistenceBillingService>
    {
        public List<(LogLevel Level,string Text)> Entries {get;}=[];
        public IDisposable? BeginScope<TState>(TState state) where TState:notnull=>null;
        public bool IsEnabled(LogLevel level)=>true;
        public void Log<TState>(LogLevel level,EventId id,TState state,Exception? ex,Func<TState,Exception?,string> formatter)=>Entries.Add((level,formatter(state,ex)));
    }
    private sealed class FailedRepository(DbContextOptions<DiagLinkDbContext> options) : AiUsageRepository(options,NullLogger<AiUsageRepository>.Instance)
    {
        public override Task<AiUsageRecordWriteResult> RecordAsync(AiUsageMeasurement m,CancellationToken ct)=>Task.FromResult(new AiUsageRecordWriteResult(m.EventId,AiUsageRecordWriteStatus.Failed));
    }

    [TestMethod]
    public async Task AllTypesPersistBeforeBillingAndExistingRecordsResume()
    {
        var options=Options();var repository=new AiUsageRepository(options,NullLogger<AiUsageRepository>.Instance);
        var measurements=Enum.GetValues<AiUsageType>().Select(Measurement).ToArray();
        var billing=new Billing(async(id,ct)=>{
            await using var db=new DiagLinkDbContext(options);
            Assert.AreEqual(3,await db.AiUsageRecords.CountAsync(ct));
            Assert.IsTrue(await db.AiUsageRecords.AnyAsync(u=>u.Id==id,ct));
            return new AiUsageBillingResult{Success=true,Status="ProcessedByMachine",UsageRecordId=id};
        });
        var service=new AiUsagePersistenceBillingService(repository,billing,new Logs());
        await service.ProcessAsync(measurements);await service.ProcessAsync(measurements);
        Assert.AreEqual(6,billing.Calls.Count);Assert.AreEqual(3,billing.Calls.Distinct().Count());
        await using var check=new DiagLinkDbContext(options);Assert.AreEqual(3,await check.AiUsageRecords.CountAsync());
    }

    [TestMethod]
    public async Task FailedAndSkippedPersistenceNeverBill()
    {
        var options=Options();var billing=new Billing((id,ct)=>throw new InvalidOperationException("must not be called"));
        await new AiUsagePersistenceBillingService(new FailedRepository(options),billing,new Logs()).ProcessAsync([Measurement()]);
        var placeholder=Measurement() with {Response=Measurement().Response with{Completed=false,ResponseId=null}};
        await new AiUsagePersistenceBillingService(new AiUsageRepository(options,NullLogger<AiUsageRepository>.Instance),billing,new Logs()).ProcessAsync([placeholder]);
        Assert.AreEqual(0,billing.Calls.Count);
    }

    [TestMethod]
    [DataRow("ProcessedByMachine",true)]
    [DataRow("ProcessedByMachineAndWallet",true)]
    [DataRow("BillingPeriodNotFound",false)]
    [DataRow("WalletNotFound",false)]
    [DataRow("AwaitingWalletCredit",false)]
    [DataRow("UsageNotValuable",false)]
    [DataRow("CurrencyConversionFailed",false)]
    [DataRow("DataInconsistency",false)]
    public async Task BusinessStatusesAreObservedWithoutPropagation(string status,bool success)
    {
        var logs=new Logs();var billing=new Billing((id,ct)=>Task.FromResult(new AiUsageBillingResult{UsageRecordId=id,Status=status,Success=success,FailureReason=success?null:"reason"}));
        await new AiUsagePersistenceBillingService(new AiUsageRepository(Options(),NullLogger<AiUsageRepository>.Instance),billing,logs).ProcessAsync([Measurement()]);
        Assert.AreEqual(1,billing.Calls.Count);
        var entry=logs.Entries.Single();Assert.AreEqual(status=="DataInconsistency"?LogLevel.Error:success?LogLevel.Information:LogLevel.Warning,entry.Level);
        foreach(var key in new[]{"UsageRecordId=","UsageType=","CompanyId=","MachineId=","BillingStatus="+status,"FailureReason="})StringAssert.Contains(entry.Text,key);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task TechnicalFailureOrCancellationDoesNotPreventNextBilling(bool cancellation)
    {
        var count=0;var logs=new Logs();var billing=new Billing((id,ct)=>{
            Assert.IsTrue(ct.CanBeCanceled);
            if(count++==0)throw cancellation?new OperationCanceledException():new InvalidOperationException("test");
            return Task.FromResult(new AiUsageBillingResult{UsageRecordId=id,Success=true,Status="ProcessedByMachine"});
        });
        await new AiUsagePersistenceBillingService(new AiUsageRepository(Options(),NullLogger<AiUsageRepository>.Instance),billing,logs).ProcessAsync([Measurement(),Measurement()]);
        Assert.AreEqual(2,billing.Calls.Count);Assert.AreEqual(LogLevel.Error,logs.Entries[0].Level);
        StringAssert.Contains(logs.Entries[0].Text,"TechnicalFailure");
    }

    [TestMethod]
    public async Task RepositoryReportsPersistedExistingFailedAndSkipped()
    {
        var repository=new AiUsageRepository(Options(),NullLogger<AiUsageRepository>.Instance);var measurement=Measurement();
        var first=await repository.RecordAsync(measurement,CancellationToken.None);
        Assert.AreEqual(AiUsageRecordWriteStatus.Persisted,first.Status);Assert.AreEqual(measurement.EventId,first.UsageRecordId);
        Assert.AreEqual(AiUsageRecordWriteStatus.AlreadyExists,(await repository.RecordAsync(measurement,CancellationToken.None)).Status);
        Assert.AreEqual(AiUsageRecordWriteStatus.Failed,(await repository.RecordAsync(Measurement(),new CancellationToken(true))).Status);
        Assert.AreEqual(AiUsageRecordWriteStatus.Skipped,(await repository.RecordAsync(measurement with{Response=measurement.Response with{Completed=false,ResponseId=null}},CancellationToken.None)).Status);
    }

    [TestMethod]
    public async Task RealBillingReplayDoesNotDoubleDebit()
    {
        await using var fixture=new AiCreditConsumptionServiceTests.Fixture();await fixture.Seed();
        await using(var db=fixture.Db())
        {
            var usage=await db.AiUsageRecords.SingleAsync();usage.Provider="Test";usage.Model="model";usage.Available=true;usage.InputTokens=100000;usage.OutputTokens=0;usage.TotalTokens=100000;
            db.AiPricing.Add(new WebApp.Api.Models.Entities.AiPricing{Id=Guid.NewGuid(),Provider="Test",Model="model",Currency="EUR",InputPricePerMillion=1m,EffectiveFromUtc=usage.CreatedAtUtc.AddDays(-1)});await db.SaveChangesAsync();
        }
        var logs=new Logs();var repository=new AiUsageRepository(fixture.Options,NullLogger<AiUsageRepository>.Instance);
        var service=new AiUsagePersistenceBillingService(repository,new AiUsageBillingOrchestrator(fixture.Options),logs);
        var measurement=Measurement() with{EventId=fixture.UsageId};
        await service.ProcessAsync([measurement]);await service.ProcessAsync([measurement]);
        await using var check=fixture.Db();Assert.AreEqual(1,await check.CreditLedger.CountAsync());
        Assert.AreEqual(0.1m,(await check.MachineBillingPeriods.SingleAsync()).IncludedAiUsedRealCost);
        Assert.AreEqual(0,await check.CompanyWallets.CountAsync());Assert.AreEqual(1,await check.MachineBillingPeriods.CountAsync());
        StringAssert.Contains(logs.Entries.Last().Text,"AlreadyFullyProcessed");
    }
}
