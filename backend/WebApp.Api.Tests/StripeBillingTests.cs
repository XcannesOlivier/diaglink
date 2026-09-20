using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Stripe;
using WebApp.Api.Models.Entities;
using WebApp.Api.Services;
using Fixture = WebApp.Api.Tests.AiCreditConsumptionServiceTests.Fixture;

namespace WebApp.Api.Tests;

[TestClass]
public partial class StripeBillingTests
{
    private static StripeBillingOptions Options => new() { Enabled = true, SecretKey = "sk_test_local_only", PriceId = "price_local" };

    private sealed class FakeGateway : IStripeBillingGateway
    {
        public int Customers, Subscriptions, Reads;
        public bool FailSubscription;
        public Guid LastAccountId;
        public int Quantity;
        public Task ValidatePriceAsync(CancellationToken ct) => Task.CompletedTask;
        public Task PrepareInitialPaymentAsync(string id, string customer, Guid company, CancellationToken ct) => Task.CompletedTask;
        public Task<string> CreateCustomerAsync(Guid companyId, Guid accountId, CancellationToken ct)
        { Customers++; return Task.FromResult("cus_local"); }
        public Task<string> GetCustomerAsync(string customerId, Guid companyId, CancellationToken ct) => Task.FromResult(customerId);
        public Task<StripeSubscriptionSnapshot> CreateSubscriptionAsync(string customerId, Guid companyId, Guid accountId, int quantity, CancellationToken ct)
        {
            Subscriptions++; LastAccountId = accountId; Quantity = quantity;
            if (FailSubscription) throw new TimeoutException("Local simulated failure");
            return Task.FromResult(Snapshot(customerId));
        }
        public Task<StripeSubscriptionSnapshot> GetSubscriptionAsync(string subscriptionId, string customerId, Guid companyId, CancellationToken ct)
        { Reads++; return Task.FromResult(Snapshot(customerId)); }
        private StripeSubscriptionSnapshot Snapshot(string customerId) => new("sub_local", customerId, "incomplete",
            new DateTime(2026, 9, 11, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 11, 0, 0, 0, DateTimeKind.Utc), Quantity);
    }

    private static async Task Prepare(Fixture f)
    {
        await f.Seed();
        await using var db = f.Db();
        (await db.Companies.SingleAsync()).Status = "active";
        (await db.Machines.SingleAsync()).Status = "active";
        db.Machines.Add(new Machine { Id = Guid.NewGuid(), CompanyId = f.CompanyId, Name = "inactive", Status = "inactive" });
        var other = new Company { Id = Guid.NewGuid(), Name = "other", Status = "active" };
        db.Companies.Add(other);
        db.Machines.Add(new Machine { Id = Guid.NewGuid(), CompanyId = other.Id, Name = "other", Status = "active" });
        await db.SaveChangesAsync();
    }

    [TestMethod]
    public async Task CreatesThenRetrievesAndPersistsStripeStateWithoutFinancialWrites()
    {
        await using var f = new Fixture(); await Prepare(f);
        var gateway = new FakeGateway();
        var service = new StripeBillingService(f.Options, gateway, Options);
        var created = await service.GetOrCreateSubscriptionAsync(f.CompanyId);
        var existing = await service.GetOrCreateSubscriptionAsync(f.CompanyId);
        Assert.AreEqual(created, existing);
        Assert.AreEqual(1, gateway.Quantity);
        Assert.AreEqual(1, gateway.Customers); Assert.AreEqual(1, gateway.Subscriptions); Assert.AreEqual(1, gateway.Reads);
        await using var db = f.Db();
        var account = await db.BillingAccounts.SingleAsync();
        Assert.AreEqual("cus_local", account.StripeCustomerId); Assert.AreEqual("sub_local", account.StripeSubscriptionId);
        Assert.AreEqual("incomplete", account.SubscriptionStatus);
        Assert.AreEqual(created.PeriodStartUtc, account.CurrentPeriodStartUtc); Assert.AreEqual(created.PeriodEndUtc, account.CurrentPeriodEndUtc);
        Assert.AreEqual(0, await db.CreditLedger.CountAsync()); Assert.AreEqual(0, await db.CompanyWallets.CountAsync());
        Assert.AreEqual(1, await db.MachineBillingPeriods.CountAsync());
        Assert.AreEqual(0m, (await db.MachineBillingPeriods.SingleAsync()).IncludedAiUsedRealCost);
    }

