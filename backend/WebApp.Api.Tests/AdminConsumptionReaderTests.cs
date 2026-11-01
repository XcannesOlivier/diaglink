using System.Data.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebApp.Api.Services;
using WebApp.Api.Models;
using Fixture=WebApp.Api.Tests.AiCreditConsumptionServiceTests.Fixture;
namespace WebApp.Api.Tests;
[TestClass]
public class AdminConsumptionReaderTests
{
 [TestMethod]
 public async Task MachineTokenHistoryReturnsStructuredCallsAndToleratesLegacyOrInvalidBreakdown()
 {
  await using var f=new Fixture();await f.Seed();await using var db=f.Db();
  var now=DateTime.UtcNow;
  db.AiUsageRecords.RemoveRange(db.AiUsageRecords);
  db.AiPricing.Add(new(){Id=Guid.NewGuid(),Provider="Anthropic",Model="claude-sonnet-5",UsageType="ChatResponse",Currency="EUR",
   InputPricePerMillion=1,OutputPricePerMillion=2,CacheReadPricePerMillion=0.1m,CacheCreation5mPricePerMillion=1.25m,
   CacheCreation1hPricePerMillion=2,WebSearchPricePerRequest=0.01m,EffectiveFromUtc=now.AddDays(-1)});
  db.AiUsageRecords.AddRange(
   new(){Id=Guid.NewGuid(),MachineId=f.MachineId,CompanyId=f.CompanyId,CreatedAtUtc=now,UsageType=AiUsageType.ChatResponse,
    Provider="Anthropic",Model="claude-sonnet-5",Available=true,Completed=true,InputTokens=30,OutputTokens=7,TotalTokens=37,
    CacheReadInputTokens=12,CacheCreationInputTokens=9,CacheCreation5mInputTokens=7,CacheCreation1hInputTokens=2,
    CallBreakdownJson="""[{"callNumber":1,"inputTokens":10,"outputTokens":2,"cacheReadInputTokens":12,"cacheCreationInputTokens":9,"cacheCreation5mInputTokens":7,"cacheCreation1hInputTokens":2,"webSearchRequests":0,"totalTokens":12,"model":"claude-sonnet-5","stopReason":"tool_use","tools":["load_skill"],"loadedSkills":["diagnostic"],"skillContinuationFor":[]}]"""},
   new(){Id=Guid.NewGuid(),MachineId=f.MachineId,CompanyId=f.CompanyId,CreatedAtUtc=now.AddMinutes(-1),UsageType=AiUsageType.ChatResponse,
    Provider="Anthropic",Model="claude-sonnet-5",Available=true,Completed=true,InputTokens=20,OutputTokens=5,TotalTokens=25,CallBreakdownJson=null},
   new(){Id=Guid.NewGuid(),MachineId=f.MachineId,CompanyId=f.CompanyId,CreatedAtUtc=now.AddMinutes(-2),UsageType=AiUsageType.ChatResponse,
    Provider="Anthropic",Model="claude-sonnet-5",Available=true,Completed=true,InputTokens=1,OutputTokens=1,TotalTokens=2,CallBreakdownJson="invalid"});
  await db.SaveChangesAsync();db.ChangeTracker.Clear();

  var response=(AdminConsumptionReader.MachineTokenHistoryResponse)((IValueHttpResult)await AdminConsumptionReader.ReadMachineTokenHistoryAsync(
   f.CompanyId,f.MachineId,0,20,db,default)).Value!;

  Assert.HasCount(3,response.Items);
  var current=response.Items[0];
  Assert.AreEqual(30,current.InputTokens);Assert.AreEqual(7,current.OutputTokens);Assert.AreEqual(37,current.TotalTokens);
  Assert.AreEqual(12,current.CacheReadInputTokens);Assert.AreEqual(9,current.CacheCreationInputTokens);
  Assert.AreEqual(7,current.CacheCreation5mInputTokens);Assert.AreEqual(2,current.CacheCreation1hInputTokens);
  Assert.AreEqual("claude-sonnet-5",current.Model);Assert.AreEqual("Anthropic",current.Provider);
  Assert.IsNotNull(current.Calls);Assert.HasCount(1,current.Calls);
  var call=current.Calls[0];
  Assert.AreEqual(1,call.CallNumber);Assert.AreEqual(10L,call.InputTokens);Assert.AreEqual(2L,call.OutputTokens);Assert.AreEqual(12L,call.TotalTokens);
  Assert.AreEqual(12L,call.CacheReadInputTokens);Assert.AreEqual(9L,call.CacheCreationInputTokens);
  Assert.AreEqual("claude-sonnet-5",call.Model);Assert.AreEqual("tool_use",call.StopReason);CollectionAssert.AreEqual(new[]{"load_skill"},call.Tools);
  CollectionAssert.AreEqual(new[]{"diagnostic"},call.LoadedSkills);Assert.IsNotNull(call.EstimatedCostEur);
  Assert.IsNull(response.Items[1].Calls);
  Assert.IsNull(response.Items[2].Calls);
 }

