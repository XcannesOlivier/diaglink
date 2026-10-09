namespace WebApp.Api.Services;

public sealed record CompanyBrandingBlobUploadResult(string ETag);

public sealed record CompanyBrandingBlobReadResult(Stream Content, string ContentType, string ETag);

public interface ICompanyBrandingBlobClient
{
    Task EnsurePrivateContainerAsync(CancellationToken cancellationToken = default);
    Task<CompanyBrandingBlobUploadResult> UploadAsync(
        string blobName,
        Stream content,
        string contentType,
        CancellationToken cancellationToken = default);
    Task<CompanyBrandingBlobReadResult?> OpenReadAsync(string blobName, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(string blobName, CancellationToken cancellationToken = default);
}
