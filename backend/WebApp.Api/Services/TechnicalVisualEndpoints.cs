namespace WebApp.Api.Services;

public static class TechnicalVisualEndpoints
{
    public static WebApplication MapTechnicalVisualEndpoints(this WebApplication app, string authorizationPolicy)
    {
        app.MapGet("/api/chat/visuals/{visualId:long}", OpenAsync)
            .RequireAuthorization(authorizationPolicy)
            .WithName("OpenTechnicalVisual");
        return app;
    }

    private static async Task<IResult> OpenAsync(
        long visualId,
        HttpContext httpContext,
        UserIdentityService userIdentityService,
        TechnicalVisualAccessService accessService,
        CancellationToken cancellationToken)
    {
        var canonicalUserObjectId = await userIdentityService.GetCanonicalUserObjectIdAsync(
            httpContext.User,
            cancellationToken);
        var blobReader = httpContext.RequestServices.GetService<ITechnicalVisualBlobReader>();

        return await OpenResolvedAsync(
            visualId,
            canonicalUserObjectId,
            httpContext.User,
            accessService,
            blobReader,
            httpContext.Response,
            cancellationToken);
    }

    internal static async Task<IResult> OpenResolvedAsync(
        long visualId,
        string? canonicalUserObjectId,
        System.Security.Claims.ClaimsPrincipal user,
        TechnicalVisualAccessService accessService,
        ITechnicalVisualBlobReader? blobReader,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(canonicalUserObjectId))
        {
            return Results.NotFound();
        }

        var resolution = await accessService.ResolveAsync(
            visualId,
            canonicalUserObjectId,
            user,
            cancellationToken);

        if (resolution.Kind == TechnicalVisualAccessKind.Forbidden)
        {
            return Results.Forbid();
        }

        if (resolution.Kind != TechnicalVisualAccessKind.Success || resolution.BlobName is null)
        {
            return Results.NotFound();
        }

        if (blobReader is null)
        {
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        var stream = await blobReader.OpenReadAsync(resolution.BlobName, cancellationToken);
        if (stream is null)
        {
            return Results.NotFound();
        }

        response.Headers.CacheControl = "no-store";
        return Results.Stream(stream, "image/png");
    }
}
