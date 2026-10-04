using Azure;
using Azure.Storage.Blobs;

namespace WebApp.Api.Services;

public sealed class AzureClaudeDirectToolboxMarkerReader(BlobServiceClient blobServiceClient)
    : IClaudeDirectToolboxMarkerReader
{
    public async Task<Stream?> OpenReadAsync(string blobName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(blobName) ||
            blobName.StartsWith('/') ||
            blobName.StartsWith('\\') ||
            blobName.Contains("..", StringComparison.Ordinal) ||
            blobName.Contains('\\') ||
            blobName.Any(char.IsControl) ||
            !blobName.EndsWith("/.foundry/toolbox.json", StringComparison.Ordinal) ||
            Uri.TryCreate(blobName, UriKind.Absolute, out _))
        {
            return null;
        }

        try
        {
            return await blobServiceClient
                .GetBlobContainerClient("documents")
                .GetBlobClient(blobName)
                .OpenReadAsync(cancellationToken: cancellationToken);
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            return null;
        }
    }
}
