using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Stripe;
using WebApp.Api.Models.Entities;
using WebApp.Api.Services;

namespace WebApp.Api.Tests;
public partial class StripeBillingTests
{
    [TestMethod]
    public async Task ExistingIncompleteSubscriptionSavesPaymentMethodWithoutChangingQuantityOrAnchor()
    {
        var company=Guid.NewGuid(); var writes=0;
        using var http=new HttpClient(new Handler(async request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                writes++;
                var body=Uri.UnescapeDataString(await request.Content!.ReadAsStringAsync());
                Assert.AreEqual("payment_settings[save_default_payment_method]=on_subscription",body);
                Assert.AreEqual("diaglink:subscription:sub_local:save-payment-method",request.Headers.GetValues("Idempotency-Key").Single());
            }
            return Json($$$"""{"id":"sub_local","object":"subscription","customer":"cus_local","status":"incomplete","metadata":{"diaglink_company_id":"{{{company}}}"},"items":{"object":"list","has_more":false,"data":[{"id":"si_local","object":"subscription_item","quantity":1,"current_period_start":1789084800,"current_period_end":1791676800,"price":{"id":"price_local","object":"price"}}]}}""");
        }));
        var gateway=new StripeBillingGateway(Options,new StripeClient(Options.SecretKey,httpClient:new SystemNetHttpClient(http,maxNetworkRetries:0)));
        await gateway.PrepareInitialPaymentAsync("sub_local","cus_local",company,default);
        Assert.AreEqual(1,writes);
    }
    [TestMethod]
    [DataRow("paid")]
    [DataRow("open")]
    [DataRow("wrong_currency")]
    [DataRow("wrong_customer")]
    [DataRow("wrong_subscription")]
    [DataRow("wrong_amount")]
    [DataRow("proration")]
    [DataRow("unsettled")]
    [DataRow("manual_paid")]
    [DataRow("clock")]
    [DataRow("future_without_clock")]
    [DataRow("live_future")]
    [DataRow("clock_mismatch")]
    [DataRow("clock_behind")]
    [DataRow("clock_advancing")]
    [DataRow("clock_history")]
    [DataRow("clock_history_bad_event")]
    public async Task SubscriptionPaymentIsRereadAndVerifiedWithRealSdk(string scenario)
    {
        var company=Guid.NewGuid(); var now=DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var clockScenario=scenario.StartsWith("clock");
        if(clockScenario || scenario is "future_without_clock" or "live_future") now+=86400*40;
        var invoice=JsonNode.Parse($$$$"""
        {"id":"in_local","object":"invoice","livemode":false,"customer":"cus_local","currency":"eur",
         "billing_reason":"subscription_create","status":"paid","subtotal":2990,"total_excluding_tax":2990,"total":2990,
         "amount_due":2990,"amount_paid":2990,"amount_remaining":0,"status_transitions":{"paid_at":{{{{now}}}}},
         "hosted_invoice_url":"https://invoice.stripe.com/i/test",
         "parent":{"type":"subscription_details","subscription_details":{"subscription":"sub_local"}},
         "lines":{"object":"list","has_more":false,"data":[{"id":"il_local","object":"line_item","amount":2990,"quantity":1,
          "period":{"start":{{{{now-86400}}}},"end":{{{{now+86400}}}}},
          "pricing":{"type":"price_details","price_details":{"price":"price_local"}},
          "parent":{"type":"subscription_item_details","subscription_item_details":{"subscription":"sub_local","subscription_item":"si_local","proration":false}}}]}}
        """)!;
        var historical=scenario.StartsWith("clock_history");
        if(historical) { invoice["number"]="number"; invoice["billing_reason"]="subscription_cycle"; }
        if(clockScenario) invoice["test_clock"]=scenario=="clock_mismatch"?"clock_other":"clock_local";
        if(scenario=="live_future") invoice["livemode"]=true;
        if(scenario=="open") invoice["status"]="open";
        if(scenario=="wrong_currency") invoice["currency"]="usd";
        if(scenario=="wrong_customer") invoice["customer"]="cus_other";
        if(scenario=="wrong_subscription") invoice["parent"]!["subscription_details"]!["subscription"]="sub_other";
        if(scenario=="wrong_amount") invoice["amount_paid"]=2989;
        if(scenario=="proration") invoice["lines"]!["data"]![0]!["parent"]!["subscription_item_details"]!["proration"]=true;
        using var http=new HttpClient(new Handler(request=>
        {
            Assert.AreEqual(HttpMethod.Get,request.Method);
            var path=request.RequestUri!.AbsolutePath;
            if(path.Contains("/events/")) return Task.FromResult(Json($$$$"""{"id":"evt_history","object":"event","type":"invoice.payment_succeeded","livemode":false,"data":{"object":{"id":"{{{{(scenario=="clock_history_bad_event"?"in_other":"in_local")}}}}","object":"invoice","status":"paid"}}}"""));
            if(path.Contains("/test_helpers/test_clocks/")) return Task.FromResult(Json($$$"""{"id":"clock_local","object":"test_helpers.test_clock","livemode":false,"status":"{{{(scenario=="clock_advancing"?"advancing":"ready")}}}","frozen_time":{{{(scenario=="clock_behind"?now-10:historical?now+90000:now)}}}}"""));
            if(path.Contains("/prices/")) return Task.FromResult(Json("""{"id":"price_local","object":"price","active":true,"livemode":false,"currency":"eur","unit_amount":2990,"unit_amount_decimal":"2990","type":"recurring","billing_scheme":"per_unit","tax_behavior":"exclusive","recurring":{"interval":"month","interval_count":1,"usage_type":"licensed"}}"""));
            if(path.Contains("/subscriptions/")) return Task.FromResult(Json($$$$"""{"id":"sub_local","object":"subscription","test_clock":{{{{(clockScenario?"\"clock_local\"":"null")}}}},"customer":"cus_local","status":"active","latest_invoice":"in_local","metadata":{"diaglink_company_id":"{{{{company}}}}"},"items":{"object":"list","has_more":false,"data":[{"id":"si_local","object":"subscription_item","quantity":1,"current_period_start":{{{{(historical?now+86400:now-86400)}}}},"current_period_end":{{{{(historical?now+172800:now+86400)}}}},"price":{"id":"price_local","object":"price"}}]}}"""));
            if(path.Contains("/invoices/")) return Task.FromResult(Json(invoice.ToJsonString()));
            StringAssert.Contains(path,"/invoice_payments");
            if(scenario=="manual_paid") return Task.FromResult(Json("""{"object":"list","has_more":false,"data":[]}"""));
            return Task.FromResult(Json($$$$"""{"object":"list","has_more":false,"data":[{"id":"inpay_local","object":"invoice_payment","invoice":"in_local","status":"paid","currency":"eur","amount_paid":2990,"payment":{"type":"payment_intent","payment_intent":{"id":"pi_local","object":"payment_intent","status":"{{{{(scenario=="unsettled"?"processing":"succeeded")}}}}","livemode":false,"currency":"eur","customer":"cus_local","amount_received":2990}}}]}"""));
        }));
        var gateway=new StripeBillingGateway(Options,new StripeClient(Options.SecretKey,httpClient:new SystemNetHttpClient(http,maxNetworkRetries:0)));
        var account=new BillingAccount {CompanyId=company,StripeCustomerId="cus_local",StripeSubscriptionId="sub_local"};
        if(historical)
        {
            var request=new SubscriptionReconciliationRequest(company,Guid.NewGuid(),"sub_local","in_local","number","evt_history",
                DateTimeOffset.FromUnixTimeSeconds(now-86400).UtcDateTime,DateTimeOffset.FromUnixTimeSeconds(now+86400).UtcDateTime,2990);
            if(scenario=="clock_history_bad_event")
                await Assert.ThrowsExactlyAsync<SubscriptionReconciliationException>(()=>gateway.ReadForReconciliationAsync(account,request,default));
            else Assert.IsTrue((await gateway.ReadForReconciliationAsync(account,request,default)).Confirmed);
            return;
        }
        if(scenario is "paid" or "open" or "clock")
        {
            var result=await gateway.ReadAsync(account,null,default);
            Assert.AreEqual(scenario!="open",result.Confirmed);
            Assert.AreEqual(scenario=="open"?"https://invoice.stripe.com/i/test":null,result.PaymentUrl);
        }
        else if(scenario=="clock_advancing") await Assert.ThrowsExactlyAsync<InvalidOperationException>(()=>gateway.ReadAsync(account,"in_local",default));
        else await Assert.ThrowsExactlyAsync<SubscriptionReconciliationException>(()=>gateway.ReadAsync(account,"in_local",default));
    }
}



