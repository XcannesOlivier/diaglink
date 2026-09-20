using System.Net;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Stripe;
using WebApp.Api.Models.Entities;
using WebApp.Api.Services;

namespace WebApp.Api.Tests;
[TestClass]
public class WalletTopUpGatewayTests
{
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> action) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) => action(r); }
    private static StripeWalletTopUp Op() => new() { Id = Guid.NewGuid(), CompanyId = Guid.NewGuid(), StripeCustomerId = "cus_local",
        AmountCents = 2000, Currency = "EUR", ReturnUrl = "http://localhost:5173/", CreatedAtUtc = DateTime.UtcNow };
    private static JsonObject Session(StripeWalletTopUp op) => new()
    {
        ["id"] = "cs_test_local", ["object"] = "checkout.session", ["livemode"] = false, ["mode"] = "payment", ["currency"] = "eur",
        ["status"] = "complete", ["payment_status"] = "paid", ["customer"] = op.StripeCustomerId, ["client_reference_id"] = op.Id.ToString(),
        ["amount_total"] = 2000, ["amount_subtotal"] = 2000, ["url"] = "https://checkout.stripe.com/c/pay/local",
        ["metadata"] = new JsonObject { ["diaglink_topup_id"] = op.Id.ToString(), ["diaglink_company_id"] = op.CompanyId.ToString() },
        ["payment_intent"] = new JsonObject { ["id"] = "pi_local", ["object"] = "payment_intent", ["livemode"] = false,
            ["status"] = "succeeded", ["customer"] = op.StripeCustomerId, ["currency"] = "eur", ["amount"] = 2000, ["amount_received"] = 2000,
            ["metadata"] = new JsonObject { ["diaglink_topup_id"] = op.Id.ToString(), ["diaglink_company_id"] = op.CompanyId.ToString() },
            ["latest_charge"] = new JsonObject { ["id"] = "ch_local", ["object"] = "charge", ["paid"] = true, ["captured"] = true,
                ["amount_refunded"] = 0, ["payment_intent"] = "pi_local", ["customer"] = op.StripeCustomerId, ["currency"] = "eur",
                ["amount"] = 2000, ["status"] = "succeeded", ["created"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds() } }
    };
    private static StripeWalletTopUpGateway Gateway(HttpClient http) => new(new StripeBillingOptions { Enabled = true, SecretKey = "sk_test_local", PriceId = "price_local" },
        new StripeClient("sk_test_local", httpClient: new SystemNetHttpClient(http, maxNetworkRetries: 0)));
    private static HttpResponseMessage Json(JsonObject value) => new(HttpStatusCode.OK) { Content = new StringContent(value.ToJsonString(), System.Text.Encoding.UTF8, "application/json") };

    [TestMethod]
    public async Task CreatesFixedCardCheckoutWithFrozenAmountAndIdempotencyKey()
    {
        var op = Op(); var requests = new List<string>();
        using var http = new HttpClient(new Handler(async req =>
        {
            Assert.AreEqual(HttpMethod.Post, req.Method); Assert.IsTrue(req.RequestUri!.AbsolutePath.EndsWith("/checkout/sessions"));
            Assert.AreEqual($"diaglink:wallet-topup:{op.Id:N}:checkout", req.Headers.GetValues("Idempotency-Key").Single());
            var body = Uri.UnescapeDataString(await req.Content!.ReadAsStringAsync()); requests.Add(body);
            StringAssert.Contains(body, "line_items[0][price_data][unit_amount]=2000");
            StringAssert.Contains(body, "payment_method_types[0]=card");
            StringAssert.Contains(body, "currency]=eur"); StringAssert.Contains(body, "automatic_tax[enabled]=false");
            StringAssert.Contains(body, "payment_intent_data[metadata][diaglink_company_id]=" + op.CompanyId);
            return Json(Session(op));
        }));
        await Gateway(http).CreateAsync(op, default); await Gateway(http).CreateAsync(op, default);
        Assert.AreEqual(requests[0], requests[1]);
    }
    [TestMethod]
    [DataRow("valid", "PaymentConfirmed")]
    [DataRow("unpaid", "AwaitingPayment")]
    [DataRow("expired", "ReconciliationRequired")]
    [DataRow("processing", "ReconciliationRequired")]
    [DataRow("pi-amount", "ReconciliationRequired")]
    [DataRow("pi-customer", "ReconciliationRequired")]
    [DataRow("pi-currency", "ReconciliationRequired")]
    [DataRow("refunded", "ReconciliationRequired")]
    [DataRow("not-captured", "ReconciliationRequired")]
    [DataRow("metadata", "reject")]
    [DataRow("currency", "reject")]
    [DataRow("total", "reject")]
    [DataRow("customer", "reject")]
    [DataRow("live", "reject")]
    public async Task RevalidatesSessionIntentAndChargeBeforeConfirming(string scenario, string expected)
    {
        var op = Op(); var session = Session(op); var pi = session["payment_intent"]!;
        switch (scenario)
        {
            case "unpaid": session["payment_status"] = "unpaid"; break;
            case "expired": session["status"] = "expired"; break;
            case "processing": pi["status"] = "processing"; break;
            case "pi-amount": pi["amount_received"] = 1999; break;
            case "pi-customer": pi["customer"] = "cus_other"; break;
            case "pi-currency": pi["currency"] = "usd"; break;
            case "refunded": pi["latest_charge"]!["amount_refunded"] = 100; break;
            case "not-captured": pi["latest_charge"]!["captured"] = false; break;
            case "metadata": session["metadata"]!["diaglink_company_id"] = Guid.NewGuid().ToString(); break;
            case "currency": session["currency"] = "usd"; break;
            case "total": session["amount_total"] = 1999; break;
            case "customer": session["customer"] = "cus_other"; break;
            case "live": session["livemode"] = true; break;
        }
        using var http = new HttpClient(new Handler(req => { Assert.AreEqual(HttpMethod.Get, req.Method); return Task.FromResult(Json(session)); }));
        if (expected == "reject") await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Gateway(http).ReadAsync(op, "cs_test_local", default));
        else Assert.AreEqual(expected, (await Gateway(http).ReadAsync(op, "cs_test_local", default)).Status);
    }
}
