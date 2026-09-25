using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Stripe;
using WebApp.Api.Models.Entities;
using WebApp.Api.Services;
using PaymentEntity = WebApp.Api.Models.Entities.MachineRequestPayment;
using Fixture = WebApp.Api.Tests.AiCreditConsumptionServiceTests.Fixture;

namespace WebApp.Api.Tests;

[TestClass]
public sealed class MachineRequestCustomerLinkTests
{
    [TestMethod]
    public async Task LinksExistingCheckoutCustomerAndIsRetryableWithoutCreatingSubscription()
    {
        await using var f = new Fixture(); await f.Seed();
        var payment = await SeedPayment(f, MachineRequestPaymentStatus.Captured, MachineRequestProvisioningStage.BusinessEntitiesCreated);
        var stripe = new CustomerGateway();
        await using var before = f.Db();
        var periodCount = await before.MachineBillingPeriods.CountAsync();
        var creditCount = await before.CreditLedger.CountAsync();
        await using var db = f.Db();
        var service = new MachineRequestCustomerLinkService(new MachineRequestPaymentStore(db),
            new StripeBillingService(f.Options, new ForbiddenBillingGateway(), Settings), stripe,
            new AdditionalMachinePaymentContextResolver(db, new DiagLinkUserLookupService(db)));

        var first = await service.LinkAsync(payment.Id, default);
        var second = await service.LinkAsync(payment.Id, default);

        Assert.AreEqual(MachineRequestProvisioningStage.CustomerLinked, first!.ProvisioningStage);
        Assert.AreEqual(MachineRequestProvisioningStage.CustomerLinked, second!.ProvisioningStage);
        Assert.AreEqual(2, stripe.Reads); Assert.AreEqual(2, stripe.Configurations);
        await using var check = f.Db();
        var account = await check.BillingAccounts.SingleAsync();
        Assert.AreEqual("cus_checkout", account.StripeCustomerId);
        Assert.IsNull(account.StripeSubscriptionId);
        Assert.AreEqual(periodCount, await check.MachineBillingPeriods.CountAsync());
        Assert.AreEqual(creditCount, await check.CreditLedger.CountAsync());
    }

