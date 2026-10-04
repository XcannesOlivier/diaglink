using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebApp.Api.Services;
using WebApp.Api.Models.Entities;
using Fixture=WebApp.Api.Tests.AiCreditConsumptionServiceTests.Fixture;
namespace WebApp.Api.Tests;
[TestClass]
public class GlobalFinancePeriodTests
{
 [TestMethod]
 public async Task CountsHistoricalActivityAndOverlappingPeriodsNotCurrentStatus()
 {
  await using var f=new Fixture();await f.Seed();await using var db=f.Db();
  var start=new DateTime(2026,9,1,0,0,0,DateTimeKind.Utc);var end=start.AddMonths(1);
  var p=await db.MachineBillingPeriods.SingleAsync();p.PeriodStartUtc=start.AddDays(-10);p.PeriodEndUtc=start.AddDays(1);p.Status="Closed";
  (await db.Machines.SingleAsync()).Status="inactive";
  var other=Guid.NewGuid();db.Companies.Add(new(){Id=other,Name="Currently active but no history",Status="active"});
  db.Machines.Add(new(){Id=Guid.NewGuid(),CompanyId=other,Name="No period",Status="active"});
  db.AiUsageRecords.RemoveRange(db.AiUsageRecords);
  var user=Guid.NewGuid();
  foreach(var date in new[]{start,start.AddDays(2),end})db.AiUsageRecords.Add(new(){Id=Guid.NewGuid(),CompanyId=f.CompanyId,UserId=date==end?Guid.NewGuid():user,CreatedAtUtc=date});
  await db.SaveChangesAsync();
  var result=(AdminFinanceReader.PeriodFinance)((IValueHttpResult)await AdminFinanceReader.PeriodFinanceAsync(start,end,db,default)).Value!;
  Assert.AreEqual(1,result.Companies);Assert.AreEqual(1,result.Machines);Assert.AreEqual(1,result.Users);
  var empty=(AdminFinanceReader.PeriodFinance)((IValueHttpResult)await AdminFinanceReader.PeriodFinanceAsync(end.AddDays(1),end.AddMonths(1),db,default)).Value!;
  Assert.AreEqual(0,empty.Companies);Assert.AreEqual(0,empty.Machines);Assert.AreEqual(0,empty.Users);
 }
 [TestMethod]
 public async Task UsesConfirmedPaymentDatesAndActualGrantedPeriodsWithExclusiveEnd()
 {
  await using var f=new Fixture();await f.Seed();await using var db=f.Db();
  var start=new DateTime(2026,9,1,0,0,0,DateTimeKind.Utc);var end=start.AddMonths(1);
  var period=await db.MachineBillingPeriods.SingleAsync();period.PeriodStartUtc=start;period.PeriodEndUtc=end;period.IncludedAiBudgetRealCost=7.5m;
  foreach(var offset in new[]{-1,0,30}){
   db.StripeSubscriptionPayments.Add(new(){Id=Guid.NewGuid(),CompanyId=f.CompanyId,StripeSubscriptionId=$"sub_{offset}",StripeInvoiceId=$"in_{offset}",ExternalEventId=$"evt_{offset}",BillingReason="subscription_cycle",PeriodStartUtc=start,PeriodEndUtc=end,PaymentConfirmedAtUtc=start.AddDays(offset),AmountPaidCents=2990,PaymentReference="verified",MachineIdsJson=System.Text.Json.JsonSerializer.Serialize(new[]{f.MachineId})});
  }
  // A confirmed payment with no granted machine period must not invent a 10 EUR budget.
  db.StripeSubscriptionPayments.Add(new(){Id=Guid.NewGuid(),CompanyId=f.CompanyId,StripeSubscriptionId="sub_partial",StripeInvoiceId="in_partial",ExternalEventId="evt_partial",BillingReason="subscription_create",PeriodStartUtc=start,PeriodEndUtc=end,PaymentConfirmedAtUtc=start.AddDays(1),AmountPaidCents=2990,PaymentReference="verified",MachineIdsJson="[]"});
  foreach(var stage in new[]{StripeWalletTopUpStage.AwaitingPayment,StripeWalletTopUpStage.PaymentConfirmed,StripeWalletTopUpStage.WalletCredited,StripeWalletTopUpStage.Completed}){
   var id=Guid.NewGuid();bool paid=stage>=StripeWalletTopUpStage.PaymentConfirmed,credited=stage>=StripeWalletTopUpStage.WalletCredited;
   db.StripeWalletTopUps.Add(new(){Id=id,CompanyId=f.CompanyId,StripeCustomerId="cus_test",ReturnUrl="https://example.com",AmountCents=2000,Stage=stage,StripeSessionId=$"cs_{stage}",StripePaymentIntentId=paid?$"pi_{stage}":null,ExternalEventId=paid?$"evt_topup_{stage}":null,PaymentConfirmedAtUtc=paid?start:null,LedgerEntryId=credited?id:null,CompletedAtUtc=stage==StripeWalletTopUpStage.Completed?start:null});
   if(credited)db.CreditLedger.Add(new(){Id=id,CompanyId=f.CompanyId,EntryType="TopUp",BucketType="CompanyWallet",Currency="EUR",CommercialCreditAmount=20,CreatedAtUtc=end.AddDays(2)});
  }
  await db.SaveChangesAsync();
  var result=(AdminFinanceReader.PeriodFinance)((IValueHttpResult)await AdminFinanceReader.PeriodFinanceAsync(start,end,db,default)).Value!;
  Assert.AreEqual(59.8m,result.SubscriptionsPaidEur);Assert.AreEqual(7.5m,result.IncludedCreditGrantedEur);Assert.AreEqual(40m,result.TopUpsAddedEur);
  Assert.AreEqual(400,((IStatusCodeHttpResult)await AdminFinanceReader.PeriodFinanceAsync(end,start,db,default)).StatusCode);
  Assert.AreEqual(2,await db.CreditLedger.CountAsync());Assert.AreEqual(0,await db.CompanyWallets.CountAsync());
 }

