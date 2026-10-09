using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebApp.Api.Services;

namespace WebApp.Api.Tests;

[TestClass]
public sealed class CompanyLogoStorageServiceTests
{
    private static readonly byte[] ValidPng = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x01];
    private static readonly byte[] ValidJpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x01, 0xFF, 0xD9];

    [TestMethod]
    public async Task UploadAsync_ValidPng_UploadsDetectedContentType()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.UploadAsync(
            fixture.CompanyA, StreamOf(ValidPng), "customer-logo.png", "image/png");

        Assert.AreEqual("image/png", result.ContentType);
        Assert.IsTrue(result.BlobName.EndsWith(".png", StringComparison.Ordinal));
        Assert.AreEqual("image/png", fixture.Blobs.Uploads[result.BlobName].ContentType);
        Assert.AreEqual(1, fixture.Blobs.EnsurePrivateCount);
    }

    [TestMethod]
    public async Task UploadAsync_ValidJpeg_UploadsDetectedContentType()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.UploadAsync(
            fixture.CompanyA, StreamOf(ValidJpeg), "customer-logo.jpeg", "image/jpeg");

        Assert.AreEqual("image/jpeg", result.ContentType);
        Assert.IsTrue(result.BlobName.EndsWith(".jpg", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task UploadAsync_TooLarge_RejectsBeforeBlobAccess()
    {
        var fixture = new Fixture();
        var content = new byte[CompanyLogoStorageService.MaxLogoBytes + 1];
        ValidPng.CopyTo(content, 0);

        await Assert.ThrowsExactlyAsync<ArgumentException>(() => fixture.Service.UploadAsync(
            fixture.CompanyA, StreamOf(content), "customer-logo.png", "image/png"));
        Assert.AreEqual(0, fixture.Blobs.EnsurePrivateCount);
        Assert.AreEqual(0, fixture.Blobs.Uploads.Count);
    }

    [TestMethod]
    public async Task UploadAsync_Svg_RejectsBeforeBlobAccess()
    {
        var fixture = new Fixture();

        await Assert.ThrowsExactlyAsync<ArgumentException>(() => fixture.Service.UploadAsync(
            fixture.CompanyA,
            StreamOf("<svg xmlns=\"http://www.w3.org/2000/svg\"></svg>"u8.ToArray()),
            "customer-logo.svg",
            "image/svg+xml"));
        Assert.AreEqual(0, fixture.Blobs.Uploads.Count);
    }

    [TestMethod]
    public async Task UploadAsync_LyingExtensionOrContentType_IsRejected()
    {
        var fixture = new Fixture();

        await Assert.ThrowsExactlyAsync<ArgumentException>(() => fixture.Service.UploadAsync(
            fixture.CompanyA, StreamOf(ValidJpeg), "customer-logo.png", "image/png"));
        Assert.AreEqual(0, fixture.Blobs.Uploads.Count);
    }

    [TestMethod]
    public async Task UploadAsync_GeneratesServerOwnedVersionedBlobName()
    {
        var fixture = new Fixture();

        var first = await fixture.Service.UploadAsync(
            fixture.CompanyA, StreamOf(ValidPng), "do-not-use-this-name.png", "image/png");
        var second = await fixture.Service.UploadAsync(
            fixture.CompanyA, StreamOf(ValidPng), "do-not-use-this-name.png", "image/png");

        var prefix = $"companies/{fixture.CompanyA:N}/logos/";
        StringAssert.StartsWith(first.BlobName, prefix);
        Assert.IsFalse(first.BlobName.Contains("do-not-use-this-name", StringComparison.Ordinal));
        Assert.AreNotEqual(first.BlobName, second.BlobName);
        Assert.IsTrue(Guid.TryParseExact(
            Path.GetFileNameWithoutExtension(first.BlobName), "N", out _));
    }

    [TestMethod]
    public async Task Operations_RejectBlobOwnedByAnotherCompany()
    {
        var fixture = new Fixture();
        var uploaded = await fixture.Service.UploadAsync(
            fixture.CompanyB, StreamOf(ValidPng), "logo.png", "image/png");

        await Assert.ThrowsExactlyAsync<ArgumentException>(
            () => fixture.Service.OpenReadAsync(fixture.CompanyA, uploaded.BlobName));
        await Assert.ThrowsExactlyAsync<ArgumentException>(
            () => fixture.Service.DeleteAsync(fixture.CompanyA, uploaded.BlobName));
    }

    [TestMethod]
    public async Task OpenReadAsync_ExistingLogo_ReturnsContentAndMetadata()
    {
        var fixture = new Fixture();
        var uploaded = await fixture.Service.UploadAsync(
            fixture.CompanyA, StreamOf(ValidPng), "logo.png", "image/png");

        var result = await fixture.Service.OpenReadAsync(fixture.CompanyA, uploaded.BlobName);

        Assert.IsNotNull(result);
        await using var content = result.Content;
        using var copy = new MemoryStream();
        await content.CopyToAsync(copy);
        CollectionAssert.AreEqual(ValidPng, copy.ToArray());
        Assert.AreEqual("image/png", result.ContentType);
        Assert.AreEqual(uploaded.ETag, result.ETag);
    }

    [TestMethod]
    public async Task DeleteAsync_ExistingLogo_RemovesIt()
    {
        var fixture = new Fixture();
        var uploaded = await fixture.Service.UploadAsync(
            fixture.CompanyA, StreamOf(ValidPng), "logo.png", "image/png");

        Assert.IsTrue(await fixture.Service.DeleteAsync(fixture.CompanyA, uploaded.BlobName));
        Assert.IsNull(await fixture.Service.OpenReadAsync(fixture.CompanyA, uploaded.BlobName));
    }

    [TestMethod]
    public async Task MissingBlob_ReadReturnsNullAndDeleteReturnsFalse()
    {
        var fixture = new Fixture();
        var missing = $"companies/{fixture.CompanyA:N}/logos/{Guid.NewGuid():N}.png";

        Assert.IsNull(await fixture.Service.OpenReadAsync(fixture.CompanyA, missing));
        Assert.IsFalse(await fixture.Service.DeleteAsync(fixture.CompanyA, missing));
    }

    private static MemoryStream StreamOf(byte[] content) => new(content, writable: false);

    private sealed class Fixture
    {
        public Guid CompanyA { get; } = Guid.NewGuid();
        public Guid CompanyB { get; } = Guid.NewGuid();
        public FakeBlobClient Blobs { get; } = new();
        public CompanyLogoStorageService Service { get; }

        public Fixture()
        {
            Service = new CompanyLogoStorageService(Blobs);
        }
    }

    private sealed class FakeBlobClient : ICompanyBrandingBlobClient
    {
        public Dictionary<string, StoredBlob> Uploads { get; } = [];
        public int EnsurePrivateCount { get; private set; }

        public Task EnsurePrivateContainerAsync(CancellationToken cancellationToken = default)
        {
            EnsurePrivateCount++;
            return Task.CompletedTask;
        }

        public async Task<CompanyBrandingBlobUploadResult> UploadAsync(
            string blobName,
            Stream content,
            string contentType,
            CancellationToken cancellationToken = default)
        {
            using var copy = new MemoryStream();
            await content.CopyToAsync(copy, cancellationToken);
            var etag = $"\"{Guid.NewGuid():N}\"";
            Uploads.Add(blobName, new StoredBlob(copy.ToArray(), contentType, etag));
            return new CompanyBrandingBlobUploadResult(etag);
        }

        public Task<CompanyBrandingBlobReadResult?> OpenReadAsync(
            string blobName,
            CancellationToken cancellationToken = default)
        {
            if (!Uploads.TryGetValue(blobName, out var stored))
            {
                return Task.FromResult<CompanyBrandingBlobReadResult?>(null);
            }

            return Task.FromResult<CompanyBrandingBlobReadResult?>(new(
                new MemoryStream(stored.Content, writable: false),
                stored.ContentType,
                stored.ETag));
        }

        public Task<bool> DeleteAsync(string blobName, CancellationToken cancellationToken = default) =>
            Task.FromResult(Uploads.Remove(blobName));
    }

    private sealed record StoredBlob(byte[] Content, string ContentType, string ETag);
}