 [TestMethod]
 public async Task IsolatesCompanyMachineUserAndSeparatesLedgerFromProviderCost()
 {
  await using var f=new Fixture();await f.Seed();await using var db=f.Db();
  await db.Database.ExecuteSqlRawAsync("CREATE TABLE Users (Id TEXT PRIMARY KEY, CompanyId TEXT, EntraObjectId TEXT, Email TEXT, Role TEXT, Status TEXT, CreatedAt TEXT, UpdatedAt TEXT, FirstName TEXT, LastName TEXT, PhoneNumber TEXT)");
  await db.Database.ExecuteSqlRawAsync("CREATE TABLE UserMachines (UserId TEXT NOT NULL, MachineId TEXT NOT NULL, CreatedAt TEXT NOT NULL, PRIMARY KEY (UserId, MachineId))");
  var now=DateTime.UtcNow;var user=Guid.NewGuid();var other=Guid.NewGuid();
  var period=await db.MachineBillingPeriods.SingleAsync();period.PeriodStartUtc=now.AddDays(-1);period.PeriodEndUtc=now.AddDays(5);period.IncludedAiUsedRealCost=2;
  db.Machines.Add(new(){Id=other,CompanyId=f.CompanyId,Name="Other",Status="inactive"});
  db.AiUsageRecords.RemoveRange(db.AiUsageRecords);
  db.AiPricing.Add(new(){Id=Guid.NewGuid(),Provider="test",Model="test",Currency="EUR",InputPricePerMillion=1,OutputPricePerMillion=1,EffectiveFromUtc=now.AddDays(-5)});
  foreach(var (machine,company,date,type) in new[]{(f.MachineId,f.CompanyId,now,AiUsageType.ChatResponse),(other,f.CompanyId,now,AiUsageType.VisionTool),(f.MachineId,Guid.NewGuid(),now,AiUsageType.ChatResponse),(f.MachineId,f.CompanyId,now.AddDays(1),AiUsageType.ChatResponse)})
  {
   if(company!=f.CompanyId)db.Companies.Add(new(){Id=company,Name="Foreign",Status="active"});
   var id=Guid.NewGuid();db.AiUsageRecords.Add(new(){Id=id,MachineId=machine,CompanyId=company,UserId=user,CreatedAtUtc=date,UsageType=type,Provider="test",Model="test",Available=true,InputTokens=1000000,OutputTokens=0,TotalTokens=1000000});
   db.CreditLedger.Add(new(){Id=Guid.NewGuid(),CompanyId=company,MachineId=machine,AiUsageRecordId=id,EntryType="AiUsage",BucketType="CompanyWallet",Currency="EUR",CommercialCreditAmount=3,RealAiCost=1});
  }
  await db.SaveChangesAsync();db.ChangeTracker.Clear();
  var result=(AdminConsumptionReader.Report)((IValueHttpResult)await AdminConsumptionReader.ReadAsync(f.CompanyId,now.AddHours(-1).ToString("O"),now.AddDays(1).ToString("O"),null,db,default)).Value!;
  Assert.AreEqual(2m,result.Metrics.RealCost);Assert.AreEqual(6m,result.Metrics.CommercialCredit);
  Assert.AreEqual(1,result.Metrics.Responses);Assert.AreEqual(1,result.Metrics.Vision);
  Assert.AreEqual(2,result.Machines.Length);
  var machineRow=result.Machines.Single(m=>m.Id==f.MachineId);
  Assert.AreEqual(8m,machineRow.Remaining);Assert.AreEqual(10m,machineRow.Budget);
  Assert.AreEqual(1m,machineRow.Users.Single().Metrics.RealCost);
  Assert.AreEqual(0m,result.Machines.Single(m=>m.Id==other).Budget);
  Assert.AreEqual(0,db.ChangeTracker.Entries().Count());
  var vision=(AdminConsumptionReader.Report)((IValueHttpResult)await AdminConsumptionReader.ReadAsync(f.CompanyId,null,now.AddDays(1).ToString("O"),"VisionTool",db,default)).Value!;
  Assert.AreEqual(1m,vision.Metrics.RealCost);Assert.AreEqual(0,vision.Metrics.Responses);
  Assert.AreEqual(400,((IStatusCodeHttpResult)await AdminConsumptionReader.ReadAsync(f.CompanyId,"invalid",null,null,db,default)).StatusCode);
 }

