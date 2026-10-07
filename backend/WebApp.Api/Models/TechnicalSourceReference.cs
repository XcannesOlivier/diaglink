namespace WebApp.Api.Models;

/// <summary>A server-side reference from assistant text to a verified physical PDF page.</summary>
public sealed record TechnicalSourceReference(
    string DocumentId,
    int PdfPage,
    string DisplayPage,
    string Label,
    int StartIndex,
    int EndIndex,
    int DisplayOrder);