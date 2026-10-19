using System.Text.Json.Serialization;

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
    [JsonIgnore]
    public TechnicalSourcePageKind PageKind { get; init; } = TechnicalSourcePageKind.Display;
}

public enum TechnicalSourcePageKind
{
    Display,
    Pdf
}

/// <summary>A document/physical-page pair and its containing individual File Search result.</summary>
public sealed record TechnicalDocumentPageEvidence(
    string DocumentId,
    int PdfPage,
    string? ResultText = null);

public static class TechnicalSourcePageStorage
{
    private const string PdfPrefix = "pdf:";

    public static string Store(TechnicalSourceReference source) =>
        source.PageKind == TechnicalSourcePageKind.Pdf
            ? PdfPrefix + source.PdfPage
            : source.DisplayPage;

    public static bool TryReadPdfPage(string value, out int pdfPage)
    {
        pdfPage = 0;
        return value.StartsWith(PdfPrefix, StringComparison.Ordinal) &&
            int.TryParse(value.AsSpan(PdfPrefix.Length), out pdfPage) &&
            pdfPage > 0;
    }

    public static string PublicDisplayPage(string value) =>
        TryReadPdfPage(value, out var pdfPage) ? pdfPage.ToString() : value;
}
