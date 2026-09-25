using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebApp.Api.Services;

namespace WebApp.Api.Tests;

[TestClass]
public sealed class AdditionalDocumentsPricingTests
{
    [TestMethod]
    [DataRow(1L, 50L)]
    [DataRow(2L, 54L)]
    [DataRow(3L, 81L)]
    [DataRow(10L, 270L)]
    [DataRow(100L, 2700L)]
    [DataRow(370L, 9990L)]
    [DataRow(400L, 10800L)]
    [DataRow(1000L, 27000L)]
    public void CalculatesExactIntegerCentsPerPage(long pages, long expectedCents)
    {
        Assert.AreEqual(expectedCents, AdditionalDocumentsPricing.CalculateAmountCents(pages));
    }

    [TestMethod]
    [DataRow(0L)]
    [DataRow(-1L)]
    public void RejectsNonPositivePageCount(long pages)
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            AdditionalDocumentsPricing.CalculateAmountCents(pages));
    }

    [TestMethod]
    public void RejectsOverflowInAControlledWay()
    {
        Assert.ThrowsExactly<OverflowException>(() =>
            AdditionalDocumentsPricing.CalculateAmountCents(long.MaxValue));
    }
}
