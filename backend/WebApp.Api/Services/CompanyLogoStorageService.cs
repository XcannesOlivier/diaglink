using System.Buffers;

namespace WebApp.Api.Services;

public sealed class CompanyLogoStorageService(ICompanyBrandingBlobClient blobClient) : ICompanyLogoStorageService
{
    public const int MaxLogoBytes = 2 * 1024 * 1024;

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] JpegSignature = [0xFF, 0xD8, 0xFF];

    public async Task<CompanyLogoMetadata> UploadAsync(
        Guid companyId,
        Stream content,
        string? originalFileName,
        string? declaredContentType,
        CancellationToken cancellationToken = default)
    {
        ValidateCompanyId(companyId);
        ArgumentNullException.ThrowIfNull(content);
        if (!content.CanRead)
        {
            throw new ArgumentException("Logo content stream must be readable.", nameof(content));
        }

        await using var validatedContent = await ReadWithinLimitAsync(content, cancellationToken);
        var detected = DetectFormat(validatedContent.GetBuffer().AsSpan(0, checked((int)validatedContent.Length)));
        ValidateDeclaredMetadata(detected, originalFileName, declaredContentType);

        var blobName = $"companies/{companyId:N}/logos/{Guid.NewGuid():N}{detected.Extension}";
        validatedContent.Position = 0;
        await blobClient.EnsurePrivateContainerAsync(cancellationToken);
        var upload = await blobClient.UploadAsync(blobName, validatedContent, detected.ContentType, cancellationToken);
        return new CompanyLogoMetadata(blobName, detected.ContentType, upload.ETag);
    }

    public async Task<CompanyLogoContent?> OpenReadAsync(
        Guid companyId,
        string blobName,
        CancellationToken cancellationToken = default)
    {
        ValidateOwnedBlobName(companyId, blobName);
        var result = await blobClient.OpenReadAsync(blobName, cancellationToken);
        return result is null
            ? null
            : new CompanyLogoContent(result.Content, result.ContentType, result.ETag);
    }

    public Task<bool> DeleteAsync(
        Guid companyId,
        string blobName,
        CancellationToken cancellationToken = default)
    {
        ValidateOwnedBlobName(companyId, blobName);
        return blobClient.DeleteAsync(blobName, cancellationToken);
    }

    private static async Task<MemoryStream> ReadWithinLimitAsync(Stream content, CancellationToken cancellationToken)
    {
        var result = new MemoryStream();
        var buffer = ArrayPool<byte>.Shared.Rent(81920);
        try
        {
            while (true)
            {
                var read = await content.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (read == 0)
                {
                    break;
                }

                if (result.Length + read > MaxLogoBytes)
                {
                    throw new ArgumentException($"Company logo must not exceed {MaxLogoBytes} bytes.", nameof(content));
                }

                await result.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }

            if (result.Length == 0)
            {
                throw new ArgumentException("Company logo must not be empty.", nameof(content));
            }

            return result;
        }
        catch
        {
            await result.DisposeAsync();
            throw;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static LogoFormat DetectFormat(ReadOnlySpan<byte> content)
    {
        if (content.StartsWith(PngSignature))
        {
            return new LogoFormat("image/png", ".png");
        }

        if (content.StartsWith(JpegSignature))
        {
            return new LogoFormat("image/jpeg", ".jpg");
        }

        throw new ArgumentException("Unsupported company logo. Only valid PNG and JPEG images are accepted.", nameof(content));
    }

    private static void ValidateDeclaredMetadata(
        LogoFormat detected,
        string? originalFileName,
        string? declaredContentType)
    {
        if (!string.IsNullOrWhiteSpace(originalFileName))
        {
            var extension = Path.GetExtension(originalFileName).ToLowerInvariant();
            var expectedExtension = extension switch
            {
                ".png" => ".png",
                ".jpg" or ".jpeg" => ".jpg",
                _ => null,
            };
            if (expectedExtension is null || expectedExtension != detected.Extension)
            {
                throw new ArgumentException("Logo file extension does not match its content.", nameof(originalFileName));
            }
        }

        if (!string.IsNullOrWhiteSpace(declaredContentType))
        {
            var normalizedContentType = declaredContentType.ToLowerInvariant() switch
            {
                "image/jpg" => "image/jpeg",
                var value => value,
            };
            if (normalizedContentType != detected.ContentType)
            {
                throw new ArgumentException("Logo content type does not match its content.", nameof(declaredContentType));
            }
        }
    }

    private static void ValidateOwnedBlobName(Guid companyId, string blobName)
    {
        ValidateCompanyId(companyId);
        var prefix = $"companies/{companyId:N}/logos/";
        if (string.IsNullOrWhiteSpace(blobName) ||
            !blobName.StartsWith(prefix, StringComparison.Ordinal) ||
            blobName.Length == prefix.Length ||
            blobName.Contains('\\') ||
            blobName.Split('/').Any(segment => segment is "" or "." or "..") ||
            blobName.Any(char.IsControl) ||
            Uri.TryCreate(blobName, UriKind.Absolute, out _))
        {
            throw new ArgumentException("Logo blob does not belong to the specified company.", nameof(blobName));
        }
    }

    private static void ValidateCompanyId(Guid companyId)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }
    }

    private sealed record LogoFormat(string ContentType, string Extension);
}
