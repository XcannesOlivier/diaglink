using Microsoft.AspNetCore.Http;
using WebApp.Api.Services;

namespace WebApp.Api.Tests;

[TestClass]
public sealed class DomainRoutingMiddlewareTests
{
    [TestMethod]
    [DataRow("/login", "")]
    [DataRow("/login", "?install=1")]
    [DataRow("/app", "")]
    [DataRow("/app/machines", "?tab=documents")]
    [DataRow("/app/checkout-return", "?payment=ok")]
    [DataRow("/administration", "")]
    public async Task PublicHostRedirectsApplicationPagesToAppHost(string path, string query)
    {
        var context = Context(DomainRoutingMiddleware.PublicHost, path, query);
        var nextCalled = false;
        var middleware = new DomainRoutingMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });

        await middleware.InvokeAsync(context);

        Assert.IsFalse(nextCalled);
        Assert.AreEqual(StatusCodes.Status308PermanentRedirect, context.Response.StatusCode);
        Assert.AreEqual($"https://{DomainRoutingMiddleware.AppHost}{path}{query}", context.Response.Headers.Location.ToString());
    }

    [TestMethod]
    [DataRow("/", "")]
    [DataRow("/contact", "?source=footer")]
    [DataRow("/demonstration", "")]
    [DataRow("/commencer", "?checkout=returned")]
    [DataRow("/confidentialite", "")]
    [DataRow("/mentions-legales/", "")]
    public async Task AppHostRedirectsPublicPagesToPublicHost(string path, string query)
    {
        var context = Context(DomainRoutingMiddleware.AppHost, path, query);
        var middleware = new DomainRoutingMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);

        Assert.AreEqual(StatusCodes.Status308PermanentRedirect, context.Response.StatusCode);
        Assert.AreEqual($"https://{DomainRoutingMiddleware.PublicHost}{path}{query}", context.Response.Headers.Location.ToString());
    }

    [TestMethod]
    [DataRow("diaglink.com", "/commencer")]
    [DataRow("diaglink.com", "/api/public/machine-requests")]
    [DataRow("diaglink.com", "/api/health")]
    [DataRow("diaglink.com", "/api/auth/me")]
    [DataRow("app.diaglink.com", "/login")]
    [DataRow("app.diaglink.com", "/app")]
    [DataRow("app.diaglink.com", "/app/checkout-return")]
    [DataRow("app.diaglink.com", "/api/health")]
    [DataRow("app.diaglink.com", "/api/auth/me")]
    [DataRow("localhost", "/")]
    public async Task CanonicalAndApiRequestsContinueWithoutRedirect(string host, string path)
    {
        var context = Context(host, path);
        var nextCalled = false;
        var middleware = new DomainRoutingMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });

        await middleware.InvokeAsync(context);

        Assert.IsTrue(nextCalled);
        Assert.AreNotEqual(StatusCodes.Status308PermanentRedirect, context.Response.StatusCode);
        Assert.IsFalse(context.Response.Headers.ContainsKey("Location"));
    }

    private static DefaultHttpContext Context(string host, string path, string query = "")
    {
        var context = new DefaultHttpContext();
        context.Request.Host = new HostString(host);
        context.Request.Path = path;
        context.Request.QueryString = new QueryString(query);
        return context;
    }
}
