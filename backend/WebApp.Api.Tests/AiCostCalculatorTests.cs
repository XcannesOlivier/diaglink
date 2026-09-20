using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebApp.Api.Data;
using WebApp.Api.Models;
using WebApp.Api.Models.Entities;
using WebApp.Api.Services;

namespace WebApp.Api.Tests;

[TestClass]
public class AiCostCalculatorTests
{
    private static readonly DateTime At = new(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc);
    private static DiagLinkDbContext Db() => new(new DbContextOptionsBuilder<DiagLinkDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static AiUsageRecord Usage() => new()
    {
        Id = Guid.NewGuid(), Provider = "TestProvider", Model = "test-model",
        UsageType = AiUsageType.ChatResponse, Available = true, Completed = true,
        InputTokens = 100, OutputTokens = 20, TotalTokens = 999, CreatedAtUtc = At
    };
    private static AiPricing Price(string? type = null, DateTime? start = null, DateTime? end = null) => new()
    {
        Id = Guid.NewGuid(), Provider = "TestProvider", Model = "test-model", UsageType = type,
        InputPricePerMillion = 2m, OutputPricePerMillion = 5m, Currency = "USD",
        EffectiveFromUtc = start ?? At.AddDays(-1), EffectiveToUtc = end, CreatedAtUtc = At
    };
    private static async Task Seed(DiagLinkDbContext db, params AiPricing[] prices)
    {
        db.AiPricing.AddRange(prices);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }
    private static void Failed(AiCostCalculationResult result, string reason)
    {
        Assert.IsFalse(result.IsValuable);
        Assert.AreEqual(reason, result.FailureReason);
        Assert.IsNull(result.InputCost);
        Assert.IsNull(result.OutputCost);
        Assert.IsNull(result.RealAiCost);
    }

    [TestMethod]
    [DataRow(1000000, 0, "2", "0", "2")]
    [DataRow(0, 1000000, "0", "5", "5")]
    [DataRow(1000000, 1000000, "2", "5", "7")]
    [DataRow(0, 0, "0", "0", "0")]
    [DataRow(1, 1, "0.000002", "0.000005", "0.000007")]
    public async Task DecimalFormulaIgnoresTotalTokens(int input, int output, string inputCost, string outputCost, string total)
    {
        await using var db = Db();
        var pricing = Price();
        await Seed(db, pricing);
        var usage = Usage(); usage.InputTokens = input; usage.OutputTokens = output;
        var result = await new AiCostCalculator(db).CalculateAsync(usage);
        Assert.IsTrue(result.IsValuable);
        Assert.IsNull(result.FailureReason);
        Assert.AreEqual(decimal.Parse(inputCost, CultureInfo.InvariantCulture), result.InputCost);
        Assert.AreEqual(decimal.Parse(outputCost, CultureInfo.InvariantCulture), result.OutputCost);
        Assert.AreEqual(decimal.Parse(total, CultureInfo.InvariantCulture), result.RealAiCost);
        Assert.AreEqual(pricing.Id, result.PricingId);
        Assert.AreEqual(pricing.EffectiveFromUtc, result.PricingEffectiveFromUtc);
        Assert.AreEqual(usage.Id, result.UsageRecordId);
        Assert.AreEqual((long)input, result.InputTokens);
        Assert.AreEqual((long)output, result.OutputTokens);
    }

    [TestMethod]
    [DataRow("0.25", "0.25", "0.000001")]
    [DataRow("0.4", "0.4", "0.000001")]
    [DataRow("0.1", "0.1", "0")]
    public async Task RoundsRawSumOnceAwayFromZero(string inputPrice, string outputPrice, string expected)
    {
        await using var db = Db();
        var price = Price();
        price.InputPricePerMillion = decimal.Parse(inputPrice, CultureInfo.InvariantCulture);
        price.OutputPricePerMillion = decimal.Parse(outputPrice, CultureInfo.InvariantCulture);
        await Seed(db, price);
        var usage = Usage(); usage.InputTokens = 1; usage.OutputTokens = 1;
        var result = await new AiCostCalculator(db).CalculateAsync(usage);
        Assert.IsTrue(result.IsValuable);
        Assert.AreEqual(price.InputPricePerMillion / 1_000_000m, result.InputCost);
        Assert.AreEqual(price.OutputPricePerMillion / 1_000_000m, result.OutputCost);
        Assert.AreEqual(decimal.Parse(expected, CultureInfo.InvariantCulture), result.RealAiCost);
    }

    [TestMethod]
    [DataRow(1, 0, null)]
    [DataRow(0, 1, null)]
    [DataRow(1, 1, null)]
    [DataRow(1, 2, null)]
    [DataRow(2, 1, "AmbiguousPricing")]
    [DataRow(0, 2, "AmbiguousPricing")]
    [DataRow(0, 0, "PricingNotFound")]
    public async Task SpecificThenGenericWithNoArbitraryTieBreaking(int specificCount, int genericCount, string? failure)
    {
        await using var db = Db();
        var prices = Enumerable.Range(0, specificCount).Select(_ => Price("ChatResponse"))
            .Concat(Enumerable.Range(0, genericCount).Select(_ => Price())).ToArray();
        await Seed(db, prices);
        var result = await new AiCostCalculator(db).CalculateAsync(Usage());
        if (failure != null) { Failed(result, failure); return; }
        Assert.IsTrue(result.IsValuable);
        Assert.AreEqual(prices[0].Id, result.PricingId);
    }

    [TestMethod]
    [DataRow(0, 1, true)]
    [DataRow(-1, 0, false)]
    [DataRow(1, 2, false)]
    public async Task ValidityIsStartInclusiveEndExclusive(int startDays, int endDays, bool expected)
    {
        await using var db = Db();
        await Seed(db, Price(start: At.AddDays(startDays), end: At.AddDays(endDays)));
        var result = await new AiCostCalculator(db).CalculateAsync(Usage());
        Assert.AreEqual(expected, result.IsValuable);
        if (!expected) Failed(result, "PricingNotFound");
    }

    [TestMethod]
    public async Task SelectsHistoricalPriceAtUsageTime()
    {
        await using var db = Db();
        var historical = Price(start: At.AddDays(-10), end: At.AddDays(1));
        var future = Price(start: At.AddDays(1));
        future.InputPricePerMillion = 999m;
        await Seed(db, historical, future);
        var result = await new AiCostCalculator(db).CalculateAsync(Usage());
        Assert.AreEqual(historical.Id, result.PricingId);
        Assert.AreEqual(0.0003m, result.RealAiCost);
    }

    [TestMethod]
    [DataRow("ProviderMissing")]
    [DataRow("ModelMissing")]
    [DataRow("UsageUnavailable")]
    [DataRow("InputTokensMissing")]
    [DataRow("OutputTokensMissing")]
    [DataRow("NegativeTokenCount")]
    [DataRow("BlankProvider")]
    [DataRow("BlankModel")]
    public async Task InvalidUsageFailsBeforeAnyPricingQuery(string condition)
    {
        var db = Db();
        await db.DisposeAsync(); // Any query against this context would fail.
        var usage = Usage();
        switch (condition)
        {
            case "ProviderMissing": usage.Provider = null; usage.Available = false; break;
            case "ModelMissing": usage.Model = null; break;
            case "UsageUnavailable": usage.Available = false; break;
            case "InputTokensMissing": usage.InputTokens = null; break;
            case "OutputTokensMissing": usage.OutputTokens = null; break;
            case "NegativeTokenCount": usage.OutputTokens = -1; break;
            case "BlankProvider": usage.Provider = " "; break;
            case "BlankModel": usage.Model = ""; break;
        }
        var result = await new AiCostCalculator(db).CalculateAsync(usage);
        Failed(result, condition switch { "BlankProvider" => "ProviderMissing", "BlankModel" => "ModelMissing", _ => condition });
    }

    [TestMethod]
    [DataRow(AiUsageType.ChatResponse)]
    [DataRow(AiUsageType.VisionTool)]
    [DataRow(AiUsageType.ConversationSummary)]
    public async Task EachUsageTypeIsValuedIndependentlyAndCurrencyPropagates(AiUsageType type)
    {
        await using var db = Db();
        var pricing = Price(type.ToString()); pricing.Currency = "EUR";
        await Seed(db, pricing);
        var usage = Usage(); usage.UsageType = type;
        var result = await new AiCostCalculator(db).CalculateAsync(usage);
        Assert.IsTrue(result.IsValuable);
        Assert.AreEqual(type.ToString(), result.UsageType);
        Assert.AreEqual("EUR", result.Currency);
        Assert.AreEqual(0.0003m, result.RealAiCost);
    }

    [TestMethod]
    [DataRow("OtherProvider", "test-model", null)]
    [DataRow("TestProvider", "test-model-extra", null)]
    [DataRow("testprovider", "test-model", null)]
    [DataRow("TestProvider", "test-model ", null)]
    [DataRow("TestProvider", "test-model", "VisionTool")]
    public async Task NoApproximateIdentityOrWrongUsageType(string provider, string model, string? type)
    {
        await using var db = Db();
        var price = Price(type); price.Provider = provider; price.Model = model;
        await Seed(db, price);
        Failed(await new AiCostCalculator(db).CalculateAsync(Usage()), "PricingNotFound");
    }

    [TestMethod]
    public async Task LookupByIdIsReadOnlyAndUnknownIdHasExplicitReason()
    {
        await using var db = Db();
        var usage = Usage();
        db.AiUsageRecords.Add(usage);
        var pricing = Price(); await Seed(db, pricing);
        var before = JsonSerializer.Serialize(usage);
        var calculator = new AiCostCalculator(db);
        Assert.IsTrue((await calculator.CalculateAsync(usage.Id)).IsValuable);
        Assert.AreEqual(0, db.ChangeTracker.Entries().Count());
        Failed(await calculator.CalculateAsync(Guid.NewGuid()), "UsageRecordNotFound");
        Assert.AreEqual(before, JsonSerializer.Serialize(await db.AiUsageRecords.AsNoTracking().SingleAsync()));
        Assert.AreEqual(1, await db.AiPricing.CountAsync());
    }

    [TestMethod]
    [DataRow("InvalidPricing")]
    [DataRow("InvalidCurrency")]
    [DataRow("CostOutOfRange")]
    public async Task InvalidFinancialInputsNeverProduceAValue(string reason)
    {
        await using var db = Db();
        var price = Price(); var usage = Usage();
        if (reason == "InvalidPricing") price.InputPricePerMillion = -1m;
        if (reason == "InvalidCurrency") price.Currency = "";
        if (reason == "CostOutOfRange")
        {
            price.InputPricePerMillion = 9999999999.99999999m;
            usage.InputTokens = int.MaxValue;
        }
        await Seed(db, price);
        Failed(await new AiCostCalculator(db).CalculateAsync(usage), reason);
    }
}