 [TestMethod]
 public async Task MachineUsersIncludeActiveAssignmentsWithoutUsageAndPreserveHistoricalUsage()
 {
  await using var f=new Fixture();await f.Seed();await using var db=f.Db();
  await db.Database.ExecuteSqlRawAsync("CREATE TABLE Users (Id TEXT PRIMARY KEY, CompanyId TEXT, EntraObjectId TEXT, Email TEXT, Role TEXT, Status TEXT, CreatedAt TEXT, UpdatedAt TEXT, FirstName TEXT, LastName TEXT, PhoneNumber TEXT)");
  await db.Database.ExecuteSqlRawAsync("CREATE TABLE UserMachines (UserId TEXT NOT NULL, MachineId TEXT NOT NULL, CreatedAt TEXT NOT NULL, PRIMARY KEY (UserId, MachineId))");
  var now=DateTime.UtcNow;
  var consuming=Guid.NewGuid();var withoutUsage=Guid.NewGuid();var unassigned=Guid.NewGuid();var admin=Guid.NewGuid();var deleted=Guid.NewGuid();
  db.Users.AddRange(
   new(){Id=consuming,CompanyId=f.CompanyId,Email="active@example.test",FirstName="Active",LastName="Usage",Role="technician",Status="active",CreatedAt=now,UpdatedAt=now},
   new(){Id=withoutUsage,CompanyId=f.CompanyId,Email="zero@example.test",FirstName="Active",LastName="Zero",Role="technician",Status="active",CreatedAt=now,UpdatedAt=now},
   new(){Id=unassigned,CompanyId=f.CompanyId,Email="other@example.test",FirstName="Not",LastName="Assigned",Role="technician",Status="active",CreatedAt=now,UpdatedAt=now},
   new(){Id=admin,CompanyId=f.CompanyId,Email="admin@example.test",FirstName="Company",LastName="Admin",Role="company_admin",Status="active",CreatedAt=now,UpdatedAt=now});
  db.UserMachineAccess.AddRange(
   new(){UserId=consuming,MachineId=f.MachineId,CreatedAtUtc=now},
   new(){UserId=withoutUsage,MachineId=f.MachineId,CreatedAtUtc=now});
  db.AiUsageRecords.RemoveRange(db.AiUsageRecords);
  db.AiPricing.Add(new(){Id=Guid.NewGuid(),Provider="test",Model="test",Currency="EUR",InputPricePerMillion=1,OutputPricePerMillion=1,EffectiveFromUtc=now.AddDays(-1)});
  var consumingUsage=Guid.NewGuid();var historicalUsage=Guid.NewGuid();
  db.AiUsageRecords.AddRange(
   new(){Id=consumingUsage,MachineId=f.MachineId,CompanyId=f.CompanyId,UserId=consuming,CreatedAtUtc=now,UsageType=AiUsageType.ChatResponse,Provider="test",Model="test",Available=true,InputTokens=1000000,OutputTokens=0,TotalTokens=1000000},
   new(){Id=historicalUsage,MachineId=f.MachineId,CompanyId=f.CompanyId,UserId=deleted,CreatedAtUtc=now,UsageType=AiUsageType.VisionTool,Provider="test",Model="test",Available=true,InputTokens=0,OutputTokens=1000000,TotalTokens=1000000});
  db.CreditLedger.Add(new(){Id=Guid.NewGuid(),CompanyId=f.CompanyId,MachineId=f.MachineId,AiUsageRecordId=consumingUsage,EntryType="AiUsage",BucketType="CompanyWallet",Currency="EUR",CommercialCreditAmount=2.5m,RealAiCost=1m});
  await db.SaveChangesAsync();db.ChangeTracker.Clear();

  var report=(AdminConsumptionReader.Report)((IValueHttpResult)await AdminConsumptionReader.ReadAsync(
   f.CompanyId,now.AddMinutes(-1).ToString("O"),now.AddMinutes(1).ToString("O"),null,db,default)).Value!;
  var rows=report.Machines.Single(machine=>machine.Id==f.MachineId).Users;
  Assert.AreEqual(4,rows.Length);
  var actual=rows.Single(user=>user.Id==consuming);
  Assert.AreEqual("Active Usage",actual.Name);Assert.AreEqual(1,actual.Metrics.Responses);
  Assert.AreEqual(1m,actual.Metrics.RealCost);Assert.AreEqual(2.5m,actual.Metrics.CommercialCredit);
  var zero=rows.Single(user=>user.Id==withoutUsage);
  Assert.AreEqual("Active Zero",zero.Name);Assert.AreEqual(0,zero.Metrics.Responses);Assert.AreEqual(0,zero.Metrics.Vision);
  Assert.AreEqual(0m,zero.Metrics.RealCost);Assert.AreEqual(0m,zero.Metrics.CommercialCredit);Assert.AreEqual(0m,zero.Metrics.IncludedQuotaConsumed);
  var implicitAdmin=rows.Single(user=>user.Id==admin);
  Assert.AreEqual("Company Admin",implicitAdmin.Name);Assert.AreEqual(0,implicitAdmin.Metrics.Responses);
  Assert.AreEqual(0m,implicitAdmin.Metrics.RealCost);Assert.AreEqual(0,await db.UserMachineAccess.CountAsync(access=>access.UserId==admin));
  Assert.IsFalse(rows.Any(user=>user.Id==unassigned));
  var historical=rows.Single(user=>user.Id==deleted);
  Assert.AreEqual("Utilisateur non attribué / supprimé",historical.Name);Assert.AreEqual(1,historical.Metrics.Vision);
 }

