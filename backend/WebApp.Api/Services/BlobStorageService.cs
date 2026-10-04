using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using WebApp.Api.Models;

namespace WebApp.Api.Services;

public class BlobStorageService : ITechnicalVisualBlobReader
{
    private readonly BlobServiceClient _client;
    private readonly IPdfPageCounter _pageCounter;

    public BlobStorageService(BlobServiceClient client, IPdfPageCounter pageCounter)
    {
        _client = client ?? throw new InvalidOperationException("BlobServiceClient is not configured. Set AZURE_STORAGE_CONNECTION_STRING.");
        _pageCounter = pageCounter ?? throw new ArgumentNullException(nameof(pageCounter));
    }

    public async Task<List<string>> UploadFilesAsync(string containerName, string companyName, string machineName, IFormFileCollection files, CancellationToken cancellationToken = default)
    {
        if (files is null || files.Count == 0) throw new ArgumentException("No files provided.");
        if (files.Count > MachineRequestUploadLimits.MaxDocumentCount)
            throw new ArgumentException($"A maximum of {MachineRequestUploadLimits.MaxDocumentCount} PDF files is allowed.");

        foreach (var file in files)
        {
            if (file.Length <= 0) throw new ArgumentException($"File {file.FileName} is empty.");
            if (file.Length > MachineRequestUploadLimits.MaxDocumentBytes)
                throw new ArgumentException($"File {file.FileName} exceeds size limit ({MachineRequestUploadLimits.MaxDocumentBytes} bytes).");
            if (!file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(file.ContentType, "application/pdf", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Only PDF files are accepted.");
        }

        if (files.Sum(file => file.Length) > MachineRequestUploadLimits.MaxCombinedDocumentBytes)
            throw new ArgumentException($"Combined PDF size exceeds limit ({MachineRequestUploadLimits.MaxCombinedDocumentBytes} bytes).");

        foreach (var file in files)
        {
            await using var inspectionStream = file.OpenReadStream();
            try { await _pageCounter.CountPagesAsync(inspectionStream, cancellationToken); }
            catch (InvalidDataException exception)
            { throw new ArgumentException($"File {file.FileName} is not a valid readable PDF.", exception); }
        }

        var container = _client.GetBlobContainerClient(containerName);
        await container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);

        var companySlug = Slugify(companyName ?? throw new ArgumentException("companyName required"));
        var machineSlug = Slugify(machineName ?? throw new ArgumentException("machineName required"));

        var uploaded = new List<string>();

        foreach (var f in files)
        {
            var safeName = SanitizeFileName(f.FileName);
            var blobName = $"{companySlug}/{machineSlug}/{safeName}";
            var blob = container.GetBlobClient(blobName);
            using var stream = f.OpenReadStream();
            var headers = new BlobHttpHeaders { ContentType = "application/pdf" };
            await blob.UploadAsync(stream, headers, cancellationToken: cancellationToken);
            uploaded.Add(blobName);
        }

        return uploaded;
    }

    public async Task<List<MachineDocumentDto>> ListMachinePdfDocumentsAsync(string blobPrefix, CancellationToken cancellationToken)
    {
        var prefix = GetMachinePrefix(blobPrefix);
        var result = new List<MachineDocumentDto>();
        var container = _client.GetBlobContainerClient("documents");
        await foreach (var blob in container.GetBlobsAsync(prefix: prefix, cancellationToken: cancellationToken))
        {
            if (!blob.Name.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var name = blob.Name[prefix.Length..];
            if (!IsPdfName(name)) continue;
            result.Add(new MachineDocumentDto(WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(name)), name));
        }
        return result.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<Stream?> OpenMachineDocumentAsync(string blobPrefix, string documentId, CancellationToken cancellationToken)
    {
        var prefix = GetMachinePrefix(blobPrefix);
        string name;
        try { name = new UTF8Encoding(false, true).GetString(WebEncoders.Base64UrlDecode(documentId)); }
        catch (Exception ex) when (ex is FormatException or ArgumentException) { return null; }
        if (!IsPdfName(name)) return null;
        var blobName = prefix + name;
        if (!blobName.StartsWith(prefix, StringComparison.Ordinal)) return null;
        var blob = _client.GetBlobContainerClient("documents").GetBlobClient(blobName);
        try
        {
            if (!await blob.ExistsAsync(cancellationToken)) return null;
            return await blob.OpenReadAsync(cancellationToken: cancellationToken);
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404) { return null; }
    }

    public async Task<Stream?> OpenReadAsync(string blobName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(blobName) ||
            blobName.StartsWith('/') ||
            blobName.StartsWith('\\') ||
            blobName.Contains("..", StringComparison.Ordinal) ||
            blobName.Contains('\\') ||
            blobName.Any(char.IsControl) ||
            !blobName.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
            Uri.TryCreate(blobName, UriKind.Absolute, out _))
        {
            return null;
        }

        try
        {
            return await _client
                .GetBlobContainerClient("documents")
                .GetBlobClient(blobName)
                .OpenReadAsync(cancellationToken: cancellationToken);
        }
        catch (Azure.RequestFailedException)
        {
            return null;
        }
    }

    private static bool IsPdfName(string name) =>
        name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
        && !name.Contains('/') && !name.Contains('\\') && !name.Any(char.IsControl);

    private static string GetMachinePrefix(string blobPrefix)
    {
        if (string.IsNullOrWhiteSpace(blobPrefix) || blobPrefix.Contains('\\')
            || blobPrefix.Split('/').Any(p => p is "" or "." or ".."))
            throw new ArgumentException("Machine BlobPrefix is missing or invalid.");
        return blobPrefix + "/";
    }

    private static string Slugify(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var normalized = input.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var ch in normalized)
        {
            var uc = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (uc != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(ch);
            }
        }
        var cleaned = sb.ToString().Normalize(NormalizationForm.FormC);
        cleaned = Regex.Replace(cleaned, "[^A-Za-z0-9]+", "-").Trim('-');
        return cleaned.ToLowerInvariant();
    }

    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrEmpty(name)) return "file";
        // Remove path chars and control chars
        var fileName = Path.GetFileName(name);
        fileName = Regex.Replace(fileName, "[\\\\/:*?\"<>|\\r\\n]+", "_");
        fileName = fileName.Trim();
        if (fileName.Length == 0) return "file";
        return fileName;
    }
}
