using Microsoft.VisualStudio.TestTools.UnitTesting;
using Stripe;
using WebApp.Api.Models.Entities;
using WebApp.Api.Services;
namespace WebApp.Api.Tests;
public partial class StripeBillingTests
{
    [TestMethod]
    [DataRow(0)]
    [DataRow(2)]
    public async Task NextCycleQuantityDoesNotProrateInvoiceOrMoveAnchor(int quantity)
    {
        var start=DateTimeOffset.FromUnixTimeSeconds(1789084800).UtcDateTime;var end=DateTimeOffset.FromUnixTimeSeconds(1791676800).UtcDateTime;
        using var http=new HttpClient(new Handler(async request=>
        {
            var body=Uri.UnescapeDataString(await request.Content!.ReadAsStringAsync());
            StringAssert.Contains(body,$"quantity={quantity}");StringAssert.Contains(body,"proration_behavior=none");
            Assert.IsFalse(body.Contains("billing_cycle_anchor"));Assert.IsFalse(body.Contains("invoice_now"));
            Assert.AreEqual("/v1/subscription_items/si_local",request.RequestUri!.AbsolutePath);
            return Json($$$"""{"id":"si_local","object":"subscription_item","quantity":{{{quantity}}},"current_period_start":1789084800,"current_period_end":1791676800}""");
        }));
        var gateway=new StripeBillingGateway(Options,new StripeClient(Options.SecretKey,httpClient:new SystemNetHttpClient(http,maxNetworkRetries:0)));
        await gateway.SetQuantityAsync(new BillingAccount(),new("active",start,end,false,null,null,null,"si_local",1),quantity,"evt_quantity",default);
    }
}
