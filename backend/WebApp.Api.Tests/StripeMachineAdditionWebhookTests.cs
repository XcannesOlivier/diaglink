using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebApp.Api.Models.Entities;
using WebApp.Api.Services;
using Fixture = WebApp.Api.Tests.AiCreditConsumptionServiceTests.Fixture;

namespace WebApp.Api.Tests;

public partial class StripeMachineAdditionTests
{
    private const string WebhookSecret = "whsec_local_only";
    [TestMethod]
    public async Task SharedInvoiceWebhookPreservesAdditionProcessing()
    {
        await using var f = new Fixture(); var (_, op, remote) = await PendingWebhook(f);
        remote.PaymentStatus = "paid";
        var dispatcher = new StripeSubscriptionWebhook(new StripeBillingOptions
        { Enabled=true, SecretKey="sk_test_local", PriceId="price_local", WebhookSecret=WebhookSecret },
            null!, Webhook(f,remote), NullLogger<StripeSubscriptionWebhook>.Instance);
        var body=PaymentEvent(op).ToJsonString();
        Assert.AreEqual("Completed",(await dispatcher.HandleAsync(body,Sign(body))).Status);
        Assert.AreEqual("AlreadyCompleted",(await dispatcher.HandleAsync(body,Sign(body))).Status);
    }
    private static StripeMachineAdditionWebhook Webhook(Fixture f, Remote remote) => new(f.Options,
        new StripeBillingOptions { Enabled = true, SecretKey = "sk_test_local", PriceId = "price_local", WebhookSecret = WebhookSecret },
        Service(f, remote), NullLogger<StripeMachineAdditionWebhook>.Instance);

