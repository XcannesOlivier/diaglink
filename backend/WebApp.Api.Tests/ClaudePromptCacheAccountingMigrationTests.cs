using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebApp.Api.Data;
using WebApp.Api.Migrations.PostgreSql.Migrations;
using WebApp.Api.Models.Entities;

namespace WebApp.Api.Tests;

[TestClass]
public sealed class ClaudePromptCacheAccountingMigrationTests
{
    [TestMethod]
    public void InitialPostgreSql_ContainsNullableClaudePromptCacheColumns()
    {
        var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        typeof(InitialPostgreSql)
            .GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(new InitialPostgreSql(), [builder]);

        var tables = builder.Operations
            .OfType<CreateTableOperation>()
            .ToDictionary(table => $"{table.Schema}.{table.Name}");
        var expected = new (string Table, string Name, Type ClrType, string ColumnType, int? Precision, int? Scale)[]
        {
            ("chat.AiUsageRecords", "CacheReadInputTokens", typeof(int), "integer", null, null),
            ("chat.AiUsageRecords", "CacheCreationInputTokens", typeof(int), "integer", null, null),
            ("chat.AiUsageRecords", "CacheCreation5mInputTokens", typeof(int), "integer", null, null),
            ("chat.AiUsageRecords", "CacheCreation1hInputTokens", typeof(int), "integer", null, null),
            ("dbo.AiPricing", "CacheReadPricePerMillion", typeof(decimal), "numeric(18,8)", 18, 8),
            ("dbo.AiPricing", "CacheCreation5mPricePerMillion", typeof(decimal), "numeric(18,8)", 18, 8),
            ("dbo.AiPricing", "CacheCreation1hPricePerMillion", typeof(decimal), "numeric(18,8)", 18, 8)
        };
        var actualCacheColumns = new[] { "chat.AiUsageRecords", "dbo.AiPricing" }
            .SelectMany(table => tables[table].Columns
                .Where(column => column.Name.StartsWith("Cache", StringComparison.Ordinal))
                .Select(column => $"{table}.{column.Name}"))
            .ToArray();
        CollectionAssert.AreEquivalent(
            expected.Select(item => $"{item.Table}.{item.Name}").ToArray(),
            actualCacheColumns);

        using var db = new DiagLinkDbContext(new DbContextOptionsBuilder<DiagLinkDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=15439;Database=cache_metadata_only;Username=test;Password=test")
            .Options);
        var runtimeModel = db.GetService<IDesignTimeModel>().Model;
        var snapshotType = typeof(InitialPostgreSql).Assembly.GetType(
            "WebApp.Api.Migrations.PostgreSql.Migrations.DiagLinkDbContextModelSnapshot",
            throwOnError: true)!;
        var snapshotModel = ((ModelSnapshot)Activator.CreateInstance(snapshotType, nonPublic: true)!).Model;

        foreach (var item in expected)
        {
            var column = tables[item.Table].Columns.Single(column => column.Name == item.Name);
            Assert.AreEqual(item.ClrType, column.ClrType, item.Name);
            Assert.AreEqual(item.ColumnType, column.ColumnType, item.Name);
            Assert.IsTrue(column.IsNullable, item.Name);
            Assert.AreEqual(item.Precision, column.Precision, item.Name);
            Assert.AreEqual(item.Scale, column.Scale, item.Name);
            Assert.IsNull(column.DefaultValue, item.Name);
            Assert.IsNull(column.DefaultValueSql, item.Name);

            AssertModelProperty(runtimeModel, item);
            AssertModelProperty(snapshotModel, item);
        }
    }

    private static void AssertModelProperty(
        IModel model,
        (string Table, string Name, Type ClrType, string ColumnType, int? Precision, int? Scale) expected)
    {
        var entityType = expected.Table switch
        {
            "chat.AiUsageRecords" => model.FindEntityType(typeof(AiUsageRecord)),
            "dbo.AiPricing" => model.FindEntityType(typeof(AiPricing)),
            _ => null
        };
        var property = entityType?.FindProperty(expected.Name);

        Assert.IsNotNull(property, expected.Name);
        Assert.AreEqual(expected.ClrType, Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType, expected.Name);
        Assert.AreEqual(expected.ColumnType, property.GetColumnType(), expected.Name);
        Assert.IsTrue(property.IsNullable, expected.Name);
        Assert.AreEqual(expected.Precision, property.GetPrecision(), expected.Name);
        Assert.AreEqual(expected.Scale, property.GetScale(), expected.Name);
        Assert.IsNull(property.GetDefaultValue(), expected.Name);
        Assert.IsNull(property.GetDefaultValueSql(), expected.Name);
    }
}
