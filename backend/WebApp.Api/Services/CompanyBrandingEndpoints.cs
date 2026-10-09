using Microsoft.AspNetCore.Mvc;
using WebApp.Api.Models;
using WebApp.Api.Models.Entities;

namespace WebApp.Api.Services;

public static class CompanyBrandingEndpoints
{
    public const string Route = "/api/company/branding";
    private const long MaxUploadRequestBytes = CompanyLogoStorageService.MaxLogoBytes + 256 * 1024;

    public static void MapCompanyBranding(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(Route);
        group.MapPatch("", UpdateAccentAsync)
            .RequireAuthorization("CompanyAdminOnly");
        group.MapPut("/logo", UploadLogoAsync)
            .RequireAuthorization("CompanyAdminOnly")
            .Accepts<IFormFileCollection>("multipart/form-data")
            .WithMetadata(new RequestSizeLimitAttribute(MaxUploadRequestBytes))
            .WithMetadata(new RequestFormLimitsAttribute { MultipartBodyLengthLimit = MaxUploadRequestBytes });
        group.MapDelete("/logo", DeleteLogoAsync)
            .RequireAuthorization("CompanyAdminOnly");
        group.MapDelete("", ResetAsync)
            .RequireAuthorization("CompanyAdminOnly");
        group.MapGet("/logo", ReadLogoAsync)
            .RequireAuthorization("CompanyUserOnly");
    }

