namespace WebApp.Api.Services;

/// <summary>Reads an already-authorized PNG from the private documents container.</summary>
public interface ITechnicalVisualBlobReader
{
    Task<Stream?> OpenReadAsync(string blobName, CancellationToken cancellationToken);
}
