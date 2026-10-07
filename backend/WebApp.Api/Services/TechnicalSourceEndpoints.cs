namespace WebApp.Api.Services;

public static class TechnicalSourceEndpoints
{
    public static WebApplication MapTechnicalSourceEndpoints(this WebApplication app, string authorizationPolicy)
    {
        app.MapGet("/api/chat/sources/{sourceReferenceId:long}/document", OpenAsync)
            .RequireAuthorization(authorizationPolicy)
            .WithName("OpenTechnicalSourceDocument");
        return app;
    }

    private static async Task<IResult> OpenAsync(
        long sourceReferenceId,
        HttpContext httpContext,
        UserIdentityService userIdentityService,
        CancellationToken cancellationToken)
    {
        var canonicalUserObjectId = await userIdentityService.GetCanonicalUserObjectIdAsync(
            httpContext.User,
            cancellationToken);
        var accessService = httpContext.RequestServices.GetService<TechnicalSourceAccessService>();
        var blobReader = httpContext.RequestServices.GetService<ITechnicalDocumentBlobReader>();

        if (accessService is null)
        {
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        return await OpenResolvedAsync(
            sourceReferenceId,
            canonicalUserObjectId,
            httpContext.User,
            accessService,
            blobReader,
            httpContext.Response,
            cancellationToken);
    }

    internal static async Task<IResult> OpenResolvedAsync(
        long sourceReferenceId,
        string? canonicalUserObjectId,
        System.Security.Claims.ClaimsPrincipal user,
        TechnicalSourceAccessService accessService,
        ITechnicalDocumentBlobReader? blobReader,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(canonicalUserObjectId))
        {
            return Results.NotFound();
        }

        var resolution = await accessService.ResolveAsync(
            sourceReferenceId,
            canonicalUserObjectId,
            user,
            cancellationToken);

        if (resolution.Kind == TechnicalSourceAccessKind.Forbidden)
        {
            return Results.Forbid();
        }

        if (resolution.Kind != TechnicalSourceAccessKind.Success || resolution.SourceBlob is null)
        {
            return Results.NotFound();
        }

        if (blobReader is null)
        {
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        var stream = await blobReader.OpenPdfAsync(resolution.SourceBlob, cancellationToken);
        if (stream is null)
        {
            return Results.NotFound();
        }

        response.Headers.CacheControl = "no-store";
        response.Headers.ContentDisposition = "inline";
        return Results.Stream(stream, "application/pdf", enableRangeProcessing: stream.CanSeek);
    }
}