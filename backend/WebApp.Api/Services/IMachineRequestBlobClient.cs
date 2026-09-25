namespace WebApp.Api.Services;

public interface IMachineRequestBlobClient
{
    Task EnsurePrivateContainerAsync(CancellationToken cancellationToken = default);
    Task UploadAsync(string blobName, Stream content, string contentType, bool overwrite, CancellationToken cancellationToken = default);
    Task<Stream?> OpenReadAsync(string blobName, CancellationToken cancellationToken = default);
    IAsyncEnumerable<string> ListNamesAsync(string? prefix = null, CancellationToken cancellationToken = default);
    Task DeletePrefixAsync(string prefix, CancellationToken cancellationToken = default);
}