    private static string Sign(string body, long? timestamp = null, string secret = WebhookSecret)
    {
        var t = timestamp ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{t}.{body}"));
        return $"t={t},v1={Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    private static JsonObject PaymentEvent(StripeMachineAddition op, string id = "evt_local") => new()
    {
        ["id"] = id, ["object"] = "event", ["type"] = "invoice.payment_succeeded",
        ["api_version"] = Stripe.StripeConfiguration.ApiVersion, ["livemode"] = false,
        ["data"] = new JsonObject { ["object"] = new JsonObject {
            ["id"] = op.StripeInvoiceId, ["object"] = "invoice", ["customer"] = op.StripeCustomerId,
            ["currency"] = "eur", ["status"] = "paid",
            ["subtotal"] = op.AiAmountCents + op.ServiceAmountCents,
            ["total_excluding_tax"] = op.AiAmountCents + op.ServiceAmountCents,
            ["total"] = op.AiAmountCents + op.ServiceAmountCents,
            ["amount_paid"] = op.AiAmountCents + op.ServiceAmountCents, ["amount_remaining"] = 0,
            ["status_transitions"] = new JsonObject { ["paid_at"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds() },
            ["metadata"] = new JsonObject { ["diaglink_addition_id"] = op.Id.ToString(),
                ["diaglink_machine_id"] = op.MachineId.ToString(), ["diaglink_subscription_id"] = op.StripeSubscriptionId }
        } }
    };

    private static async Task<(Guid Machine, StripeMachineAddition Op, Remote Remote)> PendingWebhook(Fixture f)
    {
        var machine = await Prepare(f); var remote = new Remote { PaymentStatus = "open" };
        Assert.AreEqual("AwaitingPayment", (await Service(f, remote).AddActiveMachineAsync(machine, Activation)).Status);
        await using var db = f.Db();
        return (machine, await db.StripeMachineAdditions.AsNoTracking().SingleAsync(), remote);
    }

    [TestMethod]
    [DataRow("missing")]
    [DataRow("wrong")]
    [DataRow("expired")]
    [DataRow("tampered")]
    public async Task WebhookRejectsInvalidSignatureBeforeDatabaseAccess(string scenario)
    {
        await using var f = new Fixture(); // No schema: any database access would fail.
        const string body = "{}";
        var signature = scenario switch { "missing" => "", "wrong" => Sign(body, secret: "whsec_wrong"),
            "expired" => Sign(body, DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeSeconds()), _ => Sign(body + " ") };
        Assert.AreEqual(400, (await Webhook(f, new Remote()).HandleAsync(body, signature)).HttpStatus);
    }

    [TestMethod]
    public async Task WebhookCompletesOnceAndRepeatedOrDistinctEventsHaveNoEffect()
    {
        await using var f = new Fixture(); var (machine, op, remote) = await PendingWebhook(f);
        remote.PaymentStatus = "paid";
        var webhook = Webhook(f, remote); var body = PaymentEvent(op).ToJsonString();
        Assert.AreEqual("Completed", (await webhook.HandleAsync(body, Sign(body))).Status);
        var calls = remote.Calls;
        Assert.AreEqual("AlreadyCompleted", (await webhook.HandleAsync(body, Sign(body))).Status);
        var second = PaymentEvent(op, "evt_second").ToJsonString();
        Assert.AreEqual("AlreadyCompleted", (await webhook.HandleAsync(second, Sign(second))).Status);
        Assert.AreEqual(calls, remote.Calls);
        await using var db = f.Db(); var saved = await db.StripeMachineAdditions.SingleAsync();
        Assert.AreEqual("evt_local", saved.ExternalEventId);
        Assert.AreEqual("[\"inpay_local\"]", saved.PaymentReference);
        Assert.IsNotNull(saved.PaymentConfirmedAtUtc);
        Assert.AreEqual(StripeMachineAdditionStage.Completed, saved.Stage);
        Assert.AreEqual(1, await db.MachineBillingPeriods.CountAsync(p => p.MachineId == machine));
        Assert.AreEqual(10m, (await db.MachineBillingPeriods.SingleAsync(p => p.MachineId == machine)).IncludedAiBudgetRealCost);
        Assert.AreEqual(0, await db.CreditLedger.CountAsync());
        Assert.AreEqual(0, await db.CompanyWallets.CountAsync());
    }

    [TestMethod]
    public async Task WebhookRetryAfterCrashResumesSameEventAndInvoice()
    {
        await using var f = new Fixture(); var (machine, op, remote) = await PendingWebhook(f);
        remote.PaymentStatus = "paid"; remote.FailPaymentReads = 1;
        var body = PaymentEvent(op).ToJsonString(); var webhook = Webhook(f, remote);
        Assert.AreEqual(503, (await webhook.HandleAsync(body, Sign(body))).HttpStatus);
        await using (var db = f.Db())
        {
            Assert.AreEqual("evt_local", (await db.StripeMachineAdditions.SingleAsync()).ExternalEventId);
            Assert.AreEqual(0, await db.MachineBillingPeriods.CountAsync(p => p.MachineId == machine));
        }
        Assert.AreEqual("Completed", (await webhook.HandleAsync(body, Sign(body))).Status);
        Assert.AreEqual(1, remote.InvoiceCount); Assert.AreEqual(1, remote.QuantityWrites);
        await using var after = f.Db(); Assert.AreEqual(1, await after.MachineBillingPeriods.CountAsync(p => p.MachineId == machine));
    }

    [TestMethod]
    [DataRow("unknown", "UnknownInvoice")]
    [DataRow("currency", "ReconciliationRequired")]
    [DataRow("customer", "ReconciliationRequired")]
    [DataRow("company", "ReconciliationRequired")]
    [DataRow("metadata", "ReconciliationRequired")]
    [DataRow("amount", "ReconciliationRequired")]
    [DataRow("partial", "PaymentIncomplete")]
    [DataRow("open", "PaymentIncomplete")]
    [DataRow("not-ready", "OperationNotReady")]
    public async Task WebhookInvalidInvoiceNeverCreatesPeriod(string scenario, string expected)
    {
        await using var f = new Fixture(); var (machine, op, remote) = await PendingWebhook(f);
        var evt = PaymentEvent(op); var invoice = evt["data"]!["object"]!;
        switch (scenario)
        {
            case "unknown": invoice["id"] = "in_unknown"; break;
            case "currency": invoice["currency"] = "usd"; break;
            case "customer": invoice["customer"] = "cus_other"; break;
            case "metadata": invoice["metadata"]!["diaglink_machine_id"] = Guid.NewGuid().ToString(); break;
            case "amount": invoice["subtotal"] = 1; break;
            case "partial": invoice["amount_paid"] = 1; invoice["amount_remaining"] = 1994; break;
            case "open": invoice["status"] = "open"; break;
            case "company":
                await using (var db = f.Db()) { (await db.BillingAccounts.SingleAsync()).StripeCustomerId = "cus_other"; await db.SaveChangesAsync(); }
                break;
            case "not-ready":
                await using (var db = f.Db()) { (await db.StripeMachineAdditions.SingleAsync()).Stage = StripeMachineAdditionStage.Reserved; await db.SaveChangesAsync(); }
                break;
        }
        var calls = remote.Calls; var body = evt.ToJsonString();
        Assert.AreEqual(expected, (await Webhook(f, remote).HandleAsync(body, Sign(body))).Status);
        Assert.AreEqual(calls, remote.Calls);
        await using var after = f.Db();
        Assert.AreEqual(0, await after.MachineBillingPeriods.CountAsync(p => p.MachineId == machine));
        Assert.IsNull((await after.StripeMachineAdditions.SingleAsync()).ExternalEventId);
    }

    [TestMethod]
    public async Task WebhookDoesNotTrustPayloadOverProviderAndCanRetryAfter23Hours()
    {
        await using var f = new Fixture(); var (machine, op, remote) = await PendingWebhook(f);
        await using (var db = f.Db()) { (await db.StripeMachineAdditions.SingleAsync()).CreatedAtUtc = DateTime.UtcNow.AddDays(-2); await db.SaveChangesAsync(); }
        var body = PaymentEvent(op).ToJsonString(); var webhook = Webhook(f, remote);
        Assert.AreEqual("AwaitingPayment", (await webhook.HandleAsync(body, Sign(body))).Status);
        await using (var db = f.Db()) Assert.AreEqual(0, await db.MachineBillingPeriods.CountAsync(p => p.MachineId == machine));
        remote.PaymentStatus = "paid";
        Assert.AreEqual("Completed", (await webhook.HandleAsync(body, Sign(body))).Status);
    }

    [TestMethod]
    public async Task WebhookLatePaymentPersistsProofButCreatesNoExpiredPeriod()
    {
        await using var f = new Fixture(); var (machine, op, remote) = await PendingWebhook(f);
        await using (var db = f.Db()) { (await db.StripeMachineAdditions.SingleAsync()).CycleEndUtc = DateTime.UtcNow.AddSeconds(-1); await db.SaveChangesAsync(); }
        remote.PaymentStatus = "paid"; remote.PaidAtUtc = DateTime.UtcNow;
        var body = PaymentEvent(op).ToJsonString();
        Assert.AreEqual("ReconciliationRequired", (await Webhook(f, remote).HandleAsync(body, Sign(body))).Status);
        await using var after = f.Db(); var saved = await after.StripeMachineAdditions.SingleAsync();
        Assert.AreEqual(StripeMachineAdditionStage.PaymentConfirmed, saved.Stage);
        Assert.IsNotNull(saved.PaymentReference); Assert.IsNotNull(saved.PaymentConfirmedAtUtc);
        Assert.AreEqual(0, await after.MachineBillingPeriods.CountAsync(p => p.MachineId == machine));
    }

    [TestMethod]
    public async Task WebhookRecoversLostFinalizationCheckpointAfter23HoursWithoutStripeWrites()
    {
        await using var f = new Fixture(); var (_, op, remote) = await PendingWebhook(f);
        await using (var db = f.Db())
        {
            var saved = await db.StripeMachineAdditions.SingleAsync();
            saved.Stage = StripeMachineAdditionStage.StripeQuantityUpdated;
            saved.CreatedAtUtc = DateTime.UtcNow.AddDays(-2); await db.SaveChangesAsync();
        }
        remote.PaymentStatus = "paid";
        var body = PaymentEvent(op).ToJsonString();
        Assert.AreEqual("Completed", (await Webhook(f, remote).HandleAsync(body, Sign(body))).Status);
        Assert.AreEqual(1, remote.QuantityWrites); Assert.AreEqual(1, remote.InvoiceCount); Assert.AreEqual(1, remote.FinalizeCount);
    }

    [TestMethod]
    public async Task WebhookIgnoresUnrelatedEventsAndRejectsWrongMode()
    {
        await using var f = new Fixture(); var (_, op, remote) = await PendingWebhook(f);
        var evt = PaymentEvent(op); evt["type"] = "invoice.paid";
        var body = evt.ToJsonString(); Assert.AreEqual("Ignored", (await Webhook(f, remote).HandleAsync(body, Sign(body))).Status);
        evt["type"] = "invoice.payment_succeeded"; evt["livemode"] = true;
        body = evt.ToJsonString(); Assert.AreEqual(400, (await Webhook(f, remote).HandleAsync(body, Sign(body))).HttpStatus);
    }

    [TestMethod]
    public async Task WebhookHttpAdapterVerifiesExactBodyAndReturnsStatus()
    {
        await using var f = new Fixture(); var (_, op, remote) = await PendingWebhook(f);
        var evt = PaymentEvent(op); evt["type"] = "invoice.paid";
        var body = "\n " + evt.ToJsonString() + "  \n";
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        context.Request.Headers["Stripe-Signature"] = Sign(body);
        var result = await StripeMachineAdditionWebhook.HandleHttpAsync(context.Request, Webhook(f, remote), default);
        Assert.AreEqual(200, ((Microsoft.AspNetCore.Http.IStatusCodeHttpResult)result).StatusCode);
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body.Trim()));
        result = await StripeMachineAdditionWebhook.HandleHttpAsync(context.Request, Webhook(f, remote), default);
        Assert.AreEqual(400, ((Microsoft.AspNetCore.Http.IStatusCodeHttpResult)result).StatusCode);
    }

