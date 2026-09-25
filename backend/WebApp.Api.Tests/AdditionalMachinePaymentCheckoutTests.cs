using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Stripe;
using WebApp.Api.Data;
using WebApp.Api.Models;
using WebApp.Api.Models.Entities;
using WebApp.Api.Services;
using Payment = WebApp.Api.Services.MachineRequestPayment;

namespace WebApp.Api.Tests;

[TestClass]
public class AdditionalMachinePaymentCheckoutTests
{
    private const string WebhookSecret = "whsec_additional_test";

    [TestMethod]
    public async Task CompanyAdminCreate_UsesServerPagesPersistsOwnershipAndRetriesSamePayment()
    {
        await using var db = Database();
        var identity = await SeedAsync(db);
        var resolver = new AdditionalMachinePaymentContextResolver(db, new DiagLinkUserLookupService(db));
        var gateway = new FakeGateway();
        var service = new MachineRequestPaymentService(new MachineRequestPaymentStore(db), gateway, resolver, TimeProvider.System);
        var operationId = Guid.NewGuid();
        var http = Multipart(identity, operationId, claimedPages: "999999");

        var first = await MachineRequestPaymentEndpoints.CreateAdditionalAsync(http, resolver, service,
            new FixedPageCounter(416), default);
        var retry = await MachineRequestPaymentEndpoints.CreateAdditionalAsync(
            Multipart(identity, operationId, claimedPages: "1"), resolver, service,
            new FixedPageCounter(416), default);

        Assert.AreEqual(StatusCodes.Status200OK, ((IStatusCodeHttpResult)first).StatusCode);
        Assert.AreEqual(StatusCodes.Status200OK, ((IStatusCodeHttpResult)retry).StatusCode);
        var payment = await db.MachineRequestPayments.SingleAsync();
        Assert.AreEqual(operationId, payment.Id);
        Assert.AreEqual(MachineRequestKind.AdditionalMachine, payment.RequestKind);
        Assert.AreEqual(identity.UserId, payment.RequestedByUserId);
        Assert.AreEqual(identity.CompanyId, payment.CompanyId);
        Assert.AreEqual(416, payment.EstimatedTotalPages);
        Assert.AreEqual(13412, payment.AmountCents);
        Assert.AreEqual(2, gateway.AdditionalCreateCalls);
        Assert.AreEqual(1, await db.MachineRequestPayments.CountAsync());
        Assert.AreEqual(0, await db.Machines.CountAsync());
        Assert.AreEqual(0, await db.MachineBillingPeriods.CountAsync());
        Assert.AreEqual(0, await db.CompanyWallets.CountAsync());
    }

    [TestMethod]
    public async Task TechnicianAndMismatchedCompany_AreRejectedBeforePaymentOrGateway()
    {
        await using var db = Database();
        var identity = await SeedAsync(db);
        var resolver = new AdditionalMachinePaymentContextResolver(db, new DiagLinkUserLookupService(db));
        var gateway = new FakeGateway();
        var service = new MachineRequestPaymentService(new MachineRequestPaymentStore(db), gateway, resolver, TimeProvider.System);

        var technician = identity with { Role = DiagLinkRoles.Technician };
        var technicianResult = await MachineRequestPaymentEndpoints.CreateAdditionalAsync(
            Multipart(technician, Guid.NewGuid()), resolver, service, new FixedPageCounter(10), default);
        var mismatch = identity with { CompanyId = Guid.NewGuid() };
        var mismatchResult = await MachineRequestPaymentEndpoints.CreateAdditionalAsync(
            Multipart(mismatch, Guid.NewGuid()), resolver, service, new FixedPageCounter(10), default);

        Assert.IsInstanceOfType<Microsoft.AspNetCore.Http.HttpResults.ForbidHttpResult>(technicianResult);
        Assert.IsInstanceOfType<Microsoft.AspNetCore.Http.HttpResults.ForbidHttpResult>(mismatchResult);
        Assert.AreEqual(0, gateway.AdditionalCreateCalls);
        Assert.AreEqual(0, await db.MachineRequestPayments.CountAsync());
    }

