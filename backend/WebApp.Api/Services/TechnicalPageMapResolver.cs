using System.Text.Json;
using System.Text.RegularExpressions;

namespace WebApp.Api.Services;

public sealed record TechnicalPageMapResolution(
    string SourceBlob,
    IReadOnlyDictionary<string, int> PdfPagesByDisplayPage);

/// <summary>Loads and validates private page maps, with one download per document in a request scope.</summary>
public sealed partial class TechnicalPageMapResolver(
    ITechnicalDocumentBlobReader blobReader,
    ILogger<TechnicalPageMapResolver> logger)
{
    private readonly Dictionary<string, Task<ValidatedPageMap?>> _cache = new(StringComparer.Ordinal);

    public async Task<TechnicalPageMapResolution?> ResolveAsync(
        string blobPrefix,
        string documentId,
        IReadOnlyCollection<string> displayPages,
        CancellationToken cancellationToken)
    {
        if (!TryBuildPageMapBlobName(blobPrefix, documentId, out var pageMapBlobName))
        {
            return null;
        }

        if (!_cache.TryGetValue(pageMapBlobName, out var loadTask))
        {
            loadTask = LoadAsync(pageMapBlobName, blobPrefix, documentId, cancellationToken);
            _cache.Add(pageMapBlobName, loadTask);
        }

        var pageMap = await loadTask;
        if (pageMap is null)
        {
            return null;
        }

        var resolvedPages = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var displayPage in displayPages.Distinct(StringComparer.Ordinal))
        {
            if (!pageMap.Pages.TryGetValue(displayPage, out var candidates))
            {
                continue;
            }

            var physicalPages = candidates.Distinct().ToArray();
            if (physicalPages.Length == 1)
            {
                resolvedPages.Add(displayPage, physicalPages[0]);
            }
        }

        return new(pageMap.SourceBlob, resolvedPages);
    }

    private async Task<ValidatedPageMap?> LoadAsync(
        string pageMapBlobName,
        string blobPrefix,
        string expectedDocumentId,
        CancellationToken cancellationToken)
    {
        await using var stream = await blobReader.OpenPageMapAsync(pageMapBlobName, cancellationToken);
        if (stream is null)
        {
            return null;
        }

        try
        {
            using var document = await JsonDocument.ParseAsync(
                stream,
                new JsonDocumentOptions { MaxDepth = 16 },
                cancellationToken);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !HasSupportedSchemaVersion(root) ||
                !TryReadRequiredString(root, "documentId", out var documentId) ||
                !string.Equals(documentId, expectedDocumentId, StringComparison.Ordinal) ||
                !TryReadRequiredString(root, "sourceBlob", out var sourceBlob) ||
                !IsSafeSourceBlob(blobPrefix, sourceBlob) ||
                !root.TryGetProperty("pages", out var pagesElement) ||
                pagesElement.ValueKind != JsonValueKind.Array ||
                pagesElement.GetArrayLength() == 0)
            {
                return null;
            }

            var pages = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            foreach (var page in pagesElement.EnumerateArray())
            {
                if (page.ValueKind != JsonValueKind.Object ||
                    !page.TryGetProperty("pdfPage", out var pdfPageElement) ||
                    !pdfPageElement.TryGetInt32(out var pdfPage) ||
                    pdfPage <= 0)
                {
                    return null;
                }

                if (!page.TryGetProperty("displayPage", out var displayPageElement) ||
                    !TryReadRequiredString(page, "source", out _))
                {
                    return null;
                }

                if (displayPageElement.ValueKind == JsonValueKind.Null)
                {
                    continue;
                }

                if (displayPageElement.ValueKind != JsonValueKind.String)
                {
                    return null;
                }

                var displayPage = displayPageElement.GetString()!;
                if (string.IsNullOrWhiteSpace(displayPage) || displayPage.Length > 64)
                {
                    return null;
                }

                if (!pages.TryGetValue(displayPage, out var candidates))
                {
                    candidates = [];
                    pages.Add(displayPage, candidates);
                }

                candidates.Add(pdfPage);
            }

            return new(sourceBlob, pages);
        }
        catch (JsonException exception)
        {
            logger.LogDebug(exception, "Technical page map is invalid. DocumentId={DocumentId}", expectedDocumentId);
            return null;
        }
    }

    internal static bool TryBuildPageMapBlobName(
        string blobPrefix,
        string documentId,
        out string pageMapBlobName)
    {
        pageMapBlobName = string.Empty;
        if (!IsSafeBlobPrefix(blobPrefix) || !DocumentIdPattern().IsMatch(documentId))
        {
            return false;
        }

        pageMapBlobName = $"{blobPrefix}/{documentId}/page-map.json";
        return true;
    }

    private static bool HasSupportedSchemaVersion(JsonElement root)
    {
        return root.TryGetProperty("schemaVersion", out var schemaVersion) &&
            schemaVersion.ValueKind == JsonValueKind.Number &&
            schemaVersion.TryGetInt32(out var value) &&
            value == 1;
    }

    private static bool TryReadRequiredString(JsonElement parent, string name, out string value)
    {
        value = string.Empty;
        return parent.TryGetProperty(name, out var element) &&
            element.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(value = element.GetString()!);
    }

    private static bool IsSafeSourceBlob(string blobPrefix, string sourceBlob) =>
        IsSafeBlobPrefix(blobPrefix) &&
        sourceBlob.StartsWith(blobPrefix + "/", StringComparison.Ordinal) &&
        sourceBlob.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) &&
        !sourceBlob.Contains("..", StringComparison.Ordinal) &&
        !sourceBlob.Contains('\\') &&
        !sourceBlob.Any(char.IsControl) &&
        !Uri.TryCreate(sourceBlob, UriKind.Absolute, out _);

    private static bool IsSafeBlobPrefix(string blobPrefix) =>
        !string.IsNullOrWhiteSpace(blobPrefix) &&
        !blobPrefix.StartsWith('/') &&
        !blobPrefix.StartsWith('\\') &&
        !blobPrefix.Contains('\\') &&
        !blobPrefix.Any(char.IsControl) &&
        blobPrefix.Split('/').All(part => part is not ("" or "." or "..")) &&
        !Uri.TryCreate(blobPrefix, UriKind.Absolute, out _);

    [GeneratedRegex(@"\A[A-Za-z0-9][A-Za-z0-9._-]{0,127}\z", RegexOptions.CultureInvariant)]
    private static partial Regex DocumentIdPattern();

    private sealed record ValidatedPageMap(string SourceBlob, Dictionary<string, List<int>> Pages);
}