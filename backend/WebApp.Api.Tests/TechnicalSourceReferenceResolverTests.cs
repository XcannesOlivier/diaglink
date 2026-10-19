using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebApp.Api.Models;
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
    public async Task NumericPageRangeCreatesIndependentReferences()
    {
        var fixture = Fixture.Valid(("72", 172), ("73", 173));

        var references = await fixture.Resolver.ResolveAsync(
            "Voir p. 72-73.",
            BlobPrefix,
            [DocumentId],
            default);

        CollectionAssert.AreEqual(new[] { 172, 173 }, references.Select(item => item.PdfPage).ToArray());
        CollectionAssert.AreEqual(new[] { "p. 72", "73" }, references.Select(item => item.Label).ToArray());
    }

    [TestMethod]
    public async Task FrenchPageRangeCreatesIndependentReferences()
    {
        var fixture = Fixture.Valid(("72", 172), ("73", 173));

        var references = await fixture.Resolver.ResolveAsync(
            "Voir pages 72 à 73.",
            BlobPrefix,
            [DocumentId],
            default);

        CollectionAssert.AreEqual(new[] { 172, 173 }, references.Select(item => item.PdfPage).ToArray());
    }

    [TestMethod]
    public async Task PageAndBareContinuationCreatesIndependentReferences()
    {
        var fixture = Fixture.Valid(("72", 172), ("73", 173));

        var references = await fixture.Resolver.ResolveAsync(
            "Voir p. 72 et 73.",
            BlobPrefix,
            [DocumentId],
            default);

        CollectionAssert.AreEqual(new[] { 172, 173 }, references.Select(item => item.PdfPage).ToArray());
    }

    [TestMethod]
    public async Task ExactCompoundDisplayPageIsNotExpandedAsRange()
    {
        var fixture = Fixture.Valid(("1720-1", 1721), ("1720", 1720), ("1", 1));

        var references = await fixture.Resolver.ResolveAsync(
            "Fig. 1720, p. 1720-1",
            BlobPrefix,
            [DocumentId],
            default);

        var reference = references.Single();
        Assert.AreEqual(1721, reference.PdfPage);
        Assert.AreEqual("1720-1", reference.DisplayPage);
        Assert.AreEqual("p. 1720-1", reference.Label);
    }

    [TestMethod]
    public async Task CompoundDisplayPageIsNotArtificiallyExpandedWhenExactPageIsMissing()
    {
        var fixture = Fixture.Valid(("1720", 1720), ("1", 1));

        var references = await fixture.Resolver.ResolveAsync(
            "Fig. 1720, p. 1720-1",
            BlobPrefix,
            [DocumentId],
            default);

        Assert.IsEmpty(references);
    }

    [TestMethod]
    public async Task BarePhysicalPageHeadingIsRecognized()
    {
        var fixture = Fixture.Valid(("72", 285));

        var references = await fixture.Resolver.ResolveAsync(
            "Page PDF : 285",
            BlobPrefix,
            [DocumentId],
            default);

        var reference = references.Single();
        Assert.AreEqual(285, reference.PdfPage);
        Assert.AreEqual("285", reference.Label);
        Assert.AreEqual(TechnicalSourcePageKind.Pdf, reference.PageKind);
    }

    [TestMethod]
    public async Task RangeResolvesOnlyPagesWithConfirmedDocumentPageEvidence()
    {
        var fixture = Fixture.WithDocuments(
            ("manual-a", Fixture.PageMap("manual-a", """
                {"pdfPage":72,"displayPage":"72","source":"ExtractedText"},
                {"pdfPage":73,"displayPage":"73","source":"ExtractedText"}
                """)),
            ("manual-b", Fixture.PageMap("manual-b", """
                {"pdfPage":172,"displayPage":"72","source":"ExtractedText"},
                {"pdfPage":173,"displayPage":"73","source":"ExtractedText"}
                """)));

        var references = await fixture.Resolver.ResolveAsync(
            "Voir p. 72-73.",
            BlobPrefix,
            ["manual-a", "manual-b"],
            [new("manual-b", 173)],
            [],
            default);

        var reference = references.Single();
        Assert.AreEqual("manual-b", reference.DocumentId);
        Assert.AreEqual(173, reference.PdfPage);
        Assert.AreEqual("73", reference.Label);
    }

    [TestMethod]
    public async Task RangePreservesDistinctConfirmedDocumentsPerPage()
    {
        var fixture = Fixture.WithDocuments(
            ("manual-a", Fixture.PageMap("manual-a", """
                {"pdfPage":72,"displayPage":"72","source":"ExtractedText"}
                """)),
            ("manual-b", Fixture.PageMap("manual-b", """
                {"pdfPage":173,"displayPage":"73","source":"ExtractedText"}
                """)));

        var references = await fixture.Resolver.ResolveAsync(
            "Voir p. 72-73.",
            BlobPrefix,
            ["manual-a", "manual-b"],
            [new("manual-a", 72), new("manual-b", 173)],
            [],
            default);

        CollectionAssert.AreEqual(new[] { "manual-a", "manual-b" }, references.Select(item => item.DocumentId).ToArray());
        CollectionAssert.AreEqual(new[] { 72, 173 }, references.Select(item => item.PdfPage).ToArray());
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
    public async Task MultipleDocumentsDoNotResolveUniqueDisplayedPageWithoutEvidence()
    {
        var fixture = Fixture.WithDocuments(
            (DocumentId, Fixture.PageMap(DocumentId, """{"pdfPage":72,"displayPage":"70","source":"ExtractedText"}""")),
            ("another-manual", Fixture.PageMap("another-manual", """{"pdfPage":80,"displayPage":"78","source":"ExtractedText"}""")),
            ("parts-catalog", Fixture.PageMap("parts-catalog", """{"pdfPage":9,"displayPage":"9","source":"ExtractedText"}""")));

        var references = await fixture.Resolver.ResolveAsync(
            "p. 9",
            BlobPrefix,
            [DocumentId, "another-manual", "parts-catalog"],
            default);

        Assert.IsEmpty(references);
        Assert.AreEqual(3, fixture.Reader.OpenCount);
    }

    [TestMethod]
    public async Task MultipleDocumentsWithSameDisplayedPageRequireMatchingPhysicalEvidence()
    {
        var fixture = Fixture.WithDocuments(
            ("manual-a", Fixture.PageMap("manual-a", """{"pdfPage":20,"displayPage":"18","source":"ExtractedText"}""")),
            ("manual-b", Fixture.PageMap("manual-b", """{"pdfPage":24,"displayPage":"18","source":"ExtractedText"}""")));

        var ambiguous = await fixture.Resolver.ResolveAsync(
            "Source : p. 18", BlobPrefix, ["manual-a", "manual-b"], [], [], default);
        var resolved = await fixture.Resolver.ResolveAsync(
            "Source : p. 18",
            BlobPrefix,
            ["manual-a", "manual-b"],
            [new("manual-b", 24)],
            [],
            default);

        Assert.IsEmpty(ambiguous);
        Assert.AreEqual("manual-b", resolved.Single().DocumentId);
        Assert.AreEqual(24, resolved.Single().PdfPage);
    }

    [TestMethod]
    public async Task SectionCitationRequiresAnchorInSameIndividualFileSearchResult()
    {
        var fixture = Fixture.WithDocuments(
            ("dx225-workshop", Fixture.PageMap("dx225-workshop", """{"pdfPage":127,"displayPage":"9","source":"ExtractedText"}""")),
            ("dx225-parts", Fixture.PageMap("dx225-parts", """{"pdfPage":11,"displayPage":"9","source":"ExtractedText"}""")),
            ("dx225-operator", Fixture.PageMap("dx225-operator", """{"pdfPage":15,"displayPage":"12","source":"ExtractedText"}""")));
        const string citation = "SP000913 p. 9 (Boom Down Circuit)";

        var missingAnchor = await fixture.Resolver.ResolveAsync(
            citation,
            BlobPrefix,
            ["dx225-workshop", "dx225-parts", "dx225-operator"],
            [new("dx225-workshop", 127, "Document ID: dx225-workshop\nPage number: 127\nHydraulic circuit")],
            [],
            default);
        var confirmed = await fixture.Resolver.ResolveAsync(
            citation,
            BlobPrefix,
            ["dx225-workshop", "dx225-parts", "dx225-operator"],
            [new("dx225-workshop", 127, "Document ID: dx225-workshop\nPage number: 127\nSP000913 Boom Down Circuit")],
            [],
            default);
        var contradictory = await fixture.Resolver.ResolveAsync(
            citation,
            BlobPrefix,
            ["dx225-workshop", "dx225-parts", "dx225-operator"],
            [
                new("dx225-workshop", 127, "Document ID: dx225-workshop\nPage: 127\nSP000913 Boom Down Circuit"),
                new("dx225-parts", 11, "Document ID: dx225-parts\nPage: 11\nSP000913")
            ],
            [],
            default);

        Assert.IsEmpty(missingAnchor);
        Assert.AreEqual("dx225-workshop", confirmed.Single().DocumentId);
        Assert.AreEqual(127, confirmed.Single().PdfPage);
        Assert.IsEmpty(contradictory);
    }

    [TestMethod]
    public async Task RepeatedWorkshopFoliosResolveFromConfirmedPhysicalPages()
    {
        var fixture = Fixture.WithDocuments(
            ("dx225-workshop", Fixture.PageMap("dx225-workshop", """
                {"pdfPage":100,"displayPage":"9","source":"ExtractedText"},
                {"pdfPage":285,"displayPage":"9","source":"ExtractedText"},
                {"pdfPage":300,"displayPage":"19","source":"ExtractedText"},
                {"pdfPage":491,"displayPage":"19","source":"ExtractedText"}
                """)));

        var references = await fixture.Resolver.ResolveAsync(
            "SP000913 p. 9 et SP001024 p. 19",
            BlobPrefix,
            ["dx225-workshop"],
            [
                new("dx225-workshop", 285, "Document ID: dx225-workshop\nPage number: 285\nSP000913"),
                new("dx225-workshop", 491, "Document ID: dx225-workshop\nPage number: 491\nSP001024")
            ],
            [],
            default);

        CollectionAssert.AreEqual(new[] { 285, 491 }, references.Select(item => item.PdfPage).ToArray());
    }

    [TestMethod]
    public async Task RepeatedFoliosWithoutFileSearchEvidenceDoNotResolve()
    {
        var fixture = Fixture.WithDocuments(
            ("dx225-workshop", Fixture.PageMap("dx225-workshop", """
                {"pdfPage":100,"displayPage":"9","source":"ExtractedText"},
                {"pdfPage":285,"displayPage":"9","source":"ExtractedText"}
                """)));

        var references = await fixture.Resolver.ResolveAsync(
            "SP000913 p. 9",
            BlobPrefix,
            ["dx225-workshop"],
            [],
            [],
            default);

        Assert.IsEmpty(references);
    }

    [TestMethod]
    public async Task RepeatedFolioAcrossDocumentsUsesOnlyConfirmedDocumentPage()
    {
        var fixture = Fixture.WithDocuments(
            ("dx225-workshop", Fixture.PageMap("dx225-workshop", """
                {"pdfPage":285,"displayPage":"9","source":"ExtractedText"}
                """)),
            ("dx225-parts", Fixture.PageMap("dx225-parts", """
                {"pdfPage":11,"displayPage":"9","source":"ExtractedText"}
                """)));

        var references = await fixture.Resolver.ResolveAsync(
            "SP000913 p. 9",
            BlobPrefix,
            ["dx225-workshop", "dx225-parts"],
            [new("dx225-workshop", 285, "Document ID: dx225-workshop\nPage: 285\nSP000913")],
            [],
            default);

        Assert.AreEqual("dx225-workshop", references.Single().DocumentId);
        Assert.AreEqual(285, references.Single().PdfPage);
    }

    [TestMethod]
    public async Task RepeatedFolioWithContradictoryPhysicalEvidenceDoesNotResolve()
    {
        var fixture = Fixture.WithDocuments(
            ("dx225-workshop", Fixture.PageMap("dx225-workshop", """
                {"pdfPage":285,"displayPage":"9","source":"ExtractedText"},
                {"pdfPage":286,"displayPage":"9","source":"ExtractedText"}
                """)));

        var references = await fixture.Resolver.ResolveAsync(
            "SP000913 p. 9",
            BlobPrefix,
            ["dx225-workshop"],
            [
                new("dx225-workshop", 285, "Document ID: dx225-workshop\nPage: 285\nSP000913"),
                new("dx225-workshop", 286, "Document ID: dx225-workshop\nPage: 286\nSP000913")
            ],
            [],
            default);

        Assert.IsEmpty(references);
    }

    [TestMethod]
    public async Task PhysicalCitationStillResolvesFromPhysicalEvidence()
    {
        var fixture = Fixture.WithDocuments(
            ("dx225-workshop", Fixture.PageMap("dx225-workshop", """
                {"pdfPage":285,"displayPage":"9","source":"ExtractedText"},
                {"pdfPage":286,"displayPage":"9","source":"ExtractedText"}
                """)));

        var references = await fixture.Resolver.ResolveAsync(
            "Page PDF : p. 285",
            BlobPrefix,
            ["dx225-workshop"],
            [new("dx225-workshop", 285)],
            [],
            default);

        Assert.AreEqual(285, references.Single().PdfPage);
        Assert.AreEqual(TechnicalSourcePageKind.Pdf, references.Single().PageKind);
    }

    [TestMethod]
    public async Task VisualFromAnotherDocumentCannotConfirmSectionCitation()
    {
        var fixture = Fixture.WithDocuments(
            ("dx225-workshop", Fixture.PageMap("dx225-workshop", """{"pdfPage":127,"displayPage":"9","source":"ExtractedText"}""")),
            ("dx225-parts", Fixture.PageMap("dx225-parts", """{"pdfPage":11,"displayPage":"9","source":"ExtractedText"}""")),
            ("dx225-operator", Fixture.PageMap("dx225-operator", """{"pdfPage":15,"displayPage":"12","source":"ExtractedText"}""")));
        TechnicalVisualReference[] unrelatedVisual =
        [
            new("dx225-parts", 11, "full", null, "parts.png", "dx225-parts/page-00011/parts.png")
        ];

        var references = await fixture.Resolver.ResolveAsync(
            "SP000913 p. 9 (Boom Down Circuit)",
            BlobPrefix,
            ["dx225-workshop", "dx225-parts", "dx225-operator"],
            [new("dx225-workshop", 127, "Document ID: dx225-workshop\nPage: 127\nSP000913 Boom Down Circuit")],
            unrelatedVisual,
            default);

        Assert.AreEqual("dx225-workshop", references.Single().DocumentId);
    }

    [TestMethod]
    public async Task SectionCitationWithRepeatedPagePrefixesResolvesEachConfirmedPageIndependently()
    {
        var fixture = Fixture.WithDocuments(
            ("dx225-workshop", Fixture.PageMap("dx225-workshop", """
                {"pdfPage":117,"displayPage":"17","source":"ExtractedText"},
                {"pdfPage":119,"displayPage":"19","source":"ExtractedText"},
                {"pdfPage":132,"displayPage":"32","source":"ExtractedText"}
                """)),
            ("dx225-parts", Fixture.PageMap("dx225-parts", """
                {"pdfPage":17,"displayPage":"17","source":"ExtractedText"},
                {"pdfPage":19,"displayPage":"19","source":"ExtractedText"},
                {"pdfPage":32,"displayPage":"32","source":"ExtractedText"}
                """)),
            ("dx225-operator", Fixture.PageMap("dx225-operator", """{"pdfPage":20,"displayPage":"18","source":"ExtractedText"}""")));
        const string text = "Sources : SP001024 p. 17, p. 19, p. 32 (Boom Spool Shift, Antidrift Valve)";
        TechnicalDocumentPageEvidence[] evidence =
        [
            new("dx225-workshop", 117, "Document ID: dx225-workshop\nPage: 117\nSP001024 Boom Spool Shift"),
            new("dx225-workshop", 119, "Document ID: dx225-workshop\nPage: 119\nAntidrift Valve"),
            new("dx225-workshop", 132, "Document ID: dx225-workshop\nPage: 132\nHydraulic schematic")
        ];

        var references = await fixture.Resolver.ResolveAsync(
            text,
            BlobPrefix,
            ["dx225-workshop", "dx225-parts", "dx225-operator"],
            evidence,
            [],
            default);
        var mentions = TechnicalSourceReferenceResolver.FindMentions(text);

        Assert.HasCount(3, references);
        CollectionAssert.AreEqual(new[] { "p. 17", "p. 19", "p. 32" }, references.Select(item => item.Label).ToArray());
        CollectionAssert.AreEqual(new[] { 117, 119, 132 }, references.Select(item => item.PdfPage).ToArray());
        Assert.IsTrue(references.All(item => item.DocumentId == "dx225-workshop"));
        Assert.IsTrue(mentions.All(mention => mention.CitationAnchor == "SP001024"));

        var partiallyConfirmed = await fixture.Resolver.ResolveAsync(
            text,
            BlobPrefix,
            ["dx225-workshop", "dx225-parts", "dx225-operator"],
            [evidence[0], evidence[2]],
            [],
            default);

        CollectionAssert.AreEqual(new[] { 117, 132 }, partiallyConfirmed.Select(item => item.PdfPage).ToArray());
    }

    [TestMethod]
    public async Task Dx225Lca_ThreeDocumentsResolvePhysicalPageGroupOnlyFromFileSearchEvidence()
    {
        var fixture = Fixture.WithDocuments(
            ("dx225-operator", Fixture.PageMap("dx225-operator", """{"pdfPage":20,"displayPage":"16","source":"ExtractedText"},{"pdfPage":21,"displayPage":"17","source":"ExtractedText"}""")),
            ("dx225-workshop", Fixture.PageMap("dx225-workshop", """{"pdfPage":20,"displayPage":"18","source":"ExtractedText"},{"pdfPage":21,"displayPage":"19","source":"ExtractedText"}""")),
            ("dx225-parts", Fixture.PageMap("dx225-parts", """{"pdfPage":20,"displayPage":null,"source":"ExtractedText"},{"pdfPage":21,"displayPage":null,"source":"ExtractedText"}""")));
        const string text = "Page PDF : p. 20 (schéma) et p. 21 (tableau des pièces)";
        TechnicalVisualReference[] visuals =
        [
            new("dx225-parts", 20, "full", null, "page.png", "dx225-parts/page-00020/page.png")
        ];

        var references = await fixture.Resolver.ResolveAsync(
            text,
            BlobPrefix,
            ["dx225-operator", "dx225-workshop", "dx225-parts"],
            [new("dx225-parts", 20), new("dx225-parts", 21)],
            visuals,
            default);

        Assert.HasCount(2, references);
        CollectionAssert.AreEqual(new[] { 20, 21 }, references.Select(item => item.PdfPage).ToArray());
        Assert.IsTrue(references.All(item => item.DocumentId == "dx225-parts"));
        Assert.IsTrue(references.All(item => item.PageKind == TechnicalSourcePageKind.Pdf));
    }

    [TestMethod]
    public async Task PhysicalCitationWithThreeDocumentsRequiresConfirmedDocumentPagePair()
    {
        var fixture = Fixture.WithDocuments(
            ("dx225-operator", Fixture.PageMap("dx225-operator", """{"pdfPage":20,"displayPage":"16","source":"ExtractedText"}""")),
            ("dx225-workshop", Fixture.PageMap("dx225-workshop", """{"pdfPage":20,"displayPage":"18","source":"ExtractedText"}""")),
            ("dx225-parts", Fixture.PageMap("dx225-parts", """{"pdfPage":20,"displayPage":null,"source":"ExtractedText"}""")));

        var unconfirmed = await fixture.Resolver.ResolveAsync(
            "Page PDF : p. 20",
            BlobPrefix,
            ["dx225-operator", "dx225-workshop", "dx225-parts"],
            [],
            [new("dx225-parts", 20, "full", null, "parts.png", "dx225-parts/page-00020/parts.png")],
            default);
        var confirmed = await fixture.Resolver.ResolveAsync(
            "Page PDF : p. 20",
            BlobPrefix,
            ["dx225-operator", "dx225-workshop", "dx225-parts"],
            [new("dx225-workshop", 20)],
            [new("dx225-parts", 20, "full", null, "parts.png", "dx225-parts/page-00020/parts.png")],
            default);

        Assert.IsEmpty(unconfirmed);
        Assert.AreEqual("dx225-workshop", confirmed.Single().DocumentId);
        Assert.AreEqual(TechnicalSourcePageKind.Pdf, confirmed.Single().PageKind);
    }

    [TestMethod]
    public async Task MultipleDocumentsWithSamePhysicalPagesRemainAmbiguousWithoutDocumentEvidence()
    {
        var fixture = Fixture.WithDocuments(
            ("manual-a", Fixture.PageMap("manual-a", """{"pdfPage":20,"displayPage":"18","source":"ExtractedText"}""")),
            ("manual-b", Fixture.PageMap("manual-b", """{"pdfPage":20,"displayPage":"19","source":"ExtractedText"}""")));

        var references = await fixture.Resolver.ResolveAsync(
            "Page PDF : p. 20", BlobPrefix, ["manual-a", "manual-b"], [], [], default);

        Assert.IsEmpty(references);
    }

    [TestMethod]
    public async Task PhysicalCitationWithoutReliableDocumentIdCreatesNoReference()
    {
        var fixture = Fixture.Valid(("18", 20));

        var references = await fixture.Resolver.ResolveAsync(
            "Page PDF : p. 20", BlobPrefix, [], [], [], default);

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
            PageMap(DocumentId, pagesJson));

        public static string PageMap(string documentId, string pagesJson) =>
            $$"""{"schemaVersion":1,"documentId":"{{documentId}}","sourceBlob":"{{BlobPrefix}}/{{documentId}}.pdf","pages":[{{pagesJson}}]}""";

        public static Fixture WithDocuments(params (string DocumentId, string? Content)[] documents)
        {
            var reader = new FakeDocumentBlobReader();
            foreach (var document in documents)
            {
                reader.PageMaps[$"{BlobPrefix}/{document.DocumentId}/page-map.json"] = document.Content;
            }
            var pageMapResolver = new TechnicalPageMapResolver(reader, NullLogger<TechnicalPageMapResolver>.Instance);
            return new(new TechnicalSourceReferenceResolver(pageMapResolver), pageMapResolver, reader);
        }

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
        public Dictionary<string, string?> PageMaps { get; } = new(StringComparer.Ordinal);
        public int OpenCount { get; private set; }

        public Task<Stream?> OpenPageMapAsync(string blobName, CancellationToken cancellationToken)
        {
            OpenCount++;
            var content = PageMaps.Count > 0
                ? PageMaps.GetValueOrDefault(blobName)
                : PageMapContent;
            Stream? stream = content is null
                ? null
                : new MemoryStream(Encoding.UTF8.GetBytes(content));
            return Task.FromResult(stream);
        }

        public Task<Stream?> OpenPdfAsync(string blobName, CancellationToken cancellationToken) =>
            Task.FromResult<Stream?>(null);
    }

}
