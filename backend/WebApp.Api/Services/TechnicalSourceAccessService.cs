using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using WebApp.Api.Data;

namespace WebApp.Api.Services;

public enum TechnicalSourceAccessKind
{
    Success,
    NotFound,
    Forbidden
}

public sealed record TechnicalSourceAccessResult(
    TechnicalSourceAccessKind Kind,
    string? SourceBlob = null);

/// <summary>Revalidates ownership, machine access, and page-map provenance for a persisted source.</summary>
public sealed class TechnicalSourceAccessService(
    DiagLinkDbContext db,
    MachineAccessService machineAccess,
    TechnicalPageMapResolver pageMapResolver,
    ILogger<TechnicalSourceAccessService> logger)
{
    public async Task<TechnicalSourceAccessResult> ResolveAsync(
        long sourceReferenceId,
        string canonicalUserObjectId,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var candidate = await db.ConversationMessageSourceReferences
            .AsNoTracking()
            .Where(source =>
                source.Id == sourceReferenceId &&
                source.ConversationMessage!.Conversation!.UserObjectId == canonicalUserObjectId)
            .Select(source => new
            {
                source.DocumentId,
                source.DisplayPage,
                source.PdfPage,
                source.ConversationMessage!.Conversation!.MachineId
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (candidate?.MachineId is not Guid machineId)
        {
            logger.LogDebug("Technical source access rejected. Outcome={Outcome}", "not_found");
            return new(TechnicalSourceAccessKind.NotFound);
        }

        if (!await machineAccess.CanAccessMachineAsync(user, machineId, cancellationToken))
        {
            logger.LogDebug("Technical source access rejected. Outcome={Outcome}", "machine_forbidden");
            return new(TechnicalSourceAccessKind.Forbidden);
        }

        var blobPrefix = await db.Machines
            .AsNoTracking()
            .Where(machine => machine.Id == machineId)
            .Select(machine => machine.BlobPrefix)
            .SingleOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(blobPrefix))
        {
            logger.LogDebug("Technical source access rejected. Outcome={Outcome}", "missing_blob_prefix");
            return new(TechnicalSourceAccessKind.NotFound);
        }

        var pageMap = await pageMapResolver.ResolveAsync(
            blobPrefix,
            candidate.DocumentId,
            [candidate.DisplayPage],
            cancellationToken);
        if (pageMap is null ||
            !pageMap.PdfPagesByDisplayPage.TryGetValue(candidate.DisplayPage, out var currentPdfPage) ||
            currentPdfPage != candidate.PdfPage)
        {
            logger.LogDebug("Technical source access rejected. Outcome={Outcome}", "page_map_mismatch");
            return new(TechnicalSourceAccessKind.NotFound);
        }

        return new(TechnicalSourceAccessKind.Success, pageMap.SourceBlob);
    }
}