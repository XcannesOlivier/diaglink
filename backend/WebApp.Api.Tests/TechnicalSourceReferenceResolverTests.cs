using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebApp.Api.Services;

namespace WebApp.Api.Tests;

[TestClass]
public sealed class TechnicalSourceReferenceResolverTests
{
    private const string BlobPrefix = "develon/excavatrice-doosan-dx10z";
    private const string DocumentId = "develon-dx10z-manuel-utilisation-maintenance-en";
    private const string SourceBlob = BlobPrefix + "/DEVELON-DX10z-Manuel-Utilisation-Maintenance-EN.pdf";

    [TestMethod]
    public async Task Dx10z_DisplayPage70ResolvesPhysicalPdfPage72()
    {
        var fixture = Fixture.Valid(("70", 72));
        const string text = "Source : p. 70 (Figure 102) du manuel DX10z.";

        var references = await fixture.Resolver.ResolveAsync(text, BlobPrefix, [DocumentId], default);

        var reference = references.Single();
        Assert.AreEqual(DocumentId, reference.DocumentId);
        Assert.AreEqual(72, reference.PdfPage);
        Assert.AreEqual("70", reference.DisplayPage);
        Assert.AreEqual("p. 70", reference.Label);
        Assert.AreEqual(text.IndexOf("p. 70", StringComparison.Ordinal), reference.StartIndex);
        Assert.AreEqual(reference.StartIndex + reference.Label.Length, reference.EndIndex);
        Assert.AreEqual(0, reference.DisplayOrder);
    }

    [TestMethod]
    public async Task MultipleDifferentPagesResolveInTextOrderWithOneDownload()
    {
        var fixture = Fixture.Valid(("70", 72), ("93", 95));

        var references = await fixture.Resolver.ResolveAsync(
            "Voir p. 70 et p. 93, puis pages 70 et 93.",
            BlobPrefix,
            [DocumentId],
            default);

        CollectionAssert.AreEqual(new[] { 72, 95, 72, 95 }, references.Select(item => item.PdfPage).ToArray());
        CollectionAssert.AreEqual(new[] { 0, 1, 2, 3 }, references.Select(item => item.DisplayOrder).ToArray());
        Assert.AreEqual(1, fixture.Reader.OpenCount);
    }

    [TestMethod]
    public async Task RepeatedPageCreatesOneReferencePerOccurrence()
    {
        var fixture = Fixture.Valid(("70", 72));

        var references = await fixture.Resolver.ResolveAsync(
            "p. 70 confirme page 70.",
            BlobPrefix,
            [DocumentId],
            default);

        Assert.HasCount(2, references);
        Assert.AreNotEqual(references[0].StartIndex, references[1].StartIndex);
    }

    [TestMethod]
    public async Task MissingPageMapCreatesNoReference()
    {
        var fixture = Fixture.WithContent(null);

        var references = await fixture.Resolver.ResolveAsync("p. 70", BlobPrefix, [DocumentId], default);

        Assert.IsEmpty(references);
    }

    [TestMethod]
    public async Task InvalidPageMapCreatesNoReference()
    {
        var fixture = Fixture.WithContent("""{"schemaVersion":2,"documentId":"wrong","sourceBlob":"https://example.invalid/manual.pdf","pages":[]}""");

        var references = await fixture.Resolver.ResolveAsync("p. 70", BlobPrefix, [DocumentId], default);

        Assert.IsEmpty(references);
    }

    [TestMethod]
    public async Task MissingSchemaVersionCreatesNoReference()
    {
        var fixture = Fixture.WithContent(
            $$"""{"documentId":"{{DocumentId}}","sourceBlob":"{{SourceBlob}}","pages":[{"pdfPage":72,"displayPage":"70","source":"ExtractedText"}]}""");

        var references = await fixture.Resolver.ResolveAsync("p. 70", BlobPrefix, [DocumentId], default);

        Assert.IsEmpty(references);
    }

    [TestMethod]
    public async Task UnsupportedSchemaVersionCreatesNoReference()
    {
        var fixture = Fixture.WithContent(
            $$"""{"schemaVersion":2,"documentId":"{{DocumentId}}","sourceBlob":"{{SourceBlob}}","pages":[{"pdfPage":72,"displayPage":"70","source":"ExtractedText"}]}""");

        var references = await fixture.Resolver.ResolveAsync("p. 70", BlobPrefix, [DocumentId], default);

        Assert.IsEmpty(references);
    }

    [TestMethod]
    public async Task UnknownDisplayPageCreatesNoReference()
    {
        var fixture = Fixture.Valid(("70", 72));

        var references = await fixture.Resolver.ResolveAsync("p. 93", BlobPrefix, [DocumentId], default);

        Assert.IsEmpty(references);
    }

