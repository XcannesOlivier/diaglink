using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Stripe;
using WebApp.Api.Data;
using WebApp.Api.Models;
using WebApp.Api.Services;
using MachineRequestPaymentStatus = WebApp.Api.Models.Entities.MachineRequestPaymentStatus;
using MachineRequestProvisioningStage = WebApp.Api.Models.Entities.MachineRequestProvisioningStage;

namespace WebApp.Api.Tests;

[TestClass]
public class MachineRequestPaymentTests
{
    private const string WebhookSecret = "whsec_machine_request_test";

    [TestMethod]
    [DataRow(400, 99.90)]
    [DataRow(550, 140.40)]
    [DataRow(1000, 261.90)]
    public void ServerPricingUsesExpectedFormula(int pages, double expected)
    {
        var amount = MachineRequestPreparationPricing.Calculate(pages);
        var expectedAmount = Convert.ToDecimal(expected);
        Assert.AreEqual(expectedAmount, amount);
        Assert.AreEqual((long)(expectedAmount * 100m), MachineRequestPreparationPricing.ToCents(amount));
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    public async Task InvalidPageCountIsRejected(int pages)
    {
        var service = Service(new FakeGateway());
        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() =>
            service.CreateAsync(Guid.NewGuid(), pages, null, default));
    }

    [TestMethod]
    public async Task ServiceCalculatesAmountAndClientRequestHasNoAmountField()
    {
        Assert.IsNull(typeof(CreateMachineRequestPayment).GetProperty("Amount"));
        Assert.IsNull(typeof(CreateMachineRequestPayment).GetProperty("Price"));
        Assert.IsNull(typeof(CreateMachineRequestPayment).GetProperty("UnitAmount"));
        var gateway = new FakeGateway();
        var service = Service(gateway);
        var result = await service.CreateAsync(Guid.NewGuid(), 550, "test@example.com", default);
        Assert.AreEqual(170.30m, result.Amount);
        Assert.AreEqual(17030, gateway.Created!.AmountCents);
        Assert.AreEqual("pending", result.Status);
        Assert.AreEqual(MachineRequestKind.InitialMachine, gateway.Created.RequestKind);
        Assert.IsNull(gateway.Created.RequestedByUserId);
        Assert.IsNull(gateway.Created.CompanyId);
    }

    [TestMethod]
    [DataRow(400, 12980L)]
    [DataRow(550, 17030L)]
    [DataRow(817, 24239L)]
    public void MaximumAuthorizationIncludesPreparationAndFirstSubscription(int pages, long expectedCents)
    {
        Assert.AreEqual(expectedCents, MachineRequestPreparationPricing.CalculateMaximumAuthorizationCents(pages));
    }

    [TestMethod]
    public async Task CheckoutUsesPaymentModeEurServerAmountMetadataAndIdempotency()
    {
        var payment = MaximumPayment();
        string? body = null; string? key = null;
        using var http = new HttpClient(new Handler(async request =>
        {
            body = Uri.UnescapeDataString(await request.Content!.ReadAsStringAsync());
            key = request.Headers.GetValues("Idempotency-Key").Single();
            return Json(Session(payment, "pending"));
        }));
        var checkout = await Gateway(http).CreateAsync(payment, default);
        Assert.AreEqual("cs_test_local", checkout.SessionId);
        StringAssert.Contains(body!, "mode=payment");
        StringAssert.Contains(body, "success_url=http://localhost:5173/commencer");
        StringAssert.Contains(body, "cancel_url=http://localhost:5173/commencer");
        StringAssert.Contains(body!, "customer_creation=always");
        StringAssert.Contains(body!, "customer_email=test@example.com");
        StringAssert.Contains(body!, "payment_intent_data[capture_method]=manual");
        StringAssert.Contains(body!, "payment_intent_data[setup_future_usage]=off_session");
        StringAssert.Contains(body!, "line_items[0][price_data][currency]=eur");
        StringAssert.Contains(body!, "line_items[0][price_data][unit_amount]=17030");
        StringAssert.Contains(body!, "metadata[diaglink_payment_type]=machine_request_preparation");
        StringAssert.Contains(body!, $"metadata[diaglink_payment_request_id]={payment.PaymentRequestId}");
        StringAssert.Contains(body!, "metadata[diaglink_estimated_total_pages]=550");
        StringAssert.Contains(body!, "metadata[diaglink_authorization_model]=preparation_plus_subscription_max_v1");
        StringAssert.Contains(body!, "metadata[diaglink_maximum_subscription_cents]=2990");
        Assert.AreEqual($"diaglink:machine-request-payment:{payment.PaymentRequestId:N}:checkout", key);
    }

