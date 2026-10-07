using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebApp.Api.Models;
using WebApp.Api.Services;

namespace WebApp.Api.Tests;

[TestClass]
public class TechnicalVisualTransportTests
{
    private static readonly TechnicalVisualReference First = new(
        "manual", 71, "full", null, "first.png", "manual/page-00071/first.png");
    private static readonly TechnicalVisualReference Second = new(
        "manual", 71, "tile", "r02-c01", "second.png", "manual/page-00071/second.png");

    [TestMethod]
    public void VisualChunk_PreservesReferencesAndOrder()
    {
        var chunk = StreamChunk.WithVisuals([First, Second]);

        CollectionAssert.AreEqual(new[] { First, Second }, chunk.Visuals!);
        Assert.IsTrue(chunk.HasVisuals);
    }

    [TestMethod]
    public void MessageAccumulator_DuplicateAssetKeyUsesFirstOccurrence()
    {
        var replacement = First with { DocumentId = "other", Name = "replacement.png" };
        var accumulator = new TechnicalVisualAccumulator();
        accumulator.AddRange([First, replacement]);
        Assert.HasCount(1, accumulator.Items);
        Assert.AreEqual(First, accumulator.Items[0]);
    }

    [TestMethod]
    public void MessageAccumulator_DifferentAssetKeysRemainDistinctAndOrdered()
    {
        var accumulator = new TechnicalVisualAccumulator();
        accumulator.AddRange([Second, First]);
        CollectionAssert.AreEqual(new[] { Second, First }, accumulator.Items.ToArray());
    }

    [TestMethod]
    public void MessageAccumulator_UsesOrdinalCaseSensitiveAssetKeys()
    {
        var caseVariant = First with { AssetKey = First.AssetKey.ToUpperInvariant() };
        var accumulator = new TechnicalVisualAccumulator();
        accumulator.AddRange([First, caseVariant]);
        CollectionAssert.AreEqual(new[] { First, caseVariant }, accumulator.Items.ToArray());
    }

    [TestMethod]
    public void TextChunkBehaviorIsUnchanged()
    {
        var chunk = StreamChunk.Text("texte");
        Assert.AreEqual("texte", chunk.TextDelta);
        Assert.IsTrue(chunk.IsText);
        Assert.IsFalse(chunk.HasVisuals);
    }
}
