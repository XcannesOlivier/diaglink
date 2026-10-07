using System.Reflection;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebApp.Api.Migrations;

namespace WebApp.Api.Tests;

[TestClass]
public sealed class FinalClaudeDirectCleanupMigrationTests
{
    [TestMethod]
    public void Up_RenamesConversationIdentifiersAndDropsOnlyLegacyColumns()
    {
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
        typeof(FinalClaudeDirectCleanup)
            .GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(new FinalClaudeDirectCleanup(), [builder]);

        var renames = builder.Operations.OfType<RenameColumnOperation>().ToList();
        Assert.IsTrue(renames.Any(operation =>
            operation.Schema == "chat" &&
            operation.Table == "Conversations" &&
            operation.Name == "FoundryConversationId" &&
            operation.NewName == "ConversationPublicId"));
        Assert.IsTrue(renames.Any(operation =>
            operation.Schema == "chat" &&
            operation.Table == "AiUsageRecords" &&
            operation.Name == "FoundryConversationId" &&
            operation.NewName == "ConversationPublicId"));

        Assert.IsFalse(builder.Operations.OfType<DropColumnOperation>().Any(operation =>
            operation.Schema == "chat" &&
            operation.Table == "Conversations" &&
            operation.Name == "FoundryConversationId"));

        var droppedColumns = builder.Operations
            .OfType<DropColumnOperation>()
            .Select(operation => $"{operation.Schema}.{operation.Table}.{operation.Name}")
            .OrderBy(value => value)
            .ToList();

        CollectionAssert.AreEquivalent(
            new[]
            {
                "chat.AiUsageRecords.AgentVersion",
                "dbo.Machines.AgentVersion",
                "dbo.Machines.FoundryAgentId"
            },
            droppedColumns);
    }
}