 [TestMethod]
 public async Task IncludesInitialMachinePaymentAndGrantedCreditOnFirstDayOfMonth()
 {
  await using var f=new Fixture();await f.Seed();await using var db=f.Db();
  var start=new DateTime(2026,10,1,0,0,0,DateTimeKind.Utc);var end=start.AddDays(1);
  var company=await db.Companies.SingleAsync(c=>c.Id==f.CompanyId);company.Name="Develon";
  var machine=await db.Machines.SingleAsync(m=>m.Id==f.MachineId);machine.Status="active";
  var period=await db.MachineBillingPeriods.SingleAsync(p=>p.MachineId==f.MachineId);
  period.PeriodStartUtc=start.AddHours(8);period.PeriodEndUtc=start.AddMonths(1);
  period.IncludedAiBudgetRealCost=10m;period.IncludedAiUsedRealCost=0m;period.Status="Active";
  db.BillingAccounts.Add(new(){Id=Guid.NewGuid(),CompanyId=f.CompanyId,StripeCustomerId="cus_develon",
   StripeSubscriptionId="sub_develon",SubscriptionStatus="active",CurrentPeriodStartUtc=start,
   CurrentPeriodEndUtc=period.PeriodEndUtc,CreatedAtUtc=start,UpdatedAtUtc=start});
  db.StripeSubscriptionPayments.RemoveRange(db.StripeSubscriptionPayments);
  await db.SaveChangesAsync();
  var payment=new WebApp.Api.Models.Entities.MachineRequestPayment
  {
   Id=Guid.NewGuid(),Status=MachineRequestPaymentStatus.Captured,EstimatedTotalPages=1,AmountCents=1195,
   Currency="EUR",StripePaymentIntentId="pi_develon",AuthorizationEventId="evt_develon",
   CreatedAtUtc=start.AddHours(7),UpdatedAtUtc=start.AddHours(8),AuthorizedAtUtc=start.AddHours(7),CapturedAtUtc=start.AddHours(8),
   ActivatedAtUtc=period.PeriodStartUtc,FirstPeriodEndUtc=period.PeriodEndUtc,ServiceAmountCents=195,
   FinalCaptureAmountCents=1195,CompanyId=f.CompanyId,MachineId=f.MachineId,
   ProvisioningStage=MachineRequestProvisioningStage.Completed,ProvisioningCompletedAtUtc=start.AddHours(9),RowVersion=[1]
  };
  await db.Database.ExecuteSqlInterpolatedAsync($@"INSERT INTO MachineRequestPayments
   (Id,Status,EstimatedTotalPages,AmountCents,Currency,StripePaymentIntentId,AuthorizationEventId,
    CreatedAtUtc,UpdatedAtUtc,AuthorizedAtUtc,CapturedAtUtc,
    ActivatedAtUtc,FirstPeriodEndUtc,ServiceAmountCents,FinalCaptureAmountCents,CompanyId,MachineId,ProvisioningStage,ProvisioningCompletedAtUtc,RowVersion)
   VALUES ({payment.Id},{(int)payment.Status},{payment.EstimatedTotalPages},{payment.AmountCents},{payment.Currency},
    {payment.StripePaymentIntentId},{payment.AuthorizationEventId},{payment.CreatedAtUtc},{payment.UpdatedAtUtc},
    {payment.AuthorizedAtUtc},{payment.CapturedAtUtc},{payment.ActivatedAtUtc},{payment.FirstPeriodEndUtc},
    {payment.ServiceAmountCents},{payment.FinalCaptureAmountCents},{payment.CompanyId},{payment.MachineId},
    {(int)payment.ProvisioningStage},{payment.ProvisioningCompletedAtUtc},{payment.RowVersion})");

  var result=(AdminFinanceReader.PeriodFinance)((IValueHttpResult)await AdminFinanceReader.PeriodFinanceAsync(start,end,db,default)).Value!;

  Assert.AreEqual(1.95m,result.SubscriptionsPaidEur);
  Assert.AreEqual(10m,result.IncludedCreditGrantedEur);
  Assert.AreEqual(0m,result.TopUpsAddedEur);
  Assert.AreEqual(1,result.Companies);Assert.AreEqual(1,result.Machines);
 }
}
