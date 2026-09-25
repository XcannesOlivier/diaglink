using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Stripe;
using WebApp.Api.Data;
using WebApp.Api.Models;
using WebApp.Api.Models.Entities;
using WebApp.Api.Services;
using Payment = WebApp.Api.Services.MachineRequestPayment;

namespace WebApp.Api.Tests;

[TestClass]
public class AdditionalDocumentsPaymentTests
{
    private const string WebhookSecret = "whsec_additional_documents";

    [TestMethod]
    public async Task CheckoutUsesExistingCustomerExactDurableAmountManualCaptureMetadataAndStableKey()
    {
        var payment = PaymentRecord(); var context = Context(payment); string? body = null; string? key = null;
        using var http = new HttpClient(new Handler(async request =>
        {
            body = Uri.UnescapeDataString(await request.Content!.ReadAsStringAsync());
            key = request.Headers.GetValues("Idempotency-Key").Single();
            return Json(Session(payment, context.StripeCustomerId, "open"));
        }));
        await Gateway(http).CreateAdditionalDocumentsAsync(payment, context, default);
        StringAssert.Contains(body!, "customer=cus_existing");
        StringAssert.Contains(body, "success_url=http://localhost:5173/app/checkout-return");
        StringAssert.Contains(body, "cancel_url=http://localhost:5173/app/checkout-return");
        StringAssert.Contains(body, "payment_intent_data[capture_method]=manual");
        StringAssert.Contains(body, "line_items[0][price_data][unit_amount]=9990");
        StringAssert.Contains(body, "DiagLink+—+ajout+de+documentation+technique");
        Assert.IsFalse(body.Contains("customer_creation", StringComparison.Ordinal));
        Assert.IsFalse(body.Contains("customer_email", StringComparison.Ordinal));
        Assert.IsFalse(body.Contains("setup_future_usage", StringComparison.Ordinal));
        foreach (var pair in Metadata(payment)) StringAssert.Contains(body, $"metadata[{pair.Key}]={pair.Value}");
        Assert.AreEqual($"diaglink:machine-request-payment:{payment.PaymentRequestId:N}:checkout:additional-documents:v1", key);
    }