 [TestMethod]
 public async Task ReportsExactIncludedQuotaConsumptionPerUserFromMachineLedgerEntries()
 {
  await using var f=new Fixture();await f.Seed();await using var db=f.Db();
  await db.Database.ExecuteSqlRawAsync("CREATE TABLE Users (Id TEXT PRIMARY KEY, CompanyId TEXT, EntraObjectId TEXT, Email TEXT, Role TEXT, Status TEXT, CreatedAt TEXT, UpdatedAt TEXT, FirstName TEXT, LastName TEXT, PhoneNumber TEXT)");
  await db.Database.ExecuteSqlRawAsync("CREATE TABLE UserMachines (UserId TEXT NOT NULL, MachineId TEXT NOT NULL, CreatedAt TEXT NOT NULL, PRIMARY KEY (UserId, MachineId))");
  var now=DateTime.UtcNow;
  var fullUser=Guid.NewGuid();var walletUser=Guid.NewGuid();var splitUser=Guid.NewGuid();var multiUser=Guid.NewGuid();
  var zeroUser=Guid.NewGuid();var noQuotaUser=Guid.NewGuid();var outsideUser=Guid.NewGuid();var noQuotaMachine=Guid.NewGuid();
  foreach(var (id,name) in new[]{(fullUser,"Full"),(walletUser,"Wallet"),(splitUser,"Split"),(multiUser,"Multi"),
      (zeroUser,"Zero"),(noQuotaUser,"NoQuota"),(outsideUser,"Outside")})
   db.Users.Add(new(){Id=id,CompanyId=f.CompanyId,Email=$"{name.ToLowerInvariant()}@example.test",FirstName=name,LastName="User",
       Role="technician",Status="active",CreatedAt=now,UpdatedAt=now});
  db.UserMachineAccess.Add(new(){UserId=zeroUser,MachineId=f.MachineId,CreatedAtUtc=now});
  db.Machines.Add(new(){Id=noQuotaMachine,CompanyId=f.CompanyId,Name="Without quota",Status="active"});
  db.AiUsageRecords.RemoveRange(db.AiUsageRecords);await db.SaveChangesAsync();
  var period=await db.MachineBillingPeriods.SingleAsync();period.PeriodStartUtc=now.AddDays(-2);period.PeriodEndUtc=now.AddDays(2);
  period.IncludedAiBudgetRealCost=10m;period.IncludedAiUsedRealCost=2.4m;
  db.AiPricing.Add(new(){Id=Guid.NewGuid(),Provider="test",Model="test",Currency="EUR",InputPricePerMillion=1,OutputPricePerMillion=1,EffectiveFromUtc=now.AddDays(-10)});
  Guid Usage(Guid user,Guid machine,DateTime created,int tokens=1_000_000)
  {
   var id=Guid.NewGuid();db.AiUsageRecords.Add(new(){Id=id,MachineId=machine,CompanyId=f.CompanyId,UserId=user,CreatedAtUtc=created,
       UsageType=AiUsageType.ChatResponse,Provider="test",Model="test",Available=true,InputTokens=tokens,OutputTokens=0,TotalTokens=tokens});return id;
  }
  var full=Usage(fullUser,f.MachineId,now);var wallet=Usage(walletUser,f.MachineId,now);var split=Usage(splitUser,f.MachineId,now);
  var multi1=Usage(multiUser,f.MachineId,now,500_000);var multi2=Usage(multiUser,f.MachineId,now,500_000);
  var outside=Usage(outsideUser,f.MachineId,now.AddDays(-5));var noQuota=Usage(noQuotaUser,noQuotaMachine,now);
  void Ledger(Guid usage,Guid machine,string bucket,decimal real,decimal? commercial=null,Guid? billingPeriodId=null)
   =>db.CreditLedger.Add(new(){Id=Guid.NewGuid(),CompanyId=f.CompanyId,MachineId=machine,MachineBillingPeriodId=billingPeriodId,
       AiUsageRecordId=usage,EntryType="AiUsage",BucketType=bucket,Currency="EUR",RealAiCost=real,
       CommercialCreditAmount=commercial,BalanceAfter=0,CreatedAtUtc=now});
  Ledger(full,f.MachineId,"MachineIncluded",1m,null,period.Id);
  Ledger(wallet,f.MachineId,"CompanyWallet",1m,2m);
  Ledger(split,f.MachineId,"MachineIncluded",.4m,null,period.Id);Ledger(split,f.MachineId,"CompanyWallet",.6m,1.2m);
  Ledger(multi1,f.MachineId,"MachineIncluded",.5m,null,period.Id);Ledger(multi2,f.MachineId,"MachineIncluded",.5m,null,period.Id);
  Ledger(outside,f.MachineId,"MachineIncluded",1m,null,period.Id);Ledger(noQuota,noQuotaMachine,"CompanyWallet",1m,2m);
  await db.SaveChangesAsync();db.ChangeTracker.Clear();

  var report=(AdminConsumptionReader.Report)((IValueHttpResult)await AdminConsumptionReader.ReadAsync(
      f.CompanyId,now.AddDays(-1).ToString("O"),now.AddDays(1).ToString("O"),null,db,default)).Value!;
  var machine=report.Machines.Single(row=>row.Id==f.MachineId);var users=machine.Users;
  Assert.AreEqual(1m,users.Single(user=>user.Id==fullUser).Metrics.IncludedQuotaConsumed);
  Assert.AreEqual(0m,users.Single(user=>user.Id==walletUser).Metrics.IncludedQuotaConsumed);
  Assert.AreEqual(.4m,users.Single(user=>user.Id==splitUser).Metrics.IncludedQuotaConsumed);
  Assert.AreEqual(1m,users.Single(user=>user.Id==multiUser).Metrics.IncludedQuotaConsumed);
  Assert.AreEqual(0m,users.Single(user=>user.Id==zeroUser).Metrics.IncludedQuotaConsumed);
  Assert.IsFalse(users.Any(user=>user.Id==outsideUser));
  Assert.AreEqual(2.4m,users.Sum(user=>user.Metrics.IncludedQuotaConsumed));Assert.AreEqual(machine.Used,users.Sum(user=>user.Metrics.IncludedQuotaConsumed));
  var splitMetrics=users.Single(user=>user.Id==splitUser).Metrics;
  Assert.AreEqual(1m,splitMetrics.RealCost);Assert.AreEqual(1.2m,splitMetrics.CommercialCredit);Assert.AreEqual(.6m,splitMetrics.WalletRealCost);
  var withoutQuota=report.Machines.Single(row=>row.Id==noQuotaMachine);
  Assert.AreEqual(0m,withoutQuota.Budget);Assert.AreEqual(0m,withoutQuota.Users.Single(user=>user.Id==noQuotaUser).Metrics.IncludedQuotaConsumed);
 }

