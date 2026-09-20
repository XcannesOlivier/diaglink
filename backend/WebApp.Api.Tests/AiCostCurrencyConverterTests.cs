using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebApp.Api.Data;
using WebApp.Api.Models.Entities;
using WebApp.Api.Services;

namespace WebApp.Api.Tests;

[TestClass]
public class AiCostCurrencyConverterTests
{
    private static readonly DateTime At = new(2020, 9, 10, 12, 0, 0, DateTimeKind.Utc);
    private static decimal D(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);
    private static DiagLinkDbContext Db() => new(new DbContextOptionsBuilder<DiagLinkDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static ExchangeRate Rate(decimal value = 0.9m) => new()
    {
        Id = Guid.NewGuid(), BaseCurrency = "USD", QuoteCurrency = "EUR", Rate = value,
        EffectiveFromUtc = At, Source = "fictional-test", CreatedAtUtc = At
    };
    private static async Task Seed(DiagLinkDbContext db, params ExchangeRate[] rows)
    {
        db.ExchangeRates.AddRange(rows);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    [TestMethod]
    [DataRow("0.109446", "0.9", "0.098501")]
    [DataRow("0.000001", "0.1", "0")]
    [DataRow("0.000001", "0.5", "0.000001")]
    [DataRow("1.234565", "0.1", "0.123457")]
    [DataRow("0", "0.9", "0")]
    public async Task ConvertsDecimalAndRoundsOnce(string amount, string rate, string expected)
    {
        await using var db = Db();
        var row = Rate(D(rate));
        await Seed(db, row);
        var result = await new AiCostCurrencyConverter(db).ConvertAsync(D(amount), "USD", new AiUsageRecord { CreatedAtUtc = At });
        Assert.IsTrue(result.IsConvertible);
        Assert.AreEqual(D(expected), result.ConvertedAmount);
        Assert.AreEqual(row.Id, result.ExchangeRateId);
        Assert.AreEqual(At, result.ExchangeRateEffectiveFromUtc);
        Assert.AreEqual(D(rate), result.ExchangeRate);
        Assert.AreEqual(D(amount), result.SourceAmount);
        Assert.AreEqual("EUR", result.TargetCurrency);
        Assert.AreEqual(0, db.ChangeTracker.Entries().Count());
        Assert.AreEqual(0, await db.CreditLedger.CountAsync());
        Assert.AreEqual(0, await db.CompanyWallets.CountAsync());
        Assert.AreEqual(0, await db.MachineBillingPeriods.CountAsync());
    }

    [TestMethod]
    public async Task EurIsExactWithoutRateRow()
    {
        await using var db = Db();
        var result = await new AiCostCurrencyConverter(db).ConvertAsync(0.109446m, "EUR", At);
        Assert.IsTrue(result.IsConvertible);
        Assert.AreEqual(0.109446m, result.ConvertedAmount);
        Assert.AreEqual(1m, result.ExchangeRate);
        Assert.IsNull(result.ExchangeRateId);
        Assert.IsNull(result.ExchangeRateEffectiveFromUtc);
        Assert.AreEqual(0, await db.ExchangeRates.CountAsync());
    }

    [TestMethod]
    public async Task HistoricalIntervalsUseConsumptionDateAndExcludeEnd()
    {
        await using var db = Db();
        var historical = Rate(0.8m);
        historical.EffectiveToUtc = At.AddDays(1);
        var later = Rate(0.9m);
        later.EffectiveFromUtc = At.AddDays(1);
        await Seed(db, historical, later);
        var converter = new AiCostCurrencyConverter(db);
        Assert.AreEqual(historical.Id, (await converter.ConvertAsync(1m, "USD", new AiUsageRecord { CreatedAtUtc = At })).ExchangeRateId);
        Assert.AreEqual(later.Id, (await converter.ConvertAsync(1m, "USD", historical.EffectiveToUtc.Value)).ExchangeRateId);
        Assert.AreEqual("ExchangeRateNotFound", (await converter.ConvertAsync(1m, "USD", At.AddTicks(-1))).FailureReason);
    }

    [TestMethod]
    public async Task MissingExpiredWrongPairAndAmbiguousRatesFail()
    {
        await using var db = Db();
        var converter = new AiCostCurrencyConverter(db);
        Assert.AreEqual("ExchangeRateNotFound", (await converter.ConvertAsync(1m, "USD", At)).FailureReason);
        var expired = Rate(); expired.EffectiveFromUtc = At.AddDays(-1); expired.EffectiveToUtc = At;
        var wrong = Rate(); wrong.QuoteCurrency = "GBP";
        await Seed(db, expired, wrong);
        Assert.AreEqual("ExchangeRateNotFound", (await converter.ConvertAsync(1m, "USD", At)).FailureReason);
        await Seed(db, Rate(), Rate());
        var result = await converter.ConvertAsync(1m, "USD", At);
        Assert.AreEqual("AmbiguousExchangeRate", result.FailureReason);
        Assert.IsFalse(result.IsConvertible);
        Assert.IsNull(result.ConvertedAmount);
        Assert.IsNull(result.ExchangeRateId);
    }

    [TestMethod]
    [DataRow(null, "SourceCurrencyMissing")]
    [DataRow("", "SourceCurrencyMissing")]
    [DataRow(" ", "SourceCurrencyMissing")]
    [DataRow("usd", "InvalidSourceCurrency")]
    [DataRow("US", "InvalidSourceCurrency")]
    [DataRow("USD ", "InvalidSourceCurrency")]
    [DataRow("U1D", "InvalidSourceCurrency")]
    public async Task InvalidCurrency(string? currency, string reason)
    {
        await using var db = Db();
        Assert.AreEqual(reason, (await new AiCostCurrencyConverter(db).ConvertAsync(1m, currency, At)).FailureReason);
    }

    [TestMethod]
    [DataRow("0")]
    [DataRow("-0.9")]
    public async Task InvalidRatesFail(string value)
    {
        await using var db = Db();
        await Seed(db, Rate(D(value)));
        Assert.AreEqual("InvalidExchangeRate", (await new AiCostCurrencyConverter(db).ConvertAsync(1m, "USD", At)).FailureReason);
    }

    [TestMethod]
    public async Task InvalidAmountsOverflowAndLocalDateFail()
    {
        await using var db = Db();
        await Seed(db, Rate(2m));
        var converter = new AiCostCurrencyConverter(db);
        Assert.AreEqual("InvalidSourceAmount", (await converter.ConvertAsync(-1m, "USD", At)).FailureReason);
        foreach (var amount in new[] { decimal.MaxValue, 1000000000000m })
            Assert.AreEqual("ConvertedAmountOutOfRange", (await converter.ConvertAsync(amount, "USD", At)).FailureReason);
        Assert.AreEqual("ConvertedAmountOutOfRange", (await converter.ConvertAsync(decimal.MaxValue, "EUR", At)).FailureReason);
        Assert.AreEqual("InvalidUsageDate", (await converter.ConvertAsync(1m, "USD", DateTime.SpecifyKind(At, DateTimeKind.Local))).FailureReason);
        Assert.IsTrue((await converter.ConvertAsync(1m, "USD", DateTime.SpecifyKind(At, DateTimeKind.Unspecified))).IsConvertible);
    }
}
