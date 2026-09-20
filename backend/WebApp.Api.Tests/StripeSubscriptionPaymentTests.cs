using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebApp.Api.Models.Entities;
using WebApp.Api.Services;
using Fixture = WebApp.Api.Tests.AiCreditConsumptionServiceTests.Fixture;

namespace WebApp.Api.Tests;

[TestClass]
public class StripeSubscriptionPaymentTests
{
    private static readonly DateTime Start = DateTime.UtcNow.Date.AddDays(-1);
    private static readonly DateTime End = Start.AddMonths(1);
    private static StripeBillingOptions Settings => new() { Enabled=true, SecretKey="sk_test_local", PriceId="price_local", WebhookSecret="whsec_test" };
    private sealed class Gateway : IStripeSubscriptionPaymentGateway
    {
        public SubscriptionInvoice Invoice = new("in_test", "sub_test", "paid", "subscription_create", Start, End,
            1, 2990, DateTime.UtcNow.AddMinutes(-1), "[\"inpay_test\"]", null, "active");
        public Task<SubscriptionInvoice> ReadForReconciliationAsync(BillingAccount a, SubscriptionReconciliationRequest r, CancellationToken ct) => Task.FromResult(Invoice);
        public Task<SubscriptionInvoice> ReadAsync(BillingAccount a, string? id, CancellationToken ct) => Task.FromResult(Invoice);
    }
    private static async Task Prepare(Fixture f, bool renewal = false)
    {
        await f.Seed();
        await using var db = f.Db();
        (await db.Companies.SingleAsync()).Status = "active";
        (await db.Machines.SingleAsync()).Status = "active";
        var period = await db.MachineBillingPeriods.SingleAsync();
        if (renewal) { period.PeriodStartUtc=Start.AddMonths(-1); period.PeriodEndUtc=Start; period.IncludedAiUsedRealCost=3m; }
        else db.MachineBillingPeriods.Remove(period);
        db.BillingAccounts.Add(new() { Id=Guid.NewGuid(), CompanyId=f.CompanyId, StripeCustomerId="cus_test",
            StripeSubscriptionId="sub_test", SubscriptionStatus="incomplete", CurrentPeriodStartUtc=renewal ? Start.AddMonths(-1) : Start,
            CurrentPeriodEndUtc=renewal ? Start : End });
        await db.SaveChangesAsync();
    }
    private static StripeSubscriptionPaymentService Service(Fixture f, Gateway g) => new(f.Options, g, new(f.Options));
    private static Task<string> Process(Fixture f, Gateway g, string evt="evt_test") => Service(f,g).ProcessAsync("in_test",evt,"sub_test",default);

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task FutureCycleRequiresVerifiedClock(bool clock)
    {
        await using var f=new Fixture(); await Prepare(f); var g=new Gateway();
        var start=DateTime.UtcNow.AddMonths(2);
        g.Invoice=g.Invoice with {StartUtc=start,EndUtc=start.AddMonths(1),PaidAtUtc=start.AddHours(1),
            VerifiedTestClockId=clock?"clock_test":null,VerifiedTestClockUtc=clock?start.AddHours(2):null};
        Assert.AreEqual(clock?"Completed":"ReconciliationRequired",await Process(f,g));
        if(clock) Assert.AreEqual("AlreadyCompleted",await Process(f,g));
        await using var db=f.Db();Assert.AreEqual(clock?1:0,await db.MachineBillingPeriods.CountAsync());
    }