    public static async Task<IResult> UpdateAccentAsync(
        UpdateCompanyBrandingRequest request,
        HttpContext httpContext,
        [FromServices] ICompanyBrandingService brandingService,
        CancellationToken cancellationToken)
    {
        if (!TryGetCompanyId(httpContext, out var companyId))
        {
            return Results.Forbid();
        }

        if (HasClientCompanyId(httpContext) || request.AdditionalProperties is { Count: > 0 })
        {
            return TenantScopeError();
        }

        try
        {
            var branding = await brandingService.SetAccentColorAsync(
                companyId,
                request.AccentColor,
                cancellationToken);
            return Results.Ok(CompanyBrandingResponse.From(branding));
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
    }

    public static async Task<IResult> UploadLogoAsync(
        HttpContext httpContext,
        [FromServices] ICompanyBrandingService brandingService,
        [FromServices] ICompanyLogoStorageService logoStorage,
        [FromServices] ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (!TryGetCompanyId(httpContext, out var companyId))
        {
            return Results.Forbid();
        }

        if (HasClientCompanyId(httpContext) || !httpContext.Request.HasFormContentType)
        {
            return Results.BadRequest(new { error = "Un formulaire multipart valide est requis." });
        }

        var logger = loggerFactory.CreateLogger(nameof(CompanyBrandingEndpoints));
        try
        {
            var form = await httpContext.Request.ReadFormAsync(cancellationToken);
            if (form.ContainsKey("companyId"))
            {
                return TenantScopeError();
            }

            var logo = form.Files.GetFile("logo");
            if (logo is null || form.Files.Count != 1)
            {
                return Results.BadRequest(new { error = "Un seul fichier logo est requis." });
            }

            var previousBranding = await brandingService.GetAsync(companyId, cancellationToken);
            CompanyLogoMetadata uploaded;
            await using (var content = logo.OpenReadStream())
            {
                uploaded = await logoStorage.UploadAsync(
                    companyId,
                    content,
                    logo.FileName,
                    logo.ContentType,
                    cancellationToken);
            }

            CompanyBranding persistedBranding;
            try
            {
                persistedBranding = await brandingService.SetLogoMetadataAsync(
                    companyId,
                    uploaded.BlobName,
                    uploaded.ContentType,
                    cancellationToken);
            }
            catch
            {
                await TryDeleteLogoAsync(companyId, uploaded.BlobName, logoStorage, logger, cancellationToken);
                throw;
            }

            if (!string.IsNullOrWhiteSpace(previousBranding?.LogoBlobName) &&
                !string.Equals(previousBranding.LogoBlobName, uploaded.BlobName, StringComparison.Ordinal))
            {
                await TryDeleteLogoAsync(
                    companyId,
                    previousBranding.LogoBlobName,
                    logoStorage,
                    logger,
                    cancellationToken);
            }

            return Results.Ok(CompanyBrandingResponse.From(persistedBranding));
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unable to replace company logo for {CompanyId}.", companyId);
            return Results.Problem("Impossible d’enregistrer le logo de l’entreprise.");
        }
    }

    public static async Task<IResult> DeleteLogoAsync(
        HttpContext httpContext,
        [FromServices] ICompanyBrandingService brandingService,
        [FromServices] ICompanyLogoStorageService logoStorage,
        [FromServices] ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (!TryGetCompanyId(httpContext, out var companyId))
        {
            return Results.Forbid();
        }

        if (HasClientCompanyId(httpContext))
        {
            return TenantScopeError();
        }

        var logger = loggerFactory.CreateLogger(nameof(CompanyBrandingEndpoints));
        try
        {
            var previousBranding = await brandingService.GetAsync(companyId, cancellationToken);
            var branding = await brandingService.ClearLogoMetadataAsync(companyId, cancellationToken);
            if (!string.IsNullOrWhiteSpace(previousBranding?.LogoBlobName))
            {
                await TryDeleteLogoAsync(
                    companyId,
                    previousBranding.LogoBlobName,
                    logoStorage,
                    logger,
                    cancellationToken);
            }

            return Results.Ok(CompanyBrandingResponse.From(branding));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unable to delete company logo for {CompanyId}.", companyId);
            return Results.Problem("Impossible de supprimer le logo de l’entreprise.");
        }
    }

    public static async Task<IResult> ResetAsync(
        HttpContext httpContext,
        [FromServices] ICompanyBrandingService brandingService,
        [FromServices] ICompanyLogoStorageService logoStorage,
        [FromServices] ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (!TryGetCompanyId(httpContext, out var companyId))
        {
            return Results.Forbid();
        }

        if (HasClientCompanyId(httpContext))
        {
            return TenantScopeError();
        }

        var logger = loggerFactory.CreateLogger(nameof(CompanyBrandingEndpoints));
        try
        {
            var previousBranding = await brandingService.GetAsync(companyId, cancellationToken);
            await brandingService.ResetAsync(companyId, cancellationToken);
            if (!string.IsNullOrWhiteSpace(previousBranding?.LogoBlobName))
            {
                await TryDeleteLogoAsync(
                    companyId,
                    previousBranding.LogoBlobName,
                    logoStorage,
                    logger,
                    cancellationToken);
            }

            return Results.Ok(new CompanyBrandingResponse(null, false));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unable to reset company branding for {CompanyId}.", companyId);
            return Results.Problem("Impossible de rétablir l’apparence DiagLink.");
        }
    }

    public static async Task<IResult> ReadLogoAsync(
        HttpContext httpContext,
        [FromServices] ICompanyBrandingService brandingService,
        [FromServices] ICompanyLogoStorageService logoStorage,
        CancellationToken cancellationToken)
    {
        if (!TryGetCompanyId(httpContext, out var companyId))
        {
            return Results.Forbid();
        }

        if (HasClientCompanyId(httpContext))
        {
            return TenantScopeError();
        }

        var branding = await brandingService.GetAsync(companyId, cancellationToken);
        if (string.IsNullOrWhiteSpace(branding?.LogoBlobName) ||
            string.IsNullOrWhiteSpace(branding.LogoContentType))
        {
            return Results.NotFound();
        }

        try
        {
            var logo = await logoStorage.OpenReadAsync(companyId, branding.LogoBlobName, cancellationToken);
            return logo is null
                ? Results.NotFound()
                : Results.Stream(logo.Content, branding.LogoContentType);
        }
        catch (ArgumentException)
        {
            return Results.NotFound();
        }
    }

    private static async Task TryDeleteLogoAsync(
        Guid companyId,
        string blobName,
        ICompanyLogoStorageService logoStorage,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            await logoStorage.DeleteAsync(companyId, blobName, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Unable to delete obsolete company logo for {CompanyId}.",
                companyId);
        }
    }

    private static bool TryGetCompanyId(HttpContext httpContext, out Guid companyId) =>
        Guid.TryParse(
            httpContext.User.FindFirst(DiagLinkClaimTypes.CompanyId)?.Value,
            out companyId);

    private static bool HasClientCompanyId(HttpContext httpContext) =>
        httpContext.Request.Query.ContainsKey("companyId");

    private static IResult TenantScopeError() => Results.BadRequest(new
    {
        error = "Le périmètre entreprise est imposé par l’identité serveur."
    });
}
