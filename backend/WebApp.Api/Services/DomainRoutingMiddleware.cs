namespace WebApp.Api.Services;

public sealed class DomainRoutingMiddleware(RequestDelegate next)
{
    public const string PublicHost = "diaglink.com";
    public const string AppHost = "app.diaglink.com";

    private static readonly HashSet<string> PublicPages = new(StringComparer.OrdinalIgnoreCase)
    {
        "/",
        "/contact",
        "/demonstration",
        "/commencer",
        "/confidentialite",
        "/mentions-legales"
    };

    public async Task InvokeAsync(HttpContext context)
    {
        var targetHost = ResolveTargetHost(context.Request.Host.Host, context.Request.Path);
        if (targetHost is null)
        {
            await next(context);
            return;
        }

        // URL fragments are not sent in HTTP requests. By omitting a fragment from Location,
        // browsers preserve the original fragment when following the redirect.
        var location = $"https://{targetHost}{context.Request.PathBase}{context.Request.Path}{context.Request.QueryString}";
        context.Response.Redirect(location, permanent: true, preserveMethod: true);
    }

    internal static string? ResolveTargetHost(string host, PathString path)
    {
        if (host.Equals(PublicHost, StringComparison.OrdinalIgnoreCase)
            && IsApplicationPage(path))
        {
            return AppHost;
        }

        if (host.Equals(AppHost, StringComparison.OrdinalIgnoreCase)
            && IsPublicPage(path))
        {
            return PublicHost;
        }

        return null;
    }

    private static bool IsApplicationPage(PathString path) =>
        path.StartsWithSegments("/app", StringComparison.OrdinalIgnoreCase)
        || PathEquals(path, "/login")
        || PathEquals(path, "/administration");

    private static bool IsPublicPage(PathString path)
    {
        var value = path.Value ?? "/";
        if (value.Length > 1) value = value.TrimEnd('/');
        return PublicPages.Contains(value);
    }

    private static bool PathEquals(PathString path, string expected)
    {
        var value = path.Value ?? string.Empty;
        return value.Equals(expected, StringComparison.OrdinalIgnoreCase)
            || value.Equals($"{expected}/", StringComparison.OrdinalIgnoreCase);
    }
}

public static class DomainRoutingMiddlewareExtensions
{
    public static IApplicationBuilder UseDomainRouting(this IApplicationBuilder app) =>
        app.UseMiddleware<DomainRoutingMiddleware>();
}