    [TestMethod]
    public async Task HistoricalClockReconciliationPreservesUnpaidCycleAndResumesAfterCrash()
    {
        await using var f=new Fixture();await Prepare(f,true);var g=new Gateway();
        g.Invoice=g.Invoice with {BillingReason="subscription_cycle",VerifiedTestClockId="clock_test",VerifiedTestClockUtc=End.AddDays(1),SubscriptionStatus="past_due"};
        await using(var db=f.Db())
        {
            var a=await db.BillingAccounts.SingleAsync();a.SubscriptionStatus="past_due";a.CurrentPeriodStartUtc=End;a.CurrentPeriodEndUtc=End.AddMonths(1);
            a.LatestInvoiceId="in_unpaid";a.LatestInvoiceStatus="open";a.AmountRemainingCents=2990;await db.SaveChangesAsync();
            await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER fail_completion BEFORE UPDATE ON StripeSubscriptionPayments BEGIN SELECT RAISE(ABORT,'crash'); END");
        }
        var request=new SubscriptionReconciliationRequest(f.CompanyId,f.MachineId,"sub_test","in_test","number","evt_test",Start,End,2990);
        await Assert.ThrowsExactlyAsync<DbUpdateException>(()=>Service(f,g).ReconcileTestClockPaymentAsync(request));
        await using(var db=f.Db())await db.Database.ExecuteSqlRawAsync("DROP TRIGGER fail_completion");
        Assert.AreEqual("Completed",await Service(f,g).ReconcileTestClockPaymentAsync(request));
        Assert.AreEqual("AlreadyCompleted",await Service(f,g).ReconcileTestClockPaymentAsync(request));
        Assert.AreEqual("ReconciliationRequired",await Service(f,g).ReconcileTestClockPaymentAsync(request with {AmountPaidCents=5980}));
        await using(var db=f.Db())
        {
            Assert.AreEqual(2,await db.MachineBillingPeriods.CountAsync());
            Assert.AreEqual(1,await db.StripeSubscriptionPayments.CountAsync());
            var a=await db.BillingAccounts.SingleAsync();Assert.AreEqual("past_due",a.SubscriptionStatus);
            Assert.AreEqual(End,a.CurrentPeriodStartUtc);Assert.AreEqual(End.AddMonths(1),a.CurrentPeriodEndUtc);
            Assert.AreEqual("in_unpaid",a.LatestInvoiceId);Assert.AreEqual(2990,a.AmountRemainingCents);
            Assert.AreEqual(0,await db.CreditLedger.CountAsync());Assert.AreEqual(0,await db.CompanyWallets.CountAsync());
        }
    }
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task PaidInitialAndRenewalCreateFullBudgetOnce(bool renewal)
    {
        await using var f=new Fixture(); await Prepare(f,renewal); var g=new Gateway();
        if (renewal) g.Invoice=g.Invoice with { BillingReason="subscription_cycle" };
        Assert.AreEqual("Completed", await Process(f,g));
        Assert.AreEqual("AlreadyCompleted", await Process(f,g));
        Assert.AreEqual("AlreadyCompleted", await Process(f,g,"evt_other"));
        await using var db=f.Db(); var periods=await db.MachineBillingPeriods.OrderBy(p=>p.PeriodStartUtc).ToListAsync();
        Assert.HasCount(renewal?2:1,periods); Assert.AreEqual(10m,periods[^1].IncludedAiBudgetRealCost);
        Assert.AreEqual(0m,periods[^1].IncludedAiUsedRealCost);
        if (renewal) { Assert.AreEqual("Closed",periods[0].Status); Assert.AreEqual(3m,periods[0].IncludedAiUsedRealCost); }
        var account=await db.BillingAccounts.SingleAsync(); Assert.AreEqual("active",account.SubscriptionStatus);
        Assert.AreEqual(Start,account.CurrentPeriodStartUtc); Assert.AreEqual(End,account.CurrentPeriodEndUtc);
        Assert.AreEqual(1,await db.StripeSubscriptionPayments.CountAsync());
        Assert.AreEqual(0,await db.CreditLedger.CountAsync()); Assert.AreEqual(0,await db.CompanyWallets.CountAsync());
    }

    [TestMethod]
    [DataRow("open")]
    [DataRow("processing")]
    [DataRow("failed")]
    [DataRow("paid")]
    public async Task NoVerifiedPaymentNoBudget(string status)
    {
        await using var f=new Fixture(); await Prepare(f); var g=new Gateway();
        g.Invoice=g.Invoice with { Status=status,PaymentReference=null,PaidAtUtc=null };
        Assert.AreEqual("AwaitingPayment",await Process(f,g));
        await using var db=f.Db(); Assert.AreEqual(0,await db.MachineBillingPeriods.CountAsync());
        Assert.AreEqual(0,await db.StripeSubscriptionPayments.CountAsync());
    }

    [TestMethod]
    public async Task CrashAfterPeriodsBeforeCompletionIsResumable()
    {
        await using var f=new Fixture(); await Prepare(f); var g=new Gateway();
        await using (var db=f.Db()) await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER fail_completion BEFORE UPDATE ON StripeSubscriptionPayments BEGIN SELECT RAISE(ABORT,'crash'); END");
        await Assert.ThrowsExactlyAsync<DbUpdateException>(()=>Process(f,g));
        await using (var db=f.Db())
        {
            Assert.AreEqual(1,await db.MachineBillingPeriods.CountAsync());
            Assert.IsNull((await db.StripeSubscriptionPayments.SingleAsync()).CompletedAtUtc);
            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER fail_completion");
        }
        Assert.AreEqual("Completed",await Process(f,g));
        await using var after=f.Db(); Assert.AreEqual(1,await after.MachineBillingPeriods.CountAsync());
    }