    [TestMethod]
    [DataRow(1, 50L)]
    [DataRow(3, 81L)]
    [DataRow(370, 9990L)]
    public async Task CheckoutSelectsAdditionalDocumentsPricingFromRequestKind(int pages, long amountCents)
    {
        var payment = PaymentRecord(pages, amountCents); string? body = null;
        using var http = new HttpClient(new Handler(async request =>
        {
            body = Uri.UnescapeDataString(await request.Content!.ReadAsStringAsync());
            return Json(Session(payment, "cus_existing", "open"));
        }));

        await Gateway(http).CreateAdditionalDocumentsAsync(payment, Context(payment), default);

        StringAssert.Contains(body!, $"line_items[0][price_data][unit_amount]={amountCents}");
        StringAssert.Contains(body, "metadata[diaglink_request_kind]=additional_documents");
        StringAssert.Contains(body, "metadata[diaglink_pricing_model]=additional_documents_per_page_v1");
        Assert.IsFalse(body.Contains("diaglink_authorization_model", StringComparison.Ordinal));
        Assert.IsFalse(body.Contains("diaglink_maximum_subscription_cents", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task CheckoutRejectsAdditionalDocumentsAmountThatDoesNotMatchServerPricing()
    {
        foreach (var invalidAmount in new[] { 27L, 51L })
        {
            var payment = PaymentRecord(1, invalidAmount); var requests = 0;
            using var http = new HttpClient(new Handler(_ =>
            {
                requests++;
                return Task.FromResult(Json(Session(payment, "cus_existing", "open")));
            }));

            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                Gateway(http).CreateAdditionalDocumentsAsync(payment, Context(payment), default));

            Assert.AreEqual(0, requests);
        }
    }

    [TestMethod]
    public async Task OnePageCheckoutReconciliationCaptureAndCancelUseExactMinimumAmount()
    {
        foreach (var terminalAction in new[] { "capture", "cancel" })
        {
            var payment = PaymentRecord(1, 50) with
            { StripeSessionId = "cs_docs", StripePaymentIntentId = "pi_docs", Status = "authorized" };
            var context = Context(payment); var stripeState = "authorized";
            using var http = new HttpClient(new Handler(request =>
            {
                if (request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath.Contains("checkout/sessions"))
                    return Task.FromResult(Json(Session(payment, "cus_existing", "complete")));
                if (request.Method == HttpMethod.Get) return Task.FromResult(Json(Intent(payment, stripeState)));
                stripeState = terminalAction == "capture" ? "captured" : "cancelled";
                return Task.FromResult(Json(Intent(payment, stripeState)));
            }));
            var gateway = Gateway(http);

            Assert.AreEqual("authorized", (await gateway.ReadAdditionalDocumentsAsync(payment, "cs_docs", context, default)).Status);
            var result = terminalAction == "capture"
                ? await gateway.CaptureAdditionalDocumentsAsync(payment, context, default)
                : await gateway.CancelAdditionalDocumentsAsync(payment, context, default);

            Assert.AreEqual(stripeState, result.Status);
        }
    }

    [TestMethod]
    public async Task VerifiedReadRequiresCustomerAmountCurrencyMetadataAndFullyCapturableIntent()
    {
        var payment = PaymentRecord() with { StripeSessionId = "cs_docs" }; var context = Context(payment);
        using (var valid = new HttpClient(new Handler(_ => Task.FromResult(Json(Session(payment, "cus_existing", "complete"))))))
            Assert.AreEqual("authorized", (await Gateway(valid).ReadAdditionalDocumentsAsync(payment, "cs_docs", context, default)).Status);
        foreach (var invalid in new[] { "customer", "amount", "currency", "metadata", "status", "capturable" })
        {
            using var http = new HttpClient(new Handler(_ => Task.FromResult(Json(Session(payment, "cus_existing", "complete", invalid)))));
            if (invalid is "status" or "capturable")
                Assert.AreEqual("pending", (await Gateway(http).ReadAdditionalDocumentsAsync(payment, "cs_docs", context, default)).Status);
            else
                await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                    Gateway(http).ReadAdditionalDocumentsAsync(payment, "cs_docs", context, default));
        }
    }

    [TestMethod]
    public async Task ServiceStartsOnlyOwnedValidPendingAttemptAndReconcilesWithoutCapture()
    {
        await using var fixture = await Fixture.CreateAsync();
        var gateway = new FakeGateway { ReadStatus = "authorized" };
        var service = fixture.Service(gateway);
        var started = await service.StartAdditionalDocumentsAsync(fixture.PaymentId, fixture.Context, default);
        Assert.AreEqual("pending", started.Status); Assert.IsNotNull(started.CheckoutUrl);
        var reconciled = await service.ReadAdditionalDocumentsAsync(fixture.PaymentId, fixture.Context, default);
        Assert.AreEqual("authorized", reconciled!.Status);
        Assert.AreEqual(1, gateway.CreateCalls); Assert.AreEqual(1, gateway.ReadCalls);
        Assert.AreEqual(0, gateway.CaptureCalls); Assert.AreEqual(0, gateway.CancelCalls);
        var entity = await fixture.Db.MachineRequestPayments.SingleAsync();
        Assert.AreEqual(MachineRequestPaymentStatus.Authorized, entity.Status);
        Assert.AreEqual("pi_docs", entity.StripePaymentIntentId);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service.CaptureAsync(fixture.PaymentId, default));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service.CancelAsync(fixture.PaymentId, default));
        Assert.AreEqual(0, gateway.CaptureCalls); Assert.AreEqual(0, gateway.CancelCalls);

