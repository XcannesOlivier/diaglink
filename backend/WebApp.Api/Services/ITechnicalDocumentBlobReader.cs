namespace WebApp.Api.Services;

/// <summary>Reads private page maps and their source PDFs without exposing storage locations.</summary>
public interface ITechnicalDocumentBlobReader
{
    Task<Stream?> OpenPageMapAsync(string blobName, CancellationToken cancellationToken);
    Task<Stream?> OpenPdfAsync(string blobName, CancellationToken cancellationToken);
}