    [TestMethod]
    public async Task AuthenticatedPolling_OnlyAllowsExactOwnerAndNeverExposesInitialPayment()
    {
        await using var db = Database();
        var owner = await SeedAsync(db);
        var resolver = new AdditionalMachinePaymentContextResolver(db, new DiagLinkUserLookupService(db));
        var service = new MachineRequestPaymentService(new MachineRequestPaymentStore(db), new FakeGateway(), resolver, TimeProvider.System);
        var context = (await resolver.ResolveAsync(Principal(owner), default)).Context!;
        var additional = await service.CreateAdditionalAsync(Guid.NewGuid(), 10, context, default);
        var initialId = Guid.NewGuid();
        db.MachineRequestPayments.Add(new WebApp.Api.Models.Entities.MachineRequestPayment
        {
            Id = initialId, RequestKind = MachineRequestKind.InitialMachine,
            Status = MachineRequestPaymentStatus.Pending, EstimatedTotalPages = 10, AmountCents = 12980,
            Currency = "EUR", CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var allowed = await MachineRequestPaymentEndpoints.ReadAdditionalAsync(additional.PaymentRequestId,
            Http(Principal(owner)), resolver, service, default);
        var other = owner with { CompanyId = Guid.NewGuid() };
        var deniedTenant = await MachineRequestPaymentEndpoints.ReadAdditionalAsync(additional.PaymentRequestId,
            Http(Principal(other)), resolver, service, default);
        var deniedInitial = await MachineRequestPaymentEndpoints.ReadAdditionalAsync(initialId,
            Http(Principal(owner)), resolver, service, default);

        Assert.AreEqual(StatusCodes.Status200OK, ((IStatusCodeHttpResult)allowed).StatusCode);
        Assert.IsInstanceOfType<Microsoft.AspNetCore.Http.HttpResults.ForbidHttpResult>(deniedTenant);
        Assert.IsInstanceOfType<Microsoft.AspNetCore.Http.HttpResults.ForbidHttpResult>(deniedInitial);
        Assert.IsFalse(JsonSerializer.Serialize(((IValueHttpResult)allowed).Value)
            .Contains("stripe", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task StripeCheckoutAdditional_UsesExistingCustomerManualCaptureMetadataAndDistinctStableKey()
    {
        var payment = AdditionalPayment();
        var context = Context(payment);
        string? body = null; string? key = null;
        using var http = new HttpClient(new Handler(async request =>
        {
            body = Uri.UnescapeDataString(await request.Content!.ReadAsStringAsync());
            key = request.Headers.GetValues("Idempotency-Key").Single();
            return Json(Session(payment, context.StripeCustomerId, "open"));
        }));

        await Gateway(http).CreateAdditionalAsync(payment, context, default);

        StringAssert.Contains(body!, "customer=cus_existing");
        StringAssert.Contains(body, "success_url=http://localhost:5173/app/checkout-return");
        StringAssert.Contains(body, "cancel_url=http://localhost:5173/app/checkout-return");
        Assert.IsFalse(body!.Contains("customer_creation", StringComparison.Ordinal));
        Assert.IsFalse(body.Contains("customer_email", StringComparison.Ordinal));
        Assert.IsFalse(body.Contains("setup_future_usage", StringComparison.Ordinal));
        StringAssert.Contains(body, "payment_intent_data[capture_method]=manual");
        StringAssert.Contains(body, "line_items[0][price_data][unit_amount]=13412");
        StringAssert.Contains(body, "metadata[diaglink_request_kind]=additional_machine");
        StringAssert.Contains(body, $"metadata[diaglink_company_id]={payment.CompanyId}");
        StringAssert.Contains(body, $"metadata[diaglink_requested_by_user_id]={payment.RequestedByUserId}");
        StringAssert.Contains(body, "metadata[diaglink_estimated_total_pages]=416");
        StringAssert.Contains(body, "metadata[diaglink_preparation_amount_cents]=10422");
        Assert.AreEqual($"diaglink:machine-request-payment:{payment.PaymentRequestId:N}:checkout:additional:v1", key);
    }

    [TestMethod]
    public async Task AdditionalReread_RejectsCustomerMismatchAndWebhookUsesSameVerifiedPath()
    {
        var payment = AdditionalPayment() with { StripeSessionId = "cs_test_local" };
        var context = Context(payment);
        using var mismatchHttp = new HttpClient(new Handler(_ => Task.FromResult(Json(Session(payment, "cus_other", "complete")))));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            Gateway(mismatchHttp).ReadAdditionalAsync(payment, "cs_test_local", context, default));

        await using var db = Database();
        var identity = await SeedAsync(db);
        var resolver = new AdditionalMachinePaymentContextResolver(db, new DiagLinkUserLookupService(db));
        var fake = new FakeGateway { ReadStatus = "authorized" };
        var service = new MachineRequestPaymentService(new MachineRequestPaymentStore(db), fake, resolver, TimeProvider.System);
        var resolved = (await resolver.ResolveAsync(Principal(identity), default)).Context!;
        var created = await service.CreateAdditionalAsync(Guid.NewGuid(), 416, resolved, default);
        var webhook = new StripeMachineRequestPaymentWebhook(Settings(), service,
            NullLogger<StripeMachineRequestPaymentWebhook>.Instance);
        var eventBody = Event(created.PaymentRequestId).ToJsonString();

        Assert.AreEqual("authorized", (await webhook.HandleAsync(eventBody, Sign(eventBody))).Status);
        Assert.AreEqual(1, fake.AdditionalReadCalls);
    }

    private sealed record Identity(Guid UserId, Guid CompanyId, string Role);

    private static async Task<Identity> SeedAsync(DiagLinkDbContext db)
    {
        var now = DateTime.UtcNow; var companyId = Guid.NewGuid(); var userId = Guid.NewGuid();
        db.Companies.Add(new Company { Id = companyId, Name = "Company", Status = "active", CreatedAtUtc = now, UpdatedAtUtc = now });
        db.Users.Add(new User { Id = userId, CompanyId = companyId, Email = "admin@example.com", Role = DiagLinkRoles.CompanyAdmin, Status = "active", CreatedAt = now, UpdatedAt = now });
        db.BillingAccounts.Add(new BillingAccount { Id = Guid.NewGuid(), CompanyId = companyId, StripeCustomerId = "cus_existing", StripeSubscriptionId = "sub_existing", CreatedAtUtc = now, UpdatedAtUtc = now });
        await db.SaveChangesAsync(); return new(userId, companyId, DiagLinkRoles.CompanyAdmin);
    }

    private static DiagLinkDbContext Database() => new(new DbContextOptionsBuilder<DiagLinkDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static ClaimsPrincipal Principal(Identity identity) => new(new ClaimsIdentity([
        new Claim(DiagLinkClaimTypes.UserId, identity.UserId.ToString()),
        new Claim(DiagLinkClaimTypes.CompanyId, identity.CompanyId.ToString()),
        new Claim(ClaimTypes.Role, identity.Role)], "Test"));
    private static DefaultHttpContext Http(ClaimsPrincipal principal) => new() { User = principal };
    private static DefaultHttpContext Multipart(Identity identity, Guid key, string? claimedPages = null)
    {
        var context = Http(Principal(identity));
        context.Request.Headers["Idempotency-Key"] = key.ToString();
        context.Request.ContentType = "multipart/form-data; boundary=test";
        var fields = claimedPages is null ? new Dictionary<string, StringValues>()
            : new Dictionary<string, StringValues> { ["totalPages"] = claimedPages };
        var files = new FormFileCollection
        {
            new FormFile(new MemoryStream("fake-pdf"u8.ToArray()), 0, 8, "documents", "manual.pdf")
        };
        context.Features.Set<IFormFeature>(new FormFeature(new FormCollection(fields, files)));
        return context;
    }

    private sealed class FixedPageCounter(int pages) : IPdfPageCounter
    { public Task<int> CountPagesAsync(Stream content, CancellationToken cancellationToken = default) => Task.FromResult(pages); }

    private sealed class FakeGateway : IMachineRequestPaymentGateway
    {
        public int AdditionalCreateCalls { get; private set; }
        public int AdditionalReadCalls { get; private set; }
        public string ReadStatus { get; init; } = "pending";
        public Task<MachineRequestCheckout> CreateAsync(Payment payment, CancellationToken ct) => throw new AssertFailedException();
        public Task<MachineRequestCheckout> CreateAdditionalAsync(Payment payment, AdditionalMachinePaymentContext context, CancellationToken ct)
        { AdditionalCreateCalls++; return Task.FromResult(new MachineRequestCheckout("cs_additional", "https://checkout.stripe.com/c/pay/test")); }
        public Task<MachineRequestPaymentProof> ReadAsync(Payment payment, string sessionId, CancellationToken ct) => throw new AssertFailedException();
        public Task<MachineRequestPaymentProof> ReadAdditionalAsync(Payment payment, string sessionId, AdditionalMachinePaymentContext context, CancellationToken ct)
        { AdditionalReadCalls++; return Task.FromResult(new MachineRequestPaymentProof(ReadStatus, "pi_additional")); }
        public Task<MachineRequestPaymentProof> CaptureAsync(Payment payment, CancellationToken ct) => throw new AssertFailedException();
        public Task<MachineRequestPaymentProof> CancelAsync(Payment payment, CancellationToken ct) => throw new AssertFailedException();
    }

    private static Payment AdditionalPayment() => new(Guid.NewGuid(), 416, 13412, "EUR", null,
        null, null, "pending", DateTime.UtcNow, CompanyId: Guid.NewGuid(),
        RequestKind: MachineRequestKind.AdditionalMachine, RequestedByUserId: Guid.NewGuid());
    private static AdditionalMachinePaymentContext Context(Payment payment) =>
        new(payment.RequestedByUserId!.Value, payment.CompanyId!.Value, "cus_existing", "sub_existing");
    private static StripeBillingOptions Settings() => new() { Enabled = true, SecretKey = "sk_test_local", PriceId = "price_local", MachineRequestWebhookSecret = WebhookSecret, MachineRequestReturnUrl = "http://localhost:5173/commencer", MachineRequestAppReturnUrl = "http://localhost:5173/app/checkout-return" };
    private static StripeMachineRequestPaymentGateway Gateway(HttpClient http) => new(Settings(), new StripeClient("sk_test_local", httpClient: new SystemNetHttpClient(http, maxNetworkRetries: 0)));
    private static object Session(Payment payment, string customer, string status) => new
    {
        id = "cs_test_local", @object = "checkout.session", livemode = false, mode = "payment",
        client_reference_id = payment.PaymentRequestId.ToString(), currency = "eur", amount_total = payment.AmountCents,
        amount_subtotal = payment.AmountCents, status, payment_status = "unpaid", customer,
        url = "https://checkout.stripe.com/c/pay/test", metadata = Metadata(payment),
        payment_intent = status == "complete" ? new { id = "pi_test_local", @object = "payment_intent", livemode = false,
            status = "requires_capture", capture_method = "manual", currency = "eur", amount = payment.AmountCents,
            amount_capturable = payment.AmountCents, amount_received = 0, customer, metadata = Metadata(payment),
            latest_charge = new { id = "ch_test", @object = "charge", livemode = false, paid = true, captured = false,
                status = "succeeded", amount = payment.AmountCents, currency = "eur", amount_refunded = 0,
                amount_captured = 0, payment_intent = "pi_test_local" } } : null
    };
    private static Dictionary<string, string> Metadata(Payment p) => new()
    {
        ["diaglink_payment_type"] = "machine_request_preparation", ["diaglink_payment_request_id"] = p.PaymentRequestId.ToString(),
        ["diaglink_estimated_total_pages"] = "416", ["diaglink_server_amount_cents"] = "13412",
        ["diaglink_authorization_model"] = "preparation_plus_subscription_max_v1", ["diaglink_maximum_subscription_cents"] = "2990",
        ["diaglink_request_kind"] = "additional_machine", ["diaglink_company_id"] = p.CompanyId!.Value.ToString(),
        ["diaglink_requested_by_user_id"] = p.RequestedByUserId!.Value.ToString(), ["diaglink_preparation_amount_cents"] = "10422"
    };
    private static JsonObject Event(Guid id) => new() { ["id"] = "evt_additional", ["object"] = "event", ["type"] = "checkout.session.completed", ["api_version"] = StripeConfiguration.ApiVersion, ["livemode"] = false,
        ["data"] = new JsonObject { ["object"] = new JsonObject { ["id"] = "cs_additional", ["object"] = "checkout.session", ["livemode"] = false,
            ["metadata"] = new JsonObject { ["diaglink_payment_type"] = "machine_request_preparation", ["diaglink_payment_request_id"] = id.ToString() } } } };
    private static string Sign(string body) { var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(); var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(WebhookSecret), Encoding.UTF8.GetBytes($"{timestamp}.{body}")); return $"t={timestamp},v1={Convert.ToHexString(hash).ToLowerInvariant()}"; }
    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request); }
}