    [TestMethod]
    public async Task FailedCreationKeepsSameReservationOnRetry()
    {
        await using var f = new Fixture(); await Prepare(f);
        var gateway = new FakeGateway { FailSubscription = true };
        var service = new StripeBillingService(f.Options, gateway, Options);
        await Assert.ThrowsExactlyAsync<TimeoutException>(() => service.GetOrCreateSubscriptionAsync(f.CompanyId));
        var reserved = gateway.LastAccountId;
        gateway.FailSubscription = false;
        await service.GetOrCreateSubscriptionAsync(f.CompanyId);
        Assert.AreEqual(reserved, gateway.LastAccountId); Assert.AreEqual(1, gateway.Customers);
    }

    [TestMethod]
    public async Task SqlFailureAfterRemoteCreationKeepsReservationForSameKey()
    {
        await using var f = new Fixture(); await Prepare(f);
        var gateway = new FakeGateway(); var service = new StripeBillingService(f.Options, gateway, Options);
        await service.GetOrCreateCustomerAsync(f.CompanyId);
        await using (var db = f.Db())
            await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER fail_subscription BEFORE UPDATE ON BillingAccounts WHEN NEW.StripeSubscriptionId IS NOT NULL BEGIN SELECT RAISE(ABORT,'test'); END");
        await Assert.ThrowsExactlyAsync<DbUpdateException>(() => service.GetOrCreateSubscriptionAsync(f.CompanyId));
        var reserved = gateway.LastAccountId;
        await using (var db = f.Db())
        {
            var a = await db.BillingAccounts.SingleAsync();
            Assert.IsNull(a.StripeSubscriptionId); Assert.AreEqual("creation_pending", a.SubscriptionStatus);
            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER fail_subscription");
        }
        await service.GetOrCreateSubscriptionAsync(f.CompanyId);
        Assert.AreEqual(reserved, gateway.LastAccountId);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task OldUnconfirmedCreationRequiresReconciliation(bool subscription)
    {
        await using var f = new Fixture(); await Prepare(f);
        await using (var db = f.Db())
        {
            db.BillingAccounts.Add(new BillingAccount { Id = Guid.NewGuid(), CompanyId = f.CompanyId,
                StripeCustomerId = subscription ? "cus_local" : null, SubscriptionStatus = subscription ? "creation_pending" : null,
                CreatedAtUtc = DateTime.UtcNow.AddDays(-2), UpdatedAtUtc = DateTime.UtcNow.AddDays(-2) });
            await db.SaveChangesAsync();
        }
        var gateway = new FakeGateway(); var service = new StripeBillingService(f.Options, gateway, Options);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service.GetOrCreateSubscriptionAsync(f.CompanyId));
        Assert.AreEqual(0, gateway.Customers); Assert.AreEqual(0, gateway.Subscriptions);
    }

