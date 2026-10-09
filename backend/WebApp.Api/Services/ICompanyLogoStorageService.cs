namespace WebApp.Api.Services;

public sealed record CompanyLogoMetadata(string BlobName, string ContentType, string ETag);

public sealed record CompanyLogoContent(Stream Content, string ContentType, string ETag);

public interface ICompanyLogoStorageService
{
    Task<CompanyLogoMetadata> UploadAsync(
        Guid companyId,
        Stream content,
        string? originalFileName,
        string? declaredContentType,
        CancellationToken cancellationToken = default);
    Task<CompanyLogoContent?> OpenReadAsync(
        Guid companyId,
        string blobName,
        CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(
        Guid companyId,
        string blobName,
        CancellationToken cancellationToken = default);
}
