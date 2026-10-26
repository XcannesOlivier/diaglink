using System.Globalization;
using System.Text.RegularExpressions;
using WebApp.Api.Models;

namespace WebApp.Api.Services;

/// <summary>Locates displayed or explicitly physical page labels and resolves only unambiguous document mappings.</summary>
public sealed partial class TechnicalSourceReferenceResolver(TechnicalPageMapResolver pageMapResolver)
{
    private const int MaxExpandedPageRange = 1000;

    public Task<IReadOnlyList<TechnicalSourceReference>> ResolveAsync(
        string? assistantText,
        string blobPrefix,
        IReadOnlyCollection<string> allowedDocumentIds,
        CancellationToken cancellationToken) =>
        ResolveAsync(assistantText, blobPrefix, allowedDocumentIds, [], [], [], cancellationToken);

    public Task<IReadOnlyList<TechnicalSourceReference>> ResolveAsync(
        string? assistantText,
        string blobPrefix,
        IReadOnlyCollection<string> allowedDocumentIds,
        IReadOnlyCollection<TechnicalDocumentPageEvidence> fileSearchPageEvidence,
        IReadOnlyCollection<TechnicalVisualReference> visuals,
        CancellationToken cancellationToken) =>
        ResolveAsync(
            assistantText,
            blobPrefix,
            allowedDocumentIds,
            fileSearchPageEvidence,
            visuals,
            [],
            cancellationToken);

    public async Task<IReadOnlyList<TechnicalSourceReference>> ResolveAsync(
        string? assistantText,
        string blobPrefix,
        IReadOnlyCollection<string> allowedDocumentIds,
        IReadOnlyCollection<TechnicalDocumentPageEvidence> fileSearchPageEvidence,
        IReadOnlyCollection<TechnicalVisualReference> visuals,
        IReadOnlyCollection<HistoricalTechnicalSourceEvidence> historicalSourceEvidence,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(assistantText))
        {
            return [];
        }

        var mentions = FindMentions(assistantText);
        if (mentions.Count == 0)
        {
            return [];
        }

        // Visuals are intentionally not documentary provenance: an image may have been
        // consulted for another part of the answer. Only File Search results establish
        // document/page evidence for citations.
        _ = visuals;
        var currentDocumentIds = allowedDocumentIds
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var documentIds = currentDocumentIds
            .Concat(historicalSourceEvidence.Select(source => source.DocumentId))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (documentIds.Length == 0)
        {
            return [];
        }

        var requestedDisplayPages = mentions
            .Where(mention => mention.PageKind == TechnicalSourcePageKind.Display)
            .SelectMany(GetDisplayPagesForLookup)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var pageMaps = new Dictionary<string, TechnicalPageMapResolution>(StringComparer.Ordinal);
        foreach (var documentId in documentIds)
        {
            var pageMap = await pageMapResolver.ResolveAsync(
                blobPrefix,
                documentId,
                requestedDisplayPages,
                cancellationToken);
            if (pageMap is not null)
            {
                pageMaps.Add(documentId, pageMap);
            }
        }

        if (pageMaps.Count == 0)
        {
            return [];
        }

        mentions = ExpandRangeMentions(mentions, pageMaps);

        var currentPageMaps = pageMaps
            .Where(item => currentDocumentIds.Contains(item.Key, StringComparer.Ordinal))
            .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        var evidence = fileSearchPageEvidence
            .Where(item => currentPageMaps.ContainsKey(item.DocumentId))
            .ToArray();
        var resolutions = new Dictionary<PageMention, ResolvedMention>();
        var isSingleDocument = currentDocumentIds.Length == 1;

        ResolvePhysicalGroups(mentions, currentPageMaps, evidence, isSingleDocument, resolutions);
        ResolveDisplayMentions(mentions, currentPageMaps, evidence, isSingleDocument, resolutions);
        ResolveFromHistoricalEvidence(
            mentions,
            pageMaps,
            currentPageMaps,
            historicalSourceEvidence,
            resolutions);

