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
public sealed class WebSearchPricingMigrationTests
{
    [TestMethod]
    public void InitialPostgreSql_DefinesNullableWebSearchPricePerRequestWithoutDatabaseDefault()
    {
        Assert.AreEqual(
            "20261020002202_InitialPostgreSql",
            typeof(InitialPostgreSql).GetCustomAttribute<MigrationAttribute>()?.Id);
        var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        typeof(InitialPostgreSql)
            .GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(new InitialPostgreSql(), [builder]);

        var table = builder.Operations
            .OfType<CreateTableOperation>()
            .Single(operation => operation.Schema == "dbo" && operation.Name == "AiPricing");
        var column = table.Columns.Single(operation => operation.Name == "WebSearchPricePerRequest");

        Assert.AreEqual(typeof(decimal), column.ClrType);
        Assert.AreEqual("numeric(18,8)", column.ColumnType);
        Assert.AreEqual(18, column.Precision);
        Assert.AreEqual(8, column.Scale);
        Assert.IsTrue(column.IsNullable);
        Assert.IsNull(column.DefaultValue);
        Assert.IsNull(column.DefaultValueSql);
        Assert.IsFalse(builder.Operations
            .OfType<CreateIndexOperation>()
            .Where(index => index.Schema == "dbo" && index.Table == "AiPricing")
            .Any(index => index.Columns.Contains("WebSearchPricePerRequest", StringComparer.Ordinal)));
        Assert.IsFalse(table.CheckConstraints.Any(constraint =>
            constraint.Sql.Contains("WebSearchPricePerRequest", StringComparison.Ordinal)));

        using var db = new DiagLinkDbContext(new DbContextOptionsBuilder<DiagLinkDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=15439;Database=web_search_pricing_metadata_only;Username=test;Password=test")
            .Options);
        var runtimeModel = db.GetService<IDesignTimeModel>().Model;
        var snapshotType = typeof(InitialPostgreSql).Assembly.GetType(
            "WebApp.Api.Migrations.PostgreSql.Migrations.DiagLinkDbContextModelSnapshot",
            throwOnError: true)!;
        var snapshotModel = ((ModelSnapshot)Activator.CreateInstance(snapshotType, nonPublic: true)!).Model;

        AssertModel(runtimeModel, requireAiPricingClrType: true);
        AssertModel(snapshotModel, requireAiPricingClrType: false);
    }

    private static void AssertModel(IModel model, bool requireAiPricingClrType)
    {
        var entity = model.GetEntityTypes()
            .Single(entityType => entityType.GetSchema() == "dbo" && entityType.GetTableName() == "AiPricing");
        if (requireAiPricingClrType)
        {
            Assert.AreEqual(typeof(AiPricing), entity.ClrType);
        }

        var property = entity.FindProperty("WebSearchPricePerRequest");
        Assert.IsNotNull(property);
        Assert.AreEqual(typeof(decimal?), property.ClrType);
        Assert.AreEqual("numeric(18,8)", property.GetColumnType());
        Assert.AreEqual(18, property.GetPrecision());
        Assert.AreEqual(8, property.GetScale());
        Assert.IsTrue(property.IsNullable);
        Assert.IsNull(property.FindAnnotation(RelationalAnnotationNames.DefaultValue));
        Assert.IsNull(property.FindAnnotation(RelationalAnnotationNames.DefaultValueSql));
        Assert.IsFalse(entity.GetIndexes().Any(index => index.Properties.Contains(property)));
        Assert.IsFalse(entity.GetCheckConstraints().Any(constraint =>
            constraint.Sql.Contains("WebSearchPricePerRequest", StringComparison.Ordinal)));
    }
}
