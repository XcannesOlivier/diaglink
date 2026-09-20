using System.Net;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Stripe;
using WebApp.Api.Models.Entities;
using WebApp.Api.Services;

namespace WebApp.Api.Tests;

[TestClass]
public class StripeMachineAdditionGatewayTests
{
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }
    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK)
    { Content = new StringContent(JsonSerializer.Serialize(value), System.Text.Encoding.UTF8, "application/json") };
    private static StripeMachineAddition Operation() => new()
    {
        Id = Guid.NewGuid(), CompanyId = Guid.NewGuid(), MachineId = Guid.NewGuid(),
        StripeCustomerId = "cus_local", StripeSubscriptionId = "sub_local", StripeSubscriptionItemId = "si_local", StripePriceId = "price_local",
        CycleStartUtc = DateTime.UtcNow.Date.AddDays(-15), CycleEndUtc = DateTime.UtcNow.Date.AddDays(15),
        ActivatedAtUtc = DateTime.UtcNow.Date, OriginalQuantity = 1, TargetQuantity = 2,
        AiAmountCents = 1000, ServiceAmountCents = 995, CreatedAtUtc = DateTime.UtcNow
    };
    private static StripeBillingGateway Gateway(HttpClient http) => new(
        new StripeBillingOptions { Enabled = true, SecretKey = "sk_test_local", PriceId = "price_local" },
        new StripeClient("sk_test_local", httpClient: new SystemNetHttpClient(http, maxNetworkRetries: 0)));
    private static long Unix(DateTime date) => new DateTimeOffset(date).ToUnixTimeSeconds();

    [TestMethod]
    public async Task QuantityUpdateHasNoProrationOrAnchorChange()
    {
        var op = Operation(); var writes = 0;
        using var http = new HttpClient(new Handler(async request =>
        {
            if (request.Method == HttpMethod.Get)
                return Json(new { id = "sub_local", @object = "subscription", customer = "cus_local", status = "active",
                    metadata = new Dictionary<string, string> { ["diaglink_company_id"] = op.CompanyId.ToString() },
                    items = new { @object = "list", has_more = false, data = new[] {
                        new { id = "si_local", @object = "subscription_item", quantity = 1,
                            current_period_start = Unix(op.CycleStartUtc), current_period_end = Unix(op.CycleEndUtc),
                            price = new { id = "price_local", @object = "price" } } } } });
            writes++;
            var body = Uri.UnescapeDataString(await request.Content!.ReadAsStringAsync());
            Assert.AreEqual("/v1/subscription_items/si_local", request.RequestUri!.AbsolutePath);
            StringAssert.Contains(body, "quantity=2"); StringAssert.Contains(body, "proration_behavior=none");
            Assert.IsFalse(body.Contains("billing_cycle_anchor")); Assert.IsFalse(body.Contains("price"));
            Assert.AreEqual($"diaglink:machine-add:{op.Id:N}:quantity", request.Headers.GetValues("Idempotency-Key").Single());
            return Json(new { id = "si_local", @object = "subscription_item", quantity = 2,
                current_period_start = Unix(op.CycleStartUtc), current_period_end = Unix(op.CycleEndUtc) });
        }));
        await Gateway(http).SetQuantityAsync(op, default);
        Assert.AreEqual(1, writes);
    }

    [TestMethod]
    public async Task InvoiceIsIsolatedHasTwoExplicitTaxExclusiveLinesAndIsFinalizedWithoutPayment()
    {
        var op = Operation(); var steps = new List<string>();
        object Invoice(string status) => new { id = "in_local", @object = "invoice", customer = "cus_local", currency = "eur", status,
            metadata = new Dictionary<string, string> { ["diaglink_addition_id"] = op.Id.ToString() }, subtotal = 1995,
            lines = new { @object = "list", has_more = false, data = new[] { new { id = "il_ai", @object = "line_item", amount = 1000 }, new { id = "il_service", @object = "line_item", amount = 995 } } } };
        using var http = new HttpClient(new Handler(async request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get) return Json(Invoice("draft"));
            var key = request.Headers.GetValues("Idempotency-Key").Single();
            StringAssert.StartsWith(key, $"diaglink:machine-add:{op.Id:N}:");
            steps.Add(key.Split(':')[^1]);
            var body = Uri.UnescapeDataString(await request.Content!.ReadAsStringAsync());
            if (path == "/v1/invoices")
            {
                StringAssert.Contains(body, "auto_advance=false"); StringAssert.Contains(body, "pending_invoice_items_behavior=exclude");
                StringAssert.Contains(body, "customer=cus_local");
                return Json(Invoice("draft"));
            }
            if (path == "/v1/invoiceitems")
            {
                StringAssert.Contains(body, "invoice=in_local"); StringAssert.Contains(body, "currency=eur");
                StringAssert.Contains(body, "tax_behavior=exclusive"); StringAssert.Contains(body, "discountable=false");
                StringAssert.Contains(body, key.EndsWith(":ai") ? "amount=1000" : "amount=995");
                StringAssert.Contains(body, "period[start]=" + Unix(op.ActivatedAtUtc)); StringAssert.Contains(body, "period[end]=" + Unix(op.CycleEndUtc));
                return Json(new { id = "ii_local", @object = "invoiceitem" });
            }
            Assert.AreEqual("/v1/invoices/in_local/finalize", path);
            StringAssert.Contains(body, "auto_advance=false");
            return Json(Invoice("open"));
        }));
        var gateway = Gateway(http);
        op.StripeInvoiceId = await gateway.CreateInvoiceAsync(op, default);
        await gateway.AddInvoiceLinesAsync(op, default);
        await gateway.FinalizeInvoiceAsync(op, default);
        CollectionAssert.AreEqual(new[] { "invoice", "ai", "service", "finalize" }, steps);
    }

    [TestMethod]
    public async Task UnexpectedInvoiceAmountIsNotFinalized()
    {
        var op = Operation(); op.StripeInvoiceId = "in_local";
        using var http = new HttpClient(new Handler(request =>
        {
            Assert.AreEqual(HttpMethod.Get, request.Method);
            return Task.FromResult(Json(new { id = "in_local", @object = "invoice", customer = "cus_local", currency = "eur", status = "draft", subtotal = 9999,
                metadata = new Dictionary<string, string> { ["diaglink_addition_id"] = op.Id.ToString() } }));
        }));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Gateway(http).FinalizeInvoiceAsync(op, default));
    }

    [TestMethod]
    [DataRow("open", "succeeded", 1995, false)]
    [DataRow("paid", "processing", 1995, false)]
    [DataRow("paid", "requires_payment_method", 1995, false)]
    [DataRow("paid", "succeeded", 1000, false)]
    [DataRow("paid", "succeeded", 1995, true)]
    public async Task PaymentRequiresSettledStripeEvidenceForTheEntireInvoice(string invoiceStatus, string intentStatus, int paid, bool confirmed)
    {
        var op = Operation(); op.StripeInvoiceId = "in_local";
        op.CreatedAtUtc = DateTime.UtcNow.AddDays(-3); op.CycleEndUtc = DateTime.UtcNow.AddDays(-1);
        var paidAt = DateTime.UtcNow.AddHours(-1);
        using var http = new HttpClient(new Handler(request =>
        {
            Assert.AreEqual(HttpMethod.Get, request.Method); // Verification must never charge/recreate anything.
            if (request.RequestUri!.AbsolutePath == "/v1/invoices/in_local")
                return Task.FromResult(Json(new { id = "in_local", @object = "invoice", customer = "cus_local", currency = "eur", status = invoiceStatus,
                    subtotal = 1995, total_excluding_tax = 1995, total = 1995, amount_remaining = invoiceStatus == "paid" ? 0 : 1995, amount_paid = paid,
                    status_transitions = new { paid_at = Unix(paidAt) },
                    metadata = new Dictionary<string, string> { ["diaglink_addition_id"] = op.Id.ToString() },
                    lines = new { @object = "list", has_more = false, data = new[] { new { id = "il_ai", @object = "line_item", amount = 1000 }, new { id = "il_service", @object = "line_item", amount = 995 } } } }));
            Assert.AreEqual("/v1/invoice_payments", request.RequestUri.AbsolutePath);
            StringAssert.Contains(Uri.UnescapeDataString(request.RequestUri.Query), "invoice=in_local");
            return Task.FromResult(Json(new { @object = "list", has_more = false, data = new[] {
                new { id = "inpay_local", @object = "invoice_payment", invoice = "in_local", status = "paid", amount_paid = paid, currency = "eur",
                    payment = new { type = "payment_intent", payment_intent = new { id = "pi_local", @object = "payment_intent",
                        status = intentStatus, currency = "eur", customer = "cus_local", amount_received = paid } } } } }));
        }));
        var state = await Gateway(http).GetInvoicePaymentAsync(op, default);
        Assert.AreEqual(confirmed, state.Payment != null);
        if (confirmed)
        {
            Assert.AreEqual("[\"inpay_local\"]", state.Payment!.Reference);
            Assert.AreEqual(Unix(paidAt), Unix(state.Payment.PaidAtUtc));
            Assert.AreEqual(DateTimeKind.Utc, state.Payment.PaidAtUtc.Kind);
        }
    }

    [TestMethod]
    public async Task MarkedPaidWithoutStripePaymentEvidenceIsNotAccepted()
    {
        var op = Operation(); op.StripeInvoiceId = "in_local";
        using var http = new HttpClient(new Handler(request =>
        {
            Assert.AreEqual(HttpMethod.Get, request.Method);
            if (request.RequestUri!.AbsolutePath == "/v1/invoice_payments")
                return Task.FromResult(Json(new { @object = "list", has_more = false, data = Array.Empty<object>() }));
            return Task.FromResult(Json(new { id = "in_local", @object = "invoice", customer = "cus_local", currency = "eur", status = "paid",
                subtotal = 1995, total_excluding_tax = 1995, total = 1995, amount_remaining = 0, amount_paid = 1995,
                status_transitions = new { paid_at = Unix(DateTime.UtcNow) },
                metadata = new Dictionary<string, string> { ["diaglink_addition_id"] = op.Id.ToString() },
                lines = new { @object = "list", has_more = false, data = new[] { new { id = "il_ai", @object = "line_item", amount = 1000 }, new { id = "il_service", @object = "line_item", amount = 995 } } } }));
        }));
        var state = await Gateway(http).GetInvoicePaymentAsync(op, default);
        Assert.IsTrue(state.ReconciliationRequired); Assert.IsNull(state.Payment);
    }
}
