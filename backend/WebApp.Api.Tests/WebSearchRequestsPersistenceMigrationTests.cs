using System.Reflection;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebApp.Api.Migrations;

namespace WebApp.Api.Tests;

[TestClass]
public sealed class WebSearchRequestsPersistenceMigrationTests
{
    [TestMethod]
    public void Up_AddsOnlyNonNullableWebSearchRequestsWithHistoricalZeroDefault()
    {
        Assert.AreEqual(
            "20261008120000_AddWebSearchRequestsToAiUsageRecords",
            typeof(AddWebSearchRequestsToAiUsageRecords).GetCustomAttribute<MigrationAttribute>()?.Id);
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
        typeof(AddWebSearchRequestsToAiUsageRecords)
            .GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(new AddWebSearchRequestsToAiUsageRecords(), [builder]);

        var operation = builder.Operations.Single();
        var column = operation as AddColumnOperation;
        Assert.IsNotNull(column);
        Assert.AreEqual("chat", column.Schema);
        Assert.AreEqual("AiUsageRecords", column.Table);
        Assert.AreEqual("WebSearchRequests", column.Name);
        Assert.AreEqual("int", column.ColumnType);
        Assert.IsFalse(column.IsNullable);
        Assert.AreEqual(0, column.DefaultValue);
    }

    [TestMethod]
    public void Down_RemovesOnlyWebSearchRequests()
    {
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
        typeof(AddWebSearchRequestsToAiUsageRecords)
            .GetMethod("Down", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(new AddWebSearchRequestsToAiUsageRecords(), [builder]);

        var operation = builder.Operations.Single();
        var column = operation as DropColumnOperation;
        Assert.IsNotNull(column);
        Assert.AreEqual("chat", column.Schema);
        Assert.AreEqual("AiUsageRecords", column.Table);
        Assert.AreEqual("WebSearchRequests", column.Name);
    }
}
