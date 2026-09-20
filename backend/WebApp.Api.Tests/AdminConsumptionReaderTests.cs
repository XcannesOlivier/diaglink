using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebApp.Api.Services;
using WebApp.Api.Models;
using Fixture=WebApp.Api.Tests.AiCreditConsumptionServiceTests.Fixture;
namespace WebApp.Api.Tests;
[TestClass]
public class AdminConsumptionReaderTests
{
 [TestMethod]
 public async Task IsolatesCompanyMachineUserAndSeparatesLedgerFromProviderCost()
 {
  await using var f=new Fixture();await f.Seed();await using var db=f.Db();
  await db.Database.ExecuteSqlRawAsync("CREATE TABLE Users (Id TEXT PRIMARY KEY, CompanyId TEXT, EntraObjectId TEXT, Email TEXT, Role TEXT, Status TEXT, CreatedAt TEXT, UpdatedAt TEXT, FirstName TEXT, LastName TEXT, PhoneNumber TEXT)");
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
}
