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
public sealed class WebSearchRequestsPersistenceMigrationTests
{
    [TestMethod]
    public void InitialPostgreSql_DefinesNonNullableWebSearchRequestsWithoutDatabaseDefault()
    {
        var builder = BuildMigration();
        var table = builder.Operations
            .OfType<CreateTableOperation>()
            .Single(operation => operation.Schema == "chat" && operation.Name == "AiUsageRecords");
        var column = table.Columns.Single(operation => operation.Name == "WebSearchRequests");

        Assert.AreEqual(typeof(int), column.ClrType);
        Assert.AreEqual("integer", column.ColumnType);
        Assert.IsFalse(column.IsNullable);
        Assert.IsNull(column.DefaultValue);
        Assert.IsNull(column.DefaultValueSql);
        Assert.IsFalse(builder.Operations
            .OfType<CreateIndexOperation>()
            .Where(index => index.Schema == "chat" && index.Table == "AiUsageRecords")
            .Any(index => index.Columns.Contains("WebSearchRequests", StringComparer.Ordinal)));
        Assert.IsFalse(table.CheckConstraints.Any(constraint =>
            constraint.Sql.Contains("WebSearchRequests", StringComparison.Ordinal)));

        foreach (var model in PostgreSqlModels())
        {
            var entity = model.FindEntityType(typeof(AiUsageRecord));
            Assert.IsNotNull(entity);
            Assert.AreEqual("AiUsageRecords", entity.GetTableName());
            Assert.AreEqual("chat", entity.GetSchema());

            var property = entity.FindProperty("WebSearchRequests");
            Assert.IsNotNull(property);
            Assert.AreEqual(typeof(int), property.ClrType);
            Assert.IsFalse(property.IsNullable);
            Assert.AreEqual("integer", property.GetColumnType());
            Assert.IsNull(property.FindAnnotation(RelationalAnnotationNames.DefaultValue));
            Assert.IsNull(property.FindAnnotation(RelationalAnnotationNames.DefaultValueSql));
            Assert.IsFalse(entity.GetIndexes().Any(index =>
                index.Properties.Any(indexProperty => indexProperty.Name == "WebSearchRequests")));
            Assert.IsFalse(entity.GetCheckConstraints().Any(constraint =>
                constraint.Sql.Contains("WebSearchRequests", StringComparison.Ordinal)));
        }
    }

    private static MigrationBuilder BuildMigration()
    {
        Assert.AreEqual(
            "20261020002202_InitialPostgreSql",
            typeof(InitialPostgreSql).GetCustomAttribute<MigrationAttribute>()?.Id);
        var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        typeof(InitialPostgreSql)
            .GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(new InitialPostgreSql(), [builder]);
        return builder;
    }

    private static IReadOnlyList<IModel> PostgreSqlModels()
    {
        using var db = new DiagLinkDbContext(new DbContextOptionsBuilder<DiagLinkDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=15439;Database=web_search_metadata_only;Username=test;Password=test")
            .Options);
        var runtimeModel = db.GetService<IDesignTimeModel>().Model;
        var snapshotType = typeof(InitialPostgreSql).Assembly.GetType(
            "WebApp.Api.Migrations.PostgreSql.Migrations.DiagLinkDbContextModelSnapshot",
            throwOnError: true)!;
        var snapshotModel = ((ModelSnapshot)Activator.CreateInstance(snapshotType, nonPublic: true)!).Model;
        return [runtimeModel, snapshotModel];
    }
}