    [TestMethod]
    [DataRow("empty")]
    [DataRow("inactive")]
    [DataRow("missing")]
    public async Task InvalidCompanyDoesNotCreateExternalObjects(string scenario)
    {
        await using var f = new Fixture(); await Prepare(f);
        await using (var db = f.Db())
        {
            if (scenario == "empty") (await db.Machines.SingleAsync(m => m.Id == f.MachineId)).Status = "inactive";
            if (scenario == "inactive") (await db.Companies.SingleAsync(c => c.Id == f.CompanyId)).Status = "inactive";
            await db.SaveChangesAsync();
        }
        var gateway = new FakeGateway();
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => new StripeBillingService(f.Options, gateway, Options)
            .GetOrCreateSubscriptionAsync(scenario == "missing" ? Guid.NewGuid() : f.CompanyId));
        Assert.AreEqual(0, gateway.Customers); Assert.AreEqual(0, gateway.Subscriptions);
        await using var check = f.Db(); Assert.AreEqual(0, await check.BillingAccounts.CountAsync());
    }

    [TestMethod]
    public void ConfigurationIsOptInAndLiveRequiresExplicitPermission()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() => new StripeBillingOptions().Validate());
        Assert.ThrowsExactly<InvalidOperationException>(() => new StripeBillingOptions { Enabled = true, SecretKey = "pk_test_fake", PriceId = "price_local" }.Validate());
        Assert.ThrowsExactly<InvalidOperationException>(() => new StripeBillingOptions { Enabled = true, SecretKey = "sk_live_fake", PriceId = "price_local" }.Validate());
        Assert.ThrowsExactly<InvalidOperationException>(() => new StripeBillingOptions { Enabled = true, SecretKey = "sk_test_fake" }.Validate());
        Options.Validate();
    }

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }
    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };

    [TestMethod]
    public async Task SdkSendsStableKeysConfiguredPriceQuantityAndIncompletePaymentBehavior()
    {
        var companyId = Guid.NewGuid(); var accountId = Guid.NewGuid(); var requests = 0;
        using var http = new HttpClient(new Handler(async request =>
        {
            requests++;
            var body = Uri.UnescapeDataString(await request.Content!.ReadAsStringAsync());
            Assert.AreEqual("api.stripe.com", request.RequestUri!.Host);
            Assert.IsTrue(request.Headers.Contains("Authorization"));
            if (request.RequestUri.AbsolutePath.EndsWith("/customers"))
            {
                Assert.AreEqual($"diaglink:customer:{accountId:N}", request.Headers.GetValues("Idempotency-Key").Single());
                StringAssert.Contains(body, companyId.ToString());
                return Json($$$"""{"id":"cus_local","object":"customer","metadata":{"diaglink_company_id":"{{{companyId}}}"}}""");
            }
            Assert.AreEqual($"diaglink:subscription:{accountId:N}", request.Headers.GetValues("Idempotency-Key").Single());
            StringAssert.Contains(body, "items[0][price]=price_local"); StringAssert.Contains(body, "items[0][quantity]=3");
            StringAssert.Contains(body, "payment_behavior=default_incomplete");
            StringAssert.Contains(body, "payment_settings[save_default_payment_method]=on_subscription");
            StringAssert.Contains(body, "collection_method=charge_automatically");
            return Json($$$"""{"id":"sub_local","object":"subscription","customer":"cus_local","status":"incomplete","metadata":{"diaglink_company_id":"{{{companyId}}}"},"items":{"object":"list","has_more":false,"data":[{"id":"si_local","object":"subscription_item","quantity":3,"current_period_start":1789084800,"current_period_end":1791676800,"price":{"id":"price_local","object":"price"}}]}}""");
        }));
        var client = new StripeClient(Options.SecretKey, httpClient: new SystemNetHttpClient(http, maxNetworkRetries: 0));
        var gateway = new StripeBillingGateway(Options, client);
        Assert.AreEqual("cus_local", await gateway.CreateCustomerAsync(companyId, accountId, default));
        var result = await gateway.CreateSubscriptionAsync("cus_local", companyId, accountId, 3, default);
        Assert.AreEqual("incomplete", result.Status); Assert.AreEqual(3L, result.Quantity);
        Assert.AreEqual(DateTimeKind.Utc, result.PeriodStartUtc.Kind); Assert.IsTrue(result.PeriodEndUtc > result.PeriodStartUtc);
        Assert.AreEqual(2, requests);
    }

    [TestMethod]
    [DataRow(2990, "month", "exclusive", true)]
    [DataRow(2991, "month", "exclusive", false)]
    [DataRow(2990, "year", "exclusive", false)]
    [DataRow(2990, "month", "inclusive", false)]
    public async Task PriceIsValidatedBeforeUse(int cents, string interval, string tax, bool valid)
    {
        using var http = new HttpClient(new Handler(request => Task.FromResult(Json($$$"""{"id":"price_local","object":"price","active":true,"livemode":false,"currency":"eur","unit_amount":{{{cents}}},"unit_amount_decimal":"{{{cents}}}","type":"recurring","billing_scheme":"per_unit","tax_behavior":"{{{tax}}}","recurring":{"interval":"{{{interval}}}","interval_count":1,"usage_type":"licensed"}}"""))));
        var gateway = new StripeBillingGateway(Options, new StripeClient(Options.SecretKey, httpClient: new SystemNetHttpClient(http, maxNetworkRetries: 0)));
        if (valid) await gateway.ValidatePriceAsync(default);
        else await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => gateway.ValidatePriceAsync(default));
    }
}
