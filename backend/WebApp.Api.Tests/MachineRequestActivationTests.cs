using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebApp.Api.Models;
using WebApp.Api.Models.Entities;
using WebApp.Api.Services;
using PaymentEntity = WebApp.Api.Models.Entities.MachineRequestPayment;
using Fixture = WebApp.Api.Tests.AiCreditConsumptionServiceTests.Fixture;

namespace WebApp.Api.Tests;

[TestClass]
public sealed class MachineRequestActivationTests
{
    [TestMethod]
    public async Task AdditionalMachineCreatesFullTenEuroPeriodFromFrozenDatesAndCompletesReadOnly()
    {
        await using var f = new Fixture(); await f.Seed();
        var activated = new DateTime(2026, 8, 31, 20, 30, 0, DateTimeKind.Utc);
        var periodEnd = new DateTime(2026, 8, 31, 22, 0, 0, DateTimeKind.Utc);
        var payment = await Seed(f, MachineRequestProvisioningStage.SubscriptionCreated,
            MachineRequestKind.AdditionalMachine, activated, periodEnd);
        var stripe = new ReadOnlyStripe();
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 8, 31, 21, 0, 0, TimeSpan.Zero));
        await using var before = f.Db(); var wallets = await before.CompanyWallets.CountAsync();
        var ledger = await before.CreditLedger.CountAsync();
        await using var db = f.Db();

        var result = await new MachineRequestActivationService(f.Options, new MachineRequestPaymentStore(db),
            new MachineBillingPeriodService(f.Options), stripe, clock).ActivateAsync(payment.Id, default);

        Assert.AreEqual(MachineRequestProvisioningStage.Completed, result!.ProvisioningStage);
        Assert.AreEqual(clock.GetUtcNow().UtcDateTime, result.ProvisioningCompletedAtUtc);
        Assert.AreEqual(1, stripe.AdditionalReads); Assert.AreEqual(0, stripe.InitialReads);
        await using var check = f.Db(); var period = await check.MachineBillingPeriods.SingleAsync();
        Assert.AreEqual(activated, period.PeriodStartUtc);
        Assert.AreEqual(payment.FirstPeriodEndUtc, period.PeriodEndUtc);
        Assert.AreEqual(10m, period.IncludedAiBudgetRealCost);
        Assert.AreEqual(0m, period.IncludedAiUsedRealCost);
        Assert.AreEqual(wallets, await check.CompanyWallets.CountAsync());
        Assert.AreEqual(ledger, await check.CreditLedger.CountAsync());
        Assert.AreEqual(1, await check.MachineBillingPeriods.CountAsync());
    }

    [TestMethod]
    public async Task CreatesExactInitialPeriodAndCompletesWithoutWalletCredit()
    {
        await using var f = new Fixture(); await f.Seed(); var payment = await Seed(f,
            MachineRequestProvisioningStage.SubscriptionCreated, MachineRequestKind.AdditionalMachine);
        await using var before = f.Db(); var ledger = await before.CreditLedger.CountAsync(); var wallets = await before.CompanyWallets.CountAsync();
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 9, 22, 13, 0, 0, TimeSpan.Zero));
        await using var db = f.Db(); var service = new MachineRequestActivationService(f.Options,
            new MachineRequestPaymentStore(db), new MachineBillingPeriodService(f.Options), new ReadOnlyStripe(), clock);
        var result = await service.ActivateAsync(payment.Id, default);
        Assert.AreEqual(MachineRequestProvisioningStage.Completed, result!.ProvisioningStage);
        await using var check = f.Db(); var period = await check.MachineBillingPeriods.SingleAsync(p => p.MachineId == f.MachineId);
        Assert.AreEqual(payment.ActivatedAtUtc, period.PeriodStartUtc); Assert.AreEqual(payment.FirstPeriodEndUtc, period.PeriodEndUtc);
        Assert.AreEqual(10m, period.IncludedAiBudgetRealCost); Assert.AreEqual(0m, period.IncludedAiUsedRealCost); Assert.AreEqual("Active", period.Status);
        Assert.AreEqual(ledger, await check.CreditLedger.CountAsync()); Assert.AreEqual(wallets, await check.CompanyWallets.CountAsync());
        Assert.AreEqual(clock.GetUtcNow().UtcDateTime, (await check.MachineRequestPayments.SingleAsync(p => p.Id == payment.Id)).ProvisioningCompletedAtUtc);
    }

    [TestMethod]
    public async Task RetryPreservesUsageAndFirstCompletionTimestamp()
    {
        await using var f = new Fixture(); await f.Seed(); var payment = await Seed(f, MachineRequestProvisioningStage.SubscriptionCreated);
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 9, 22, 13, 0, 0, TimeSpan.Zero));
        await using (var firstDb = f.Db()) await new MachineRequestActivationService(f.Options,
            new MachineRequestPaymentStore(firstDb), new MachineBillingPeriodService(f.Options), new ReadOnlyStripe(), clock).ActivateAsync(payment.Id, default);
        await using (var consumed = f.Db()) { var p = await consumed.MachineBillingPeriods.SingleAsync(p => p.MachineId == f.MachineId); p.IncludedAiUsedRealCost = 2.37m; await consumed.SaveChangesAsync(); }
        clock.Set(new DateTimeOffset(2026, 9, 23, 13, 0, 0, TimeSpan.Zero));
        await using (var retryDb = f.Db()) await new MachineRequestActivationService(f.Options,
            new MachineRequestPaymentStore(retryDb), new MachineBillingPeriodService(f.Options), new ReadOnlyStripe(), clock).ActivateAsync(payment.Id, default);
        await using var check = f.Db(); Assert.AreEqual(2.37m, (await check.MachineBillingPeriods.SingleAsync(p => p.MachineId == f.MachineId)).IncludedAiUsedRealCost);
        Assert.AreEqual(new DateTime(2026, 9, 22, 13, 0, 0, DateTimeKind.Utc), (await check.MachineRequestPayments.SingleAsync(p => p.Id == payment.Id)).ProvisioningCompletedAtUtc);
    }

    [TestMethod]
    public async Task WrongStageStopsBeforeStripeAndPeriodCreation()
    {
        await using var f = new Fixture(); await f.Seed(); var payment = await Seed(f, MachineRequestProvisioningStage.CustomerLinked);
        var remote = new ReadOnlyStripe(); await using var db = f.Db();
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => new MachineRequestActivationService(f.Options,
            new MachineRequestPaymentStore(db), new MachineBillingPeriodService(f.Options), remote, TimeProvider.System).ActivateAsync(payment.Id, default));
        Assert.AreEqual(0, remote.Reads);
    }

    [TestMethod]
    public async Task AdditionalStripeInconsistencyCreatesNoPeriodAndPreservesStage()
    {
        await using var f = new Fixture(); await f.Seed(); var payment = await Seed(f,
            MachineRequestProvisioningStage.SubscriptionCreated, MachineRequestKind.AdditionalMachine);
        var remote = new ReadOnlyStripe { RejectAdditional = true }; await using var db = f.Db();

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => new MachineRequestActivationService(f.Options,
            new MachineRequestPaymentStore(db), new MachineBillingPeriodService(f.Options), remote,
            TimeProvider.System).ActivateAsync(payment.Id, default));

        await using var check = f.Db();
        Assert.AreEqual(0, await check.MachineBillingPeriods.CountAsync());
        Assert.AreEqual(MachineRequestProvisioningStage.SubscriptionCreated,
            (await check.MachineRequestPayments.SingleAsync(p => p.Id == payment.Id)).ProvisioningStage);
    }

    [TestMethod]
    public async Task AdditionalIncoherentExistingPeriodIsNeverChangedOrCompleted()
    {
        await using var f = new Fixture(); await f.Seed(); var payment = await Seed(f,
            MachineRequestProvisioningStage.SubscriptionCreated, MachineRequestKind.AdditionalMachine);
        await using (var seed = f.Db())
        {
            seed.MachineBillingPeriods.Add(new MachineBillingPeriod { Id = Guid.NewGuid(), MachineId = f.MachineId,
                PeriodStartUtc = payment.ActivatedAtUtc!.Value, PeriodEndUtc = payment.FirstPeriodEndUtc!.Value,
                IncludedAiBudgetRealCost = 9m, IncludedAiUsedRealCost = 1m, Status = "Active",
                CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow });
            await seed.SaveChangesAsync();
        }
        await using var db = f.Db();

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => new MachineRequestActivationService(f.Options,
            new MachineRequestPaymentStore(db), new MachineBillingPeriodService(f.Options), new ReadOnlyStripe(),
            TimeProvider.System).ActivateAsync(payment.Id, default));

        await using var check = f.Db(); var period = await check.MachineBillingPeriods.SingleAsync();
        Assert.AreEqual(9m, period.IncludedAiBudgetRealCost); Assert.AreEqual(1m, period.IncludedAiUsedRealCost);
        Assert.AreEqual(MachineRequestProvisioningStage.SubscriptionCreated,
            (await check.MachineRequestPayments.SingleAsync(p => p.Id == payment.Id)).ProvisioningStage);
    }

    private static async Task<PaymentEntity> Seed(Fixture f, MachineRequestProvisioningStage stage,
        MachineRequestKind requestKind = MachineRequestKind.InitialMachine, DateTime? activatedAt = null,
        DateTime? firstPeriodEnd = null)
    {
        var now = activatedAt ?? new DateTime(2026, 9, 22, 12, 0, 0, DateTimeKind.Utc);
        var end = firstPeriodEnd ?? new DateTime(2026, 9, 30, 22, 0, 0, DateTimeKind.Utc);
        await using var db = f.Db(); var company = await db.Companies.SingleAsync(c => c.Id == f.CompanyId); company.Status = "active";
        var machine = await db.Machines.SingleAsync(m => m.Id == f.MachineId); machine.Status = "active";
        db.MachineBillingPeriods.RemoveRange(await db.MachineBillingPeriods.Where(period => period.MachineId == f.MachineId).ToListAsync());
        db.BillingAccounts.Add(new BillingAccount { Id = Guid.NewGuid(), CompanyId = f.CompanyId, StripeCustomerId = "cus_checkout",
            StripeSubscriptionId = "sub_request", CreatedAtUtc = now, UpdatedAtUtc = now }); await db.SaveChangesAsync();
        var p = new PaymentEntity { Id = Guid.NewGuid(), RequestKind = requestKind, Status = MachineRequestPaymentStatus.Captured, EstimatedTotalPages = 400,
            AmountCents = 12980, Currency = "EUR", StripeSessionId = "cs", StripePaymentIntentId = "pi", AuthorizationEventId = "evt",
            MachineRequestId = "request", RequestLinkedAtUtc = now, CompanyId = f.CompanyId, MachineId = f.MachineId,
            ActivatedAtUtc = now, FirstPeriodEndUtc = end, ServiceAmountCents = 500, FinalCaptureAmountCents = 11490,
            CreatedAtUtc = now, UpdatedAtUtc = now, AuthorizedAtUtc = now, CapturedAtUtc = now, ProvisioningStage = stage, RowVersion = [1] };
        await db.Database.ExecuteSqlInterpolatedAsync($@"INSERT INTO MachineRequestPayments
          (Id,RequestKind,Status,EstimatedTotalPages,AmountCents,Currency,StripeSessionId,StripePaymentIntentId,AuthorizationEventId,MachineRequestId,
           RequestLinkedAtUtc,CreatedAtUtc,UpdatedAtUtc,AuthorizedAtUtc,CapturedAtUtc,ActivatedAtUtc,FirstPeriodEndUtc,
           ServiceAmountCents,FinalCaptureAmountCents,CompanyId,MachineId,ProvisioningStage,RowVersion)
          VALUES ({p.Id},{(int)p.RequestKind},{(int)p.Status},{p.EstimatedTotalPages},{p.AmountCents},{p.Currency},{p.StripeSessionId},{p.StripePaymentIntentId},
           {p.AuthorizationEventId},{p.MachineRequestId},{p.RequestLinkedAtUtc},{p.CreatedAtUtc},{p.UpdatedAtUtc},{p.AuthorizedAtUtc},
           {p.CapturedAtUtc},{p.ActivatedAtUtc},{p.FirstPeriodEndUtc},{p.ServiceAmountCents},{p.FinalCaptureAmountCents},
           {p.CompanyId},{p.MachineId},{(int)p.ProvisioningStage},{p.RowVersion})"); return p;
    }
    private sealed class ReadOnlyStripe : IMachineRequestSubscriptionGateway
    {
        public int Reads, InitialReads, AdditionalReads; public bool RejectAdditional;
        public Task ValidatePriceAsync(CancellationToken ct) { Reads++; return Task.CompletedTask; }
        public Task<MachineRequestSubscriptionContext> ReadContextAsync(WebApp.Api.Services.MachineRequestPayment p, string customer, string? subscription, int quantity, CancellationToken ct)
        { Reads++; InitialReads++; return Task.FromResult(new MachineRequestSubscriptionContext(customer, "pm", subscription, quantity)); }
        public Task<MachineRequestSubscriptionContext> ReadAdditionalContextAsync(WebApp.Api.Services.MachineRequestPayment p, string customer, string subscription, int quantity, CancellationToken ct)
        { Reads++; AdditionalReads++; return RejectAdditional ? Task.FromException<MachineRequestSubscriptionContext>(new InvalidOperationException("Stripe incoherent.")) : Task.FromResult(new MachineRequestSubscriptionContext(customer, "pm", subscription, quantity)); }
        public Task<StripeSubscriptionSnapshot> ReadAsync(WebApp.Api.Services.MachineRequestPayment p, MachineRequestSubscriptionContext c, CancellationToken ct)
        { Reads++; return Task.FromResult(new StripeSubscriptionSnapshot(c.SubscriptionId!, c.CustomerId, "active", p.ActivatedAtUtc!.Value, p.FirstPeriodEndUtc!.Value, c.TargetQuantity) { ItemId = "si" }); }
        public Task<StripeSubscriptionSnapshot> CreateAsync(WebApp.Api.Services.MachineRequestPayment p, MachineRequestSubscriptionContext c, DateTime e, CancellationToken ct) => throw new AssertFailedException("Stripe write forbidden.");
        public Task<StripeSubscriptionSnapshot> SetQuantityAsync(WebApp.Api.Services.MachineRequestPayment p, MachineRequestSubscriptionContext c, CancellationToken ct) => throw new AssertFailedException("Stripe write forbidden.");
    }
    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    { private DateTimeOffset current = value; public override DateTimeOffset GetUtcNow() => current; public void Set(DateTimeOffset value) => current = value; }
}
