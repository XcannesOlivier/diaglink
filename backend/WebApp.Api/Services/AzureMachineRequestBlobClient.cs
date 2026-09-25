using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace WebApp.Api.Services;

public sealed class AzureMachineRequestBlobClient : IMachineRequestBlobClient
{
    public const string ContainerName = "machine-requests";
    private readonly BlobContainerClient _container;

    public AzureMachineRequestBlobClient(BlobServiceClient blobServiceClient)
    {
        ArgumentNullException.ThrowIfNull(blobServiceClient);
        _container = blobServiceClient.GetBlobContainerClient(ContainerName);
    }

    public async Task EnsurePrivateContainerAsync(CancellationToken cancellationToken = default)
    {
        await _container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);
        await _container.SetAccessPolicyAsync(PublicAccessType.None, cancellationToken: cancellationToken);
    }

    public async Task UploadAsync(string blobName, Stream content, string contentType, bool overwrite, CancellationToken cancellationToken = default)
    {
        var options = new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = contentType },
            Conditions = overwrite ? null : new BlobRequestConditions { IfNoneMatch = ETag.All },
        };
        await _container.GetBlobClient(blobName).UploadAsync(content, options, cancellationToken);
    }

    public async Task<Stream?> OpenReadAsync(string blobName, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _container.GetBlobClient(blobName).OpenReadAsync(cancellationToken: cancellationToken);
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            return null;
        }
    }

    public async IAsyncEnumerable<string> ListNamesAsync(string? prefix = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var blob in _container.GetBlobsAsync(prefix: prefix, cancellationToken: cancellationToken))
        {
            yield return blob.Name;
        }
    }

    public async Task DeletePrefixAsync(string prefix, CancellationToken cancellationToken = default)
    {
        await foreach (var blobName in ListNamesAsync(prefix, cancellationToken))
        {
            await _container.DeleteBlobIfExistsAsync(blobName, DeleteSnapshotsOption.IncludeSnapshots, cancellationToken: cancellationToken);
        }
    }
}
