using System.Text.Json;
using System.Text.RegularExpressions;
using WebApp.Api.Models;

namespace WebApp.Api.Services;

public sealed class MachineRequestStorageService
{
    public const int IncludedPages = 400;
    public const decimal BasePreparationPrice = 99.90m;
    public const decimal AdditionalPagePrice = 0.27m;
    public const decimal MonthlySubscriptionPrice = 29.90m;
    private const string RequestFileName = "request.json";
    private readonly IMachineRequestBlobClient _blobClient;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public MachineRequestStorageService(IMachineRequestBlobClient blobClient)
    {
        _blobClient = blobClient ?? throw new ArgumentNullException(nameof(blobClient));
    }

    public async Task<MachineRequestRecord> CreateAsync(
        MachineRequestDraft draft,
        IReadOnlyCollection<MachineRequestDocumentUpload> documents,
        CancellationToken cancellationToken = default)
        => await CreateAsync(draft, documents, Guid.NewGuid().ToString("N"), null, cancellationToken);

    public async Task<MachineRequestRecord> CreateAsync(
        MachineRequestDraft draft,
        IReadOnlyCollection<MachineRequestDocumentUpload> documents,
        string requestId,
        MachineRequestPaymentReference? payment,
        CancellationToken cancellationToken = default)
        => await CreateCoreAsync(draft, documents, requestId, payment, MachineRequestKind.InitialMachine,
            null, null, cancellationToken);

    public Task<MachineRequestRecord> CreateAdditionalAsync(
        MachineRequestDraft draft,
        IReadOnlyCollection<MachineRequestDocumentUpload> documents,
        string requestId,
        MachineRequestPaymentReference payment,
        Guid companyId,
        Guid requestedByUserId,
        CancellationToken cancellationToken = default)
        => CreateCoreAsync(draft, documents, requestId, payment, MachineRequestKind.AdditionalMachine,
            companyId, requestedByUserId, cancellationToken);

    public Task<MachineRequestRecord> CreateAdditionalDocumentsAsync(
        MachineRequestDraft draft,
        IReadOnlyCollection<MachineRequestDocumentUpload> documents,
        string requestId,
        MachineRequestPaymentReference payment,
        Guid companyId,
        Guid requestedByUserId,
        Guid targetMachineId,
        CancellationToken cancellationToken = default)
        => CreateCoreAsync(draft, documents, requestId, payment, MachineRequestKind.AdditionalDocuments,
            companyId, requestedByUserId, cancellationToken, targetMachineId);

    private async Task<MachineRequestRecord> CreateCoreAsync(
        MachineRequestDraft draft,
        IReadOnlyCollection<MachineRequestDocumentUpload> documents,
        string requestId,
        MachineRequestPaymentReference? payment,
        MachineRequestKind requestKind,
        Guid? companyId,
        Guid? requestedByUserId,
        CancellationToken cancellationToken,
        Guid? targetMachineId = null)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(documents);
        if (documents.Count == 0) throw new ArgumentException("At least one PDF document is required.", nameof(documents));

        requestId = ValidateRequestId(requestId);
        var attemptId = Guid.NewGuid().ToString("N");
        var attemptPrefix = $"{requestId}/documents/{attemptId}-";
        var storedDocuments = new List<MachineRequestDocument>(documents.Count);
        await _blobClient.EnsurePrivateContainerAsync(cancellationToken);

        foreach (var document in documents) ValidateDocument(document);