    [TestMethod]
    public async Task DifferentBillingCustomerStopsBeforeStripeMutationAndStageChange()
    {
        await using var f = new Fixture(); await f.Seed();
        var payment = await SeedPayment(f, MachineRequestPaymentStatus.Captured, MachineRequestProvisioningStage.BusinessEntitiesCreated);
        await using (var seed = f.Db())
        {
            seed.BillingAccounts.Add(new BillingAccount { Id = Guid.NewGuid(), CompanyId = f.CompanyId,
                StripeCustomerId = "cus_other", CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow });
            await seed.SaveChangesAsync();
        }
        var stripe = new CustomerGateway(); await using var db = f.Db();
        var service = new MachineRequestCustomerLinkService(new MachineRequestPaymentStore(db),
            new StripeBillingService(f.Options, new ForbiddenBillingGateway(), Settings), stripe,
            new AdditionalMachinePaymentContextResolver(db, new DiagLinkUserLookupService(db)));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service.LinkAsync(payment.Id, default));
        Assert.AreEqual(0, stripe.Configurations);
        await using var check = f.Db();
        Assert.AreEqual(MachineRequestProvisioningStage.BusinessEntitiesCreated,
            (await check.MachineRequestPayments.SingleAsync()).ProvisioningStage);
    }

    [TestMethod]
    [DataRow(MachineRequestPaymentStatus.Captured, MachineRequestProvisioningStage.AmountFinalized)]
    public async Task InvalidPreconditionsDoNotCallStripe(MachineRequestPaymentStatus status, MachineRequestProvisioningStage stage)
    {
        await using var f = new Fixture(); await f.Seed(); var payment = await SeedPayment(f, status, stage);
        var stripe = new CustomerGateway(); await using var db = f.Db();
        var service = new MachineRequestCustomerLinkService(new MachineRequestPaymentStore(db),
            new StripeBillingService(f.Options, new ForbiddenBillingGateway(), Settings), stripe,
            new AdditionalMachinePaymentContextResolver(db, new DiagLinkUserLookupService(db)));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service.LinkAsync(payment.Id, default));
        Assert.AreEqual(0, stripe.Reads);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AdditionalCustomerValidation_AllowsUnattachedPaymentMethodAndHistoricalMetadata(bool includeCompanyMetadata)
    {
        var payment = StripePayment();
        var requests = new List<(HttpMethod Method, string Path)>();
        using var http = CustomerValidationHttp(payment, requests, paymentMethodCustomer: null,
            customerMetadataCompanyId: includeCompanyMetadata ? payment.CompanyId!.Value.ToString() : null);
        var gateway = new StripeMachineRequestCustomerLinkGateway(Settings,
            new StripeClient("sk_test_local", httpClient: new SystemNetHttpClient(http, maxNetworkRetries: 0)));

        var identity = await gateway.ReadAndValidateExistingCustomerAsync(payment, "cus_existing", default);

        Assert.AreEqual("cus_existing", identity.CustomerId);
        Assert.AreEqual("pm_one_time", identity.PaymentMethodId);
        Assert.IsTrue(requests.All(request => request.Method == HttpMethod.Get));
        CollectionAssert.AreEquivalent(new[]
        {
            "/v1/checkout/sessions/cs_test", "/v1/payment_intents/pi_test",
            "/v1/payment_methods/pm_one_time", "/v1/customers/cus_existing"
        }, requests.Select(request => request.Path).ToArray());
        Assert.IsFalse(requests.Any(request => request.Path.Contains("subscriptions", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task AdditionalCustomerValidation_RejectsPaymentIntentCustomerDifferentFromBillingAccount()
    {
        var payment = StripePayment();
        var requests = new List<(HttpMethod Method, string Path)>();
        using var http = CustomerValidationHttp(payment, requests, sessionCustomer: "cus_other", intentCustomer: "cus_other");
        var gateway = new StripeMachineRequestCustomerLinkGateway(Settings,
            new StripeClient("sk_test_local", httpClient: new SystemNetHttpClient(http, maxNetworkRetries: 0)));

        var error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            gateway.ReadAndValidateExistingCustomerAsync(payment, "cus_existing", default));

        Assert.AreEqual("Le PaymentIntent n'appartient pas au Customer Stripe de l'entreprise.", error.Message);
        Assert.IsTrue(requests.All(request => request.Method == HttpMethod.Get));
    }

    [TestMethod]
    public async Task AdditionalCustomerValidation_RejectsSessionCustomerDifferentFromPaymentIntent()
    {
        var payment = StripePayment();
        var requests = new List<(HttpMethod Method, string Path)>();
        using var http = CustomerValidationHttp(payment, requests, sessionCustomer: "cus_other");
        var gateway = new StripeMachineRequestCustomerLinkGateway(Settings,
            new StripeClient("sk_test_local", httpClient: new SystemNetHttpClient(http, maxNetworkRetries: 0)));

        var error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            gateway.ReadAndValidateExistingCustomerAsync(payment, "cus_existing", default));

        Assert.AreEqual("Les Customers Stripe de la Session et du PaymentIntent ne correspondent pas.", error.Message);
    }

    [TestMethod]
    public async Task AdditionalCustomerValidation_RejectsConflictingCompanyMetadata()
    {
        var payment = StripePayment();
        var requests = new List<(HttpMethod Method, string Path)>();
        using var http = CustomerValidationHttp(payment, requests, paymentMethodCustomer: null,
            customerMetadataCompanyId: Guid.NewGuid().ToString());
        var gateway = new StripeMachineRequestCustomerLinkGateway(Settings,
            new StripeClient("sk_test_local", httpClient: new SystemNetHttpClient(http, maxNetworkRetries: 0)));

        var error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            gateway.ReadAndValidateExistingCustomerAsync(payment, "cus_existing", default));

        Assert.AreEqual("Le Customer Stripe n'appartient pas à l'entreprise attendue.", error.Message);
        Assert.IsTrue(requests.All(request => request.Method == HttpMethod.Get));
    }

    [TestMethod]
    public async Task InitialMachineCustomerValidation_StillRejectsUnattachedPaymentMethod()
    {
        var payment = StripePayment() with { RequestKind = WebApp.Api.Models.MachineRequestKind.InitialMachine };
        var requests = new List<(HttpMethod Method, string Path)>();
        using var http = CustomerValidationHttp(payment, requests, paymentMethodCustomer: null);
        var gateway = new StripeMachineRequestCustomerLinkGateway(Settings,
            new StripeClient("sk_test_local", httpClient: new SystemNetHttpClient(http, maxNetworkRetries: 0)));

        var error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => gateway.ReadAndValidateAsync(payment, default));

        Assert.AreEqual("Le PaymentMethod Stripe n'appartient pas au Customer attendu.", error.Message);
    }

    private static StripeBillingOptions Settings => new() { Enabled = true, SecretKey = "sk_test_local", PriceId = "price_local" };
    private static WebApp.Api.Services.MachineRequestPayment StripePayment()
    {
        var companyId = Guid.NewGuid();
        return new(Guid.NewGuid(), 400, 12980, "EUR", null, "cs_test", "pi_test", "captured",
            DateTime.UtcNow, FinalCaptureAmountCents: 11403, CompanyId: companyId,
            RequestKind: WebApp.Api.Models.MachineRequestKind.AdditionalMachine, RequestedByUserId: Guid.NewGuid());
    }

    private static HttpClient CustomerValidationHttp(WebApp.Api.Services.MachineRequestPayment payment,
        List<(HttpMethod Method, string Path)> requests, string sessionCustomer = "cus_existing",
        string intentCustomer = "cus_existing", string? paymentMethodCustomer = "cus_existing",
        string? customerMetadataCompanyId = null)
    {
        var metadata = new Dictionary<string, string>
        {
            ["diaglink_payment_type"] = "machine_request_preparation",
            ["diaglink_payment_request_id"] = payment.PaymentRequestId.ToString(),
            ["diaglink_server_amount_cents"] = payment.AmountCents.ToString()
        };
        return new HttpClient(new StripeHandler(request =>
        {
            requests.Add((request.Method, request.RequestUri!.AbsolutePath));
            object body = request.RequestUri.AbsolutePath switch
            {
                "/v1/checkout/sessions/cs_test" => new { id = "cs_test", @object = "checkout.session", livemode = false,
                    mode = "payment", client_reference_id = payment.PaymentRequestId.ToString(), currency = "eur",
                    amount_total = payment.AmountCents, customer = sessionCustomer, payment_intent = "pi_test", metadata },
                "/v1/payment_intents/pi_test" => new { id = "pi_test", @object = "payment_intent", livemode = false,
                    status = "succeeded", capture_method = "manual", currency = "eur", amount = payment.AmountCents,
                    amount_received = payment.FinalCaptureAmountCents, amount_capturable = 0, customer = intentCustomer,
                    payment_method = "pm_one_time", metadata },
                "/v1/payment_methods/pm_one_time" => new { id = "pm_one_time", @object = "payment_method",
                    livemode = false, customer = paymentMethodCustomer, type = "card" },
                "/v1/customers/cus_existing" or "/v1/customers/cus_other" => new { id = intentCustomer,
                    @object = "customer", livemode = false, deleted = false,
                    metadata = customerMetadataCompanyId is null ? null : new Dictionary<string, string>
                        { ["diaglink_company_id"] = customerMetadataCompanyId } },
                _ => throw new AssertFailedException($"Unexpected Stripe request {request.Method} {request.RequestUri.AbsolutePath}")
            };
            return Json(body);
        }));
    }

    private static HttpResponseMessage Json(object body) => new(HttpStatusCode.OK)
        { Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json") };

    private sealed class StripeHandler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(send(request));
    }
    private static async Task<PaymentEntity> SeedPayment(Fixture f, MachineRequestPaymentStatus status, MachineRequestProvisioningStage stage)
    {
        var now = DateTime.UtcNow; await using var db = f.Db();
        (await db.Companies.SingleAsync(company => company.Id == f.CompanyId)).Status = "active";
        await db.SaveChangesAsync();
        var payment = new PaymentEntity { Id = Guid.NewGuid(), Status = status, EstimatedTotalPages = 400,
            AmountCents = 12980, Currency = "EUR", StripeSessionId = "cs_test", StripePaymentIntentId = "pi_test",
            AuthorizationEventId = "evt_test", MachineRequestId = "request", RequestLinkedAtUtc = now,
            CompanyId = stage >= MachineRequestProvisioningStage.BusinessEntitiesCreated ? f.CompanyId : null,
            MachineId = stage >= MachineRequestProvisioningStage.BusinessEntitiesCreated ? f.MachineId : null,
            ActivatedAtUtc = now, FirstPeriodEndUtc = now.AddDays(5), ServiceAmountCents = 500,
            FinalCaptureAmountCents = 11490, CreatedAtUtc = now, UpdatedAtUtc = now,
            AuthorizedAtUtc = now, CapturedAtUtc = status == MachineRequestPaymentStatus.Captured ? now : null,
            CancelledAtUtc = status == MachineRequestPaymentStatus.Cancelled ? now : null, ProvisioningStage = stage,
            RowVersion = [1] };
        await db.Database.ExecuteSqlInterpolatedAsync($@"INSERT INTO MachineRequestPayments
            (Id, Status, EstimatedTotalPages, AmountCents, Currency, StripeSessionId, StripePaymentIntentId,
             AuthorizationEventId, MachineRequestId, RequestLinkedAtUtc, CreatedAtUtc, UpdatedAtUtc, AuthorizedAtUtc, CapturedAtUtc,
             CancelledAtUtc, ActivatedAtUtc, FirstPeriodEndUtc, ServiceAmountCents, FinalCaptureAmountCents,
             CompanyId, MachineId, ProvisioningStage, RowVersion)
            VALUES ({payment.Id}, {(int)payment.Status}, {payment.EstimatedTotalPages}, {payment.AmountCents},
             {payment.Currency}, {payment.StripeSessionId}, {payment.StripePaymentIntentId}, {payment.AuthorizationEventId},
             {payment.MachineRequestId}, {payment.RequestLinkedAtUtc}, {payment.CreatedAtUtc}, {payment.UpdatedAtUtc}, {payment.AuthorizedAtUtc},
             {payment.CapturedAtUtc}, {payment.CancelledAtUtc}, {payment.ActivatedAtUtc}, {payment.FirstPeriodEndUtc},
             {payment.ServiceAmountCents}, {payment.FinalCaptureAmountCents}, {payment.CompanyId}, {payment.MachineId},
             {(int)payment.ProvisioningStage}, {payment.RowVersion})");
        return payment;
    }
    private sealed class CustomerGateway : IMachineRequestCustomerLinkGateway
    {
        public int Reads, Configurations;
        public Task<MachineRequestStripeIdentity> ReadAndValidateAsync(WebApp.Api.Services.MachineRequestPayment payment, CancellationToken ct)
        { Reads++; return Task.FromResult(new MachineRequestStripeIdentity("cus_checkout", "pm_checkout")); }
        public Task ConfirmCustomerConfigurationAsync(WebApp.Api.Services.MachineRequestPayment payment, Guid companyId, MachineRequestStripeIdentity identity, CancellationToken ct)
        { Configurations++; return Task.CompletedTask; }
    }
    private sealed class ForbiddenBillingGateway : IStripeBillingGateway
    {
        private static Task<T> No<T>() => Task.FromException<T>(new AssertFailedException("No generic Stripe creation/read expected."));
        public Task ValidatePriceAsync(CancellationToken ct) => Task.FromException(new AssertFailedException("No generic Stripe creation/read expected."));
        public Task<string> CreateCustomerAsync(Guid companyId, Guid accountId, CancellationToken ct) => No<string>();
        public Task<string> GetCustomerAsync(string customerId, Guid companyId, CancellationToken ct) => No<string>();
        public Task<StripeSubscriptionSnapshot> CreateSubscriptionAsync(string customerId, Guid companyId, Guid accountId, int quantity, CancellationToken ct) => No<StripeSubscriptionSnapshot>();
        public Task<StripeSubscriptionSnapshot> GetSubscriptionAsync(string subscriptionId, string customerId, Guid companyId, CancellationToken ct) => No<StripeSubscriptionSnapshot>();
        public Task PrepareInitialPaymentAsync(string subscriptionId, string customerId, Guid companyId, CancellationToken ct) => Task.FromException(new AssertFailedException("No generic Stripe creation/read expected."));
    }
}
