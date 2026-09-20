using System.Collections.Concurrent;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebApp.Api.Data;
using WebApp.Api.Models.Entities;
using WebApp.Api.Services;
using Fixture = WebApp.Api.Tests.AiCreditConsumptionServiceTests.Fixture;

namespace WebApp.Api.Tests;

[TestClass]
public class WalletTopUpTests
{
    private static StripeBillingOptions Settings => new() { Enabled = true, SecretKey = "sk_test_local", PriceId = "price_local",
        TopUpWebhookSecret = "whsec_topup_local", TopUpReturnUrl = "http://localhost:5173/" };
    private sealed class Remote : IStripeWalletTopUpGateway, IStripeBillingGateway
    {
        public bool Paid, FailCreate, Invalid;
        public string? PaymentOverride;
        public int Creates, Reads;
        public Func<Task>? BeforeRead;
        private readonly ConcurrentDictionary<Guid, bool> created = new();
        private readonly ConcurrentDictionary<Guid, DateTime> dates = new();
        public Task<StripeTopUpPayment> CreateAsync(StripeWalletTopUp op, CancellationToken ct)
        {
            if (created.TryAdd(op.Id, true)) Interlocked.Increment(ref Creates);
            if (FailCreate) { FailCreate = false; throw new TimeoutException("Lost checkout response"); }
            return Task.FromResult(new StripeTopUpPayment("cs_test_" + op.Id.ToString("N"), "https://checkout.stripe.com/c/pay/local", "AwaitingPayment"));
        }
        public async Task<StripeTopUpPayment> ReadAsync(StripeWalletTopUp op, string session, CancellationToken ct)
        {
            Interlocked.Increment(ref Reads); if (BeforeRead != null) await BeforeRead();
            return Invalid ? new(session, null, "ReconciliationRequired") : !Paid ? new(session, null, "AwaitingPayment")
                : new(session, null, "PaymentConfirmed", PaymentOverride ?? "pi_" + op.Id.ToString("N"), dates.GetOrAdd(op.Id, _ => DateTime.UtcNow));
        }
        public Task<string> GetCustomerAsync(string id, Guid company, CancellationToken ct) => Task.FromResult(id);
        public Task<string> CreateCustomerAsync(Guid company, Guid account, CancellationToken ct) => Task.FromResult("cus_local");
        public Task ValidatePriceAsync(CancellationToken ct) => throw new AssertFailedException("No subscription price lookup for top-up");
        public Task PrepareInitialPaymentAsync(string id, string customer, Guid company, CancellationToken ct) => throw new AssertFailedException("No subscription preparation for top-up");
        public Task<StripeSubscriptionSnapshot> CreateSubscriptionAsync(string c, Guid company, Guid account, int qty, CancellationToken ct) => throw new AssertFailedException();
        public Task<StripeSubscriptionSnapshot> GetSubscriptionAsync(string id, string c, Guid company, CancellationToken ct) => throw new AssertFailedException();
    }
    private static CompanyWalletTopUpService Service(Fixture f, Remote remote, DbContextOptions<DiagLinkDbContext>? options = null) => new(options ?? f.Options,
        new StripeBillingService(f.Options, remote, Settings), remote, Settings);
    private static async Task Prepare(Fixture f, decimal? wallet = null)
    {
        await f.Seed(); await using var db = f.Db(); (await db.Companies.SingleAsync()).Status = "active";
        db.BillingAccounts.Add(new BillingAccount { Id = Guid.NewGuid(), CompanyId = f.CompanyId, StripeCustomerId = "cus_local", CreatedAtUtc = DateTime.UtcNow });
        if (wallet != null) db.CompanyWallets.Add(new CompanyWallet { CompanyId = f.CompanyId, Balance = wallet.Value, Currency = "EUR" });
        await db.SaveChangesAsync();
    }
    private static string Body(Guid id, string session, string evt = "evt_topup", string type = "checkout.session.completed") => JsonSerializer.Serialize(new
    {
        id = evt, @object = "event", type, api_version = Stripe.StripeConfiguration.ApiVersion, livemode = false,
        data = new { @object = new { id = session, @object = "checkout.session", livemode = false, metadata = new { diaglink_topup_id = id.ToString() } } }
    });
    private static string Sign(string body, long? timestamp = null)
    {
        var t = timestamp ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return $"t={t},v1={Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(Settings.TopUpWebhookSecret), Encoding.UTF8.GetBytes($"{t}.{body}"))).ToLowerInvariant()}";
    }
    private static StripeWalletTopUpWebhook Webhook(CompanyWalletTopUpService service) => new(Settings, service, NullLogger<StripeWalletTopUpWebhook>.Instance);

