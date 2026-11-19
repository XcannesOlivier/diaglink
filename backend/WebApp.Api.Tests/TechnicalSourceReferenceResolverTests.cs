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
        Assert.AreEqual("develon-dx10z-manuel-utilisation-maintenance-en.pdf", reference.DocumentName);
    }

    [TestMethod]
    public async Task HistoricalReferenceWithChangedSourceEtagIsNotReused()
    {
        var currentVersion = TechnicalSourcePageStorage.CreateVersionToken("etag-v2");
        var historicalVersion = TechnicalSourcePageStorage.CreateVersionToken("etag-v1");
        var fixture = Fixture.WithContent($$"""
            {"schemaVersion":1,"documentId":"{{DocumentId}}","sourceBlob":"{{SourceBlob}}","sourceETag":"etag-v2","pages":[
              {"pdfPage":72,"displayPage":"70","source":"ExtractedText"}
            ]}
            """);

        var references = await fixture.Resolver.ResolveAsync(
            "Page PDF : 72",
            BlobPrefix,
            [],
            [],
            [],
            [new(DocumentId, 72, "72", TechnicalSourcePageKind.Pdf, historicalVersion)],
            default);

        Assert.AreNotEqual(historicalVersion, currentVersion);
        Assert.IsEmpty(references);
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
    public async Task MultipleDocumentsResolveUniqueDisplayedPageWithoutExtraEvidence()
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

        var reference = references.Single();
        Assert.AreEqual("parts-catalog", reference.DocumentId);
        Assert.AreEqual(9, reference.PdfPage);
        Assert.AreEqual(3, fixture.Reader.OpenCount);
    }

    [TestMethod]
    public async Task MultipleDocumentsResolveUniquePhysicalPageWithoutExtraEvidence()
    {
        var fixture = Fixture.WithDocuments(
            ("manual-a", Fixture.PageMap("manual-a", """{"pdfPage":214,"displayPage":null,"source":"ExtractedText"}""")),
            ("manual-b", Fixture.PageMap("manual-b", """{"pdfPage":670,"displayPage":null,"source":"ExtractedText"}""")));

        var references = await fixture.Resolver.ResolveAsync(
            "Source : Page PDF : 670", BlobPrefix, ["manual-a", "manual-b"], [], [], default);

        var reference = references.Single();
        Assert.AreEqual("manual-b", reference.DocumentId);
        Assert.AreEqual(670, reference.PdfPage);
        Assert.AreEqual(TechnicalSourcePageKind.Pdf, reference.PageKind);
    }

    [TestMethod]
    public async Task FileSearchPhysicalPagesRemainLinkableOnSourceLineWhenModelWritesShortPageLabels()
    {
        var fixture = Fixture.WithDocuments(
            ("catalog", Fixture.PageMap("catalog", """
                {"pdfPage":246,"displayPage":"244","source":"ExtractedText"},
                {"pdfPage":248,"displayPage":"4-78","source":"ExtractedText"}
                """)),
            ("workshop", Fixture.PageMap("workshop", """
                {"pdfPage":243,"displayPage":"241","source":"ExtractedText"},
                {"pdfPage":254,"displayPage":"252","source":"ExtractedText"}
                """)));
        const string text = """
            - Catalogue pièces, Fig. 2105 (p. 248)
            - Manuel d'atelier, p. 243
            - Manuel d'atelier, p. 254
            - Manuel d'utilisation, p. 248
            Source : p. 246 (Fig. 2100), p. 248 (Fig. 2105), p. 243, p. 254, p. 248
            """;

        var references = await fixture.Resolver.ResolveAsync(
            text,
            BlobPrefix,
            ["catalog", "workshop"],
            [
                new("catalog", 246, "Document ID: catalog\nPage: 246"),
                new("catalog", 248, "Document ID: catalog\nPage: 248"),
                new("workshop", 243, "Document ID: workshop\nPage: 243"),
                new("workshop", 254, "Document ID: workshop\nPage: 254")
            ],
            [],
            default);

        var sourceLineStart = text.IndexOf("Source :", StringComparison.Ordinal);

        Assert.HasCount(5, references);
        Assert.IsTrue(references.All(reference => reference.PageKind == TechnicalSourcePageKind.Pdf));
        CollectionAssert.AreEqual(
            new[] { 246, 248, 243, 254, 248 },
            references.Select(reference => reference.PdfPage).ToArray());
        Assert.IsTrue(references.All(reference => reference.StartIndex > sourceLineStart));
        Assert.IsTrue(references.Where(reference => reference.PdfPage is 246 or 248)
            .All(reference => reference.DocumentId == "catalog"));
        Assert.IsTrue(references.Where(reference => reference.PdfPage is 243 or 254)
            .All(reference => reference.DocumentId == "workshop"));
    }

    [TestMethod]
    public async Task PhysicalEvidenceDoesNotRewriteShortPageLabelOutsideDocumentarySourceLine()
    {
        var fixture = Fixture.WithDocuments(
            ("catalog", Fixture.PageMap("catalog", """{"pdfPage":248,"displayPage":"4-78","source":"ExtractedText"}""")));

        var references = await fixture.Resolver.ResolveAsync(
            "Manuel d'utilisation, p. 248",
            BlobPrefix,
            ["catalog"],
            [new("catalog", 248, "Document ID: catalog\nPage: 248")],
            [],
            default);

        Assert.IsEmpty(references);
    }

    [TestMethod]
    public async Task PageNumberEvidenceIsTreatedAsPhysicalPage()
    {
        var fixture = Fixture.WithDocuments(
            ("manual", Fixture.PageMap("manual", """{"pdfPage":248,"displayPage":"244","source":"ExtractedText"}""")),
            ("other-manual", Fixture.PageMap("other-manual", """{"pdfPage":248,"displayPage":"240","source":"ExtractedText"}""")));
        var physicalEvidence = new TechnicalDocumentPageEvidence(
            "manual",
            248,
            "Document ID: manual\nPage number: 248");

        var shortLabelReferences = await fixture.Resolver.ResolveAsync(
            "Source : p. 248", BlobPrefix, ["manual", "other-manual"], [physicalEvidence], [], default);
        var explicitPhysicalReferences = await fixture.Resolver.ResolveAsync(
            "Source : Page PDF : 248", BlobPrefix, ["manual", "other-manual"], [physicalEvidence], [], default);

        Assert.AreEqual("manual", shortLabelReferences.Single().DocumentId);
        Assert.AreEqual(TechnicalSourcePageKind.Pdf, shortLabelReferences.Single().PageKind);
        Assert.AreEqual("manual", explicitPhysicalReferences.Single().DocumentId);
        Assert.AreEqual(TechnicalSourcePageKind.Pdf, explicitPhysicalReferences.Single().PageKind);
    }

    [TestMethod]
    public async Task PhysicalSourceEvidenceBeatsUnprovedDisplayedPageInAnotherDocument()
    {
        var fixture = Fixture.WithDocuments(
            ("catalog", Fixture.PageMap("catalog", """{"pdfPage":246,"displayPage":"244","source":"ExtractedText"}""")),
            ("workshop", Fixture.PageMap("workshop", """{"pdfPage":300,"displayPage":"246","source":"ExtractedText"}""")));

        var references = await fixture.Resolver.ResolveAsync(
            "Source : p. 246",
            BlobPrefix,
            ["catalog", "workshop"],
            [new("catalog", 246, "Document ID: catalog\nPage: 246")],
            [],
            default);

        var reference = references.Single();
        Assert.AreEqual("catalog", reference.DocumentId);
        Assert.AreEqual(246, reference.PdfPage);
        Assert.AreEqual(TechnicalSourcePageKind.Pdf, reference.PageKind);
    }

    [TestMethod]
    public async Task PhysicalEvidenceCannotBeReusedAsPrintedPageEvidence()
    {
        var fixture = Fixture.WithDocuments(
            ("catalog", Fixture.PageMap("catalog", """{"pdfPage":246,"displayPage":"244","source":"ExtractedText"}""")),
            ("workshop", Fixture.PageMap("workshop", """{"pdfPage":300,"displayPage":"246","source":"ExtractedText"}""")));

        var references = await fixture.Resolver.ResolveAsync(
            "Source : p. 246",
            BlobPrefix,
            ["catalog", "workshop"],
            [new("workshop", 246, "Document ID: workshop\nPage number: 246")],
            [],
            default);

        Assert.IsEmpty(references);
    }

    [TestMethod]
    public async Task SamePhysicalPageProvedForTwoDocumentsRemainsAmbiguous()
    {
        var fixture = Fixture.WithDocuments(
            ("catalog", Fixture.PageMap("catalog", """{"pdfPage":248,"displayPage":"244","source":"ExtractedText"}""")),
            ("operator", Fixture.PageMap("operator", """{"pdfPage":248,"displayPage":"4-78","source":"ExtractedText"}""")));

        var references = await fixture.Resolver.ResolveAsync(
            "Source : p. 248",
            BlobPrefix,
            ["catalog", "operator"],
            [
                new("catalog", 248, "Document ID: catalog\nPage: 248"),
                new("operator", 248, "Document ID: operator\nPage: 248")
            ],
            [],
            default);

        Assert.IsEmpty(references);
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
    public async Task PhysicalPageListResolvesVerifiedPagesAcrossDocumentsIndependently()
    {
        var fixture = Fixture.WithDocuments(
            ("manual-a", Fixture.PageMap("manual-a", """
                {"pdfPage":214,"displayPage":null,"source":"ExtractedText"},
                {"pdfPage":215,"displayPage":null,"source":"ExtractedText"}
                """)),
            ("manual-b", Fixture.PageMap("manual-b", """
                {"pdfPage":670,"displayPage":null,"source":"ExtractedText"}
                """)));
        const string text = "Source : Page PDF : 214, 215, 670";

        var references = await fixture.Resolver.ResolveAsync(
            text,
            BlobPrefix,
            ["manual-a", "manual-b"],
            [new("manual-a", 214), new("manual-a", 215), new("manual-b", 670)],
            [],
            default);

        CollectionAssert.AreEqual(new[] { 214, 215, 670 }, references.Select(item => item.PdfPage).ToArray());
        CollectionAssert.AreEqual(new[] { "manual-a", "manual-a", "manual-b" }, references.Select(item => item.DocumentId).ToArray());
        Assert.IsTrue(references.All(item => item.PageKind == TechnicalSourcePageKind.Pdf));
    }

    [TestMethod]
    public async Task RepeatedPhysicalPageHeadingsResolveAcrossDocumentsWithoutLettingAmbiguityBlockSafePages()
    {
        var fixture = Fixture.WithDocuments(
            ("manual-a", Fixture.PageMap("manual-a", """
                {"pdfPage":58,"displayPage":null,"source":"ExtractedText"},
                {"pdfPage":59,"displayPage":null,"source":"ExtractedText"}
                """)),
            ("manual-b", Fixture.PageMap("manual-b", """
                {"pdfPage":59,"displayPage":null,"source":"ExtractedText"},
                {"pdfPage":670,"displayPage":null,"source":"ExtractedText"}
                """)));
        const string text = "Source : Page PDF : 58, Page PDF : 59 (Fig. 019 — repère 1A), Page PDF : 670,";

        var references = await fixture.Resolver.ResolveAsync(
            text,
            BlobPrefix,
            ["manual-a", "manual-b"],
            [
                new("manual-a", 58),
                new("manual-a", 59),
                new("manual-b", 59),
                new("manual-b", 670)
            ],
            [],
            default);

        CollectionAssert.AreEqual(new[] { 58, 670 }, references.Select(item => item.PdfPage).ToArray());
        CollectionAssert.AreEqual(new[] { "58", "670" }, references.Select(item => item.Label).ToArray());
        CollectionAssert.AreEqual(new[] { "manual-a", "manual-b" }, references.Select(item => item.DocumentId).ToArray());
    }

    [TestMethod]
    public async Task PhysicalCitationUsesExactVisualOnlyWhenFileSearchHasNoPagePair()
    {
        var fixture = Fixture.WithDocuments(
            ("dx225-operator", Fixture.PageMap("dx225-operator", """{"pdfPage":20,"displayPage":"16","source":"ExtractedText"}""")),
            ("dx225-workshop", Fixture.PageMap("dx225-workshop", """{"pdfPage":20,"displayPage":"18","source":"ExtractedText"}""")),
            ("dx225-parts", Fixture.PageMap("dx225-parts", """{"pdfPage":20,"displayPage":null,"source":"ExtractedText"}""")));

        var visualConfirmed = await fixture.Resolver.ResolveAsync(
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
        var fileSearchConfirmed = await fixture.Resolver.ResolveAsync(
            "Page PDF : p. 20",
            BlobPrefix,
            ["dx225-operator", "dx225-workshop", "dx225-parts"],
            [new("dx225-workshop", 20)],
            [],
            default);

        Assert.AreEqual("dx225-parts", visualConfirmed.Single().DocumentId);
        Assert.IsEmpty(confirmed);
        Assert.AreEqual("dx225-workshop", fileSearchConfirmed.Single().DocumentId);
        Assert.AreEqual(TechnicalSourcePageKind.Pdf, fileSearchConfirmed.Single().PageKind);
    }

    [TestMethod]
    public async Task PhysicalCitationRequiresOneVisualDocumentButAllowsFullAndTileDuplicates()
    {
        var fixture = Fixture.WithDocuments(
            ("manual-a", Fixture.PageMap("manual-a", """{"pdfPage":20,"displayPage":"18","source":"ExtractedText"}""")),
            ("manual-b", Fixture.PageMap("manual-b", """{"pdfPage":20,"displayPage":"19","source":"ExtractedText"}""")));

        var duplicateVisuals = await fixture.Resolver.ResolveAsync(
            "Page PDF : 20",
            BlobPrefix,
            ["manual-a", "manual-b"],
            [],
            [
                new("manual-a", 20, "full", null, "full.png", "manual-a/page-00020/full.png"),
                new("manual-a", 20, "tile", "1", "tile.png", "manual-a/page-00020/tile.png")
            ],
            default);
        var conflictingVisuals = await fixture.Resolver.ResolveAsync(
            "Page PDF : 20",
            BlobPrefix,
            ["manual-a", "manual-b"],
            [],
            [
                new("manual-a", 20, "full", null, "a.png", "manual-a/page-00020/a.png"),
                new("manual-b", 20, "full", null, "b.png", "manual-b/page-00020/b.png")
            ],
            default);
        var wrongPageVisual = await fixture.Resolver.ResolveAsync(
            "Page PDF : 20",
            BlobPrefix,
            ["manual-a", "manual-b"],
            [],
            [new("manual-a", 21, "full", null, "wrong.png", "manual-a/page-00021/wrong.png")],
            default);

        Assert.AreEqual("manual-a", duplicateVisuals.Single().DocumentId);
        Assert.IsEmpty(conflictingVisuals);
        Assert.IsEmpty(wrongPageVisual);
    }

    [TestMethod]
    public async Task ProductionTrackPlanSourcesResolveAcrossOverlappingPhysicalPageMaps()
    {
        var fixture = Fixture.WithDocuments(
            ("dx225-parts", Fixture.PageMap("dx225-parts", """
                {"pdfPage":234,"displayPage":null,"source":"Unknown"},
                {"pdfPage":243,"displayPage":"2","source":"ExtractedText"},
                {"pdfPage":246,"displayPage":null,"source":"Unknown"},
                {"pdfPage":249,"displayPage":null,"source":"Unknown"},
                {"pdfPage":258,"displayPage":null,"source":"Unknown"},
                {"pdfPage":264,"displayPage":null,"source":"Unknown"}
                """)),
            ("dx225-workshop", Fixture.PageMap("dx225-workshop", """
                {"pdfPage":234,"displayPage":"8","source":"ExtractedText"},
                {"pdfPage":243,"displayPage":"17","source":"ExtractedText"},
                {"pdfPage":246,"displayPage":"20","source":"ExtractedText"},
                {"pdfPage":249,"displayPage":"23","source":"ExtractedText"},
                {"pdfPage":258,"displayPage":"32","source":"ExtractedText"},
                {"pdfPage":264,"displayPage":"2","source":"ExtractedText"}
                """)));
        const string text = "Source : Page PDF : 246, Page PDF : 258, Page PDF : 264, Page PDF : 234, Page PDF : 243";

        var references = await fixture.Resolver.ResolveAsync(
            text,
            BlobPrefix,
            ["dx225-parts", "dx225-workshop"],
            [
                new("dx225-parts", 249),
                new("dx225-parts", 258),
                new("dx225-parts", 264),
                new("dx225-workshop", 234),
                new("dx225-workshop", 243)
            ],
            [new("dx225-parts", 246, "full", null, "page.png", "dx225-parts/page-00246/page.png")],
            default);

        Assert.HasCount(5, references);
        CollectionAssert.AreEqual(
            new[] { 246, 258, 264, 234, 243 },
            references.Select(reference => reference.PdfPage).ToArray());
        CollectionAssert.AreEqual(
            new[] { "dx225-parts", "dx225-parts", "dx225-parts", "dx225-workshop", "dx225-workshop" },
            references.Select(reference => reference.DocumentId).ToArray());
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
    public async Task RepeatedPhysicalCitationUsesVerifiedHistoryWhenCurrentSearchHasNoDocumentId()
    {
        var fixture = Fixture.WithDocuments(
            ("dx225-parts", Fixture.PageMap("dx225-parts", """{"pdfPage":669,"displayPage":null,"source":"ExtractedText"}""")));
        const string text = "ðŸ“„ Source : Page PDF : 669";

        var references = await fixture.Resolver.ResolveAsync(
            text,
            BlobPrefix,
            [],
            [],
            [],
            [new("dx225-parts", 669, "669", TechnicalSourcePageKind.Pdf)],
            default);

        var reference = references.Single();
        Assert.AreEqual("dx225-parts", reference.DocumentId);
        Assert.AreEqual(669, reference.PdfPage);
        Assert.AreEqual(TechnicalSourcePageKind.Pdf, reference.PageKind);
        Assert.AreEqual(text.IndexOf("669", StringComparison.Ordinal), reference.StartIndex);
    }

    [TestMethod]
    public async Task CurrentAndHistoricalPhysicalContradictionCreatesNoReference()
    {
        var fixture = Fixture.WithDocuments(
            ("current-manual", Fixture.PageMap("current-manual", """{"pdfPage":669,"displayPage":null,"source":"ExtractedText"}""")),
            ("historical-manual", Fixture.PageMap("historical-manual", """{"pdfPage":669,"displayPage":null,"source":"ExtractedText"}""")));

        var references = await fixture.Resolver.ResolveAsync(
            "Source : Page PDF : 669",
            BlobPrefix,
            ["current-manual"],
            [new("current-manual", 669)],
            [],
            [new("historical-manual", 669, "669", TechnicalSourcePageKind.Pdf)],
            default);

        Assert.IsEmpty(references);
    }

    [TestMethod]
    public async Task SameHistoricalPhysicalPageAcrossDocumentsRemainsAmbiguous()
    {
        var fixture = Fixture.WithDocuments(
            ("manual-a", Fixture.PageMap("manual-a", """{"pdfPage":669,"displayPage":null,"source":"ExtractedText"}""")),
            ("manual-b", Fixture.PageMap("manual-b", """{"pdfPage":669,"displayPage":null,"source":"ExtractedText"}""")));

        var references = await fixture.Resolver.ResolveAsync(
            "Source : Page PDF : 669",
            BlobPrefix,
            [],
            [],
            [],
            [
                new("manual-a", 669, "669", TechnicalSourcePageKind.Pdf),
                new("manual-b", 669, "669", TechnicalSourcePageKind.Pdf)
            ],
            default);

        Assert.IsEmpty(references);
    }

    [TestMethod]
    public async Task HistoricalDisplayPageRevalidatesExactPhysicalMapping()
    {
        var fixture = Fixture.WithDocuments(
            ("manual", Fixture.PageMap("manual", """{"pdfPage":72,"displayPage":"70","source":"ExtractedText"}""")));

        var references = await fixture.Resolver.ResolveAsync(
            "Source : p. 70",
            BlobPrefix,
            [],
            [],
            [],
            [new("manual", 72, "70", TechnicalSourcePageKind.Display)],
            default);

        Assert.AreEqual(72, references.Single().PdfPage);
        Assert.AreEqual(TechnicalSourcePageKind.Display, references.Single().PageKind);
    }

    [TestMethod]
    public async Task RemovedOrRemappedHistoricalDocumentCreatesNoReference()
    {
        var removed = Fixture.WithDocuments(("manual", null));
        var remapped = Fixture.WithDocuments(
            ("manual", Fixture.PageMap("manual", """{"pdfPage":73,"displayPage":"70","source":"ExtractedText"}""")));
        HistoricalTechnicalSourceEvidence[] history =
            [new("manual", 72, "70", TechnicalSourcePageKind.Display)];

        var removedReferences = await removed.Resolver.ResolveAsync(
            "Source : p. 70", BlobPrefix, [], [], [], history, default);
        var remappedReferences = await remapped.Resolver.ResolveAsync(
            "Source : p. 70", BlobPrefix, [], [], [], history, default);

        Assert.IsEmpty(removedReferences);
        Assert.IsEmpty(remappedReferences);
    }

    [TestMethod]
    public async Task HistoricalDisplayPageDoesNotProveNewCitationAnchor()
    {
        var fixture = Fixture.WithDocuments(
            ("manual", Fixture.PageMap("manual", """{"pdfPage":72,"displayPage":"70","source":"ExtractedText"}""")));

        var references = await fixture.Resolver.ResolveAsync(
            "SP000913 p. 70",
            BlobPrefix,
            [],
            [],
            [],
            [new("manual", 72, "70", TechnicalSourcePageKind.Display)],
            default);

        Assert.IsEmpty(references);
    }

    [TestMethod]
    public async Task PrintedAndPhysicalHistoricalEvidenceAreNotInterchangeable()
    {
        var fixture = Fixture.WithDocuments(
            ("manual", Fixture.PageMap("manual", """{"pdfPage":70,"displayPage":"68","source":"ExtractedText"}""")));

        var physicalFromPrinted = await fixture.Resolver.ResolveAsync(
            "Page PDF : 70",
            BlobPrefix,
            [],
            [],
            [],
            [new("manual", 70, "68", TechnicalSourcePageKind.Display)],
            default);
        var printedFromPhysical = await fixture.Resolver.ResolveAsync(
            "Source : p. 68",
            BlobPrefix,
            [],
            [],
            [],
            [new("manual", 70, "70", TechnicalSourcePageKind.Pdf)],
            default);

        Assert.IsEmpty(physicalFromPrinted);
        Assert.IsEmpty(printedFromPhysical);
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

        public Task<string?> GetPdfVersionAsync(string blobName, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(null);
    }

}