 [TestMethod]
 public async Task BatchedReportMatchesLegacyValuationAndUsesConstantQueryCount()
 {
  var counter=new CommandCounter();
  await using var f=new Fixture(counter);await f.Seed();
  await using(var setup=f.Db())
  {
   await setup.Database.ExecuteSqlRawAsync("CREATE TABLE Users (Id TEXT PRIMARY KEY, CompanyId TEXT, EntraObjectId TEXT, Email TEXT, Role TEXT, Status TEXT, CreatedAt TEXT, UpdatedAt TEXT, FirstName TEXT, LastName TEXT, PhoneNumber TEXT)");
   await setup.Database.ExecuteSqlRawAsync("CREATE TABLE UserMachines (UserId TEXT NOT NULL, MachineId TEXT NOT NULL, CreatedAt TEXT NOT NULL, PRIMARY KEY (UserId, MachineId))");
   var original=await setup.AiUsageRecords.SingleAsync();
   setup.AiUsageRecords.Remove(original);
   for(var index=0;index<25;index++)setup.AiUsageRecords.Add(ValuableUsage(f,index));
   setup.AiPricing.Add(new(){Id=Guid.NewGuid(),Provider="Test",Model="test",Currency="USD",
    InputPricePerMillion=1,OutputPricePerMillion=1,EffectiveFromUtc=original.CreatedAtUtc.AddDays(-1)});
   setup.ExchangeRates.Add(new(){Id=Guid.NewGuid(),BaseCurrency="USD",QuoteCurrency="EUR",Rate=2,
    Source="test",EffectiveFromUtc=original.CreatedAtUtc.AddDays(-1)});
   await setup.SaveChangesAsync();
  }

  decimal legacyTotal=0;
  await using(var legacyDb=f.Db())
  {
   var usages=await legacyDb.AiUsageRecords.AsNoTracking().OrderBy(usage=>usage.Id).ToArrayAsync();
   counter.Reset();
   var calculator=new AiCostCalculator(legacyDb);var converter=new AiCostCurrencyConverter(legacyDb);
   foreach(var usage in usages)
   {
    var cost=await calculator.CalculateAsync(usage);
    var converted=await converter.ConvertAsync(cost.RealAiCost!.Value,cost.Currency,usage);
    legacyTotal+=converted.ConvertedAmount!.Value;
   }
  }
  var legacyValuationQueries=counter.Count;

  counter.Reset();
  AdminConsumptionReader.Report report;
  var reportFrom=new DateTime(2026,9,9,0,0,0,DateTimeKind.Utc);
  var reportTo=new DateTime(2026,9,12,0,0,0,DateTimeKind.Utc);
  await using(var reportDb=f.Db())
   report=(AdminConsumptionReader.Report)((IValueHttpResult)await AdminConsumptionReader.ReadAsync(
    f.CompanyId,reportFrom.ToString("O"),reportTo.ToString("O"),null,reportDb,default)).Value!;
  var batchedReportQueries=counter.Count;

  Assert.AreEqual(50,legacyValuationQueries);
  Assert.AreEqual(10,batchedReportQueries);
  Assert.AreEqual(.05m,legacyTotal);
  Assert.AreEqual(legacyTotal,report.Metrics.RealCost);
  Assert.AreEqual(25,report.Metrics.Responses);

  await using(var setup=f.Db())
  {
   for(var index=25;index<50;index++)setup.AiUsageRecords.Add(ValuableUsage(f,index));
   await setup.SaveChangesAsync();
  }
  counter.Reset();
  await using(var reportDb=f.Db())
   report=(AdminConsumptionReader.Report)((IValueHttpResult)await AdminConsumptionReader.ReadAsync(
    f.CompanyId,reportFrom.ToString("O"),reportTo.ToString("O"),null,reportDb,default)).Value!;
  Assert.AreEqual(10,counter.Count);
  Assert.AreEqual(.10m,report.Metrics.RealCost);
  Assert.AreEqual(50,report.Metrics.Responses);
 }

 private static WebApp.Api.Models.Entities.AiUsageRecord ValuableUsage(Fixture fixture,int index)=>new()
 {
  Id=Guid.NewGuid(),MachineId=fixture.MachineId,CompanyId=fixture.CompanyId,
  CreatedAtUtc=new DateTime(2026,9,10,12,0,0,DateTimeKind.Utc).AddMinutes(index),
  UsageType=AiUsageType.ChatResponse,Provider="Test",Model="test",Available=true,Completed=true,
  InputTokens=1000,OutputTokens=0,TotalTokens=1000
 };

 private sealed class CommandCounter:DbCommandInterceptor
 {
  public int Count{get;private set;}
  public void Reset()=>Count=0;
  public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
   CommandEventData eventData,InterceptionResult<DbDataReader> result,CancellationToken cancellationToken=default)
  {
   Count++;
   return base.ReaderExecutingAsync(command,eventData,result,cancellationToken);
  }
 }
}
