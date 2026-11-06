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
public sealed class FinalClaudeDirectCleanupMigrationTests
{
    [TestMethod]
    public void InitialPostgreSql_DefinesClaudeDirectFinalSchemaWithoutFoundryColumns()
    {
        var builder = BuildMigration();
        var tables = builder.Operations
            .OfType<CreateTableOperation>()
            .ToDictionary(table => $"{table.Schema}.{table.Name}");

        var expectedColumns = new[]
        {
            new ExpectedColumn(typeof(Conversation), "chat.Conversations", "ConversationPublicId", typeof(string), "character varying(200)", false, 200),
            new ExpectedColumn(typeof(Conversation), "chat.Conversations", "MachineId", typeof(Guid), "uuid", true, null),
            new ExpectedColumn(typeof(AiUsageRecord), "chat.AiUsageRecords", "ConversationPublicId", typeof(string), "character varying(200)", true, 200),
            new ExpectedColumn(typeof(AiUsageRecord), "chat.AiUsageRecords", "Model", typeof(string), "character varying(256)", true, 256),
            new ExpectedColumn(typeof(AiUsageRecord), "chat.AiUsageRecords", "Provider", typeof(string), "character varying(100)", true, 100),
            new ExpectedColumn(typeof(AiUsageRecord), "chat.AiUsageRecords", "Deployment", typeof(string), "character varying(200)", true, 200),
            new ExpectedColumn(typeof(AiUsageRecord), "chat.AiUsageRecords", "ModelSource", typeof(string), "character varying(32)", true, 32),
            new ExpectedColumn(typeof(AiUsageRecord), "chat.AiUsageRecords", "CallBreakdownJson", typeof(string), "text", true, null),
            new ExpectedColumn(typeof(Machine), "dbo.Machines", "ProjectEndpoint", typeof(string), "character varying(500)", true, 500),
            new ExpectedColumn(typeof(Machine), "dbo.Machines", "VectorStoreId", typeof(string), "character varying(200)", true, 200),
            new ExpectedColumn(typeof(Machine), "dbo.Machines", "BlobPrefix", typeof(string), "character varying(200)", true, 200)
        };

        foreach (var expected in expectedColumns)
        {
            var column = tables[expected.Table].Columns.Single(item => item.Name == expected.Name);
            Assert.AreEqual(expected.ClrType, column.ClrType, expected.Name);
            Assert.AreEqual(expected.ColumnType, column.ColumnType, expected.Name);
            Assert.AreEqual(expected.Nullable, column.IsNullable, expected.Name);
            Assert.AreEqual(expected.MaxLength, column.MaxLength, expected.Name);
        }

        var legacyColumns = new[]
        {
            new LegacyColumn(typeof(Conversation), "chat.Conversations", "FoundryConversationId"),
            new LegacyColumn(typeof(AiUsageRecord), "chat.AiUsageRecords", "FoundryConversationId"),
            new LegacyColumn(typeof(AiUsageRecord), "chat.AiUsageRecords", "AgentVersion"),
            new LegacyColumn(typeof(Machine), "dbo.Machines", "FoundryAgentId"),
            new LegacyColumn(typeof(Machine), "dbo.Machines", "AgentVersion")
        };

        foreach (var legacy in legacyColumns)
        {
            Assert.IsFalse(
                tables[legacy.Table].Columns.Any(column => column.Name == legacy.Name),
                $"{legacy.Table}.{legacy.Name}");
        }

        var conversationTable = tables["chat.Conversations"];
        var machineForeignKey = conversationTable.ForeignKeys.Single(foreignKey =>
            foreignKey.Columns.SequenceEqual(["MachineId"]));
        Assert.AreEqual("FK_Conversations_Machines_MachineId", machineForeignKey.Name);
        Assert.AreEqual("dbo", machineForeignKey.PrincipalSchema);
        Assert.AreEqual("Machines", machineForeignKey.PrincipalTable);
        Assert.IsNotNull(machineForeignKey.PrincipalColumns);
        CollectionAssert.AreEqual(new[] { "Id" }, machineForeignKey.PrincipalColumns.ToArray());
        Assert.AreEqual(ReferentialAction.Restrict, machineForeignKey.OnDelete);

        var conversationIndex = builder.Operations
            .OfType<CreateIndexOperation>()
            .Single(index => index.Name == "IX_Conversations_ConversationPublicId");
        Assert.AreEqual("chat", conversationIndex.Schema);
        Assert.AreEqual("Conversations", conversationIndex.Table);
        CollectionAssert.AreEqual(new[] { "ConversationPublicId" }, conversationIndex.Columns.ToArray());
        Assert.IsFalse(conversationIndex.IsUnique);

        var models = PostgreSqlModels();
        for (var modelPosition = 0; modelPosition < models.Count; modelPosition++)
        {
            var model = models[modelPosition];
            foreach (var expected in expectedColumns)
            {
                var property = model.FindEntityType(expected.EntityType)?.FindProperty(expected.Name);
                Assert.IsNotNull(property, expected.Name);
                Assert.AreEqual(expected.ClrType, Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType, expected.Name);
                Assert.AreEqual(expected.ColumnType, property.GetColumnType(), expected.Name);
                Assert.AreEqual(expected.Nullable, property.IsNullable, expected.Name);
                Assert.AreEqual(expected.MaxLength, property.GetMaxLength(), expected.Name);
            }

            foreach (var legacy in legacyColumns)
            {
                Assert.IsNull(model.FindEntityType(legacy.EntityType)?.FindProperty(legacy.Name), legacy.Name);
            }

            var conversation = model.FindEntityType(typeof(Conversation));
            Assert.IsNotNull(conversation);
            var modelIndex = conversation.GetIndexes().Single(index =>
                index.Properties.Select(property => property.Name).SequenceEqual(["ConversationPublicId"]));
            Assert.AreEqual("IX_Conversations_ConversationPublicId", modelIndex.GetDatabaseName());
            Assert.IsFalse(modelIndex.IsUnique);

            var modelForeignKey = conversation.GetForeignKeys().Single(foreignKey =>
                foreignKey.Properties.Select(property => property.Name).SequenceEqual(["MachineId"]));
            if (modelPosition == 0)
            {
                Assert.AreEqual(typeof(Machine), modelForeignKey.PrincipalEntityType.ClrType);
            }

            Assert.AreEqual(typeof(Machine).FullName, modelForeignKey.PrincipalEntityType.Name);
            Assert.AreEqual("Machines", modelForeignKey.PrincipalEntityType.GetTableName());
            Assert.AreEqual("dbo", modelForeignKey.PrincipalEntityType.GetSchema());
            CollectionAssert.AreEqual(
                new[] { "MachineId" },
                modelForeignKey.Properties.Select(property => property.Name).ToArray());
            Assert.AreEqual(
                "MachineId",
                modelForeignKey.Properties.Single().GetColumnName(StoreObjectIdentifier.Table("Conversations", "chat")));
            CollectionAssert.AreEqual(
                new[] { "Id" },
                modelForeignKey.PrincipalKey.Properties.Select(property => property.Name).ToArray());
            Assert.AreEqual(
                "Id",
                modelForeignKey.PrincipalKey.Properties.Single().GetColumnName(StoreObjectIdentifier.Table("Machines", "dbo")));
            Assert.IsFalse(modelForeignKey.IsRequired);
            Assert.AreEqual(DeleteBehavior.Restrict, modelForeignKey.DeleteBehavior);
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
            .UseNpgsql("Host=127.0.0.1;Port=15439;Database=claude_schema_metadata_only;Username=test;Password=test")
            .Options);
        var runtimeModel = db.GetService<IDesignTimeModel>().Model;
        var snapshotType = typeof(InitialPostgreSql).Assembly.GetType(
            "WebApp.Api.Migrations.PostgreSql.Migrations.DiagLinkDbContextModelSnapshot",
            throwOnError: true)!;
        var snapshotModel = ((ModelSnapshot)Activator.CreateInstance(snapshotType, nonPublic: true)!).Model;
        return [runtimeModel, snapshotModel];
    }

    private sealed record ExpectedColumn(
        Type EntityType,
        string Table,
        string Name,
        Type ClrType,
        string ColumnType,
        bool Nullable,
        int? MaxLength);

    private sealed record LegacyColumn(Type EntityType, string Table, string Name);
}