    [TestMethod]
    public async Task WebhookMissingSecretFailsClosed()
    {
        await using var f = new Fixture(); var remote = new Remote();
        var webhook = new StripeMachineAdditionWebhook(f.Options, Settings, Service(f, remote),
            NullLogger<StripeMachineAdditionWebhook>.Instance);
        Assert.AreEqual(503, (await webhook.HandleAsync("{}", Sign("{}"))).HttpStatus);
    }

    [TestMethod]
    public async Task DatabaseRejectsEventIdReuseOnAnotherOperation()
    {
        await using var f = new Fixture(); var (_, op, remote) = await PendingWebhook(f);
        remote.PaymentStatus = "paid"; var body = PaymentEvent(op).ToJsonString();
        Assert.AreEqual("Completed", (await Webhook(f, remote).HandleAsync(body, Sign(body))).Status);
        await using var db = f.Db();
        var another = new Machine { Id = Guid.NewGuid(), CompanyId = op.CompanyId, Name = "another", Status = "active" };
        db.Machines.Add(another);
        op.Id = Guid.NewGuid(); op.MachineId = another.Id; op.ExternalEventId = "evt_local";
        op.StripeInvoiceId = "in_another";
        db.StripeMachineAdditions.Add(op);
        var error = await Assert.ThrowsExactlyAsync<DbUpdateException>(() => db.SaveChangesAsync());
        StringAssert.Contains(error.InnerException!.Message, "StripeMachineAdditions.ExternalEventId");
    }
}
