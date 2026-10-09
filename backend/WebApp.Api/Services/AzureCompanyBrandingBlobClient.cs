using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace WebApp.Api.Services;

public sealed class AzureCompanyBrandingBlobClient : ICompanyBrandingBlobClient
{
    public const string ContainerName = "company-branding";

    private readonly BlobContainerClient _container;

    public AzureCompanyBrandingBlobClient(BlobServiceClient blobServiceClient)
    {
        ArgumentNullException.ThrowIfNull(blobServiceClient);
        _container = blobServiceClient.GetBlobContainerClient(ContainerName);
    }

    public async Task EnsurePrivateContainerAsync(CancellationToken cancellationToken = default)
    {
        await _container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);
        await _container.SetAccessPolicyAsync(PublicAccessType.None, cancellationToken: cancellationToken);
    }

    public async Task<CompanyBrandingBlobUploadResult> UploadAsync(
        string blobName,
        Stream content,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        var response = await _container.GetBlobClient(blobName).UploadAsync(content, new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = contentType },
            Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All },
        }, cancellationToken);

        return new CompanyBrandingBlobUploadResult(response.Value.ETag.ToString());
    }

    public async Task<CompanyBrandingBlobReadResult?> OpenReadAsync(
        string blobName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _container.GetBlobClient(blobName)
                .DownloadStreamingAsync(cancellationToken: cancellationToken);
            return new CompanyBrandingBlobReadResult(
                response.Value.Content,
                response.Value.Details.ContentType,
                response.Value.Details.ETag.ToString());
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            return null;
        }
    }

    public async Task<bool> DeleteAsync(string blobName, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _container.DeleteBlobIfExistsAsync(
                blobName,
                DeleteSnapshotsOption.IncludeSnapshots,
                cancellationToken: cancellationToken);
            return response.Value;
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            return false;
        }
    }
}