        try
        {
            var index = 0;
            foreach (var document in documents)
            {
                var safeName = SanitizePdfFileName(document.OriginalName);
                var blobName = $"{attemptPrefix}{++index:D3}-{safeName}";
                await _blobClient.UploadAsync(blobName, document.Content, "application/pdf", overwrite: false, cancellationToken);
                storedDocuments.Add(new MachineRequestDocument(document.OriginalName, blobName, document.Size, document.PageCount));
            }

            // Page counts passed to this storage primitive must come from a trusted server-side
            // PDF inspection step. Browser-provided counts must never be used for billing.
            var totalPages = storedDocuments.Sum(document => document.PageCount);
            var isAdditionalDocuments = requestKind == MachineRequestKind.AdditionalDocuments;
            var includedPages = isAdditionalDocuments ? 0 : IncludedPages;
            var additionalPages = isAdditionalDocuments ? totalPages : Math.Max(0, totalPages - IncludedPages);
            var basePrice = isAdditionalDocuments ? 0m : BasePreparationPrice;
            var pagePrice = isAdditionalDocuments ? AdditionalDocumentsPricing.PricePerPageCents / 100m : AdditionalPagePrice;
            var preparationTotal = isAdditionalDocuments
                ? AdditionalDocumentsPricing.CalculateAmountCents(totalPages) / 100m
                : decimal.Round(BasePreparationPrice + additionalPages * AdditionalPagePrice, 2, MidpointRounding.AwayFromZero);
            var record = new MachineRequestRecord(
                requestId,
                DateTimeOffset.UtcNow,
                MachineRequestStatuses.Pending,
                draft.Client,
                draft.Machine,
                storedDocuments,
                new MachineRequestPricing(
                    totalPages,
                    includedPages,
                    additionalPages,
                    basePrice,
                    pagePrice,
                    preparationTotal,
                    isAdditionalDocuments ? 0m : MonthlySubscriptionPrice),
                payment,
                RequestKind: requestKind,
                CompanyId: companyId,
                RequestedByUserId: requestedByUserId,
                TargetMachineId: targetMachineId);

            await SaveRecordAsync(record, overwrite: false, cancellationToken);
            return record;
        }
        catch
        {
            try { await _blobClient.DeletePrefixAsync(attemptPrefix, CancellationToken.None); }
            catch { /* Preserve the original storage exception; cleanup is best effort. */ }
            throw;
        }
    }

    public async Task<IReadOnlyList<MachineRequestRecord>> ListAsync(CancellationToken cancellationToken = default)
    {
        await _blobClient.EnsurePrivateContainerAsync(cancellationToken);
        var records = new List<MachineRequestRecord>();
        await foreach (var name in _blobClient.ListNamesAsync(cancellationToken: cancellationToken))
        {
            if (!name.EndsWith($"/{RequestFileName}", StringComparison.Ordinal)) continue;
            var requestId = name[..^($"/{RequestFileName}".Length)];
            var record = await GetAsync(requestId, cancellationToken);
            if (record is not null) records.Add(record);
        }
        return records.OrderByDescending(record => record.CreatedAt).ToList();
    }

    public async Task<MachineRequestRecord?> GetAsync(string requestId, CancellationToken cancellationToken = default)
    {
        var normalizedId = ValidateRequestId(requestId);
        await _blobClient.EnsurePrivateContainerAsync(cancellationToken);
        await using var content = await _blobClient.OpenReadAsync($"{normalizedId}/{RequestFileName}", cancellationToken);
        return content is null
            ? null
            : await JsonSerializer.DeserializeAsync<MachineRequestRecord>(content, JsonOptions, cancellationToken);
    }

    public async Task<MachineRequestDocumentDownload?> OpenDocumentAsync(string requestId, string blobName, CancellationToken cancellationToken = default)
    {
        var normalizedId = ValidateRequestId(requestId);
        var record = await GetAsync(normalizedId, cancellationToken);
        var document = record?.Documents.SingleOrDefault(item => string.Equals(item.BlobName, blobName, StringComparison.Ordinal));
        if (document is null || !blobName.StartsWith($"{normalizedId}/documents/", StringComparison.Ordinal)) return null;
        var content = await _blobClient.OpenReadAsync(document.BlobName, cancellationToken);
        return content is null ? null : new MachineRequestDocumentDownload(document.OriginalName, "application/pdf", content);
    }

    public async Task<MachineRequestRecord?> UpdateStatusAsync(string requestId, string status, CancellationToken cancellationToken = default)
    {
        if (!MachineRequestStatuses.IsValid(status)) throw new ArgumentException("Invalid machine request status.", nameof(status));
        var record = await GetAsync(requestId, cancellationToken);
        if (record is null) return null;
        var updated = record with { Status = status };
        await SaveRecordAsync(updated, overwrite: true, cancellationToken);
        return updated;
    }

    public async Task<MachineRequestRecord?> UpdateArchiveAsync(
        string requestId,
        bool isArchived,
        Guid archivedByUserId,
        CancellationToken cancellationToken = default)
    {
        var record = await GetAsync(requestId, cancellationToken);
        if (record is null) return null;
        var updated = record with
        {
            IsArchived = isArchived,
            ArchivedAtUtc = isArchived ? DateTimeOffset.UtcNow : null,
            ArchivedByUserId = isArchived ? archivedByUserId : null,
        };
        await SaveRecordAsync(updated, overwrite: true, cancellationToken);
        return updated;
    }

    public static string SanitizePdfFileName(string originalName)
    {
        var fileName = Path.GetFileName(originalName ?? string.Empty);
        fileName = Regex.Replace(fileName, "[\\\\/:*?\"<>|\\r\\n]+", "_").Trim();
        if (string.IsNullOrWhiteSpace(fileName)) fileName = "document.pdf";
        if (!fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) fileName += ".pdf";
        return fileName.Length <= 160 ? fileName : $"{Path.GetFileNameWithoutExtension(fileName)[..156]}.pdf";
    }

    private async Task SaveRecordAsync(MachineRequestRecord record, bool overwrite, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(record, JsonOptions);
        await using var stream = new MemoryStream(bytes, writable: false);
        await _blobClient.UploadAsync($"{record.RequestId}/{RequestFileName}", stream, "application/json", overwrite, cancellationToken);
    }

    private static string ValidateRequestId(string requestId)
    {
        if (!Guid.TryParseExact(requestId, "N", out var parsed)) throw new ArgumentException("Invalid request id.", nameof(requestId));
        return parsed.ToString("N");
    }

    private static void ValidateDocument(MachineRequestDocumentUpload document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Size <= 0 || document.Content is null || !document.Content.CanRead) throw new ArgumentException("Document content is required.");
        if (document.PageCount <= 0) throw new ArgumentException("A verified positive page count is required.");
        if (!document.OriginalName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Only PDF documents are accepted.");
    }
}
