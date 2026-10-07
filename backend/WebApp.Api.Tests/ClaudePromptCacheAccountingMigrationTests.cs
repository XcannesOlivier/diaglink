using System.Reflection;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebApp.Api.Migrations;

namespace WebApp.Api.Tests;

[TestClass]
public sealed class ClaudePromptCacheAccountingMigrationTests
{
    [TestMethod]
    public void Up_AddsNullableUsageAndPricingColumnsForBackwardCompatibility()
    {
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
        typeof(AddClaudePromptCacheAccounting)
            .GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(new AddClaudePromptCacheAccounting(), [builder]);

        var columns = builder.Operations.OfType<AddColumnOperation>().ToArray();
        var expected = new[]
        {
            "chat.AiUsageRecords.CacheReadInputTokens",
            "chat.AiUsageRecords.CacheCreationInputTokens",
            "chat.AiUsageRecords.CacheCreation5mInputTokens",
            "chat.AiUsageRecords.CacheCreation1hInputTokens",
            "dbo.AiPricing.CacheReadPricePerMillion",
            "dbo.AiPricing.CacheCreation5mPricePerMillion",
            "dbo.AiPricing.CacheCreation1hPricePerMillion"
        };

        CollectionAssert.AreEquivalent(expected, columns.Select(column => $"{column.Schema}.{column.Table}.{column.Name}").ToArray());
        Assert.IsTrue(columns.All(column => column.IsNullable));
    }
}