    [TestMethod]
    public async Task PartialMachineProgressResumesFrozenRecipients()
    {
        await using var f=new Fixture(); await Prepare(f); var g=new Gateway();
        var second=Guid.NewGuid(); g.Invoice=g.Invoice with { Quantity=2,AmountPaidCents=5980 };
        await using (var db=f.Db())
        {
            db.Machines.Add(new() {Id=second,CompanyId=f.CompanyId,Name="second",Status="active"}); await db.SaveChangesAsync();
            await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER fail_second BEFORE INSERT ON MachineBillingPeriods WHEN (SELECT COUNT(*) FROM MachineBillingPeriods)=1 BEGIN SELECT RAISE(ABORT,'crash'); END");
        }
        await Assert.ThrowsExactlyAsync<DbUpdateException>(()=>Process(f,g));
        await using (var db=f.Db())
        { Assert.AreEqual(1,await db.MachineBillingPeriods.CountAsync()); await db.Database.ExecuteSqlRawAsync("DROP TRIGGER fail_second"); }
        Assert.AreEqual("Completed",await Process(f,g));
        await using var after=f.Db(); Assert.AreEqual(2,await after.MachineBillingPeriods.CountAsync());
    }

    [TestMethod]
    public async Task LateInvoiceAndQuantityMismatchGrantNothing()
    {
        await using var f=new Fixture(); await Prepare(f); var g=new Gateway();
        g.Invoice=g.Invoice with {StartUtc=Start.AddMonths(-2),EndUtc=Start.AddMonths(-1)};
        Assert.AreEqual("ReconciliationRequired",await Process(f,g));
        g.Invoice=g.Invoice with {StartUtc=Start,EndUtc=End,Quantity=2};
        await Assert.ThrowsExactlyAsync<SubscriptionReconciliationException>(()=>Process(f,g));
        await using var db=f.Db(); Assert.AreEqual(0,await db.MachineBillingPeriods.CountAsync());
    }

    [TestMethod]
    public async Task SignedWebhookDispatchesSubscriptionAndRejectsTampering()
    {
        await using var f=new Fixture(); await Prepare(f); var g=new Gateway();
        var webhook=new StripeSubscriptionWebhook(Settings,Service(f,g),null!,NullLogger<StripeSubscriptionWebhook>.Instance);
        var body=JsonSerializer.Serialize(new {id="evt_test",@object="event",type="invoice.payment_succeeded",livemode=false,
            api_version=Stripe.StripeConfiguration.ApiVersion,data=new {@object=new {id="in_test",@object="invoice",billing_reason="subscription_create",
                parent=new {type="subscription_details",subscription_details=new {subscription="sub_test"}}}}});
        var t=DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var hash=Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes("whsec_test"),Encoding.UTF8.GetBytes($"{t}.{body}"))).ToLowerInvariant();
        var signature=$"t={t},v1={hash}";
        Assert.AreEqual(400,(await webhook.HandleAsync(body,"invalid")).HttpStatus);
        Assert.AreEqual("Completed",(await webhook.HandleAsync(body,signature)).Status);
        Assert.AreEqual("AlreadyCompleted",(await webhook.HandleAsync(body,signature)).Status);
    }

    [TestMethod]
    public async Task ConcurrentDeliveriesAndRetryNeverDuplicatePeriods()
    {
        await using var f=new Fixture(); await Prepare(f); var g=new Gateway();
        async Task Attempt(string id)
        {
            try { await Process(f,g,id); }
            catch (Microsoft.Data.Sqlite.SqliteException) { /* Simulated provider lock: webhook retries. */ }
            catch (DbUpdateException) { /* Concurrent unique insertion: webhook retries. */ }
        }
        await Task.WhenAll(Task.Run(()=>Attempt("evt_one")),Task.Run(()=>Attempt("evt_two")));
        var result=await Process(f,g);
        Assert.IsTrue(result is "Completed" or "AlreadyCompleted");
        await using var db=f.Db(); Assert.AreEqual(1,await db.MachineBillingPeriods.CountAsync());
        Assert.AreEqual(1,await db.StripeSubscriptionPayments.CountAsync());
    }

    [TestMethod]
    public async Task PeriodCreationChecksCompanyInsideItsTransaction()
    {
        await using var f=new Fixture(); await Prepare(f);
        var result=await new MachineBillingPeriodService(f.Options).CreateInitialPeriodAsync(f.MachineId,Start,Start,End,
            expectedCompanyId:Guid.NewGuid());
        Assert.AreEqual("MachineCompanyMismatch",result.Status);
        await using var db=f.Db(); Assert.AreEqual(0,await db.MachineBillingPeriods.CountAsync());
    }
}

