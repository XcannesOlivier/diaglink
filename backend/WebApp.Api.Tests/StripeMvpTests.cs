using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebApp.Api.Models.Entities;
using WebApp.Api.Services;
using Fixture = WebApp.Api.Tests.AiCreditConsumptionServiceTests.Fixture;

namespace WebApp.Api.Tests;

public partial class StripeMachineAdditionTests
{
    [TestMethod]
    public async Task MvpCustomerSubscriptionAdditionWebhookCompletedReplay()
    {
        await using var f = new Fixture(); var machine = await Prepare(f);
        await using (var setup = f.Db())
        {
            setup.BillingAccounts.Remove(await setup.BillingAccounts.SingleAsync());
            (await setup.Machines.SingleAsync(m => m.Id == machine)).Status = "inactive";
            await setup.SaveChangesAsync();
        }
        var remote = new Remote { AllowProvisioning = true, PaymentStatus = "open" };
        var billing = new StripeBillingService(f.Options, remote, Settings);
        await using var db = f.Db();
        await StripeAdminEndpoints.WriteAsync(f.CompanyId, false, db, billing, Settings, default);
        await StripeAdminEndpoints.WriteAsync(f.CompanyId, false, db, billing, Settings, default);
        await StripeAdminEndpoints.WriteAsync(f.CompanyId, true, db, billing, Settings, default);
        await StripeAdminEndpoints.WriteAsync(f.CompanyId, true, db, billing, Settings, default);
        Assert.AreEqual(1, remote.CustomersCreated); Assert.AreEqual(1, remote.SubscriptionsCreated);
        Assert.AreEqual(1L, remote.Quantity);
        await using (var activation = f.Db())
        {
            (await activation.Machines.SingleAsync(m => m.Id == machine)).Status = "active";
            await activation.SaveChangesAsync();
        }
        var first = (StripeMachineAdditionResult)((IValueHttpResult)await StripeAdminEndpoints.AddMachineAsync(
            f.CompanyId, machine, db, Service(f, remote), Settings, default)).Value!;
        Assert.AreEqual("AwaitingPayment", first.Status);
        Assert.AreEqual(2L, remote.Quantity);
        var before = await db.StripeMachineAdditions.AsNoTracking().SingleAsync();
        Assert.IsNull(before.PaymentConfirmedAtUtc); Assert.IsNull(before.MachineBillingPeriodId);
        Assert.AreEqual(0, await db.MachineBillingPeriods.CountAsync(p => p.MachineId == machine));
        var repeat = (StripeMachineAdditionResult)((IValueHttpResult)await StripeAdminEndpoints.AddMachineAsync(
            f.CompanyId, machine, db, Service(f, remote), Settings, default)).Value!;
        Assert.AreEqual("AwaitingPayment", repeat.Status); Assert.AreEqual(first.OperationId, repeat.OperationId);
        Assert.AreEqual(before.ActivatedAtUtc, (await db.StripeMachineAdditions.AsNoTracking().SingleAsync()).ActivatedAtUtc);
        Assert.AreEqual(1, remote.InvoiceCount); Assert.AreEqual(1, remote.QuantityWrites);

        remote.PaymentStatus = "paid"; remote.PaidAtUtc = DateTime.UtcNow;
        var body = PaymentEvent(before).ToJsonString(); var webhook = Webhook(f, remote);
        Assert.AreEqual("Completed", (await webhook.HandleAsync(body, Sign(body))).Status);
        Assert.AreEqual("AlreadyCompleted", (await webhook.HandleAsync(body, Sign(body))).Status);
        var calls = remote.Calls;
        var replay = (StripeMachineAdditionResult)((IValueHttpResult)await StripeAdminEndpoints.AddMachineAsync(
            f.CompanyId, machine, db, Service(f, remote), Settings, default)).Value!;
        Assert.AreEqual("AlreadyCompleted", replay.Status); Assert.AreEqual(calls, remote.Calls);
        var list = (StripeAdditionSummary[])((IValueHttpResult)await StripeAdminEndpoints.ReadAdditionsAsync(f.CompanyId, db, default)).Value!;
        Assert.AreEqual(1, list.Length); Assert.AreEqual("Completed", list[0].Stage);
        Assert.AreEqual("evt_local", list[0].ExternalEventId); Assert.IsNotNull(list[0].PaymentConfirmedAtUtc);
        Assert.AreEqual(10m, (await db.MachineBillingPeriods.SingleAsync(p => p.MachineId == machine)).IncludedAiBudgetRealCost);
        Assert.AreEqual(1, await db.MachineBillingPeriods.CountAsync(p => p.MachineId == machine));
        Assert.AreEqual(0, await db.CreditLedger.CountAsync()); Assert.AreEqual(0, await db.CompanyWallets.CountAsync());
    }

    [TestMethod]
    public async Task AdditionEndpointsScopeCompanyAndRejectLiveWithoutEffects()
    {
        await using var f = new Fixture(); var machine = await Prepare(f); var remote = new Remote();
        await using var db = f.Db();
        var wrongCompany = await StripeAdminEndpoints.AddMachineAsync(Guid.NewGuid(), machine, db, Service(f, remote), Settings, default);
        Assert.AreEqual(404, ((IStatusCodeHttpResult)wrongCompany).StatusCode);
        var live = new StripeBillingOptions { Enabled = true, AllowLive = true, SecretKey = "sk_live_unused", PriceId = "price_local" };
        Assert.AreEqual(409, ((IStatusCodeHttpResult)await StripeAdminEndpoints.AddMachineAsync(f.CompanyId, machine, db,
            Service(f, remote), live, default)).StatusCode);
        Assert.AreEqual(0, remote.Calls); Assert.AreEqual(0, await db.StripeMachineAdditions.CountAsync());
    }
}