    [TestMethod]
    public async Task StripeProofMustBeAuthorizedUncapturedAndFullyConsistent()
    {
        var payment = MaximumPayment() with { StripeSessionId = "cs_test_local" };
        foreach (var status in new[] { "pending", "authorized", "captured" })
        {
            using var http = new HttpClient(new Handler(_ => Task.FromResult(Json(Session(payment, status)))));
            var proof = await Gateway(http).ReadAsync(payment, "cs_test_local", default);
            Assert.AreEqual(status == "authorized" ? "authorized" : "pending", proof.Status);
        }
    }

    [TestMethod]
    public async Task NewAuthorizationRequiresMatchingCustomerAndReusablePaymentMethod()
    {
        var payment = MaximumPayment() with { StripeSessionId = "cs_test_local" };
        foreach (var invalidIdentity in new[] { "missing-session-customer", "mismatched-intent-customer", "mismatched-payment-method-customer" })
        {
            using var http = new HttpClient(new Handler(_ => Task.FromResult(Json(Session(payment, "authorized", invalidIdentity)))));
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                Gateway(http).ReadAsync(payment, "cs_test_local", default));
        }
    }

    [TestMethod]
    public async Task LegacyPreparationOnlyAuthorizationDoesNotRequireNewStripeIdentityMetadata()
    {
        var payment = Payment() with { StripeSessionId = "cs_test_local" };
        using var http = new HttpClient(new Handler(_ => Task.FromResult(Json(Session(payment, "authorized")))));
        Assert.AreEqual("authorized", (await Gateway(http).ReadAsync(payment, "cs_test_local", default)).Status);
    }

    [TestMethod]
    public async Task InvalidWebhookSignatureIsRejectedAndValidVerifiedAuthorizationAuthorizes()
    {
        var gateway = new FakeGateway { ReadStatus = "authorized" };
        var service = Service(gateway);
        var created = await service.CreateAsync(Guid.NewGuid(), 550, null, default);
        var webhook = new StripeMachineRequestPaymentWebhook(Settings(), service,
            NullLogger<StripeMachineRequestPaymentWebhook>.Instance);
        var body = Event(created.PaymentRequestId).ToJsonString();
        Assert.AreEqual(400, (await webhook.HandleAsync(body, "bad")).HttpStatus);
        var result = await webhook.HandleAsync(body, Sign(body));
        Assert.AreEqual("authorized", result.Status);
        Assert.AreEqual("authorized", (await service.ReadAsync(created.PaymentRequestId, default))!.Status);
    }

    [TestMethod]
    public async Task PendingAndAuthorizedStatePersistAcrossDbContextRestart()
    {
        var options = new DbContextOptionsBuilder<DiagLinkDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var gateway = new FakeGateway { ReadStatus = "authorized" };
        Guid id;
        await using (var firstDb = new DiagLinkDbContext(options))
        {
            var firstService = new MachineRequestPaymentService(new MachineRequestPaymentStore(firstDb), gateway);
            var created = await firstService.CreateAsync(Guid.NewGuid(), 550, "test@example.com", default);
            id = created.PaymentRequestId;
            Assert.AreEqual("pending", created.Status);
            Assert.AreEqual("authorized",
                await firstService.ConfirmAuthorizationAsync(id, "cs_test_local", "evt_persisted", default));
        }

        await using var restartedDb = new DiagLinkDbContext(options);
        var restartedService = new MachineRequestPaymentService(new MachineRequestPaymentStore(restartedDb), gateway);
        Assert.AreEqual("authorized", (await restartedService.ReadAsync(id, default))!.Status);
        var entity = await restartedDb.MachineRequestPayments.SingleAsync(item => item.Id == id);
        Assert.AreEqual("evt_persisted", entity.AuthorizationEventId);
        Assert.AreEqual("pi_test_local", entity.StripePaymentIntentId);
        Assert.IsNotNull(entity.AuthorizedAtUtc);
    }

    [TestMethod]
    public async Task RepeatedWebhookEventIsIdempotent()
    {
        var gateway = new FakeGateway { ReadStatus = "authorized" };
        var service = Service(gateway);
        var created = await service.CreateAsync(Guid.NewGuid(), 550, null, default);

        Assert.AreEqual("authorized",
            await service.ConfirmAuthorizationAsync(created.PaymentRequestId, "cs_test_local", "evt_same", default));
        Assert.AreEqual("authorized",
            await service.ConfirmAuthorizationAsync(created.PaymentRequestId, "cs_test_local", "evt_same", default));
        Assert.AreEqual(1, gateway.ReadCalls);
    }

    [TestMethod]
    public async Task UnpaidProofStaysPendingAndStatusResponseHasNoSensitiveData()
    {
        var service = Service(new FakeGateway { ReadStatus = "pending" });
        var created = await service.CreateAsync(Guid.NewGuid(), 400, null, default);
        Assert.AreEqual("pending", await service.ConfirmAuthorizationAsync(created.PaymentRequestId, "cs_test_local", "evt_pending", default));
        var result = (IValueHttpResult)await MachineRequestPaymentEndpoints.ReadAsync(created.PaymentRequestId, service, default);
        var json = JsonSerializer.Serialize(result.Value);
        StringAssert.Contains(json, "pending");
        Assert.AreEqual(129.80m, JsonDocument.Parse(json).RootElement.GetProperty("Amount").GetDecimal());
        Assert.IsFalse(json.Contains("stripe", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(json.Contains("email", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(json.Contains("session", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task AuthorizedCanBeCapturedOnceButNotCancelledAfterCapture()
    {
        var gateway = new FakeGateway { ReadStatus = "authorized" };
        var service = Service(gateway);
        var created = await service.CreateAsync(Guid.NewGuid(), 550, null, default);
        Assert.AreEqual("authorized", await service.ConfirmAuthorizationAsync(created.PaymentRequestId, "cs_test_local", "evt_capture", default));
        Assert.AreEqual("captured", (await service.CaptureAsync(created.PaymentRequestId, 14040, default))!.Status);
        Assert.AreEqual("captured", (await service.CaptureAsync(created.PaymentRequestId, 14040, default))!.Status);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service.CancelAsync(created.PaymentRequestId, default));
        Assert.AreEqual(1, gateway.CaptureCalls);
    }

    [TestMethod]
    public async Task AuthorizedCanBeCancelledAndPendingCannotBeCaptured()
    {
        var gateway = new FakeGateway { ReadStatus = "authorized" };
        var service = Service(gateway);
        var authorized = await service.CreateAsync(Guid.NewGuid(), 550, null, default);
        await service.ConfirmAuthorizationAsync(authorized.PaymentRequestId, "cs_test_local", "evt_cancel", default);
        Assert.AreEqual("cancelled", (await service.CancelAsync(authorized.PaymentRequestId, default))!.Status);
        Assert.AreEqual(1, gateway.CancelCalls);
        var pending = await service.CreateAsync(Guid.NewGuid(), 400, null, default);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service.CaptureAsync(pending.PaymentRequestId, default));
    }

    [TestMethod]
    public async Task NewCaptureFreezesAmountBeforeStripeAndReusesItAfterFailure()
    {
        var options = new DbContextOptionsBuilder<DiagLinkDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new DiagLinkDbContext(options);
        var id = Guid.NewGuid();
        var created = new DateTime(2026, 9, 20, 10, 0, 0, DateTimeKind.Utc);
        db.MachineRequestPayments.Add(new WebApp.Api.Models.Entities.MachineRequestPayment
        {
            Id = id, Status = MachineRequestPaymentStatus.Authorized, EstimatedTotalPages = 550,
            AmountCents = 17030, Currency = "EUR", StripeSessionId = "cs_test_local",
            StripePaymentIntentId = "pi_test_local", AuthorizationEventId = "evt_test_local",
            MachineRequestId = id.ToString("N"), CreatedAtUtc = created, UpdatedAtUtc = created,
            AuthorizedAtUtc = created
        });
        await db.SaveChangesAsync();
        var activation = new DateTimeOffset(2026, 9, 22, 14, 37, 0, TimeSpan.Zero);
        var clock = new MutableTimeProvider(activation);
        var gateway = new FakeGateway { CaptureFailure = new StripeException("timeout") };
        var service = new MachineRequestPaymentService(new MachineRequestPaymentStore(db), gateway, clock);

        await Assert.ThrowsExactlyAsync<StripeException>(() => service.CaptureAsync(id, 14040, default));
        var frozen = await new MachineRequestPaymentStore(db).GetAsync(id, default);
        var expectedPeriod = MachineRequestFirstPeriodPricing.Calculate(activation.UtcDateTime);
        Assert.IsNotNull(frozen);
        Assert.AreEqual(MachineRequestProvisioningStage.AmountFinalized, frozen.ProvisioningStage);
        Assert.AreEqual(activation.UtcDateTime, frozen.ActivatedAtUtc);
        Assert.AreEqual(expectedPeriod.FirstPeriodEndUtc, frozen.FirstPeriodEndUtc);
        Assert.AreEqual(expectedPeriod.ServiceAmountCents, frozen.ServiceAmountCents);
        Assert.AreEqual(14040 + 1000 + expectedPeriod.ServiceAmountCents, frozen.FinalCaptureAmountCents);
        Assert.IsTrue(frozen.FinalCaptureAmountCents <= frozen.AmountCents);
        Assert.AreEqual("authorized", frozen.Status);

        gateway.CaptureFailure = null;
        clock.SetUtcNow(activation.AddMinutes(10));
        var captured = await service.CaptureAsync(id, 14040, default);
        Assert.AreEqual("captured", captured!.Status);
        Assert.AreEqual(frozen.FinalCaptureAmountCents / 100m, captured.Amount);
        Assert.AreEqual(170.30m, captured.InitialAuthorizationAmount);
        Assert.AreEqual(frozen.ActivatedAtUtc, gateway.Captured!.ActivatedAtUtc);
        Assert.AreEqual(frozen.FirstPeriodEndUtc, gateway.Captured.FirstPeriodEndUtc);
        Assert.AreEqual(frozen.ServiceAmountCents, gateway.Captured.ServiceAmountCents);
        Assert.AreEqual(frozen.FinalCaptureAmountCents, gateway.Captured.FinalCaptureAmountCents);
        Assert.AreEqual(MachineRequestProvisioningStage.AmountFinalized, gateway.Captured.ProvisioningStage);
        Assert.HasCount(0, await db.BillingAccounts.ToListAsync());
        Assert.HasCount(0, await db.MachineBillingPeriods.ToListAsync());
        Assert.HasCount(0, await db.CompanyWallets.ToListAsync());
        Assert.HasCount(0, await db.Companies.ToListAsync());
        Assert.HasCount(0, await db.Machines.ToListAsync());
    }

    [TestMethod]
    public async Task PartialCaptureUsesFrozenAmountWithoutMulticaptureAndWithStableIdempotencyKey()
    {
        var payment = MaximumPayment() with
        {
            StripeSessionId = "cs_test_local", StripePaymentIntentId = "pi_test_local", Status = "authorized",
            FinalCaptureAmountCents = 15542, ProvisioningStage = MachineRequestProvisioningStage.AmountFinalized
        };
        var writes = 0;
        using var http = new HttpClient(new Handler(async request =>
        {
            if (request.Method == HttpMethod.Get)
                return Json(Intent(payment, writes == 0 ? "authorized" : "captured"));
            writes++;
            Assert.AreEqual("/v1/payment_intents/pi_test_local/capture", request.RequestUri!.AbsolutePath);
            Assert.AreEqual($"diaglink:machine-request-payment:{payment.PaymentRequestId:N}:capture:v2",
                request.Headers.GetValues("Idempotency-Key").Single());
            var body = Uri.UnescapeDataString(await request.Content!.ReadAsStringAsync());
            StringAssert.Contains(body, "amount_to_capture=15542");
            Assert.IsFalse(body.Contains("final_capture", StringComparison.Ordinal));
            return Json(Intent(payment, "captured"));
        }));

        Assert.AreEqual("captured", (await Gateway(http).CaptureAsync(payment, default)).Status);
        Assert.AreEqual(1, writes);
    }

    [TestMethod]
    public async Task CaptureRetriesUseTheSameDeterministicV2KeyAndRecoveryDoesNotCaptureAgain()
    {
        var payment = MaximumPayment() with
        {
            StripeSessionId = "cs_test_local", StripePaymentIntentId = "pi_test_local", Status = "authorized",
            FinalCaptureAmountCents = 15542, ProvisioningStage = MachineRequestProvisioningStage.AmountFinalized
        };
        var captureAttempts = 0;
        var captured = false;
        var keys = new List<string>();
        using var http = new HttpClient(new Handler(async request =>
        {
            if (request.Method == HttpMethod.Get)
                return Json(Intent(payment, captured ? "captured" : "authorized"));

            captureAttempts++;
            keys.Add(request.Headers.GetValues("Idempotency-Key").Single());
            var body = Uri.UnescapeDataString(await request.Content!.ReadAsStringAsync());
            StringAssert.Contains(body, "amount_to_capture=15542");
            Assert.IsFalse(body.Contains("final_capture", StringComparison.Ordinal));
            if (captureAttempts == 1)
                return StripeError(HttpStatusCode.InternalServerError, "api_error", "Temporary test failure");
            captured = true;
            return Json(Intent(payment, "captured"));
        }));
        var gateway = Gateway(http);

        await Assert.ThrowsExactlyAsync<StripeException>(() => gateway.CaptureAsync(payment, default));
        Assert.AreEqual("captured", (await gateway.CaptureAsync(payment, default)).Status);
        Assert.AreEqual("captured", (await gateway.CaptureAsync(payment, default)).Status);

        var expectedKey = $"diaglink:machine-request-payment:{payment.PaymentRequestId:N}:capture:v2";
        CollectionAssert.AreEqual(new[] { expectedKey, expectedKey }, keys);
        Assert.IsFalse(keys.Any(key => key == $"diaglink:machine-request-payment:{payment.PaymentRequestId:N}:capture"));
        Assert.AreEqual(2, captureAttempts);
    }

    [TestMethod]
    public async Task RetryAfterStripeCapturedDoesNotIssueAnotherCaptureRequest()
    {
        var payment = MaximumPayment() with
        {
            StripeSessionId = "cs_test_local", StripePaymentIntentId = "pi_test_local", Status = "authorized",
            FinalCaptureAmountCents = 15542, ProvisioningStage = MachineRequestProvisioningStage.AmountFinalized
        };
        var writes = 0;
        using var http = new HttpClient(new Handler(request =>
        {
            if (request.Method != HttpMethod.Get) writes++;
            return Task.FromResult(Json(Intent(payment, "captured")));
        }));

        Assert.AreEqual("captured", (await Gateway(http).CaptureAsync(payment, default)).Status);
        Assert.AreEqual(0, writes);
    }

    [TestMethod]
    [DataRow("post-status")]
    [DataRow("post-amount-received")]
    [DataRow("post-amount-capturable")]
    [DataRow("post-charge-amount-captured")]
    [DataRow("post-charge-amount")]
    public async Task PartialCaptureStillRequiresFullyCapturedPostState(string invalidPostState)
    {
        var payment = MaximumPayment() with
        {
            StripeSessionId = "cs_test_local", StripePaymentIntentId = "pi_test_local", Status = "authorized",
            FinalCaptureAmountCents = 15542, ProvisioningStage = MachineRequestProvisioningStage.AmountFinalized
        };
        var writes = 0;
        using var http = new HttpClient(new Handler(request =>
        {
            if (request.Method != HttpMethod.Get)
            {
                writes++;
                return Task.FromResult(Json(Intent(payment, "captured")));
            }
            return Task.FromResult(Json(Intent(payment, writes == 0 ? "authorized" : "captured",
                writes == 0 ? null : invalidPostState)));
        }));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Gateway(http).CaptureAsync(payment, default));
        Assert.AreEqual(1, writes);
    }

    [TestMethod]
    public async Task GatewayCaptureAndCancelRereadAndVerifyStripe()
    {
        foreach (var action in new[] { "capture", "cancel" })
        {
            var payment = Payment() with { StripeSessionId = "cs_test_local", StripePaymentIntentId = "pi_test_local", Status = "authorized" };
            var writes = 0;
            using var http = new HttpClient(new Handler(async request =>
            {
                if (request.Method == HttpMethod.Get)
                    return Json(Intent(payment, writes == 0 ? "authorized" : action == "capture" ? "captured" : "cancelled"));
                writes++;
                Assert.AreEqual($"/v1/payment_intents/pi_test_local/{action}", request.RequestUri!.AbsolutePath);
                var key = request.Headers.GetValues("Idempotency-Key").Single();
                Assert.AreEqual(action == "capture"
                    ? $"diaglink:machine-request-payment:{payment.PaymentRequestId:N}:capture:v2"
                    : $"diaglink:machine-request-payment:{payment.PaymentRequestId:N}:cancel", key);
                if (action == "capture")
                    StringAssert.Contains(Uri.UnescapeDataString(await request.Content!.ReadAsStringAsync()), "amount_to_capture=14040");
                return Json(Intent(payment, action == "capture" ? "captured" : "cancelled"));
            }));
            var gateway = Gateway(http);
            var proof = action == "capture" ? await gateway.CaptureAsync(payment, default) : await gateway.CancelAsync(payment, default);
            Assert.AreEqual(action == "capture" ? "captured" : "cancelled", proof.Status);
            Assert.AreEqual(1, writes);
        }
    }

    [TestMethod]
    public void CaptureAndCancelRoutesRequireSuperAdminAndAreNotPublic()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddDbContext<DiagLinkDbContext>(options => options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        builder.Services.AddScoped<MachineRequestPaymentStore>();
        builder.Services.AddSingleton<IMachineRequestPaymentGateway>(new FakeGateway());
        builder.Services.AddScoped<MachineRequestPaymentService>();
        builder.Services.AddScoped<StripeMachineRequestPaymentWebhook>();
        builder.Services.AddSingleton(Settings());
        var app = builder.Build();
        app.MapMachineRequestPayments();
        var endpoints = ((Microsoft.AspNetCore.Routing.IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints)
            .OfType<Microsoft.AspNetCore.Routing.RouteEndpoint>().ToArray();
        foreach (var suffix in new[] { "/capture", "/cancel" })
        {
            var endpoint = endpoints.Single(item => item.RoutePattern.RawText!.EndsWith(suffix, StringComparison.Ordinal));
            Assert.IsTrue(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Any(item => item.Policy == "SuperAdminOnly"));
            Assert.IsNull(endpoint.Metadata.GetMetadata<IAllowAnonymous>());
        }
        foreach (var route in new[]
        {
            "/api/company/machine-request-payments",
            "/api/company/machine-request-payments/{paymentRequestId:guid}"
        })
        {
            var endpoint = endpoints.Single(item =>
                item.RoutePattern.RawText?.TrimEnd('/') == route.TrimEnd('/'));
            Assert.IsTrue(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()
                .Any(item => item.Policy == "CompanyAdminOnly"));
            Assert.IsNull(endpoint.Metadata.GetMetadata<IAllowAnonymous>());
        }
    }

    [TestMethod]
    public void InstalledStripeSdkExposesFutureUsageCustomerAndPaymentMethodProperties()
    {
        var options = new Stripe.Checkout.SessionCreateOptions
        {
            CustomerCreation = "always",
            PaymentIntentData = new Stripe.Checkout.SessionPaymentIntentDataOptions { SetupFutureUsage = "off_session" }
        };

        Assert.AreEqual("always", options.CustomerCreation);
        Assert.AreEqual("off_session", options.PaymentIntentData.SetupFutureUsage);
        Assert.IsNotNull(typeof(Stripe.Checkout.Session).GetProperty(nameof(Stripe.Checkout.Session.CustomerId)));
        Assert.IsNotNull(typeof(PaymentIntent).GetProperty(nameof(PaymentIntent.CustomerId)));
        Assert.IsNotNull(typeof(PaymentIntent).GetProperty(nameof(PaymentIntent.PaymentMethodId)));
        Assert.IsNotNull(typeof(Charge).GetProperty(nameof(Charge.AmountCaptured)));
        Assert.IsTrue(new PaymentIntentCaptureOptions { FinalCapture = true }.FinalCapture);
    }

    private static MachineRequestPaymentService Service(FakeGateway gateway, string? databaseName = null)
    {
        var options = new DbContextOptionsBuilder<DiagLinkDbContext>()
            .UseInMemoryDatabase(databaseName ?? Guid.NewGuid().ToString())
            .Options;
        return new MachineRequestPaymentService(new MachineRequestPaymentStore(new DiagLinkDbContext(options)), gateway);
    }

    private static MachineRequestPayment Payment() => new(Guid.NewGuid(), 550, 14040, "EUR", "test@example.com",
        null, null, "pending", DateTime.UtcNow);

    private static MachineRequestPayment MaximumPayment() => new(Guid.NewGuid(), 550, 17030, "EUR", "test@example.com",
        null, null, "pending", DateTime.UtcNow);

    private static StripeBillingOptions Settings() => new()
    {
        Enabled = true, SecretKey = "sk_test_local", PriceId = "price_local",
        MachineRequestWebhookSecret = WebhookSecret,
        MachineRequestReturnUrl = "http://localhost:5173/commencer",
        MachineRequestAppReturnUrl = "http://localhost:5173/app/checkout-return"
    };

    private static StripeMachineRequestPaymentGateway Gateway(HttpClient http) => new(Settings(),
        new StripeClient("sk_test_local", httpClient: new SystemNetHttpClient(http, maxNetworkRetries: 0)));

    private static object Session(MachineRequestPayment payment, string status, string? invalidIdentity = null)
    {
        var metadata = Metadata(payment);
        var isNewAuthorization = payment.AmountCents == 17030;
        var sessionCustomer = isNewAuthorization && invalidIdentity != "missing-session-customer" ? "cus_test_local" : null;
        return new
        {
            id = "cs_test_local", @object = "checkout.session", livemode = false, mode = "payment",
            client_reference_id = payment.PaymentRequestId.ToString(), currency = "eur", amount_total = payment.AmountCents,
            amount_subtotal = payment.AmountCents, status = status == "pending" ? "open" : "complete", payment_status = "unpaid",
            url = "https://checkout.stripe.com/c/pay/test", metadata,
            customer = sessionCustomer,
            payment_intent = status == "pending" ? null : Intent(payment, status, invalidIdentity)
        };
    }

    private static object Intent(MachineRequestPayment payment, string status, string? invalidIdentity = null)
    {
        var metadata = Metadata(payment);
        var captured = status == "captured";
        var isNewAuthorization = payment.AmountCents == 17030;
        var intentCustomer = isNewAuthorization
            ? invalidIdentity == "mismatched-intent-customer" ? "cus_other" : "cus_test_local"
            : null;
        var paymentMethodCustomer = invalidIdentity == "mismatched-payment-method-customer" ? "cus_other" : intentCustomer;
        return new
        {
            id = "pi_test_local", @object = "payment_intent", livemode = false,
            status = invalidIdentity == "post-status" ? "processing"
                : status switch { "authorized" => "requires_capture", "captured" => "succeeded", "cancelled" => "canceled", _ => "processing" },
            capture_method = "manual", currency = "eur", amount = payment.AmountCents,
            amount_capturable = invalidIdentity == "post-amount-capturable" ? 1 : status == "authorized" ? payment.AmountCents : 0,
            amount_received = invalidIdentity == "post-amount-received" ? 1
                : captured ? payment.FinalCaptureAmountCents ?? payment.AmountCents : 0, metadata,
            customer = intentCustomer,
            payment_method = isNewAuthorization ? new { id = "pm_test_local", @object = "payment_method", type = "card", customer = paymentMethodCustomer } : null,
            latest_charge = status == "cancelled" ? null : new { id = "ch_test_local", @object = "charge", livemode = false,
                paid = true, captured, status = "succeeded",
                amount = invalidIdentity == "post-charge-amount" ? payment.AmountCents - 1 : payment.AmountCents,
                currency = "eur", amount_refunded = 0,
                amount_captured = invalidIdentity == "post-charge-amount-captured" ? 1
                    : captured ? payment.FinalCaptureAmountCents ?? payment.AmountCents : 0,
                payment_intent = "pi_test_local" }
        };
    }

    private static Dictionary<string, string> Metadata(MachineRequestPayment payment)
    {
        var metadata = new Dictionary<string, string>
        {
            ["diaglink_payment_type"] = "machine_request_preparation",
            ["diaglink_payment_request_id"] = payment.PaymentRequestId.ToString(),
            ["diaglink_estimated_total_pages"] = payment.TotalPages.ToString(),
            ["diaglink_server_amount_cents"] = payment.AmountCents.ToString()
        };
        if (payment.AmountCents == 17030)
        {
            metadata["diaglink_authorization_model"] = "preparation_plus_subscription_max_v1";
            metadata["diaglink_maximum_subscription_cents"] = "2990";
        }
        return metadata;
    }

    private static JsonObject Event(Guid id) => new()
    {
        ["id"] = "evt_test_local", ["object"] = "event", ["type"] = "checkout.session.completed",
        ["api_version"] = StripeConfiguration.ApiVersion, ["livemode"] = false,
        ["data"] = new JsonObject { ["object"] = new JsonObject
        {
            ["id"] = "cs_test_local", ["object"] = "checkout.session", ["livemode"] = false,
            ["metadata"] = new JsonObject { ["diaglink_payment_type"] = "machine_request_preparation",
                ["diaglink_payment_request_id"] = id.ToString() }
        } }
    };

    private static string Sign(string body)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(WebhookSecret), Encoding.UTF8.GetBytes($"{timestamp}.{body}"));
        return $"t={timestamp},v1={Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK)
    { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };

    private static HttpResponseMessage StripeError(HttpStatusCode status, string type, string message) => new(status)
    {
        Content = new StringContent(JsonSerializer.Serialize(new { error = new { type, message } }), Encoding.UTF8, "application/json")
    };

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }

    private sealed class FakeGateway : IMachineRequestPaymentGateway
    {
        public MachineRequestPayment? Created { get; private set; }
        public string ReadStatus { get; init; } = "pending";
        public int CaptureCalls { get; private set; }
        public int CancelCalls { get; private set; }
        public int ReadCalls { get; private set; }
        public Exception? CaptureFailure { get; set; }
        public MachineRequestPayment? Captured { get; private set; }
        public Task<MachineRequestCheckout> CreateAsync(MachineRequestPayment payment, CancellationToken ct)
        {
            Created = payment;
            return Task.FromResult(new MachineRequestCheckout("cs_test_local", "https://checkout.stripe.com/c/pay/test"));
        }
        public Task<MachineRequestPaymentProof> ReadAsync(MachineRequestPayment payment, string sessionId, CancellationToken ct)
        {
            ReadCalls++;
            return Task.FromResult(new MachineRequestPaymentProof(ReadStatus, ReadStatus == "authorized" ? "pi_test_local" : ""));
        }
        public Task<MachineRequestPaymentProof> CaptureAsync(MachineRequestPayment payment, CancellationToken ct)
        {
            CaptureCalls++;
            Captured = payment;
            if (CaptureFailure is not null) return Task.FromException<MachineRequestPaymentProof>(CaptureFailure);
            return Task.FromResult(new MachineRequestPaymentProof("captured", payment.StripePaymentIntentId!));
        }
        public Task<MachineRequestPaymentProof> CancelAsync(MachineRequestPayment payment, CancellationToken ct)
        {
            CancelCalls++;
            return Task.FromResult(new MachineRequestPaymentProof("cancelled", payment.StripePaymentIntentId!));
        }
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset current = now;
        public override DateTimeOffset GetUtcNow() => current;
        public void SetUtcNow(DateTimeOffset value) => current = value;
    }
}
