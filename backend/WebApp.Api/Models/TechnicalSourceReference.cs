using System.Text.Json.Serialization;
using System.Security.Cryptography;
using System.Text;

namespace WebApp.Api.Models;

/// <summary>A server-side reference from assistant text to a verified physical PDF page.</summary>
public sealed record TechnicalSourceReference(
    string DocumentId,
    int PdfPage,
    string DisplayPage,
    string Label,
    int StartIndex,
    int EndIndex,
    int DisplayOrder)
{
    /// <summary>Safe PDF file name derived from the validated page-map source blob.</summary>
    public string DocumentName { get; init; } = string.Empty;

    [JsonIgnore]
    public string? VersionToken { get; init; }

    [JsonIgnore]
    public TechnicalSourcePageKind PageKind { get; init; } = TechnicalSourcePageKind.Display;
}

public enum TechnicalSourcePageKind
{
    Display,
    Pdf
}

/// <summary>A previously persisted, server-verified document/page pair from the same conversation.</summary>
public sealed record HistoricalTechnicalSourceEvidence(
    string DocumentId,
    int PdfPage,
    string DisplayPage,
    TechnicalSourcePageKind PageKind,
    string? VersionToken = null);

/// <summary>A document/physical-page pair and its containing individual File Search result.</summary>
public sealed record TechnicalDocumentPageEvidence(
    string DocumentId,
    int PdfPage,
    string? ResultText = null);

public static class TechnicalSourcePageStorage
{
    private const string PdfPrefix = "pdf:";
    private const string VersionPrefix = "v1:";

    public static string Store(TechnicalSourceReference source)
    {
        var page = source.PageKind == TechnicalSourcePageKind.Pdf
            ? PdfPrefix + source.PdfPage
            : source.DisplayPage;
        var versioned = string.IsNullOrEmpty(source.VersionToken)
            ? page
            : $"{VersionPrefix}{source.VersionToken}:{page}";
        return versioned.Length <= 64 ? versioned : page;
    }

    public static bool TryReadPdfPage(string value, out int pdfPage)
    {
        value = ReadPage(value);
        pdfPage = 0;
        return value.StartsWith(PdfPrefix, StringComparison.Ordinal) &&
            int.TryParse(value.AsSpan(PdfPrefix.Length), out pdfPage) &&
            pdfPage > 0;
    }

    public static string PublicDisplayPage(string value) =>
        TryReadPdfPage(value, out var pdfPage) ? pdfPage.ToString() : ReadPage(value);

    public static string? ReadVersionToken(string value)
    {
        if (!value.StartsWith(VersionPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var separator = value.IndexOf(':', VersionPrefix.Length);
        return separator > VersionPrefix.Length ? value[VersionPrefix.Length..separator] : null;
    }

    public static string CreateVersionToken(string sourceVersion)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sourceVersion));
        return Convert.ToHexString(hash.AsSpan(0, 8));
    }

    private static string ReadPage(string value)
    {
        if (!value.StartsWith(VersionPrefix, StringComparison.Ordinal))
        {
            return value;
        }

        var separator = value.IndexOf(':', VersionPrefix.Length);
        return separator >= 0 && separator + 1 < value.Length ? value[(separator + 1)..] : value;
    }
}
