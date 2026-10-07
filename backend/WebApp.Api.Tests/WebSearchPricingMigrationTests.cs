using System.Reflection;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebApp.Api.Migrations;

namespace WebApp.Api.Tests;

[TestClass]
public sealed class WebSearchPricingMigrationTests
{
    [TestMethod]
    public void Up_AddsOnlyNullableWebSearchPricePerRequest()
    {
        Assert.AreEqual(
            "20261008130000_AddWebSearchPricePerRequestToAiPricing",
            typeof(AddWebSearchPricePerRequestToAiPricing).GetCustomAttribute<MigrationAttribute>()?.Id);
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
        typeof(AddWebSearchPricePerRequestToAiPricing)
            .GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(new AddWebSearchPricePerRequestToAiPricing(), [builder]);

        var operation = builder.Operations.Single();
        var column = operation as AddColumnOperation;
        Assert.IsNotNull(column);
        Assert.AreEqual("dbo", column.Schema);
        Assert.AreEqual("AiPricing", column.Table);
        Assert.AreEqual("WebSearchPricePerRequest", column.Name);
        Assert.AreEqual("decimal(18,8)", column.ColumnType);
        Assert.AreEqual(18, column.Precision);
        Assert.AreEqual(8, column.Scale);
        Assert.IsTrue(column.IsNullable);
        Assert.IsNull(column.DefaultValue);
    }

    [TestMethod]
    public void Down_RemovesOnlyWebSearchPricePerRequest()
    {
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
        typeof(AddWebSearchPricePerRequestToAiPricing)
            .GetMethod("Down", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(new AddWebSearchPricePerRequestToAiPricing(), [builder]);

        var operation = builder.Operations.Single();
        var column = operation as DropColumnOperation;
        Assert.IsNotNull(column);
        Assert.AreEqual("dbo", column.Schema);
        Assert.AreEqual("AiPricing", column.Table);
        Assert.AreEqual("WebSearchPricePerRequest", column.Name);
    }
}