        var references = new List<TechnicalSourceReference>();
        foreach (var mention in mentions)
        {
            if (!resolutions.TryGetValue(mention, out var resolution))
            {
                continue;
            }

            references.Add(new(
                resolution.DocumentId,
                resolution.PdfPage,
                mention.Page,
                mention.Label,
                mention.StartIndex,
                mention.EndIndex,
                references.Count)
            {
                PageKind = mention.PageKind
            });
        }

        return references;
    }

    private static void ResolveFromHistoricalEvidence(
        IReadOnlyList<PageMention> mentions,
        IReadOnlyDictionary<string, TechnicalPageMapResolution> pageMaps,
        IReadOnlyDictionary<string, TechnicalPageMapResolution> currentPageMaps,
        IReadOnlyCollection<HistoricalTechnicalSourceEvidence> historicalSourceEvidence,
        IDictionary<PageMention, ResolvedMention> resolutions)
    {
        var verifiedHistory = historicalSourceEvidence
            .Where(source => HistoricalEvidenceMatchesCurrentPageMap(source, pageMaps))
            .Distinct()
            .ToArray();

        foreach (var group in mentions
            .Where(mention => mention.PageKind == TechnicalSourcePageKind.Pdf)
            .GroupBy(mention => mention.GroupStartIndex))
        {
            var pages = group
                .Select(mention => int.TryParse(mention.Page, out var page) ? page : 0)
                .Where(page => page > 0)
                .Distinct()
                .ToArray();
            var historicalDocuments = verifiedHistory
                .Where(source => source.PageKind == TechnicalSourcePageKind.Pdf)
                .GroupBy(source => source.DocumentId, StringComparer.Ordinal)
                .Where(document => pages.All(page => document.Any(source => source.PdfPage == page)))
                .Select(document => document.Key)
                .ToHashSet(StringComparer.Ordinal);
            if (historicalDocuments.Count == 0)
            {
                continue;
            }

            var currentResolutions = group
                .Where(resolutions.ContainsKey)
                .Select(mention => resolutions[mention].DocumentId)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var candidateDocuments = currentResolutions.Length > 0
                ? currentResolutions.Concat(historicalDocuments).ToHashSet(StringComparer.Ordinal)
                : currentPageMaps
                    .Where(item => pages.All(item.Value.PdfPages.Contains))
                    .Select(item => item.Key)
                    .Concat(historicalDocuments)
                    .ToHashSet(StringComparer.Ordinal);

            if (candidateDocuments.Count != 1)
            {
                foreach (var mention in group)
                {
                    resolutions.Remove(mention);
                }
                continue;
            }

            if (currentResolutions.Length == 0)
            {
                var documentId = candidateDocuments.Single();
                foreach (var mention in group)
                {
                    resolutions[mention] = new(
                        documentId,
                        int.Parse(mention.Page, CultureInfo.InvariantCulture));
                }
            }
        }

        foreach (var mention in mentions.Where(mention => mention.PageKind == TechnicalSourcePageKind.Display))
        {
            // Historical rows do not retain the File Search text that proved an anchor such as a
            // figure or fault code. Do not infer that relationship from old assistant prose.
            if (mention.CitationAnchor is not null)
            {
                continue;
            }

            var historicalCandidates = verifiedHistory
                .Where(source =>
                    source.PageKind == TechnicalSourcePageKind.Display &&
                    string.Equals(source.DisplayPage, mention.Page, StringComparison.Ordinal))
                .Select(source => new ResolvedMention(source.DocumentId, source.PdfPage))
                .Distinct()
                .ToArray();
            if (historicalCandidates.Length == 0)
            {
                continue;
            }

            var candidates = resolutions.TryGetValue(mention, out var currentResolution)
                ? historicalCandidates.Append(currentResolution).Distinct().ToArray()
                : currentPageMaps
                    .Where(item => item.Value.PdfPagesByDisplayPage.ContainsKey(mention.Page))
                    .SelectMany(item => item.Value.PdfPagesByDisplayPage[mention.Page]
                        .Select(pdfPage => new ResolvedMention(item.Key, pdfPage)))
                    .Concat(historicalCandidates)
                    .Distinct()
                    .ToArray();

            if (candidates.Length == 1)
            {
                resolutions[mention] = candidates[0];
            }
            else
            {
                resolutions.Remove(mention);
            }
        }
    }

    private static bool HistoricalEvidenceMatchesCurrentPageMap(
        HistoricalTechnicalSourceEvidence source,
        IReadOnlyDictionary<string, TechnicalPageMapResolution> pageMaps)
    {
        if (!pageMaps.TryGetValue(source.DocumentId, out var pageMap))
        {
            return false;
        }

        return source.PageKind == TechnicalSourcePageKind.Pdf
            ? pageMap.PdfPages.Contains(source.PdfPage)
            : pageMap.PdfPagesByDisplayPage.TryGetValue(source.DisplayPage, out var pdfPages) &&
                pdfPages.Contains(source.PdfPage);
    }

    private static void ResolvePhysicalGroups(
        IReadOnlyList<PageMention> mentions,
        IReadOnlyDictionary<string, TechnicalPageMapResolution> pageMaps,
        IReadOnlyCollection<TechnicalDocumentPageEvidence> evidence,
        bool isSingleDocument,
        IDictionary<PageMention, ResolvedMention> resolutions)
    {
        foreach (var group in mentions
            .Where(mention => mention.PageKind == TechnicalSourcePageKind.Pdf)
            .GroupBy(mention => mention.GroupStartIndex))
        {
            var pages = group
                .Select(mention => int.TryParse(mention.Page, out var page) ? page : 0)
                .Where(page => page > 0)
                .Distinct()
                .ToArray();
            if (pages.Length == 0)
            {
                continue;
            }

            var evidencedDocuments = pageMaps
                .Where(item => pages.All(page =>
                    item.Value.PdfPages.Contains(page) &&
                    evidence.Any(proof =>
                        string.Equals(proof.DocumentId, item.Key, StringComparison.Ordinal) &&
                        proof.PdfPage == page)))
                .Select(item => item.Key)
                .ToArray();

            string? documentId = evidencedDocuments.Length == 1
                ? evidencedDocuments[0]
                : evidencedDocuments.Length == 0 && isSingleDocument && pageMaps.Count == 1
                    ? pageMaps.Keys.Single()
                    : null;
            if (documentId is null || pages.Any(page => !pageMaps[documentId].PdfPages.Contains(page)))
            {
                continue;
            }

            foreach (var mention in group)
            {
                resolutions.Add(mention, new(documentId, int.Parse(mention.Page)));
            }
        }
    }

    private static void ResolveDisplayMentions(
        IEnumerable<PageMention> mentions,
        IReadOnlyDictionary<string, TechnicalPageMapResolution> pageMaps,
        IReadOnlyCollection<TechnicalDocumentPageEvidence> evidence,
        bool isSingleDocument,
        IDictionary<PageMention, ResolvedMention> resolutions)
    {
        foreach (var mention in mentions.Where(mention => mention.PageKind == TechnicalSourcePageKind.Display))
        {
            var candidates = pageMaps
                .Where(item => item.Value.PdfPagesByDisplayPage.ContainsKey(mention.Page))
                .SelectMany(item => item.Value.PdfPagesByDisplayPage[mention.Page]
                    .Select(pdfPage => new ResolvedMention(item.Key, pdfPage)))
                .ToArray();
            var evidencedCandidates = candidates
                .Where(candidate =>
                    evidence.Any(proof =>
                        string.Equals(proof.DocumentId, candidate.DocumentId, StringComparison.Ordinal) &&
                        proof.PdfPage == candidate.PdfPage) &&
                    (mention.CitationAnchor is null || evidence.Any(proof =>
                        string.Equals(proof.DocumentId, candidate.DocumentId, StringComparison.Ordinal) &&
                        EvidenceContainsCitationAnchor(proof, mention.CitationAnchor))))
                .ToArray();

            var resolution = evidencedCandidates.Length == 1
                ? evidencedCandidates[0]
                : evidencedCandidates.Length == 0 && isSingleDocument && candidates.Length == 1
                    ? candidates[0]
                    : null;
            if (resolution is not null)
            {
                resolutions.Add(mention, resolution);
            }
        }
    }

    internal static IReadOnlyList<PageMention> FindMentions(string text)
    {
        var mentions = ExplicitPagePattern().Matches(text)
            .Select(match => CreateMention(
                text,
                match.Groups["page"].Value,
                match.Groups["label"].Value,
                match.Groups["label"].Index,
                match.Groups["label"].Index + match.Groups["label"].Length))
            .ToList();

        foreach (Match pluralMatch in PluralPagesPattern().Matches(text))
        {
            var listGroup = pluralMatch.Groups["list"];
            foreach (Match pageMatch in PageNumberPattern().Matches(listGroup.Value))
            {
                var startIndex = listGroup.Index + pageMatch.Index;
                mentions.Add(CreateMention(text, pageMatch.Value, pageMatch.Value, startIndex, startIndex + pageMatch.Length));
            }
        }

        foreach (Match physicalMatch in PhysicalPagePattern().Matches(text))
        {
            var pageGroup = physicalMatch.Groups["page"];
            var pageStart = pageGroup.Index;
            var pageEnd = pageStart + pageGroup.Length;
            if (mentions.Any(mention =>
                    mention.StartIndex < pageEnd &&
                    pageStart < mention.EndIndex))
            {
                continue;
            }

            mentions.Add(CreateMention(
                text,
                pageGroup.Value,
                pageGroup.Value,
                pageStart,
                pageEnd));
        }

        foreach (Match continuationMatch in ContinuationPagePattern().Matches(text))
        {
            var pageGroup = continuationMatch.Groups["page"];
            var pageStart = pageGroup.Index;
            var pageEnd = pageStart + pageGroup.Length;
            var previous = mentions
                .Where(mention => mention.EndIndex <= continuationMatch.Index)
                .OrderByDescending(mention => mention.EndIndex)
                .FirstOrDefault();
            if (previous is null ||
                !CitationContinuationPattern().IsMatch(text[previous.EndIndex..pageStart]) ||
                mentions.Any(mention =>
                    mention.StartIndex <= pageStart &&
                    pageEnd <= mention.EndIndex))
            {
                continue;
            }

            mentions.Add(CreateMention(
                text,
                pageGroup.Value,
                pageGroup.Value,
                pageStart,
                pageEnd));
        }

        var orderedMentions = mentions
            .OrderBy(mention => mention.StartIndex)
            .ThenBy(mention => mention.EndIndex)
            .ToArray();
        for (var index = 1; index < orderedMentions.Length; index++)
        {
            var previous = orderedMentions[index - 1];
            var current = orderedMentions[index];
            if (current.CitationAnchor is null &&
                previous.CitationAnchor is not null &&
                current.PageKind == previous.PageKind &&
                CitationContinuationPattern().IsMatch(text[previous.EndIndex..current.StartIndex]))
            {
                orderedMentions[index] = current with { CitationAnchor = previous.CitationAnchor };
            }
        }

        return orderedMentions;
    }

    private static IReadOnlyList<PageMention> ExpandRangeMentions(
        IReadOnlyList<PageMention> mentions,
        IReadOnlyDictionary<string, TechnicalPageMapResolution> pageMaps)
    {
        var expanded = new List<PageMention>();
        foreach (var mention in mentions)
        {
            if (!TryParseRange(mention.Page, out var firstPage, out var lastPage) ||
                firstPage >= lastPage ||
                lastPage - firstPage >= MaxExpandedPageRange ||
                (mention.PageKind == TechnicalSourcePageKind.Display &&
                 pageMaps.Values.Any(map => map.PdfPagesByDisplayPage.ContainsKey(mention.Page))))
            {
                expanded.Add(mention);
                continue;
            }

            var separatorIndex = mention.StartIndex + mention.Label.LastIndexOf('-');
            for (var page = firstPage; page <= lastPage; page++)
            {
                var pageText = page.ToString(CultureInfo.InvariantCulture);
                var isFirst = page == firstPage;
                var startIndex = isFirst ? mention.StartIndex : separatorIndex + 1;
                var endIndex = isFirst ? separatorIndex : mention.EndIndex;
                var label = isFirst
                    ? mention.Label[..(separatorIndex - mention.StartIndex)]
                    : mention.Label[(separatorIndex + 1 - mention.StartIndex)..];
                expanded.Add(mention with
                {
                    Page = pageText,
                    Label = label,
                    StartIndex = startIndex,
                    EndIndex = endIndex
                });

                if (page == lastPage)
                {
                    break;
                }
            }
        }

        return expanded;
    }

    private static IEnumerable<string> GetDisplayPagesForLookup(PageMention mention)
    {
        yield return mention.Page;
        if (TryParseRange(mention.Page, out var firstPage, out var lastPage) &&
            firstPage < lastPage &&
            lastPage - firstPage < MaxExpandedPageRange)
        {
            for (var page = firstPage; page <= lastPage; page++)
            {
                yield return page.ToString(CultureInfo.InvariantCulture);
                if (page == lastPage)
                {
                    yield break;
                }
            }
        }
    }

    private static bool TryParseRange(string page, out int firstPage, out int lastPage)
    {
        firstPage = 0;
        lastPage = 0;
        var parts = page.Split('-', 2);
        return parts.Length == 2 &&
            int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out firstPage) &&
            int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out lastPage) &&
            firstPage > 0 &&
            lastPage > 0;
    }

    private static PageMention CreateMention(string text, string page, string label, int startIndex, int endIndex)
    {
        var lineStart = text.LastIndexOf('\n', Math.Max(0, startIndex - 1));
        lineStart = lineStart < 0 ? 0 : lineStart + 1;
        var prefix = text[lineStart..startIndex];
        var isPhysical = PdfPageHeadingPattern().IsMatch(prefix);
        var anchorMatch = CitationAnchorAtEndPattern().Match(prefix);
        var citationAnchor = anchorMatch.Success &&
            anchorMatch.Groups["anchor"].Value.Any(char.IsLetter) &&
            anchorMatch.Groups["anchor"].Value.Any(char.IsDigit)
                ? anchorMatch.Groups["anchor"].Value
                : null;
        return new(
            page,
            label,
            startIndex,
            endIndex,
            isPhysical ? TechnicalSourcePageKind.Pdf : TechnicalSourcePageKind.Display,
            isPhysical ? lineStart : startIndex,
            citationAnchor);
    }

    private static bool EvidenceContainsCitationAnchor(
        TechnicalDocumentPageEvidence evidence,
        string citationAnchor)
    {
        return !string.IsNullOrEmpty(evidence.ResultText) && Regex.IsMatch(
            evidence.ResultText,
            $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(citationAnchor)}(?![\p{{L}}\p{{N}}])",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    }

    [GeneratedRegex(
        @"(?<![\p{L}\p{N}])(?<label>(?:p\.\s*|page\s+)(?<page>[0-9]+(?:-[0-9]+)?))(?![0-9])",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex ExplicitPagePattern();

    [GeneratedRegex(
        @"(?<![\p{L}\p{N}])pages\s+(?<list>[0-9]+(?:-[0-9]+)?(?:\s*(?:(?:,|;)\s*|(?:à|et|and|&)\s+)[0-9]+(?:-[0-9]+)?)+)",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex PluralPagesPattern();

    [GeneratedRegex(@"[0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex PageNumberPattern();

    [GeneratedRegex(@"\bpages?\s+PDF\s*:\s*", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex PdfPageHeadingPattern();

    [GeneratedRegex(
        @"\bpages?\s+PDF\s*:\s*(?:p\.\s*)?(?<page>[0-9]+(?:-[0-9]+)?)(?![0-9])",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex PhysicalPagePattern();

    [GeneratedRegex(
        @"(?:,|;|et|and|&|à)\s+(?:p\.\s*)?(?<page>[0-9]+(?:-[0-9]+)?)(?![0-9])",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex ContinuationPagePattern();

    [GeneratedRegex(@"(?<anchor>[A-Z0-9][A-Z0-9._/-]{3,63})\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex CitationAnchorAtEndPattern();

    [GeneratedRegex(@"\A[ \t]*(?:(?:,|et\b|and\b|&)[ \t]*)+\z", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex CitationContinuationPattern();

    internal sealed record PageMention(
        string Page,
        string Label,
        int StartIndex,
        int EndIndex,
        TechnicalSourcePageKind PageKind,
        int GroupStartIndex,
        string? CitationAnchor);

    private sealed record ResolvedMention(string DocumentId, int PdfPage);
}
