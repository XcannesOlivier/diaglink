using System.Text.RegularExpressions;
using WebApp.Api.Models;

namespace WebApp.Api.Services;

/// <summary>Locates explicit displayed page labels and resolves them through an authorized page map.</summary>
public sealed partial class TechnicalSourceReferenceResolver(TechnicalPageMapResolver pageMapResolver)
{
    public async Task<IReadOnlyList<TechnicalSourceReference>> ResolveAsync(
        string? assistantText,
        string blobPrefix,
        IReadOnlyCollection<string> allowedDocumentIds,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(assistantText))
        {
            return [];
        }

        var documentIds = allowedDocumentIds.Distinct(StringComparer.Ordinal).ToArray();
        if (documentIds.Length != 1)
        {
            return [];
        }

        var mentions = FindMentions(assistantText);
        if (mentions.Count == 0)
        {
            return [];
        }

        var pageMap = await pageMapResolver.ResolveAsync(
            blobPrefix,
            documentIds[0],
            mentions.Select(mention => mention.DisplayPage).Distinct(StringComparer.Ordinal).ToArray(),
            cancellationToken);
        if (pageMap is null)
        {
            return [];
        }

        var references = new List<TechnicalSourceReference>();
        foreach (var mention in mentions)
        {
            if (!pageMap.PdfPagesByDisplayPage.TryGetValue(mention.DisplayPage, out var pdfPage))
            {
                continue;
            }

            references.Add(new(
                documentIds[0],
                pdfPage,
                mention.DisplayPage,
                mention.Label,
                mention.StartIndex,
                mention.EndIndex,
                references.Count));
        }

        return references;
    }

    internal static IReadOnlyList<PageMention> FindMentions(string text)
    {
        var mentions = ExplicitPagePattern().Matches(text)
            .Select(match => new PageMention(
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
                mentions.Add(new(pageMatch.Value, pageMatch.Value, startIndex, startIndex + pageMatch.Length));
            }
        }

        return mentions
            .OrderBy(mention => mention.StartIndex)
            .ThenBy(mention => mention.EndIndex)
            .ToArray();
    }

    [GeneratedRegex(
        @"(?<![\p{L}\p{N}])(?<label>(?:p\.\s*|page\s+)(?<page>[0-9]+))(?![0-9])",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex ExplicitPagePattern();

    [GeneratedRegex(
        @"(?<![\p{L}\p{N}])pages\s+(?<list>[0-9]+(?:\s*(?:(?:,|;)\s*|(?:et|and|&)\s+)[0-9]+)+)",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex PluralPagesPattern();

    [GeneratedRegex(@"[0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex PageNumberPattern();

    internal sealed record PageMention(string DisplayPage, string Label, int StartIndex, int EndIndex);
}