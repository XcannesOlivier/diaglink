namespace WebApp.Api.Services;

public interface IClaudeDirectToolboxMarkerReader
{
    Task<Stream?> OpenReadAsync(string blobName, CancellationToken cancellationToken);
}
