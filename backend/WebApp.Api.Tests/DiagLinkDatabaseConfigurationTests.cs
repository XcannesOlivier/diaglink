using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using WebApp.Api.Data;

namespace WebApp.Api.Tests;

[TestClass]
public sealed class DiagLinkDatabaseConfigurationTests
{
    private const string SqlServerConnectionString =
        "Server=localhost;Database=diaglink;User Id=test;Password=test;TrustServerCertificate=True";
    private const string PostgreSqlConnectionString =
        "Host=localhost;Database=diaglink;Username=test;Password=test";

    [TestMethod]
    public void Configure_WithoutProvider_PreservesSqlServerAsDefault()
    {
        var options = new DbContextOptionsBuilder<DiagLinkDbContext>();

        DiagLinkDatabaseConfiguration.Configure(options, SqlServerConnectionString, configuredProvider: null);

        using var db = new DiagLinkDbContext(options.Options);
        Assert.AreEqual("Microsoft.EntityFrameworkCore.SqlServer", db.Database.ProviderName);
        Assert.AreEqual("localhost", db.Database.GetDbConnection().DataSource);
        Assert.AreEqual("diaglink", db.Database.GetDbConnection().Database);
        Assert.IsTrue(db.Database.CreateExecutionStrategy().RetriesOnFailure);
    }

    [TestMethod]
    public void Configure_WithPostgreSqlProvider_UsesNpgsql()
    {
        var options = new DbContextOptionsBuilder<DiagLinkDbContext>();

        DiagLinkDatabaseConfiguration.Configure(options, PostgreSqlConnectionString, "PostgreSQL");

        using var db = new DiagLinkDbContext(options.Options);
        Assert.AreEqual("Npgsql.EntityFrameworkCore.PostgreSQL", db.Database.ProviderName);
        StringAssert.Contains(db.Database.GetDbConnection().DataSource, "localhost");
        Assert.AreEqual("diaglink", db.Database.GetDbConnection().Database);
        Assert.IsTrue(db.Database.CreateExecutionStrategy().RetriesOnFailure);
        var configuredConnection = new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString());
        Assert.AreEqual(DiagLinkDatabaseConfiguration.PostgreSqlMaximumPoolSize, configuredConnection.MaxPoolSize);
    }

    [TestMethod]
    public void Configure_WithUnsupportedProvider_ThrowsClearConfigurationError()
    {
        var options = new DbContextOptionsBuilder<DiagLinkDbContext>();

        var exception = Assert.ThrowsExactly<InvalidOperationException>(() =>
            DiagLinkDatabaseConfiguration.Configure(options, SqlServerConnectionString, "Oracle"));

        StringAssert.Contains(exception.Message, DiagLinkDatabaseConfiguration.ProviderEnvironmentVariable);
    }

    [TestMethod]
    public void ProductionPostgreSqlTarget_RequiresProductionDatabaseThroughLoopbackTunnel()
    {
        DiagLinkDatabaseConfiguration.EnsureProductionPostgreSqlTarget(
            "Host=127.0.0.1;Port=15432;Database=diaglink;Username=test;Password=test");

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            DiagLinkDatabaseConfiguration.EnsureProductionPostgreSqlTarget(
                "Host=127.0.0.1;Port=15432;Database=diaglink_test;Username=test;Password=test"));
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            DiagLinkDatabaseConfiguration.EnsureProductionPostgreSqlTarget(
                "Host=postgres.example;Port=5432;Database=diaglink;Username=test;Password=test"));
    }
}