        var wrong = fixture.Context with { UserId = Guid.NewGuid() };
        await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() =>
            service.ReadAdditionalDocumentsAsync(fixture.PaymentId, wrong, default));
    }

    [TestMethod]
    public async Task PendingStripeProofStaysPendingAndAuthorizedReadIsIdempotent()
    {
        await using var fixture = await Fixture.CreateAsync(); var gateway = new FakeGateway { ReadStatus = "pending" };
        var service = fixture.Service(gateway); await service.StartAdditionalDocumentsAsync(fixture.PaymentId, fixture.Context, default);
        Assert.AreEqual("pending", (await service.ReadAdditionalDocumentsAsync(fixture.PaymentId, fixture.Context, default))!.Status);
        gateway.ReadStatus = "authorized";
        Assert.AreEqual("authorized", (await service.ReadAdditionalDocumentsAsync(fixture.PaymentId, fixture.Context, default))!.Status);
        Assert.AreEqual("authorized", (await service.ReadAdditionalDocumentsAsync(fixture.PaymentId, fixture.Context, default))!.Status);
        Assert.AreEqual(2, gateway.ReadCalls);
    }

    [TestMethod]
    public async Task WebhookValidatesSignatureUsesVerifiedAdditionalDocumentsPathAndIsIdempotent()
    {
        await using var fixture = await Fixture.CreateAsync(); var gateway = new FakeGateway { ReadStatus = "authorized" };
        var service = fixture.Service(gateway); await service.StartAdditionalDocumentsAsync(fixture.PaymentId, fixture.Context, default);
        var webhook = new StripeMachineRequestPaymentWebhook(Settings(), service, NullLogger<StripeMachineRequestPaymentWebhook>.Instance);
        var body = Event(fixture.PaymentId).ToJsonString();
        Assert.AreEqual(400, (await webhook.HandleAsync(body, "bad")).HttpStatus);
        Assert.AreEqual("authorized", (await webhook.HandleAsync(body, Sign(body))).Status);
        Assert.AreEqual("authorized", (await webhook.HandleAsync(body, Sign(body))).Status);
        Assert.AreEqual(1, gateway.ReadCalls);
    }

    [TestMethod]
    public async Task InvalidKindAmountCurrencyOrMachineIsRejectedBeforeCheckout()
    {
        foreach (var invalid in new[] { "kind", "amount", "currency" })
        {
            await using var fixture = await Fixture.CreateAsync(invalid);
            var gateway = new FakeGateway(); var service = fixture.Service(gateway);
            await Assert.ThrowsAsync<Exception>(() => service.StartAdditionalDocumentsAsync(fixture.PaymentId, fixture.Context, default));
            Assert.AreEqual(0, gateway.CreateCalls);
        }
    }

    [TestMethod]
    public async Task GatewayCaptureAndCancelUseDedicatedKeysAndVerifyTerminalStripeState()
    {
        foreach (var action in new[] { "capture", "cancel" })
        {
            var payment = PaymentRecord() with { StripeSessionId = "cs_docs", StripePaymentIntentId = "pi_docs", Status = "authorized" };
            var context = Context(payment); var stripeState = "authorized"; var mutations = 0;
            using var http = new HttpClient(new Handler(request =>
            {
                if (request.Method == HttpMethod.Get) return Task.FromResult(Json(Intent(payment, stripeState)));
                mutations++; stripeState = action == "capture" ? "captured" : "cancelled";
                Assert.AreEqual($"/v1/payment_intents/pi_docs/{action}", request.RequestUri!.AbsolutePath);
                Assert.AreEqual($"diaglink:machine-request-payment:{payment.PaymentRequestId:N}:{action}:additional-documents:v1",
                    request.Headers.GetValues("Idempotency-Key").Single());
                return Task.FromResult(Json(Intent(payment, stripeState)));
            }));
            var proof = action == "capture"
                ? await Gateway(http).CaptureAdditionalDocumentsAsync(payment, context, default)
                : await Gateway(http).CancelAdditionalDocumentsAsync(payment, context, default);
            Assert.AreEqual(stripeState, proof.Status); Assert.AreEqual(1, mutations);
        }
    }

    [TestMethod]
    public async Task AbandonmentExpiresAnOpenCheckoutWithAStableKey()
    {
        var payment = PaymentRecord(1, 50) with { StripeSessionId = "cs_docs" };
        var mutations = 0;
        using var http = new HttpClient(new Handler(request =>
        {
            if (request.Method == HttpMethod.Get)
                return Task.FromResult(Json(Session(payment, "cus_existing", "open")));
            mutations++;
            Assert.AreEqual("/v1/checkout/sessions/cs_docs/expire", request.RequestUri!.AbsolutePath);
            Assert.AreEqual($"diaglink:machine-request-payment:{payment.PaymentRequestId:N}:expire-checkout:additional-documents:v1",
                request.Headers.GetValues("Idempotency-Key").Single());
            return Task.FromResult(Json(Session(payment, "cus_existing", "expired")));
        }));

        var proof = await Gateway(http).AbandonAdditionalDocumentsCheckoutAsync(payment, Context(payment), default);

        Assert.AreEqual("abandoned", proof.Status);
        Assert.AreEqual(1, mutations);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public DiagLinkDbContext Db { get; } public Guid PaymentId { get; } = Guid.NewGuid();
        public AdditionalDocumentsContext Context { get; private set; } = null!;
        private Fixture(DiagLinkDbContext db) { Db = db; }
        public static async Task<Fixture> CreateAsync(string? invalid = null)
        {
            var db = new DiagLinkDbContext(new DbContextOptionsBuilder<DiagLinkDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            var f = new Fixture(db); var company = Guid.NewGuid(); var user = Guid.NewGuid(); var machine = Guid.NewGuid(); var now = DateTime.UtcNow;
            db.Companies.Add(new Company { Id = company, Name = "Company", Status = "active", CreatedAtUtc = now, UpdatedAtUtc = now });
            db.Users.Add(new User { Id = user, CompanyId = company, Email = "admin@test", Role = DiagLinkRoles.CompanyAdmin, Status = "active", CreatedAt = now, UpdatedAt = now });
            db.Machines.Add(new Machine { Id = machine, CompanyId = company, Name = "Machine", Status = invalid == "machine" ? "inactive" : "active", CreatedAtUtc = now, UpdatedAtUtc = now });
            db.BillingAccounts.Add(new BillingAccount { Id = Guid.NewGuid(), CompanyId = company, StripeCustomerId = "cus_existing", CreatedAtUtc = now, UpdatedAtUtc = now });
            db.MachineRequestPayments.Add(new WebApp.Api.Models.Entities.MachineRequestPayment
            {
                Id = f.PaymentId, RequestKind = invalid == "kind" ? MachineRequestKind.AdditionalMachine : MachineRequestKind.AdditionalDocuments,
                RequestedByUserId = user, CompanyId = company, TargetMachineId = machine,
                Status = MachineRequestPaymentStatus.Pending, EstimatedTotalPages = 370,
                AmountCents = invalid == "amount" ? 0 : 9990, Currency = invalid == "currency" ? "USD" : "EUR",
                MachineRequestId = f.PaymentId.ToString("N"), CreatedAtUtc = now, UpdatedAtUtc = now, RequestLinkedAtUtc = now
            });
            await db.SaveChangesAsync();
            f.Context = new(user, company, machine, "cus_existing", "Company", "Machine", "admin@test", null, null, null);
            return f;
        }
        public MachineRequestPaymentService Service(FakeGateway gateway)
        {
            var resolver = new AdditionalDocumentsContextResolver(Db, new DiagLinkUserLookupService(Db));
            return new(new MachineRequestPaymentStore(Db), gateway, null, resolver, TimeProvider.System);
        }
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class FakeGateway : IMachineRequestPaymentGateway
    {
        public int CreateCalls, ReadCalls, CaptureCalls, CancelCalls; public string ReadStatus { get; set; } = "pending";
        public Task<MachineRequestCheckout> CreateAdditionalDocumentsAsync(Payment payment, AdditionalDocumentsContext context, CancellationToken ct)
        { CreateCalls++; return Task.FromResult(new MachineRequestCheckout("cs_docs", "https://checkout.stripe.com/c/pay/docs")); }
        public Task<MachineRequestPaymentProof> ReadAdditionalDocumentsAsync(Payment payment, string sessionId, AdditionalDocumentsContext context, CancellationToken ct)
        { ReadCalls++; return Task.FromResult(new MachineRequestPaymentProof(ReadStatus, ReadStatus == "authorized" ? "pi_docs" : "")); }
        public Task<MachineRequestCheckout> CreateAsync(Payment payment, CancellationToken ct) => throw new AssertFailedException();
        public Task<MachineRequestPaymentProof> ReadAsync(Payment payment, string sessionId, CancellationToken ct) => throw new AssertFailedException();
        public Task<MachineRequestPaymentProof> CaptureAsync(Payment payment, CancellationToken ct) { CaptureCalls++; throw new AssertFailedException(); }
        public Task<MachineRequestPaymentProof> CancelAsync(Payment payment, CancellationToken ct) { CancelCalls++; throw new AssertFailedException(); }
    }

    private static Payment PaymentRecord(int pages = 370, long amountCents = 9990) => new(Guid.NewGuid(), pages, amountCents, "EUR", null, null, null, "pending",
        DateTime.UtcNow, MachineRequestId: Guid.NewGuid().ToString("N"), CompanyId: Guid.NewGuid(),
        RequestKind: MachineRequestKind.AdditionalDocuments, RequestedByUserId: Guid.NewGuid(), TargetMachineId: Guid.NewGuid());
    private static AdditionalDocumentsContext Context(Payment p) => new(p.RequestedByUserId!.Value, p.CompanyId!.Value,
        p.TargetMachineId!.Value, "cus_existing", "Company", "Machine", "admin@test", null, null, null);
    private static Dictionary<string, string> Metadata(Payment p) => new()
    {
        ["diaglink_payment_type"] = "machine_request_additional_documents", ["diaglink_payment_request_id"] = p.PaymentRequestId.ToString(),
        ["diaglink_estimated_total_pages"] = p.TotalPages.ToString(), ["diaglink_server_amount_cents"] = p.AmountCents.ToString(),
        ["diaglink_request_kind"] = "additional_documents", ["diaglink_machine_request_id"] = p.MachineRequestId!,
        ["diaglink_company_id"] = p.CompanyId!.Value.ToString(), ["diaglink_requested_by_user_id"] = p.RequestedByUserId!.Value.ToString(),
        ["diaglink_target_machine_id"] = p.TargetMachineId!.Value.ToString(), ["diaglink_total_pages"] = p.TotalPages.ToString(),
        ["diaglink_pricing_model"] = "additional_documents_per_page_v1"
    };
    private static object Session(Payment p, string customer, string status, string? invalid = null)
    {
        var metadata = Metadata(p); if (invalid == "metadata") metadata["diaglink_target_machine_id"] = Guid.NewGuid().ToString();
        var amount = invalid == "amount" ? p.AmountCents + 1 : p.AmountCents; var currency = invalid == "currency" ? "usd" : "eur";
        var actualCustomer = invalid == "customer" ? "cus_other" : customer;
        return new { id = "cs_docs", @object = "checkout.session", livemode = false, mode = "payment", client_reference_id = p.PaymentRequestId.ToString(),
            currency, amount_total = amount, amount_subtotal = amount, status, payment_status = "unpaid", customer = actualCustomer,
            url = "https://checkout.stripe.com/c/pay/docs", metadata,
            payment_intent = status == "complete" ? new { id = "pi_docs", @object = "payment_intent", livemode = false,
                status = invalid == "status" ? "processing" : "requires_capture", capture_method = "manual", currency,
                amount, amount_capturable = invalid == "capturable" ? amount - 1 : amount, amount_received = 0,
                customer = actualCustomer, metadata, latest_charge = new { id = "ch_docs", @object = "charge", livemode = false,
                    paid = true, captured = false, status = "succeeded", amount, currency, amount_refunded = 0,
                    amount_captured = 0, payment_intent = "pi_docs" } } : null };
    }
    private static object Intent(Payment p, string status)
    {
        var captured = status == "captured"; var cancelled = status == "cancelled";
        return new { id = "pi_docs", @object = "payment_intent", livemode = false,
            status = cancelled ? "canceled" : captured ? "succeeded" : "requires_capture", capture_method = "manual",
            currency = "eur", amount = p.AmountCents, amount_capturable = captured || cancelled ? 0 : p.AmountCents,
            amount_received = captured ? p.AmountCents : 0, customer = "cus_existing", metadata = Metadata(p),
            latest_charge = cancelled ? null : new { id = "ch_docs", @object = "charge", livemode = false, paid = true,
                captured, status = "succeeded", amount = p.AmountCents, currency = "eur", amount_refunded = 0,
                amount_captured = captured ? p.AmountCents : 0, payment_intent = "pi_docs" } };
    }
    private static StripeBillingOptions Settings() => new() { Enabled = true, SecretKey = "sk_test_local", PriceId = "price_local", MachineRequestWebhookSecret = WebhookSecret, MachineRequestReturnUrl = "http://localhost:5173/commencer", MachineRequestAppReturnUrl = "http://localhost:5173/app/checkout-return" };
    private static StripeMachineRequestPaymentGateway Gateway(HttpClient http) => new(Settings(), new StripeClient("sk_test_local", httpClient: new SystemNetHttpClient(http, maxNetworkRetries: 0)));
    private static JsonObject Event(Guid id) => new() { ["id"] = "evt_docs", ["object"] = "event", ["type"] = "checkout.session.completed", ["api_version"] = StripeConfiguration.ApiVersion, ["livemode"] = false,
        ["data"] = new JsonObject { ["object"] = new JsonObject { ["id"] = "cs_docs", ["object"] = "checkout.session", ["livemode"] = false,
            ["metadata"] = new JsonObject { ["diaglink_payment_type"] = "machine_request_additional_documents", ["diaglink_payment_request_id"] = id.ToString() } } } };
    private static string Sign(string body) { var t = DateTimeOffset.UtcNow.ToUnixTimeSeconds(); var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(WebhookSecret), Encoding.UTF8.GetBytes($"{t}.{body}")); return $"t={t},v1={Convert.ToHexString(hash).ToLowerInvariant()}"; }
    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request); }
}
