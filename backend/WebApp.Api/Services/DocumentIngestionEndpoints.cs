using System.Security.Claims;

namespace WebApp.Api.Services;

public static class DocumentIngestionAuthorization
{
    public const string PolicyName = "DocumentIngestionCallback";
    public const string AppRole = "DocumentIngestion.Finalize";
    public const string AllowedCallerObjectIdConfigurationKey =
        "DocumentIngestion:AllowedCallerObjectId";

    private const string ObjectIdClaim = "oid";
    private const string ObjectIdentifierClaim =
        "http://schemas.microsoft.com/identity/claims/objectidentifier";

    public static bool IsAllowedCaller(ClaimsPrincipal principal, string? configuredObjectId)
    {
        if (!Guid.TryParse(configuredObjectId, out var allowedObjectId) ||
            !principal.IsInRole(AppRole))
        {
            return false;
        }

        var callerObjectId = principal.FindFirst(ObjectIdClaim)?.Value
            ?? principal.FindFirst(ObjectIdentifierClaim)?.Value;

        return Guid.TryParse(callerObjectId, out var parsedCallerObjectId) &&
               parsedCallerObjectId == allowedObjectId;
    }
}

public static class DocumentIngestionEndpoints
{
    public static WebApplication MapDocumentIngestionEndpoints(this WebApplication app)
    {
        app.MapPost("/api/internal/document-ingestion/finalize", FinalizeAsync)
            .RequireAuthorization(DocumentIngestionAuthorization.PolicyName)
            .WithName("FinalizeDocumentIngestion");

        return app;
    }

    private static async Task<IResult> FinalizeAsync(
        DocumentIngestionFinalizationRequest request,
        DocumentIngestionFinalizationService service,
        CancellationToken cancellationToken)
    {
        var outcome = await service.FinalizeAsync(request, cancellationToken);
        var body = new { status = outcome.Code };

        return outcome.Kind switch
        {
            DocumentIngestionFinalizationKind.Updated or
            DocumentIngestionFinalizationKind.AlreadyFinalized => Results.Ok(body),
            DocumentIngestionFinalizationKind.InvalidRequest => Results.BadRequest(body),
            DocumentIngestionFinalizationKind.MachineNotFound => Results.NotFound(body),
            DocumentIngestionFinalizationKind.AmbiguousMachine or
            DocumentIngestionFinalizationKind.CompanyMismatch or
            DocumentIngestionFinalizationKind.ConfigurationConflict => Results.Conflict(body),
            _ => Results.StatusCode(StatusCodes.Status500InternalServerError),
        };
    }
}
