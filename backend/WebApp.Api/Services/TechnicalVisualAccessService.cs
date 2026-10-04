using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using WebApp.Api.Data;

namespace WebApp.Api.Services;

public enum TechnicalVisualAccessKind
{
    Success,
    NotFound,
    Forbidden
}

public sealed record TechnicalVisualAccessResult(TechnicalVisualAccessKind Kind, string? BlobName = null);

/// <summary>
/// Resolves a persisted visual through its owning message/conversation and re-checks current
/// machine access before producing an internal documents-container blob name.
/// </summary>
public sealed class TechnicalVisualAccessService(
    DiagLinkDbContext db,
    MachineAccessService machineAccess,
    ILogger<TechnicalVisualAccessService> logger)
{
    private static readonly Regex SchemePattern = new(
        @"\A[a-zA-Z][a-zA-Z0-9+.-]*:",
        RegexOptions.CultureInvariant);

    public async Task<TechnicalVisualAccessResult> ResolveAsync(
        long visualId,
        string canonicalUserObjectId,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var candidate = await db.ConversationMessageVisuals
            .AsNoTracking()
            .Where(visual =>
                visual.Id == visualId &&
                visual.ConversationMessage!.Conversation!.UserObjectId == canonicalUserObjectId)
            .Select(visual => new
            {
                visual.AssetKey,
                visual.ConversationMessage!.Conversation!.MachineId
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (candidate?.MachineId is not Guid machineId)
        {
            logger.LogDebug("Technical visual access rejected. Outcome={Outcome}", "not_found");
            return new(TechnicalVisualAccessKind.NotFound);
        }

        if (!await machineAccess.CanAccessMachineAsync(user, machineId, cancellationToken))
        {
            logger.LogDebug("Technical visual access rejected. Outcome={Outcome}", "machine_forbidden");
            return new(TechnicalVisualAccessKind.Forbidden);
        }

        var blobPrefix = await db.Machines
            .AsNoTracking()
            .Where(machine => machine.Id == machineId)
            .Select(machine => machine.BlobPrefix)
            .SingleOrDefaultAsync(cancellationToken);

        if (!TryBuildBlobName(blobPrefix, candidate.AssetKey, out var blobName))
        {
            logger.LogDebug("Technical visual access rejected. Outcome={Outcome}", "invalid_reference");
            return new(TechnicalVisualAccessKind.NotFound);
        }

        return new(TechnicalVisualAccessKind.Success, blobName);
    }

    internal static bool TryBuildBlobName(string? blobPrefix, string? assetKey, out string blobName)
    {
        blobName = string.Empty;
        if (string.IsNullOrWhiteSpace(blobPrefix) ||
            blobPrefix.StartsWith('/') ||
            blobPrefix.StartsWith('\\') ||
            blobPrefix.Contains('\\') ||
            blobPrefix.Split('/').Any(part => part is "" or "." or "..") ||
            SchemePattern.IsMatch(blobPrefix) ||
            Uri.TryCreate(blobPrefix, UriKind.Absolute, out _) ||
            !IsSafeAssetKey(assetKey))
        {
            return false;
        }

        blobName = $"{blobPrefix}/{assetKey}";
        return true;
    }

    private static bool IsSafeAssetKey(string? assetKey) =>
        !string.IsNullOrWhiteSpace(assetKey) &&
        !assetKey.StartsWith('/') &&
        !assetKey.StartsWith('\\') &&
        !assetKey.Contains("..", StringComparison.Ordinal) &&
        !assetKey.Contains('\\') &&
        !assetKey.Any(char.IsControl) &&
        assetKey.EndsWith(".png", StringComparison.OrdinalIgnoreCase) &&
        !SchemePattern.IsMatch(assetKey) &&
        !Uri.TryCreate(assetKey, UriKind.Absolute, out _);
}
