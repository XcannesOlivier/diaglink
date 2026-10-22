using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebApp.Api.Data;
using WebApp.Api.Models.Entities;

namespace WebApp.Api.Tests;

[TestClass]
public sealed class PostgreSqlMigrationTests
{
    private const string PostgreSqlConnectionString =
        "Host=localhost;Database=diaglink;Username=test;Password=test";
    private const string SqlServerConnectionString =
        "Server=localhost;Database=diaglink;User Id=test;Password=test;TrustServerCertificate=True";

    [TestMethod]
    public void PostgreSqlMigration_IsIndependentAndCreatesAllApplicationTables()
    {
        using var db = CreatePostgreSqlContext();

        var migrations = db.Database.GetMigrations().ToArray();
        Assert.HasCount(1, migrations);
        StringAssert.EndsWith(migrations[0], "_InitialPostgreSql");

        var script = db.GetService<IMigrator>().GenerateScript();
        Assert.AreEqual(25, Regex.Matches(script, "CREATE TABLE (?:dbo|chat)\\.\"").Count);
        StringAssert.Contains(script, "CREATE TABLE dbo.\"Companies\"");
        StringAssert.Contains(script, "CREATE TABLE dbo.\"Users\"");
        StringAssert.Contains(script, "CREATE TABLE dbo.\"Machines\"");
        StringAssert.Contains(script, "CREATE TABLE dbo.\"UserMachines\"");
        StringAssert.Contains(script, "CREATE TABLE IF NOT EXISTS chat.\"__EFMigrationsHistory\"");
    }

    [TestMethod]
    public void PostgreSqlMigration_PreservesFinancialAndIdempotencyRules()
    {
        using var db = CreatePostgreSqlContext();
        var script = db.GetService<IMigrator>().GenerateScript();

        StringAssert.Contains(script, "numeric(18,6)");
        StringAssert.Contains(script, "numeric(18,8)");
        StringAssert.Contains(script, "numeric(18,10)");
        StringAssert.Contains(script, "UX_CreditLedger_MachineIncludedUsage");
        StringAssert.Contains(script, "UX_CreditLedger_CompanyWalletUsage");
        StringAssert.Contains(script, "IX_StripeWalletTopUps_ExternalEventId");
        StringAssert.Contains(script, "IX_MachineRequestPayments_AuthorizationEventId");
        StringAssert.Contains(script, "IX_Users_EntraObjectId_NotNull");
        StringAssert.Contains(script, "IX_Users_Email_Normalized");
        StringAssert.Contains(script, "TR_CreditLedger_TopUpImmutable");
        StringAssert.Contains(script, "protect_credit_ledger_topup_immutable");
        Assert.IsFalse(script.Contains('[', StringComparison.Ordinal));
    }

    [TestMethod]
    public void PostgreSqlModel_UsesXminWithoutCreatingPhysicalXminColumns()
    {
        using var db = CreatePostgreSqlContext();

        AssertXminMapping(db, typeof(EmailOutbox));
        AssertXminMapping(db, typeof(MachineRequestPayment));

        var script = db.GetService<IMigrator>().GenerateScript();
        Assert.IsFalse(script.Contains("\"xmin\" xid", StringComparison.Ordinal));
    }

    [TestMethod]
    public void SqlServerModel_PreservesHistoricalExclusionsAndRowVersion()
    {
        var options = new DbContextOptionsBuilder<DiagLinkDbContext>();
        DiagLinkDatabaseConfiguration.Configure(options, SqlServerConnectionString, "SqlServer");
        using var db = new DiagLinkDbContext(options.Options);
        var designTimeModel = db.GetService<IDesignTimeModel>().Model;

        foreach (var entityType in new[] { typeof(Company), typeof(User), typeof(Machine), typeof(UserMachineAccess) })
        {
            var metadata = designTimeModel.FindEntityType(entityType)!;
            Assert.IsTrue(metadata.IsTableExcludedFromMigrations());
        }

        foreach (var entityType in new[] { typeof(EmailOutbox), typeof(MachineRequestPayment) })
        {
            var metadata = designTimeModel.FindEntityType(entityType)!;
            Assert.IsNotNull(metadata.FindProperty("RowVersion"));
            Assert.IsNull(metadata.FindProperty("PostgreSqlVersion"));
        }
    }

    private static DiagLinkDbContext CreatePostgreSqlContext()
    {
        var options = new DbContextOptionsBuilder<DiagLinkDbContext>();
        DiagLinkDatabaseConfiguration.Configure(options, PostgreSqlConnectionString, "PostgreSql");
        return new DiagLinkDbContext(options.Options);
    }

    private static void AssertXminMapping(DiagLinkDbContext db, Type entityType)
    {
        var metadata = db.Model.FindEntityType(entityType)!;
        Assert.IsNull(metadata.FindProperty("RowVersion"));
        var version = metadata.FindProperty("PostgreSqlVersion");
        Assert.IsNotNull(version);
        Assert.IsTrue(version.IsConcurrencyToken);
        Assert.AreEqual(ValueGenerated.OnAddOrUpdate, version.ValueGenerated);
        Assert.AreEqual("xmin", version.GetColumnName());
    }
}
