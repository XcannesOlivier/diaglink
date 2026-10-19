using System.Reflection;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebApp.Api.Migrations;

namespace WebApp.Api.Tests;

[TestClass]
public sealed class ConversationMessageWebCitationsMigrationTests
{
    [TestMethod]
    public void Up_AddsOnlyAdditiveWebCitationStorage()
    {
        Assert.AreEqual(
            "20261016201738_AddConversationMessageWebCitations",
            typeof(AddConversationMessageWebCitations).GetCustomAttribute<MigrationAttribute>()?.Id);
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
        typeof(AddConversationMessageWebCitations)
            .GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(new AddConversationMessageWebCitations(), [builder]);

        var table = builder.Operations.OfType<CreateTableOperation>().Single();
        Assert.AreEqual("chat", table.Schema);
        Assert.AreEqual("ConversationMessageWebCitations", table.Name);
        CollectionAssert.IsSubsetOf(
            new[] { "Id", "ConversationMessageId", "Url", "Label", "Quote", "DisplayOrder" },
            table.Columns.Select(column => column.Name).ToArray());
        Assert.IsFalse(builder.Operations.Any(operation => operation is DropTableOperation or DropColumnOperation));
    }

    [TestMethod]
    public void Down_RemovesOnlyWebCitationStorage()
    {
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
        typeof(AddConversationMessageWebCitations)
            .GetMethod("Down", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(new AddConversationMessageWebCitations(), [builder]);

        var operation = builder.Operations.Single() as DropTableOperation;
        Assert.IsNotNull(operation);
        Assert.AreEqual("chat", operation.Schema);
        Assert.AreEqual("ConversationMessageWebCitations", operation.Name);
    }
}
