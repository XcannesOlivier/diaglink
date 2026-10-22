using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using WebApp.Api.Data;

namespace WebApp.Api.Services;

public sealed record DocumentIngestionFinalizationRequest(
    string BlobPrefix,
    string ProjectEndpoint,
    string VectorStoreId);

public enum DocumentIngestionFinalizationKind
{
    Updated,
    AlreadyFinalized,
    InvalidRequest,
    MachineNotFound,
    AmbiguousMachine,
    CompanyMismatch,
    ConfigurationConflict,
}

public sealed record DocumentIngestionFinalizationOutcome(
    DocumentIngestionFinalizationKind Kind,
    string Code)
{
    public static DocumentIngestionFinalizationOutcome From(
        DocumentIngestionFinalizationKind kind,
        string code) => new(kind, code);
}

/// <summary>
/// Finalizes a machine after its documentary ingestion. The update is conditional, so repeated
/// callbacks are harmless and a callback can never replace a different existing Foundry binding.
/// </summary>
public sealed class DocumentIngestionFinalizationService(
    DiagLinkDbContext db,
    TimeProvider timeProvider)
{
    private static readonly Regex BlobSegmentPattern = new(
        "^[a-z0-9]+(?:-[a-z0-9]+)*$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    private static readonly Regex VectorStorePattern = new(
        "^vs_[A-Za-z0-9]+$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public async Task<DocumentIngestionFinalizationOutcome> FinalizeAsync(
        DocumentIngestionFinalizationRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryNormalize(request, out var normalized, out var companySlug, out var machineSlug))
        {
            return DocumentIngestionFinalizationOutcome.From(
                DocumentIngestionFinalizationKind.InvalidRequest,
                "invalid_request");
        }

        var matches = await db.Machines
            .AsNoTracking()
            .Include(machine => machine.Company)
            .Where(machine => machine.BlobPrefix == normalized.BlobPrefix)
            .Take(2)
            .ToListAsync(cancellationToken);

        if (matches.Count == 0)
        {
            return DocumentIngestionFinalizationOutcome.From(
                DocumentIngestionFinalizationKind.MachineNotFound,
                "machine_not_found");
        }

        if (matches.Count != 1)
        {
            return DocumentIngestionFinalizationOutcome.From(
                DocumentIngestionFinalizationKind.AmbiguousMachine,
                "ambiguous_machine");
        }

        var machine = matches[0];
        if (machine.Company is null ||
            !string.Equals(Slugify(machine.Company.Name), companySlug, StringComparison.Ordinal) ||
            !string.Equals(Slugify(machine.Name), machineSlug, StringComparison.Ordinal))
        {
            return DocumentIngestionFinalizationOutcome.From(
                DocumentIngestionFinalizationKind.CompanyMismatch,
                "machine_company_mismatch");
        }

        var currentState = ClassifyExistingConfiguration(
            machine.ProjectEndpoint,
            machine.VectorStoreId,
            normalized.ProjectEndpoint,
            normalized.VectorStoreId);

        if (currentState is not null)
        {
            return currentState;
        }

        // This is one atomic, compare-and-set UPDATE. It provides the required idempotence under
        // concurrent queue deliveries without a provider-specific lock or a new database object.
        var updated = await db.Machines
            .Where(candidate =>
                candidate.Id == machine.Id &&
                (candidate.ProjectEndpoint == null || candidate.ProjectEndpoint == normalized.ProjectEndpoint) &&
                (candidate.VectorStoreId == null || candidate.VectorStoreId == normalized.VectorStoreId))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(candidate => candidate.ProjectEndpoint, normalized.ProjectEndpoint)
                .SetProperty(candidate => candidate.VectorStoreId, normalized.VectorStoreId)
                .SetProperty(candidate => candidate.UpdatedAtUtc, timeProvider.GetUtcNow().UtcDateTime),
                cancellationToken);

        if (updated == 1)
        {
            return DocumentIngestionFinalizationOutcome.From(
                DocumentIngestionFinalizationKind.Updated,
                "updated");
        }

        // A concurrent callback won the compare-and-set. Re-read to distinguish an idempotent
        // duplicate from an attempted replacement by a different Foundry configuration.
        var persisted = await db.Machines
            .AsNoTracking()
            .Where(candidate => candidate.Id == machine.Id)
            .Select(candidate => new { candidate.ProjectEndpoint, candidate.VectorStoreId })
            .SingleAsync(cancellationToken);

        return ClassifyExistingConfiguration(
                persisted.ProjectEndpoint,
                persisted.VectorStoreId,
                normalized.ProjectEndpoint,
                normalized.VectorStoreId)
            ?? DocumentIngestionFinalizationOutcome.From(
                DocumentIngestionFinalizationKind.ConfigurationConflict,
                "configuration_conflict");
    }

    private static DocumentIngestionFinalizationOutcome? ClassifyExistingConfiguration(
        string? currentProjectEndpoint,
        string? currentVectorStoreId,
        string requestedProjectEndpoint,
        string requestedVectorStoreId)
    {
        var projectMatches = string.Equals(
            currentProjectEndpoint,
            requestedProjectEndpoint,
            StringComparison.Ordinal);
        var vectorStoreMatches = string.Equals(
            currentVectorStoreId,
            requestedVectorStoreId,
            StringComparison.Ordinal);

        if (projectMatches && vectorStoreMatches)
        {
            return DocumentIngestionFinalizationOutcome.From(
                DocumentIngestionFinalizationKind.AlreadyFinalized,
                "already_finalized");
        }

        if ((!string.IsNullOrWhiteSpace(currentProjectEndpoint) && !projectMatches) ||
            (!string.IsNullOrWhiteSpace(currentVectorStoreId) && !vectorStoreMatches))
        {
            return DocumentIngestionFinalizationOutcome.From(
                DocumentIngestionFinalizationKind.ConfigurationConflict,
                "configuration_conflict");
        }

        return null;
    }

    private static bool TryNormalize(
        DocumentIngestionFinalizationRequest request,
        out DocumentIngestionFinalizationRequest normalized,
        out string companySlug,
        out string machineSlug)
    {
        normalized = request;
        companySlug = string.Empty;
        machineSlug = string.Empty;

        var blobPrefix = request.BlobPrefix?.Trim();
        var vectorStoreId = request.VectorStoreId?.Trim();
        var endpointText = request.ProjectEndpoint?.Trim().TrimEnd('/');
        if (string.IsNullOrEmpty(blobPrefix) || blobPrefix.Length > 200 ||
            string.IsNullOrEmpty(vectorStoreId) || vectorStoreId.Length > 200 ||
            string.IsNullOrEmpty(endpointText) || endpointText.Length > 500)
        {
            return false;
        }

        var segments = blobPrefix.Split('/');
        if (segments.Length != 2 ||
            !BlobSegmentPattern.IsMatch(segments[0]) ||
            !BlobSegmentPattern.IsMatch(segments[1]) ||
            !VectorStorePattern.IsMatch(vectorStoreId))
        {
            return false;
        }

        if (!Uri.TryCreate(endpointText, UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme != Uri.UriSchemeHttps ||
            !endpoint.Host.EndsWith(".services.ai.azure.com", StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(endpoint.Query) ||
            !string.IsNullOrEmpty(endpoint.Fragment) ||
            !string.IsNullOrEmpty(endpoint.UserInfo))
        {
            return false;
        }

        var endpointSegments = endpoint.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (endpointSegments.Length != 3 ||
            !string.Equals(endpointSegments[0], "api", StringComparison.Ordinal) ||
            !string.Equals(endpointSegments[1], "projects", StringComparison.Ordinal) ||
            !string.Equals(endpointSegments[2], segments[0], StringComparison.Ordinal))
        {
            return false;
        }

        companySlug = segments[0];
        machineSlug = segments[1];
        normalized = new(blobPrefix, endpointText, vectorStoreId);
        return true;
    }

    internal static string Slugify(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var decomposed = input.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(character);
            }
        }

        var cleaned = builder.ToString().Normalize(NormalizationForm.FormC);
        return Regex.Replace(cleaned, "[^A-Za-z0-9]+", "-").Trim('-').ToLowerInvariant();
    }
}
