using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebApp.Api.Models.Entities;
using WebApp.Api.Services;
using Fixture=WebApp.Api.Tests.AiCreditConsumptionServiceTests.Fixture;
namespace WebApp.Api.Tests;

[TestClass]
public class StripeLifecycleTests
{
    [TestMethod]
    [DataRow("invoice.payment_failed")]
    [DataRow("invoice.finalization_failed")]
    [DataRow("customer.subscription.updated")]
    [DataRow("customer.subscription.deleted")]
    [DataRow("invoice.upcoming")]
    public async Task LifecycleWebhookRequiresSignatureAndUsesCurrentProviderState(string type)
    {
        await using var f=new Fixture();await Prepare(f);var remote=new Remote();remote.State=remote.State with{Status="past_due"};
        var settings=new StripeBillingOptions{Enabled=true,SecretKey="sk_test_local",PriceId="price_local",WebhookSecret="whsec_local"};
        var webhook=new StripeSubscriptionWebhook(settings,null!,null!,Microsoft.Extensions.Logging.Abstractions.NullLogger<StripeSubscriptionWebhook>.Instance,new(f.Options,remote));
        var body=System.Text.Json.JsonSerializer.Serialize(new{id="evt_signed_life",@object="event",type,livemode=false,api_version=Stripe.StripeConfiguration.ApiVersion,
            data=new{@object=new{id=type.StartsWith("customer.")?"sub_test":"in_test",@object=type.StartsWith("customer.")?"subscription":"invoice",
                parent=new{type="subscription_details",subscription_details=new{subscription="sub_test"}}}}});
        var t=DateTimeOffset.UtcNow.ToUnixTimeSeconds();var hash=Convert.ToHexString(System.Security.Cryptography.HMACSHA256.HashData(System.Text.Encoding.UTF8.GetBytes("whsec_local"),System.Text.Encoding.UTF8.GetBytes($"{t}.{body}"))).ToLowerInvariant();
        Assert.AreEqual(400,(await webhook.HandleAsync(body,"bad")).HttpStatus);
        Assert.AreEqual("Synchronized",(await webhook.HandleAsync(body,$"t={t},v1={hash}")).Status);
        Assert.AreEqual("AlreadyProcessed",(await webhook.HandleAsync(body,$"t={t},v1={hash}")).Status);
        await using var db=f.Db();Assert.AreEqual("past_due",(await db.BillingAccounts.SingleAsync()).SubscriptionStatus);
        Assert.AreEqual(1,await db.MachineBillingPeriods.CountAsync());
    }
    private sealed class Remote:IStripeLifecycleGateway
    {
        public StripeLifecycleSnapshot State=new("active",DateTime.UtcNow.AddDays(-1),DateTime.UtcNow.AddDays(29),false,"in_renew","open",2990,"si_test",1);
        public int Writes;
        public Task<StripeLifecycleSnapshot> ReadAsync(BillingAccount account,CancellationToken ct)=>Task.FromResult(State);
        public Task SetQuantityAsync(BillingAccount a,StripeLifecycleSnapshot s,int quantity,string key,CancellationToken ct)
        {Writes++;State=State with{Quantity=quantity};return Task.CompletedTask;}
    }
    [TestMethod]
    public async Task ScheduledCancellationPreservesRightsButDoesNotPrepareAnotherRenewal()
    {
        await using var f=new Fixture();await Prepare(f);var remote=new Remote();remote.State=remote.State with{CancelAtPeriodEnd=true};
        var service=new StripeLifecycleService(f.Options,remote);
        await service.ProcessAsync("sub_test","evt_cancel","customer.subscription.updated",default);
        await service.ProcessAsync("sub_test","evt_upcoming_cancel","invoice.upcoming",default);
        await using var db=f.Db();Assert.IsTrue((await db.BillingAccounts.SingleAsync()).CancelAtPeriodEnd);
        Assert.AreEqual(0,remote.Writes);Assert.AreEqual(1,await db.MachineBillingPeriods.CountAsync());
        Assert.IsTrue(await MachineEntitlements.Eligible(db,DateTime.UtcNow).AnyAsync());
        Assert.IsFalse(await MachineEntitlements.Eligible(db,DateTime.UtcNow.AddDays(40)).AnyAsync());
    }
    private static async Task Prepare(Fixture f)
    {
        await f.Seed();await using var db=f.Db();
        (await db.Companies.SingleAsync()).Status="active";(await db.Machines.SingleAsync()).Status="active";
        var p=await db.MachineBillingPeriods.SingleAsync();p.PeriodStartUtc=DateTime.UtcNow.AddDays(-1);p.PeriodEndUtc=DateTime.UtcNow.AddDays(29);p.IncludedAiUsedRealCost=2m;
        db.BillingAccounts.Add(new(){Id=Guid.NewGuid(),CompanyId=f.CompanyId,StripeCustomerId="cus_test",StripeSubscriptionId="sub_test",SubscriptionStatus="active"});
        await db.SaveChangesAsync();
    }
    [TestMethod]
    [DataRow("past_due")]
    [DataRow("unpaid")]
    [DataRow("canceled")]
    public async Task StatusChangesPreservePaidPeriodsAndReplayIsIdempotent(string status)
    {
        await using var f=new Fixture();await Prepare(f);var remote=new Remote();remote.State=remote.State with{Status=status};
        var service=new StripeLifecycleService(f.Options,remote);
        Assert.AreEqual("Synchronized",await service.ProcessAsync("sub_test","evt_life","invoice.payment_failed",default));
        Assert.AreEqual("AlreadyProcessed",await service.ProcessAsync("sub_test","evt_life","invoice.payment_failed",default));
        await using var db=f.Db();Assert.AreEqual(status,(await db.BillingAccounts.SingleAsync()).SubscriptionStatus);
        var period=await db.MachineBillingPeriods.SingleAsync();Assert.AreEqual(10m,period.IncludedAiBudgetRealCost);Assert.AreEqual(2m,period.IncludedAiUsedRealCost);
        Assert.AreEqual(1,await db.StripeLifecycleEvents.CountAsync());Assert.AreEqual(0,await db.CreditLedger.CountAsync());
        Assert.IsTrue(await MachineEntitlements.Eligible(db,DateTime.UtcNow).AnyAsync());
        Assert.IsFalse(await MachineEntitlements.Eligible(db,DateTime.UtcNow.AddDays(40)).AnyAsync());
    }
    [TestMethod]
    public async Task LateEventsRereadCurrentStripeStatusInsteadOfRestoringOldPayload()
    {
        await using var f=new Fixture();await Prepare(f);var remote=new Remote();remote.State=remote.State with{Status="canceled"};
        var service=new StripeLifecycleService(f.Options,remote);
        await service.ProcessAsync("sub_test","evt_delete","customer.subscription.deleted",default);
        await service.ProcessAsync("sub_test","evt_old_failure","invoice.payment_failed",default);
        await using var db=f.Db();Assert.AreEqual("canceled",(await db.BillingAccounts.SingleAsync()).SubscriptionStatus);
    }
    [TestMethod]
    public async Task DeactivationAndCoveredReactivationChangeNextQuantityWithoutNewBudget()
    {
        await using var f=new Fixture();await Prepare(f);var remote=new Remote();var service=new StripeMachineStatusService(f.Options,remote,null!);
        var id=Guid.NewGuid();await service.SetActiveAsync(f.CompanyId,f.MachineId,false,id,default);
        Assert.AreEqual(0L,remote.State.Quantity);
        await service.SetActiveAsync(f.CompanyId,f.MachineId,false,id,default);Assert.AreEqual(1,remote.Writes);
        await using(var db=f.Db()) {Assert.AreEqual("inactive",(await db.Machines.SingleAsync()).Status);Assert.IsTrue(await MachineEntitlements.Eligible(db,DateTime.UtcNow).AnyAsync());}
        await service.SetActiveAsync(f.CompanyId,f.MachineId,true,Guid.NewGuid(),default);Assert.AreEqual(1L,remote.State.Quantity);
        await using var after=f.Db();Assert.AreEqual(1,await after.MachineBillingPeriods.CountAsync());Assert.AreEqual(0,await after.StripeMachineAdditions.CountAsync());
    }
    [TestMethod]
    public async Task UpcomingInvoiceUsesActiveQuantityIncludingZeroWithoutGrantingRights()
    {
        await using var f=new Fixture();await Prepare(f);await using(var db=f.Db()){(await db.Machines.SingleAsync()).Status="inactive";await db.SaveChangesAsync();}
        var remote=new Remote();var service=new StripeLifecycleService(f.Options,remote);
        await service.ProcessAsync("sub_test","evt_upcoming","invoice.upcoming",default);Assert.AreEqual(0L,remote.State.Quantity);
        await service.ProcessAsync("sub_test","evt_upcoming","invoice.upcoming",default);Assert.AreEqual(1,remote.Writes);
        await using var after=f.Db();Assert.AreEqual(1,await after.MachineBillingPeriods.CountAsync());
    }
    [TestMethod]
    public async Task FailedCommitCanRetrySameLifecycleEvent()
    {
        await using var f=new Fixture();await Prepare(f);var remote=new Remote();var service=new StripeLifecycleService(f.Options,remote);
        await using(var db=f.Db())await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER fail_lifecycle BEFORE INSERT ON StripeLifecycleEvents BEGIN SELECT RAISE(ABORT,'crash'); END");
        await Assert.ThrowsExactlyAsync<DbUpdateException>(()=>service.ProcessAsync("sub_test","evt_retry","invoice.payment_failed",default));
        await using(var db=f.Db()){Assert.AreEqual("active",(await db.BillingAccounts.SingleAsync()).SubscriptionStatus);await db.Database.ExecuteSqlRawAsync("DROP TRIGGER fail_lifecycle");}
        Assert.AreEqual("Synchronized",await service.ProcessAsync("sub_test","evt_retry","invoice.payment_failed",default));
    }
}
