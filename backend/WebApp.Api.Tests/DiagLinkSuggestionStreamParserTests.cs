using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebApp.Api.Services;

namespace WebApp.Api.Tests;

[TestClass]
public sealed class DiagLinkSuggestionStreamParserTests
{
    [TestMethod]
    public void OrdinaryQuestionMarkStreamsAsNormalText()
    {
        var parser = new DiagLinkSuggestionStreamParser();

        var visible = string.Concat(parser.Push("Quelle tension obtiens-tu ?")) +
            string.Concat(parser.Complete());

        Assert.AreEqual("Quelle tension obtiens-tu ?", visible);
        Assert.IsEmpty(parser.Suggestions);
    }

    [TestMethod]
    public void SplitTagNeverLeaksAndTextAfterTagContinues()
    {
        var parser = new DiagLinkSuggestionStreamParser();
        var visible = new List<string>();

        visible.AddRange(parser.Push("Avant<diag"));
        visible.AddRange(parser.Push("link_suggestion>  Où   est\nle relais ? "));
        visible.AddRange(parser.Push("</diaglink_suggestion>Après"));
        visible.AddRange(parser.Complete());

        Assert.AreEqual("AvantAprès", string.Concat(visible));
        CollectionAssert.AreEqual(new[] { "Où est le relais ?" }, parser.Suggestions.ToArray());
    }

    [TestMethod]
    public void ValidationDeduplicatesCaseInsensitivelyAndCapsAtTwo()
    {
        var parser = new DiagLinkSuggestionStreamParser();

        parser.Push("<diaglink_suggestion>Première</diaglink_suggestion>" +
            "<diaglink_suggestion>première</diaglink_suggestion>" +
            "<diaglink_suggestion>Deuxième</diaglink_suggestion>" +
            "<diaglink_suggestion>Troisième</diaglink_suggestion>");
        parser.Complete();

        CollectionAssert.AreEqual(new[] { "Première", "Deuxième" }, parser.Suggestions.ToArray());
    }

    [TestMethod]
    public void EmptyTooLongUnclosedAndNestedBlocksCreateNoSuggestions()
    {
        var parser = new DiagLinkSuggestionStreamParser();
        var visible = new List<string>();

        visible.AddRange(parser.Push("<diaglink_suggestion> </diaglink_suggestion>"));
        visible.AddRange(parser.Push($"<diaglink_suggestion>{new string('x', 241)}</diaglink_suggestion>"));
        visible.AddRange(parser.Push("<diaglink_suggestion>extérieur<diaglink_suggestion>intérieur</diaglink_suggestion>"));
        visible.AddRange(parser.Push("<diaglink_suggestion>non fermé"));
        visible.AddRange(parser.Complete());

        Assert.IsEmpty(parser.Suggestions);
        Assert.IsFalse(string.Concat(visible).Contains("diaglink_suggestion", StringComparison.Ordinal));
        StringAssert.Contains(string.Concat(visible), "extérieurintérieur");
        StringAssert.Contains(string.Concat(visible), "non fermé");
    }
}