    [TestMethod]
    [DataRow("0", "EUR")]
    [DataRow("9.99", "EUR")]
    [DataRow("10.001", "EUR")]
    [DataRow("1000000", "EUR")]
    [DataRow("20", "USD")]
    public async Task InvalidAmountOrCurrencyRejectedBeforeDataAccess(string amount, string currency)
    {
        await using var f = new Fixture(); var remote = new Remote();
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => Service(f, remote).StartAsync(f.CompanyId, Guid.NewGuid(),
            decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture), currency));
        Assert.AreEqual(0, remote.Creates);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task SignedPaymentCreditsExactlyOnceWithAndWithoutExistingWallet(bool exists)
    {
        await using var f = new Fixture(); await Prepare(f, exists ? 7m : null); var remote = new Remote(); var service = Service(f, remote);
        var start = await service.StartAsync(f.CompanyId, Guid.NewGuid(), 20m);
        Assert.AreEqual("AwaitingPayment", start.Stage);
        await using (var db = f.Db()) { Assert.AreEqual(0, await db.CreditLedger.CountAsync()); Assert.AreEqual(exists ? 1 : 0, await db.CompanyWallets.CountAsync()); }
        var body = Body(start.Id, start.StripeSessionId!); var webhook = Webhook(service);
        Assert.AreEqual("AwaitingPayment", (await webhook.HandleAsync(body, Sign(body))).Status);
        await using (var db = f.Db()) Assert.AreEqual(0, await db.CreditLedger.CountAsync());
        remote.Paid = true;
        Assert.AreEqual("Completed", (await webhook.HandleAsync(body, Sign(body))).Status);
        Assert.AreEqual("AlreadyCompleted", (await webhook.HandleAsync(body, Sign(body))).Status);
        var second = Body(start.Id, start.StripeSessionId!, "evt_other", "checkout.session.async_payment_succeeded");
        Assert.AreEqual("AlreadyCompleted", (await webhook.HandleAsync(second, Sign(second))).Status);
        Assert.AreEqual("AlreadyCompleted", (await service.StartAsync(f.CompanyId, start.Id, 20m)).Status);
        await using var after = f.Db(); var ledger = await after.CreditLedger.SingleAsync();
        Assert.AreEqual(20m + (exists ? 7m : 0m), (await after.CompanyWallets.SingleAsync()).Balance);
        Assert.AreEqual(20m, ledger.CommercialCreditAmount); Assert.AreEqual(20m + (exists ? 7m : 0m), ledger.BalanceAfter);
        Assert.AreEqual("TopUp", ledger.EntryType); Assert.AreEqual("CompanyWallet", ledger.BucketType);
        Assert.AreEqual("EUR", ledger.Currency); Assert.AreEqual("evt_topup", ledger.ExternalEventId); Assert.IsNull(ledger.RealAiCost);
        Assert.IsNull(ledger.MachineId); Assert.IsNull(ledger.AiUsageRecordId);
        Assert.AreEqual(1, remote.Creates);
        Assert.AreEqual(1, await after.MachineBillingPeriods.CountAsync());
    }

    [TestMethod]
    public async Task LostCheckoutResponseRecoversSameSessionAndFrozenInputs()
    {
        await using var f = new Fixture(); await Prepare(f); var remote = new Remote { FailCreate = true }; var service = Service(f, remote); var id = Guid.NewGuid();
        await Assert.ThrowsExactlyAsync<TimeoutException>(() => service.StartAsync(f.CompanyId, id, 50m));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service.StartAsync(f.CompanyId, id, 100m));
        var retry = await service.StartAsync(f.CompanyId, id, 50m);
        Assert.AreEqual(1, remote.Creates); Assert.AreEqual("cs_test_" + id.ToString("N"), retry.StripeSessionId);
        remote.Paid = true;
        Assert.AreEqual("Completed", (await service.ConfirmAsync(id, retry.StripeSessionId!, "evt_lost"))!.Status);
    }

    [TestMethod]
    public async Task WebhookRecoversReservedOperationAfter23HoursWithoutRecreatingCheckout()
    {
        await using var f = new Fixture(); await Prepare(f); var remote = new Remote { FailCreate = true }; var service = Service(f, remote); var id = Guid.NewGuid();
        await Assert.ThrowsExactlyAsync<TimeoutException>(() => service.StartAsync(f.CompanyId, id, 10m));
        await using (var db = f.Db()) { (await db.StripeWalletTopUps.SingleAsync()).CreatedAtUtc = DateTime.UtcNow.AddDays(-2); await db.SaveChangesAsync(); }
        Assert.AreEqual("ReconciliationRequired", (await service.StartAsync(f.CompanyId, id, 10m)).Status);
        remote.Paid = true; var body = Body(id, "cs_test_" + id.ToString("N"));
        Assert.AreEqual("Completed", (await Webhook(service).HandleAsync(body, Sign(body))).Status); Assert.AreEqual(1, remote.Creates);
    }

    private sealed class Crash(string stage, bool after) : DbTransactionInterceptor
    {
        public int Failures;
        private void Inject(DbContext? db)
        {
            if (Failures != 0 || db?.ChangeTracker.Entries<StripeWalletTopUp>().Any(e => e.Entity.Stage.ToString() == stage) != true) return;
            Failures++; throw new TimeoutException("Simulated process interruption");
        }
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction tx, TransactionEventData data,
            InterceptionResult result, CancellationToken ct = default)
        { if (!after) Inject(data.Context); return ValueTask.FromResult(result); }
        public override Task TransactionCommittedAsync(DbTransaction tx, TransactionEndEventData data, CancellationToken ct = default)
        { if (after) Inject(data.Context); return Task.CompletedTask; }
    }
    [TestMethod]
    [DataRow("PaymentConfirmed", true)]
    [DataRow("WalletCredited", false)]
    [DataRow("WalletCredited", true)]
    [DataRow("Completed", true)]
    public async Task PaymentAndCreditCheckpointsRecoverFromCrash(string stage, bool after)
    {
        await using var f = new Fixture(); await Prepare(f); var remote = new Remote { Paid = true };
        var baseService = Service(f, remote); var op = await baseService.StartAsync(f.CompanyId, Guid.NewGuid(), 100m);
        var crash = new Crash(stage, after); var options = new DbContextOptionsBuilder<DiagLinkDbContext>(f.Options).AddInterceptors(crash).Options;
        var body = Body(op.Id, op.StripeSessionId!); var webhook = Webhook(Service(f, remote, options));
        Assert.AreEqual(503, (await webhook.HandleAsync(body, Sign(body))).HttpStatus); Assert.AreEqual(1, crash.Failures);
        if (stage == "WalletCredited" && !after)
        { await using var db = f.Db(); Assert.AreEqual(0, await db.CompanyWallets.CountAsync()); Assert.AreEqual(0, await db.CreditLedger.CountAsync()); }
        var retry = await webhook.HandleAsync(body, Sign(body)); Assert.AreEqual(200, retry.HttpStatus);
        await using var final = f.Db(); Assert.AreEqual(100m, (await final.CompanyWallets.SingleAsync()).Balance);
        Assert.AreEqual(1, await final.CreditLedger.CountAsync()); Assert.AreEqual(StripeWalletTopUpStage.Completed, (await final.StripeWalletTopUps.SingleAsync()).Stage);
    }

    [TestMethod]
    public async Task ConcurrentDuplicateEventsAndDifferentTopUpsKeepBalanceAndLedgerCoherent()
    {
        await using var f = new Fixture(); await Prepare(f); var remote = new Remote { Paid = true }; var service = Service(f, remote);
        var a = await service.StartAsync(f.CompanyId, Guid.NewGuid(), 20m); var b = await service.StartAsync(f.CompanyId, Guid.NewGuid(), 50m);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var arrived = 0;
        remote.BeforeRead = () => { if (Interlocked.Increment(ref arrived) == 3) ready.SetResult(); return ready.Task; };
        var results = await Task.WhenAll(new[] { (a, "evt_a"), (a, "evt_a"), (b, "evt_b") }.Select(async x =>
        { var body = Body(x.Item1.Id, x.Item1.StripeSessionId!, x.Item2); return await Webhook(service).HandleAsync(body, Sign(body)); }));
        // SQLite can report busy under concurrent writes; the HTTP retry contract must allow safe recovery.
        remote.BeforeRead = null;
        foreach (var op in new[] { (a, "evt_a"), (b, "evt_b") })
        { var body = Body(op.Item1.Id, op.Item1.StripeSessionId!, op.Item2); Assert.AreEqual(200, (await Webhook(service).HandleAsync(body, Sign(body))).HttpStatus); }
        Assert.IsTrue(results.All(r => r.HttpStatus is 200 or 503));
        await using var db = f.Db(); var rows = (await db.CreditLedger.ToListAsync()).OrderBy(l => l.BalanceAfter).ToList();
        Assert.AreEqual(2, rows.Count); Assert.AreEqual(70m, rows.Sum(l => l.CommercialCreditAmount));
        Assert.AreEqual(70m, rows[^1].BalanceAfter); Assert.AreEqual(70m, (await db.CompanyWallets.SingleAsync()).Balance);
    }

    [TestMethod]
    public async Task WrongCurrencyInvalidProofAndReusedPaymentCannotCredit()
    {
        await using var f = new Fixture(); await Prepare(f); var remote = new Remote { Paid = true, PaymentOverride = "pi_shared" }; var service = Service(f, remote);
        var a = await service.StartAsync(f.CompanyId, Guid.NewGuid(), 10m); var b = await service.StartAsync(f.CompanyId, Guid.NewGuid(), 10m);
        remote.Invalid = true; Assert.AreEqual("ReconciliationRequired", (await service.ConfirmAsync(a.Id, a.StripeSessionId!, "evt_a"))!.Status);
        remote.Invalid = false; await service.ConfirmAsync(a.Id, a.StripeSessionId!, "evt_a");
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service.ConfirmAsync(b.Id, b.StripeSessionId!, "evt_b"));
        await using var db = f.Db(); Assert.AreEqual(10m, (await db.CompanyWallets.SingleAsync()).Balance); Assert.AreEqual(1, await db.CreditLedger.CountAsync());
    }

    [TestMethod]
    public async Task TopUpLedgerCannotBeModifiedOrDeletedThroughContext()
    {
        await using var f = new Fixture(); await Prepare(f); var remote = new Remote { Paid = true }; var service = Service(f, remote);
        var op = await service.StartAsync(f.CompanyId, Guid.NewGuid(), 10m); await service.ConfirmAsync(op.Id, op.StripeSessionId!, "evt_a");
        await using var db = f.Db(); var row = await db.CreditLedger.SingleAsync(); row.CommercialCreditAmount = 999m;
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear(); db.CreditLedger.Remove(await db.CreditLedger.SingleAsync());
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [TestMethod]
    public async Task SignatureRequiredAndUnknownEventHasNoEffects()
    {
        await using var f = new Fixture(); var webhook = Webhook(Service(f, new Remote()));
        Assert.AreEqual(400, (await webhook.HandleAsync("{}", "")).HttpStatus);
        Assert.AreEqual(400, (await webhook.HandleAsync("{}", Sign("{}", DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeSeconds()))).HttpStatus);
        await Prepare(f); var body = Body(Guid.NewGuid(), "cs_unknown");
        Assert.AreEqual("UnknownOperation", (await webhook.HandleAsync(body, Sign(body))).Status);
        await using var db = f.Db(); Assert.AreEqual(0, await db.CompanyWallets.CountAsync());
    }

    [TestMethod]
    public async Task WalletWithWrongCurrencyNeverReceivesConfirmedMoney()
    {
        await using var f = new Fixture(); await Prepare(f, 5m); var remote = new Remote { Paid = true }; var service = Service(f, remote);
        var op = await service.StartAsync(f.CompanyId, Guid.NewGuid(), 10m);
        await using (var db = f.Db()) { (await db.CompanyWallets.SingleAsync()).Currency = "USD"; await db.SaveChangesAsync(); }
        var body = Body(op.Id, op.StripeSessionId!);
        Assert.AreEqual("ReconciliationRequired", (await Webhook(service).HandleAsync(body, Sign(body))).Status);
        await using var after = f.Db(); Assert.AreEqual(5m, (await after.CompanyWallets.SingleAsync()).Balance);
        Assert.AreEqual(0, await after.CreditLedger.CountAsync());
        Assert.AreEqual(StripeWalletTopUpStage.PaymentConfirmed, (await after.StripeWalletTopUps.SingleAsync()).Stage);
    }

    [TestMethod]
    public async Task TestEndpointsAreCompanyScopedAndLiveIsRejected()
    {
        await using var f = new Fixture(); await Prepare(f); var remote = new Remote(); var service = Service(f, remote); await using var db = f.Db();
        var missing = await StripeWalletTopUpEndpoints.StartAsync(Guid.NewGuid(), new(Guid.NewGuid(), 10m), db, service, Settings, default);
        Assert.AreEqual(404, ((Microsoft.AspNetCore.Http.IStatusCodeHttpResult)missing).StatusCode);
        var live = new StripeBillingOptions { Enabled = true, AllowLive = true, SecretKey = "sk_live_unused", PriceId = "price_local", TopUpWebhookSecret = "whsec_test" };
        var denied = await StripeWalletTopUpEndpoints.StartAsync(f.CompanyId, new(Guid.NewGuid(), 10m), db, service, live, default);
        Assert.AreEqual(409, ((Microsoft.AspNetCore.Http.IStatusCodeHttpResult)denied).StatusCode);
        var body = Body(Guid.NewGuid(), "cs_test_unused");
        var webhook = new StripeWalletTopUpWebhook(live, service, NullLogger<StripeWalletTopUpWebhook>.Instance);
        Assert.AreEqual(503, (await webhook.HandleAsync(body, Sign(body))).HttpStatus); Assert.AreEqual(0, remote.Creates);
    }

    [TestMethod]
    public async Task TopUpAndAiDebitShareOneCoherentWalletBalance()
    {
        await using var f = new Fixture(); await Prepare(f, 30m); var remote = new Remote { Paid = true }; var service = Service(f, remote);
        var op = await service.StartAsync(f.CompanyId, Guid.NewGuid(), 20m);
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        remote.BeforeRead = () => { reached.TrySetResult(); return release.Task; };
        var confirm = service.ConfirmAsync(op.Id, op.StripeSessionId!, "evt_mixed");
        await reached.Task;
        var debit = await new CompanyWalletDebitService(f.Options).DebitAsync(f.UsageId, 1m);
        Assert.IsTrue(debit.Success); release.SetResult(); await confirm;
        await using var db = f.Db(); Assert.AreEqual(47m, (await db.CompanyWallets.SingleAsync()).Balance);
        Assert.AreEqual(2, await db.CreditLedger.CountAsync());
        Assert.AreEqual(20m, (await db.CreditLedger.SingleAsync(l => l.EntryType == "TopUp")).CommercialCreditAmount);
        Assert.AreEqual(3m, (await db.CreditLedger.SingleAsync(l => l.EntryType == "AiUsage")).CommercialCreditAmount);
    }
}
