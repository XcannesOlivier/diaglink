using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebApp.Api.Models;
using WebApp.Api.Services;

namespace WebApp.Api.Tests;

[TestClass]
public class MachineRequestStorageServiceTests
{
    [TestMethod]
    public async Task CreateAsync_StoresPrivateRequestTreeAndServerOwnedPricing()
    {
        var blobs = new InMemoryMachineRequestBlobClient();
        var service = new MachineRequestStorageService(blobs);

        var record = await service.CreateAsync(CreateDraft(),
        [
            Upload("../../manual.pdf", 286, "first"),
            Upload("schema.pdf", 264, "second"),
        ]);

        Assert.IsTrue(blobs.ContainerEnsured);
        Assert.AreEqual(MachineRequestStatuses.Pending, record.Status);
        Assert.AreEqual(MachineRequestKind.InitialMachine, record.RequestKind);
        Assert.IsNull(record.CompanyId);
        Assert.IsNull(record.RequestedByUserId);
        Assert.IsNull(record.TargetMachineId);
        Assert.AreEqual(550, record.Pricing.TotalPages);
        Assert.AreEqual(150, record.Pricing.AdditionalPages);
        Assert.AreEqual(140.40m, record.Pricing.PreparationTotal);
        Assert.AreEqual(29.90m, record.Pricing.MonthlySubscriptionPrice);
        Assert.IsTrue(blobs.Names.Contains($"{record.RequestId}/request.json"));
        Assert.IsTrue(record.Documents.All(document => document.BlobName.StartsWith($"{record.RequestId}/documents/", StringComparison.Ordinal)));
        Assert.IsTrue(record.Documents.All(document => !document.BlobName.Contains("..", StringComparison.Ordinal)));

        await using var requestStream = await blobs.OpenReadAsync($"{record.RequestId}/request.json");
        var stored = await JsonSerializer.DeserializeAsync<MachineRequestRecord>(requestStream!, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.AreEqual(record.RequestId, stored?.RequestId);
        Assert.AreEqual(MachineRequestKind.InitialMachine, stored?.RequestKind);
    }

    [TestMethod]
    public async Task ExistingBlobWithoutOriginFields_IsReadAsInitialMachine()
    {
        var blobs = new InMemoryMachineRequestBlobClient();
        var requestId = Guid.NewGuid().ToString("N");
        var record = Record(requestId, MachineRequestKind.AdditionalMachine, Guid.NewGuid(), Guid.NewGuid());
        var json = JsonSerializer.SerializeToNode(record, new JsonSerializerOptions(JsonSerializerDefaults.Web))!.AsObject();
        json.Remove("requestKind");
        json.Remove("companyId");
        json.Remove("requestedByUserId");
        json.Remove("targetMachineId");
        json.Remove("isArchived");
        json.Remove("archivedAtUtc");
        json.Remove("archivedByUserId");
        await using var content = new MemoryStream(Encoding.UTF8.GetBytes(json.ToJsonString()));
        await blobs.UploadAsync($"{requestId}/request.json", content, "application/json", overwrite: false);

        var restored = await new MachineRequestStorageService(blobs).GetAsync(requestId);

        Assert.IsNotNull(restored);
        Assert.AreEqual(MachineRequestKind.InitialMachine, restored.RequestKind);
        Assert.IsNull(restored.CompanyId);
        Assert.IsNull(restored.RequestedByUserId);
        Assert.IsNull(restored.TargetMachineId);
        Assert.IsFalse(restored.IsArchived);
        Assert.IsNull(restored.ArchivedAtUtc);
        Assert.IsNull(restored.ArchivedByUserId);
    }

    [TestMethod]
    public void RequestOriginFields_RoundTripThroughJson()
    {
        var companyId = Guid.NewGuid();
        var requestedByUserId = Guid.NewGuid();
        var record = Record(Guid.NewGuid().ToString("N"), MachineRequestKind.AdditionalMachine,
            companyId, requestedByUserId);

        var json = JsonSerializer.Serialize(record, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var restored = JsonSerializer.Deserialize<MachineRequestRecord>(json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        StringAssert.Contains(json, "\"requestKind\":\"AdditionalMachine\"");
        Assert.IsNotNull(restored);
        Assert.AreEqual(MachineRequestKind.AdditionalMachine, restored.RequestKind);
        Assert.AreEqual(companyId, restored.CompanyId);
        Assert.AreEqual(requestedByUserId, restored.RequestedByUserId);
    }

    [TestMethod]
    public void AdditionalDocumentsTargetMachineId_RoundTripsThroughJson()
    {
        var targetMachineId = Guid.NewGuid();
        var record = Record(Guid.NewGuid().ToString("N"), MachineRequestKind.AdditionalDocuments,
            Guid.NewGuid(), Guid.NewGuid()) with { TargetMachineId = targetMachineId };

        var json = JsonSerializer.Serialize(record, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var restored = JsonSerializer.Deserialize<MachineRequestRecord>(json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        StringAssert.Contains(json, "\"requestKind\":\"AdditionalDocuments\"");
        StringAssert.Contains(json, $"\"targetMachineId\":\"{targetMachineId}\"");
        Assert.IsNotNull(restored);
        Assert.AreEqual(MachineRequestKind.AdditionalDocuments, restored.RequestKind);
        Assert.AreEqual(targetMachineId, restored.TargetMachineId);
    }

    [TestMethod]
    public async Task ReadListDownloadAndStatusUpdate_UseOnlyTheRequestPrefix()
    {
        var blobs = new InMemoryMachineRequestBlobClient();
        var service = new MachineRequestStorageService(blobs);
        var created = await service.CreateAsync(CreateDraft(), [Upload("manual.pdf", 400, "pdf-content")]);

        var listed = await service.ListAsync();
        var read = await service.GetAsync(created.RequestId);
        var download = await service.OpenDocumentAsync(created.RequestId, created.Documents[0].BlobName);
        var updated = await service.UpdateStatusAsync(created.RequestId, MachineRequestStatuses.Treated);

        Assert.HasCount(1, listed);
        Assert.AreEqual(created.RequestId, read?.RequestId);
        Assert.IsNotNull(download);
        using var reader = new StreamReader(download.Content, Encoding.UTF8);
        Assert.AreEqual("pdf-content", await reader.ReadToEndAsync());
        Assert.AreEqual(MachineRequestStatuses.Treated, updated?.Status);
        Assert.AreEqual(MachineRequestStatuses.Treated, (await service.GetAsync(created.RequestId))?.Status);
        Assert.IsNull(await service.OpenDocumentAsync(created.RequestId, $"{created.RequestId}/documents/not-listed.pdf"));
    }

    [TestMethod]
    public async Task ArchiveAndRestore_UpdateOnlyRequestMetadataAndPreserveDocumentsAndStatus()
    {
        var blobs = new InMemoryMachineRequestBlobClient();
        var service = new MachineRequestStorageService(blobs);
        var created = await service.CreateAsync(CreateDraft(), [Upload("manual.pdf", 400, "pdf-content")]);
        await service.UpdateStatusAsync(created.RequestId, MachineRequestStatuses.Treated);
        var documentNames = blobs.Names.Where(name => name.Contains("/documents/", StringComparison.Ordinal)).ToArray();
        var userId = Guid.NewGuid();

        var archived = await service.UpdateArchiveAsync(created.RequestId, true, userId);
        Assert.IsTrue(archived?.IsArchived);
        Assert.IsNotNull(archived?.ArchivedAtUtc);
        Assert.AreEqual(userId, archived?.ArchivedByUserId);
        Assert.AreEqual(MachineRequestStatuses.Treated, archived?.Status);
        CollectionAssert.AreEquivalent(documentNames, blobs.Names.Where(name => name.Contains("/documents/", StringComparison.Ordinal)).ToArray());

        var restored = await service.UpdateArchiveAsync(created.RequestId, false, userId);
        Assert.IsFalse(restored?.IsArchived);
        Assert.IsNull(restored?.ArchivedAtUtc);
        Assert.IsNull(restored?.ArchivedByUserId);
        Assert.AreEqual(MachineRequestStatuses.Treated, restored?.Status);
        CollectionAssert.AreEquivalent(documentNames, blobs.Names.Where(name => name.Contains("/documents/", StringComparison.Ordinal)).ToArray());
    }

    [TestMethod]
    public async Task InvalidIdentifiersStatusesAndNonPdfDocuments_AreRejected()
    {
        var service = new MachineRequestStorageService(new InMemoryMachineRequestBlobClient());

        await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.GetAsync("../request"));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.UpdateStatusAsync(Guid.NewGuid().ToString("N"), "unknown"));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.CreateAsync(CreateDraft(), [Upload("notes.txt", 1, "not-pdf")]));
    }

    [TestMethod]
    public async Task CreateAsync_WhenStorageFails_RemovesThePartialRequestTree()
    {
        var blobs = new InMemoryMachineRequestBlobClient { FailOnUploadNumber = 2 };
        var service = new MachineRequestStorageService(blobs);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service.CreateAsync(
            CreateDraft(),
            [Upload("manual.pdf", 1, "first"), Upload("schema.pdf", 1, "second")]));

        Assert.HasCount(0, blobs.Names);
    }

    private static MachineRequestDraft CreateDraft() => new(
        new MachineRequestClient("Claire", "Martin", "Ateliers Martin", "claire@example.com", "+33 6 12 34 56 78"),
        new MachineRequestMachine("Compresseur", "Atlas Copco", "GA90", null, null));

    private static MachineRequestRecord Record(string requestId, MachineRequestKind kind, Guid? companyId,
        Guid? requestedByUserId) => new(requestId, DateTimeOffset.UtcNow, MachineRequestStatuses.Pending,
        CreateDraft().Client, CreateDraft().Machine, [],
        new MachineRequestPricing(400, 400, 0, 99.90m, 0.27m, 99.90m, 29.90m),
        RequestKind: kind, CompanyId: companyId, RequestedByUserId: requestedByUserId);

    private static MachineRequestDocumentUpload Upload(string name, int pages, string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        return new MachineRequestDocumentUpload(name, "application/pdf", bytes.Length, pages, new MemoryStream(bytes));
    }

    private sealed class InMemoryMachineRequestBlobClient : IMachineRequestBlobClient
    {
        private readonly Dictionary<string, byte[]> _blobs = new(StringComparer.Ordinal);
        public bool ContainerEnsured { get; private set; }
        public int? FailOnUploadNumber { get; init; }
        private int UploadCount { get; set; }
        public IReadOnlyCollection<string> Names => _blobs.Keys;

        public Task EnsurePrivateContainerAsync(CancellationToken cancellationToken = default)
        {
            ContainerEnsured = true;
            return Task.CompletedTask;
        }

        public async Task UploadAsync(string blobName, Stream content, string contentType, bool overwrite, CancellationToken cancellationToken = default)
        {
            if (++UploadCount == FailOnUploadNumber) throw new InvalidOperationException("Simulated storage failure.");
            if (!overwrite && _blobs.ContainsKey(blobName)) throw new InvalidOperationException("Blob already exists.");
            using var copy = new MemoryStream();
            await content.CopyToAsync(copy, cancellationToken);
            _blobs[blobName] = copy.ToArray();
        }

        public Task<Stream?> OpenReadAsync(string blobName, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream?>(_blobs.TryGetValue(blobName, out var bytes) ? new MemoryStream(bytes, writable: false) : null);

        public async IAsyncEnumerable<string> ListNamesAsync(string? prefix = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var name in _blobs.Keys.Where(name => prefix is null || name.StartsWith(prefix, StringComparison.Ordinal)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return name;
                await Task.Yield();
            }
        }

        public Task DeletePrefixAsync(string prefix, CancellationToken cancellationToken = default)
        {
            foreach (var name in _blobs.Keys.Where(name => name.StartsWith(prefix, StringComparison.Ordinal)).ToList()) _blobs.Remove(name);
            return Task.CompletedTask;
        }
    }
}
