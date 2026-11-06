using System.Reflection;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using WebApp.Api.Data;
using WebApp.Api.Migrations.PostgreSql.Migrations;
using WebApp.Api.Models.Entities;

namespace WebApp.Api.Tests;

[TestClass]
public sealed class ConversationMessageWebCitationsMigrationTests
{
    private const string TableName = "ConversationMessageWebCitations";
    private const string Schema = "chat";

    [TestMethod]
    public void InitialPostgreSql_DefinesWebCitationTableAndIntegrity()
    {
        var builder = BuildMigration();
        var table = builder.Operations.OfType<CreateTableOperation>()
            .Single(operation => operation.Schema == Schema && operation.Name == TableName);
        var expectedColumns = new[]
        {
            new ExpectedColumn("Id", typeof(long), "bigint", false, null),
            new ExpectedColumn("ConversationMessageId", typeof(long), "bigint", false, null),
            new ExpectedColumn("Url", typeof(string), "character varying(2048)", false, 2048),
            new ExpectedColumn("Label", typeof(string), "character varying(512)", false, 512),
            new ExpectedColumn("Quote", typeof(string), "character varying(2000)", true, 2000),
            new ExpectedColumn("DisplayOrder", typeof(int), "integer", false, null)
        };

        CollectionAssert.AreEquivalent(
            expectedColumns.Select(column => column.Name).ToArray(),
            table.Columns.Select(column => column.Name).ToArray());
        foreach (var expected in expectedColumns)
        {
            var column = table.Columns.Single(item => item.Name == expected.Name);
            Assert.AreEqual(expected.ClrType, column.ClrType, expected.Name);
            Assert.AreEqual(expected.ColumnType, column.ColumnType, expected.Name);
            Assert.AreEqual(expected.Nullable, column.IsNullable, expected.Name);
            Assert.AreEqual(expected.MaxLength, column.MaxLength, expected.Name);
            Assert.IsNull(column.DefaultValue, expected.Name);
            Assert.IsNull(column.DefaultValueSql, expected.Name);
        }

        var id = table.Columns.Single(column => column.Name == "Id");
        Assert.AreEqual(
            NpgsqlValueGenerationStrategy.IdentityByDefaultColumn,
            id["Npgsql:ValueGenerationStrategy"]);

        Assert.IsNotNull(table.PrimaryKey);
        Assert.AreEqual("PK_ConversationMessageWebCitations", table.PrimaryKey.Name);
        CollectionAssert.AreEqual(new[] { "Id" }, table.PrimaryKey.Columns.ToArray());

        var foreignKey = table.ForeignKeys.Single();
        Assert.AreEqual("FK_ConversationMessageWebCitations_ConversationMessages_Conver~", foreignKey.Name);
        CollectionAssert.AreEqual(new[] { "ConversationMessageId" }, foreignKey.Columns.ToArray());
        Assert.AreEqual("chat", foreignKey.PrincipalSchema);
        Assert.AreEqual("ConversationMessages", foreignKey.PrincipalTable);
        Assert.IsNotNull(foreignKey.PrincipalColumns);
        CollectionAssert.AreEqual(new[] { "Id" }, foreignKey.PrincipalColumns.ToArray());
        Assert.AreEqual(ReferentialAction.Cascade, foreignKey.OnDelete);

        var checks = table.CheckConstraints.ToDictionary(constraint => constraint.Name);
        Assert.HasCount(2, checks);
        Assert.AreEqual("\"DisplayOrder\" >= 0", checks["CK_ConversationMessageWebCitations_DisplayOrder"].Sql);
        Assert.AreEqual(
            "char_length(\"Url\") > 0 AND char_length(\"Label\") > 0",
            checks["CK_ConversationMessageWebCitations_RequiredStrings"].Sql);

        foreach (var model in PostgreSqlModels())
        {
            var entity = model.FindEntityType(typeof(ConversationMessageWebCitation));
            Assert.IsNotNull(entity);
            Assert.AreEqual(Schema, entity.GetSchema());
            Assert.AreEqual(TableName, entity.GetTableName());
            Assert.AreEqual(
                "\"DisplayOrder\" >= 0",
                entity.GetCheckConstraints().Single(item => item.Name == "CK_ConversationMessageWebCitations_DisplayOrder").Sql);
            Assert.AreEqual(
                "char_length(\"Url\") > 0 AND char_length(\"Label\") > 0",
                entity.GetCheckConstraints().Single(item => item.Name == "CK_ConversationMessageWebCitations_RequiredStrings").Sql);
        }
    }

    [TestMethod]
    public void InitialPostgreSql_DefinesWebCitationIndexesAndMatchesModel()
    {
        var indexes = BuildMigration().Operations.OfType<CreateIndexOperation>()
            .Where(operation => operation.Schema == Schema && operation.Table == TableName)
            .ToDictionary(operation => operation.Name);
        var expected = new[]
        {
            new ExpectedIndex(
                "IX_ConversationMessageWebCitations_ConversationMessageId",
                ["ConversationMessageId"],
                false),
            new ExpectedIndex(
                "IX_ConversationMessageWebCitations_ConversationMessageId_Displ~",
                ["ConversationMessageId", "DisplayOrder"],
                true),
            new ExpectedIndex(
                "IX_ConversationMessageWebCitations_ConversationMessageId_Url",
                ["ConversationMessageId", "Url"],
                true)
        };

        CollectionAssert.AreEquivalent(
            expected.Select(index => index.Name).ToArray(),
            indexes.Keys.ToArray());
        foreach (var item in expected)
        {
            var index = indexes[item.Name];
            CollectionAssert.AreEqual(item.Columns, index.Columns.ToArray(), item.Name);
            Assert.AreEqual(item.Unique, index.IsUnique, item.Name);
            Assert.IsLessThanOrEqualTo(63, Encoding.UTF8.GetByteCount(item.Name), item.Name);
        }

        foreach (var model in PostgreSqlModels())
        {
            var entity = model.FindEntityType(typeof(ConversationMessageWebCitation));
            Assert.IsNotNull(entity);
            var modelIndexes = entity.GetIndexes().ToDictionary(index => index.GetDatabaseName()!);
            CollectionAssert.AreEquivalent(
                expected.Select(index => index.Name).ToArray(),
                modelIndexes.Keys.ToArray());
            foreach (var item in expected)
            {
                var index = modelIndexes[item.Name];
                CollectionAssert.AreEqual(
                    item.Columns,
                    index.Properties.Select(property => property.Name).ToArray(),
                    item.Name);
                Assert.AreEqual(item.Unique, index.IsUnique, item.Name);
            }
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
            .UseNpgsql("Host=127.0.0.1;Port=15439;Database=citation_metadata_only;Username=test;Password=test")
            .Options);
        var runtimeModel = db.GetService<IDesignTimeModel>().Model;
        var snapshotType = typeof(InitialPostgreSql).Assembly.GetType(
            "WebApp.Api.Migrations.PostgreSql.Migrations.DiagLinkDbContextModelSnapshot",
            throwOnError: true)!;
        var snapshotModel = ((ModelSnapshot)Activator.CreateInstance(snapshotType, nonPublic: true)!).Model;
        return [runtimeModel, snapshotModel];
    }

    private sealed record ExpectedColumn(
        string Name,
        Type ClrType,
        string ColumnType,
        bool Nullable,
        int? MaxLength);

    private sealed record ExpectedIndex(string Name, string[] Columns, bool Unique);
}
