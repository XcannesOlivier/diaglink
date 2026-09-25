using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Stripe;
using WebApp.Api.Models.Entities;
using WebApp.Api.Services;
using PaymentEntity = WebApp.Api.Models.Entities.MachineRequestPayment;
using Fixture = WebApp.Api.Tests.AiCreditConsumptionServiceTests.Fixture;

namespace WebApp.Api.Tests;

[TestClass]
public sealed class MachineRequestSubscriptionTests
{
    [TestMethod]
    [DataRow("subscription-customer")]
    [DataRow("price")]
    [DataRow("period")]
    [DataRow("collection")]
    [DataRow("payment-customer")]
    public async Task AdditionalContextRejectsIncoherentExistingStripeStateBeforeMutation(string scenario)
    {
        var companyId = Guid.NewGuid();
        var boundary = new DateTime(2026, 9, 30, 22, 0, 0, DateTimeKind.Utc);
        var payment = AdditionalPayment(companyId, boundary);
        var posts = 0;
        using var http = new HttpClient(new Handler(request =>
        {
            if (request.Method != HttpMethod.Get) posts++;
            return Task.FromResult(ExistingStripeObject(request.RequestUri!.AbsolutePath, companyId, boundary, scenario));
        }));
        var gateway = new StripeMachineRequestSubscriptionGateway(Settings,
            new StripeClient("sk_test_local", httpClient: new SystemNetHttpClient(http, maxNetworkRetries: 0)));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => gateway.ReadAdditionalContextAsync(
            payment, "cus_existing", "sub_existing", 2, default));
        Assert.AreEqual(0, posts);
    }

    [TestMethod]
    public async Task AdditionalContextUsesExistingRecurringPaymentMethodWithoutChangingIt()
    {
        var companyId = Guid.NewGuid();
        var boundary = new DateTime(2026, 9, 30, 22, 0, 0, DateTimeKind.Utc);
        var payment = AdditionalPayment(companyId, boundary);
        var posts = 0;
        using var http = new HttpClient(new Handler(request =>
        {
            if (request.Method != HttpMethod.Get) posts++;
            return Task.FromResult(ExistingStripeObject(request.RequestUri!.AbsolutePath, companyId, boundary, null));
        }));
        var gateway = new StripeMachineRequestSubscriptionGateway(Settings,
            new StripeClient("sk_test_local", httpClient: new SystemNetHttpClient(http, maxNetworkRetries: 0)));

        var context = await gateway.ReadAdditionalContextAsync(payment, "cus_existing", "sub_existing", 2, default);

        Assert.AreEqual("pm_recurring", context.PaymentMethodId);
        Assert.AreEqual("sub_existing", context.SubscriptionId);
        Assert.AreEqual(0, posts);
    }

    [TestMethod]
    public async Task ExistingSubscriptionQuantityUpdateUsesNoneAndStableIdempotencyKey()
    {
        var payment = new WebApp.Api.Services.MachineRequestPayment(Guid.NewGuid(), 416, 13412, "EUR", null,
            "cs_test", "pi_test", "captured", DateTime.UtcNow, DateTime.UtcNow)
        {
            CompanyId = Guid.NewGuid(), MachineId = Guid.NewGuid(),
            FirstPeriodEndUtc = new DateTime(2026, 9, 30, 22, 0, 0, DateTimeKind.Utc)
        };
        var unix = new DateTimeOffset(payment.FirstPeriodEndUtc.Value).ToUnixTimeSeconds();
        var reads = 0; string? body = null; string? key = null;
        using var http = new HttpClient(new Handler(async request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                Assert.AreEqual("/v1/subscription_items/si_existing", request.RequestUri!.AbsolutePath);
                body = Uri.UnescapeDataString(await request.Content!.ReadAsStringAsync());
                key = request.Headers.GetValues("Idempotency-Key").Single();
                return Json(new { id = "si_existing", @object = "subscription_item", quantity = 2 });
            }
            reads++;
            return Json(new
            {
                id = "sub_existing", @object = "subscription", livemode = false,
                customer = "cus_existing", default_payment_method = "pm_recurring", status = "active",
                collection_method = "charge_automatically",
                items = new { @object = "list", has_more = false, data = new[]
                {
                    new { id = "si_existing", @object = "subscription_item", quantity = reads == 1 ? 1 : 2,
                        current_period_start = unix - 2592000, current_period_end = unix,
                        price = new { id = "price_local", @object = "price" } }
                } }
            });
        }));
        var gateway = new StripeMachineRequestSubscriptionGateway(Settings,
            new StripeClient("sk_test_local", httpClient: new SystemNetHttpClient(http, maxNetworkRetries: 0)));
        var context = new MachineRequestSubscriptionContext("cus_existing", "pm_recurring", "sub_existing", 2);

        var result = await gateway.SetQuantityAsync(payment, context, default);

        Assert.AreEqual(2, result.Quantity);
        StringAssert.Contains(body!, "quantity=2");
        StringAssert.Contains(body, "proration_behavior=none");
        Assert.AreEqual($"diaglink:machine-request-payment:{payment.PaymentRequestId:N}:quantity", key);
        Assert.AreEqual(2, reads);
    }

    [TestMethod]
    [DataRow(2, false)]
    [DataRow(0, true)]
    public async Task ExistingQuantityAllowsOnlyTargetOrTargetMinusOne(int currentQuantity, bool mustFail)
    {
        var payment = AdditionalPayment(Guid.NewGuid(), new DateTime(2026, 9, 30, 22, 0, 0, DateTimeKind.Utc));
        var unix = new DateTimeOffset(payment.FirstPeriodEndUtc!.Value).ToUnixTimeSeconds(); var posts = 0;
        using var http = new HttpClient(new Handler(request =>
        {
            if (request.Method == HttpMethod.Post) posts++;
            return Task.FromResult(Json(new { id = "sub_existing", @object = "subscription", livemode = false,
                customer = "cus_existing", default_payment_method = "pm_recurring", status = "active",
                items = new { @object = "list", has_more = false, data = new[] { new { id = "si_existing",
                    @object = "subscription_item", quantity = currentQuantity, current_period_start = unix - 2592000,
                    current_period_end = unix, price = new { id = "price_local", @object = "price" } } } } }));
        }));
        var gateway = new StripeMachineRequestSubscriptionGateway(Settings,
            new StripeClient("sk_test_local", httpClient: new SystemNetHttpClient(http, maxNetworkRetries: 0)));
        var context = new MachineRequestSubscriptionContext("cus_existing", "pm_recurring", "sub_existing", 2);

        if (mustFail)
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => gateway.SetQuantityAsync(payment, context, default));
        else
            Assert.AreEqual(2, (await gateway.SetQuantityAsync(payment, context, default)).Quantity);
        Assert.AreEqual(0, posts);
    }

    [TestMethod]
    public async Task FirstSubscriptionUsesTrialBoundaryWithoutExplicitAnchorOrImmediateBilling()
    {
        var payment = new WebApp.Api.Services.MachineRequestPayment(Guid.NewGuid(), 416, 13412, "EUR", null,
            "cs_test", "pi_test", "captured", DateTime.UtcNow, DateTime.UtcNow)
        {
            CompanyId = Guid.NewGuid(), MachineId = Guid.NewGuid()
        };
        var boundary = new DateTime(2026, 9, 30, 22, 0, 0, DateTimeKind.Utc);
        var boundaryUnix = new DateTimeOffset(boundary).ToUnixTimeSeconds();
        string? createBody = null;
        string? idempotencyKey = null;
        var createCalls = 0;
        using var http = new HttpClient(new Handler(async request =>
        {
            if (request.Method == HttpMethod.Get)
                return Json(new { @object = "list", has_more = false, data = Array.Empty<object>() });

            createCalls++;
            Assert.AreEqual("/v1/subscriptions", request.RequestUri!.AbsolutePath);
            createBody = Uri.UnescapeDataString(await request.Content!.ReadAsStringAsync());
            idempotencyKey = request.Headers.GetValues("Idempotency-Key").Single();
            return Json(new
            {
                id = "sub_request", @object = "subscription", livemode = false,
                customer = "cus_checkout", default_payment_method = "pm_checkout", status = "trialing",
                trial_end = boundaryUnix, billing_cycle_anchor = boundaryUnix,
                items = new { @object = "list", has_more = false, data = new[]
                {
                    new { id = "si_request", @object = "subscription_item", quantity = 1,
                        current_period_start = boundaryUnix - 86400, current_period_end = boundaryUnix,
                        price = new { id = "price_local", @object = "price" } }
                } }
            });
        }));
        var gateway = new StripeMachineRequestSubscriptionGateway(Settings,
            new StripeClient("sk_test_local", httpClient: new SystemNetHttpClient(http, maxNetworkRetries: 0)));

        var result = await gateway.CreateAsync(payment,
            new MachineRequestSubscriptionContext("cus_checkout", "pm_checkout", null, 1), boundary, default);

        Assert.AreEqual("sub_request", result.Id);
        Assert.AreEqual(1, createCalls);
        Assert.AreEqual($"diaglink:machine-request-payment:{payment.PaymentRequestId:N}:subscription:v2", idempotencyKey);
        StringAssert.Contains(createBody!, $"trial_end={boundaryUnix}");
        StringAssert.Contains(createBody!, "proration_behavior=none");
        StringAssert.Contains(createBody!, "collection_method=charge_automatically");
        StringAssert.Contains(createBody!, "items[0][price]=price_local");
        StringAssert.Contains(createBody!, "items[0][quantity]=1");
        Assert.IsFalse(createBody!.Contains("billing_cycle_anchor", StringComparison.Ordinal));
        Assert.IsFalse(createBody.Contains("add_invoice_items", StringComparison.Ordinal));
        Assert.IsFalse(createBody.Contains("invoice_now", StringComparison.Ordinal));
        Assert.IsFalse(createBody.Contains("payment_behavior", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task CreatesFirstSubscriptionOnceAndAdvancesOnlySubscriptionCheckpoint()
    {
        await using var f = new Fixture(); await f.Seed();
        var payment = await Seed(f, existingSubscription: false); var remote = new Remote();
        await using var before = f.Db(); var periods = await before.MachineBillingPeriods.CountAsync(); var credits = await before.CreditLedger.CountAsync();
        await using var db = f.Db(); var service = Service(f, db, remote);
        var first = await service.ConfigureAsync(payment.Id, default); var retry = await service.ConfigureAsync(payment.Id, default);
        Assert.AreEqual(MachineRequestProvisioningStage.SubscriptionCreated, first!.ProvisioningStage);
        Assert.AreEqual(MachineRequestProvisioningStage.SubscriptionCreated, retry!.ProvisioningStage);
        Assert.AreEqual(1, remote.Creates); Assert.AreEqual(1, remote.QuantityWrites);
        Assert.AreEqual(payment.FirstPeriodEndUtc, remote.Boundary); Assert.AreEqual(1, remote.Target);
        await using var check = f.Db(); var account = await check.BillingAccounts.SingleAsync();
        Assert.AreEqual("sub_request", account.StripeSubscriptionId);
        Assert.AreEqual(periods, await check.MachineBillingPeriods.CountAsync());
        Assert.AreEqual(credits, await check.CreditLedger.CountAsync());
    }

    [TestMethod]
    public async Task ExistingSubscriptionUsesDeterministicTargetAndRetryDoesNotIncrementAgain()
    {
        await using var f = new Fixture(); await f.Seed();
        var payment = await Seed(f, existingSubscription: true); var remote = new Remote { Existing = true };
        await using var db = f.Db(); var service = Service(f, db, remote);
        await service.ConfigureAsync(payment.Id, default); await service.ConfigureAsync(payment.Id, default);
        Assert.AreEqual(2, remote.QuantityWrites); Assert.AreEqual(1, remote.Target);
        await using var check = f.Db(); Assert.AreEqual("sub_existing", (await check.BillingAccounts.SingleAsync()).StripeSubscriptionId);
    }

    [TestMethod]
    public async Task WrongStageStopsBeforeStripe()
    {
        await using var f = new Fixture(); await f.Seed(); var payment = await Seed(f, false, MachineRequestProvisioningStage.BusinessEntitiesCreated);
        var remote = new Remote(); await using var db = f.Db();
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Service(f, db, remote).ConfigureAsync(payment.Id, default));
        Assert.AreEqual(0, remote.Reads);
    }

    private static MachineRequestSubscriptionService Service(Fixture f, WebApp.Api.Data.DiagLinkDbContext db, Remote remote) =>
        new(f.Options, new MachineRequestPaymentStore(db), new StripeBillingService(f.Options, new ForbiddenBillingGateway(), Settings), remote,
            new AdditionalMachinePaymentContextResolver(db, new DiagLinkUserLookupService(db)));
    private static StripeBillingOptions Settings => new() { Enabled = true, SecretKey = "sk_test_local", PriceId = "price_local" };

    private static WebApp.Api.Services.MachineRequestPayment AdditionalPayment(Guid companyId, DateTime boundary) =>
        new(Guid.NewGuid(), 416, 13412, "EUR", null, "cs_test", "pi_test", "captured", DateTime.UtcNow, DateTime.UtcNow)
        { CompanyId = companyId, MachineId = Guid.NewGuid(), FirstPeriodEndUtc = boundary };

    private static HttpResponseMessage ExistingStripeObject(string path, Guid companyId, DateTime boundary, string? scenario)
    {
        var unix = new DateTimeOffset(boundary).ToUnixTimeSeconds();
        return path switch
        {
            "/v1/payment_intents/pi_test" => Json(new { id = "pi_test", @object = "payment_intent", livemode = false,
                status = "succeeded", customer = scenario == "payment-customer" ? "cus_other" : "cus_existing", payment_method = "pm_checkout" }),
            "/v1/customers/cus_existing" => Json(new { id = "cus_existing", @object = "customer", livemode = false,
                deleted = false, metadata = new Dictionary<string, string> { ["diaglink_company_id"] = companyId.ToString() },
                invoice_settings = new { default_payment_method = "pm_recurring" } }),
            "/v1/subscriptions/sub_existing" => Json(new { id = "sub_existing", @object = "subscription", livemode = false,
                customer = scenario == "subscription-customer" ? "cus_other" : "cus_existing",
                default_payment_method = "pm_recurring", status = "active",
                collection_method = scenario == "collection" ? "send_invoice" : "charge_automatically",
                metadata = new Dictionary<string, string> { ["diaglink_company_id"] = companyId.ToString() },
                items = new { @object = "list", has_more = false, data = new[] { new { id = "si_existing",
                    @object = "subscription_item", quantity = 1, current_period_start = unix - 2592000,
                    current_period_end = scenario == "period" ? unix + 1 : unix,
                    price = new { id = scenario == "price" ? "price_other" : "price_local", @object = "price" } } } } }),
            "/v1/payment_methods/pm_recurring" => Json(new { id = "pm_recurring", @object = "payment_method", livemode = false, customer = "cus_existing", type = "card" }),
            _ => throw new AssertFailedException($"Unexpected Stripe path {path}.")
        };
    }

    private static async Task<PaymentEntity> Seed(Fixture f, bool existingSubscription,
        MachineRequestProvisioningStage stage = MachineRequestProvisioningStage.CustomerLinked)
    {
        var now = new DateTime(2026, 9, 22, 12, 0, 0, DateTimeKind.Utc); var boundary = new DateTime(2026, 9, 30, 22, 0, 0, DateTimeKind.Utc);
        await using var db = f.Db(); var company = await db.Companies.SingleAsync(c => c.Id == f.CompanyId); company.Status = "active";
        var machine = await db.Machines.SingleAsync(m => m.Id == f.MachineId); machine.Status = "active";
        db.BillingAccounts.Add(new BillingAccount { Id = Guid.NewGuid(), CompanyId = f.CompanyId, StripeCustomerId = "cus_checkout",
            StripeSubscriptionId = existingSubscription ? "sub_existing" : null, CreatedAtUtc = now, UpdatedAtUtc = now });
        await db.SaveChangesAsync();
        var payment = new PaymentEntity { Id = Guid.NewGuid(), Status = MachineRequestPaymentStatus.Captured,
            EstimatedTotalPages = 400, AmountCents = 12980, Currency = "EUR", StripeSessionId = "cs_test",
            StripePaymentIntentId = "pi_test", AuthorizationEventId = "evt_test", MachineRequestId = "request",
            RequestLinkedAtUtc = now, CompanyId = f.CompanyId, MachineId = f.MachineId, ActivatedAtUtc = now,
            FirstPeriodEndUtc = boundary, ServiceAmountCents = 500, FinalCaptureAmountCents = 11490,
            CreatedAtUtc = now, UpdatedAtUtc = now, AuthorizedAtUtc = now, CapturedAtUtc = now,
            ProvisioningStage = stage, RowVersion = [1] };
        await db.Database.ExecuteSqlInterpolatedAsync($@"INSERT INTO MachineRequestPayments
          (Id,Status,EstimatedTotalPages,AmountCents,Currency,StripeSessionId,StripePaymentIntentId,AuthorizationEventId,
           MachineRequestId,RequestLinkedAtUtc,CreatedAtUtc,UpdatedAtUtc,AuthorizedAtUtc,CapturedAtUtc,ActivatedAtUtc,
           FirstPeriodEndUtc,ServiceAmountCents,FinalCaptureAmountCents,CompanyId,MachineId,ProvisioningStage,RowVersion)
          VALUES ({payment.Id},{(int)payment.Status},{payment.EstimatedTotalPages},{payment.AmountCents},{payment.Currency},
           {payment.StripeSessionId},{payment.StripePaymentIntentId},{payment.AuthorizationEventId},{payment.MachineRequestId},
           {payment.RequestLinkedAtUtc},{payment.CreatedAtUtc},{payment.UpdatedAtUtc},{payment.AuthorizedAtUtc},{payment.CapturedAtUtc},
           {payment.ActivatedAtUtc},{payment.FirstPeriodEndUtc},{payment.ServiceAmountCents},{payment.FinalCaptureAmountCents},
           {payment.CompanyId},{payment.MachineId},{(int)payment.ProvisioningStage},{payment.RowVersion})");
        return payment;
    }

    private sealed class Remote : IMachineRequestSubscriptionGateway
    {
        public bool Existing; public int Reads, Creates, QuantityWrites, Target; public DateTime? Boundary;
        public Task ValidatePriceAsync(CancellationToken ct) => Task.CompletedTask;
        public Task<MachineRequestSubscriptionContext> ReadContextAsync(WebApp.Api.Services.MachineRequestPayment payment, string customerId, string? subscriptionId, int targetQuantity, CancellationToken ct)
        { Reads++; Target = targetQuantity; return Task.FromResult(new MachineRequestSubscriptionContext(customerId, "pm_checkout", subscriptionId, targetQuantity)); }
        public Task<StripeSubscriptionSnapshot> CreateAsync(WebApp.Api.Services.MachineRequestPayment payment, MachineRequestSubscriptionContext context, DateTime firstPeriodEndUtc, CancellationToken ct)
        { Creates++; Boundary = firstPeriodEndUtc; return Task.FromResult(Snapshot("sub_request", context, firstPeriodEndUtc)); }
        public Task<StripeSubscriptionSnapshot> ReadAsync(WebApp.Api.Services.MachineRequestPayment payment, MachineRequestSubscriptionContext context, CancellationToken ct)
            => Task.FromResult(Snapshot(context.SubscriptionId!, context, payment.FirstPeriodEndUtc!.Value));
        public Task<StripeSubscriptionSnapshot> SetQuantityAsync(WebApp.Api.Services.MachineRequestPayment payment, MachineRequestSubscriptionContext context, CancellationToken ct)
        { QuantityWrites++; return Task.FromResult(Snapshot(context.SubscriptionId!, context, payment.FirstPeriodEndUtc!.Value)); }
        private static StripeSubscriptionSnapshot Snapshot(string id, MachineRequestSubscriptionContext context, DateTime end) =>
            new(id, context.CustomerId, "active", end.AddMonths(-1), end, context.TargetQuantity) { ItemId = "si_test" };
    }
    private sealed class ForbiddenBillingGateway : IStripeBillingGateway
    {
        private static Task<T> No<T>() => Task.FromException<T>(new AssertFailedException("Generic Stripe flow forbidden."));
        public Task ValidatePriceAsync(CancellationToken ct) => Task.FromException(new AssertFailedException());
        public Task<string> CreateCustomerAsync(Guid companyId, Guid accountId, CancellationToken ct) => No<string>();
        public Task<string> GetCustomerAsync(string customerId, Guid companyId, CancellationToken ct) => No<string>();
        public Task<StripeSubscriptionSnapshot> CreateSubscriptionAsync(string customerId, Guid companyId, Guid accountId, int quantity, CancellationToken ct) => No<StripeSubscriptionSnapshot>();
        public Task<StripeSubscriptionSnapshot> GetSubscriptionAsync(string subscriptionId, string customerId, Guid companyId, CancellationToken ct) => No<StripeSubscriptionSnapshot>();
        public Task PrepareInitialPaymentAsync(string subscriptionId, string customerId, Guid companyId, CancellationToken ct) => Task.FromException(new AssertFailedException());
    }

    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK)
    { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }
}
