using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebApp.Api.Data;
using WebApp.Api.Services;
namespace WebApp.Api.Tests;
[TestClass]
public class WalletTopUpAuthorizationTests
{
    [TestMethod]
    public async Task OnlyTheSignedWebhookIsAnonymous()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddScoped<DiagLinkDbContext>(); builder.Services.AddScoped<CompanyWalletTopUpService>();
        builder.Services.AddScoped<StripeWalletTopUpWebhook>(); builder.Services.AddSingleton(new StripeBillingOptions());
        await using var app = builder.Build(); app.MapStripeWalletTopUps();
        var routes = ((IEndpointRouteBuilder)app).DataSources.SelectMany(s => s.Endpoints).Cast<RouteEndpoint>().ToArray();
        Assert.AreEqual(3, routes.Length);
        foreach (var route in routes)
        {
            if (route.RoutePattern.RawText!.Contains("webhooks")) Assert.IsNotNull(route.Metadata.GetMetadata<IAllowAnonymous>());
            else
            {
                Assert.IsNull(route.Metadata.GetMetadata<IAllowAnonymous>());
                Assert.IsTrue(route.Metadata.GetOrderedMetadata<IAuthorizeData>().Any(p => p.Policy == "SuperAdminOnly"));
            }
        }
    }
}