    [TestMethod]
    public async Task MultipleDocumentsCreateNoReferenceAndDoNotReadBlob()
    {
        var fixture = Fixture.Valid(("70", 72));

        var references = await fixture.Resolver.ResolveAsync(
            "p. 70",
            BlobPrefix,
            [DocumentId, "another-manual"],
            default);

        Assert.IsEmpty(references);
        Assert.AreEqual(0, fixture.Reader.OpenCount);
    }

    [TestMethod]
    public async Task AmbiguousDisplayPageCreatesNoReference()
    {
        var fixture = Fixture.Valid(("70", 72), ("70", 73));

        var references = await fixture.Resolver.ResolveAsync("p. 70", BlobPrefix, [DocumentId], default);

        Assert.IsEmpty(references);
    }

    [TestMethod]
    public async Task NullDisplayPagesAreIgnoredAndLaterPrintedPageResolves()
    {
        var fixture = Fixture.WithPages("""
            {"pdfPage":1,"displayPage":null,"source":"ExtractedText"},
            {"pdfPage":2,"displayPage":null,"source":"ExtractedText"},
            {"pdfPage":3,"displayPage":"1","source":"ExtractedText"},
            {"pdfPage":73,"displayPage":"71","source":"ExtractedText"}
            """);

        var references = await fixture.Resolver.ResolveAsync("Source : p. 71", BlobPrefix, [DocumentId], default);

        var reference = references.Single();
        Assert.AreEqual("71", reference.DisplayPage);
        Assert.AreEqual(73, reference.PdfPage);
    }

    [TestMethod]
    public async Task NullDisplayPageIsValidButNotResolvable()
    {
        var fixture = Fixture.WithPages(
            """{"pdfPage":1,"displayPage":null,"source":"ExtractedText"}""");

        var result = await fixture.PageMapResolver.ResolveAsync(
            BlobPrefix,
            DocumentId,
            ["1"],
            default);

        Assert.IsNotNull(result);
        Assert.IsEmpty(result.PdfPagesByDisplayPage);
    }

    [TestMethod]
    public async Task NumericDisplayPageInvalidatesPageMap()
    {
        var fixture = Fixture.WithPages(
            """{"pdfPage":1,"displayPage":1,"source":"ExtractedText"}""");

        var result = await fixture.PageMapResolver.ResolveAsync(
            BlobPrefix,
            DocumentId,
            ["1"],
            default);

        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task MissingDisplayPageInvalidatesPageMap()
    {
        var fixture = Fixture.WithPages(
            """{"pdfPage":1,"source":"ExtractedText"}""");

        var result = await fixture.PageMapResolver.ResolveAsync(
            BlobPrefix,
            DocumentId,
            ["1"],
            default);

        Assert.IsNull(result);
    }

    private sealed record Fixture(
        TechnicalSourceReferenceResolver Resolver,
        TechnicalPageMapResolver PageMapResolver,
        FakeDocumentBlobReader Reader)
    {
        public static Fixture Valid(params (string DisplayPage, int PdfPage)[] pages)
        {
            var pageJson = string.Join(',', pages.Select(page =>
                $$"""{"pdfPage":{{page.PdfPage}},"displayPage":"{{page.DisplayPage}}","source":"ExtractedText"}"""));
            return WithPages(pageJson);
        }

        public static Fixture WithPages(string pagesJson) => WithContent(
            $$"""{"schemaVersion":1,"documentId":"{{DocumentId}}","sourceBlob":"{{SourceBlob}}","pages":[{{pagesJson}}]}""");

        public static Fixture WithContent(string? content)
        {
            var reader = new FakeDocumentBlobReader { PageMapContent = content };
            var pageMapResolver = new TechnicalPageMapResolver(
                reader,
                NullLogger<TechnicalPageMapResolver>.Instance);
            return new(
                new TechnicalSourceReferenceResolver(pageMapResolver),
                pageMapResolver,
                reader);
        }
    }

    private sealed class FakeDocumentBlobReader : ITechnicalDocumentBlobReader
    {
        public string? PageMapContent { get; init; }
        public int OpenCount { get; private set; }

        public Task<Stream?> OpenPageMapAsync(string blobName, CancellationToken cancellationToken)
        {
            OpenCount++;
            Stream? stream = PageMapContent is null
                ? null
                : new MemoryStream(Encoding.UTF8.GetBytes(PageMapContent));
            return Task.FromResult(stream);
        }

        public Task<Stream?> OpenPdfAsync(string blobName, CancellationToken cancellationToken) =>
            Task.FromResult<Stream?>(null);
    }

}