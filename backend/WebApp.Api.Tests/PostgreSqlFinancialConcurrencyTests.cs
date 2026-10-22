using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using WebApp.Api.Data;

namespace WebApp.Api.Tests;

[TestClass]
public sealed class PostgreSqlFinancialConcurrencyTests
{
    private const string ConnectionString =
        "Host=localhost;Database=diaglink;Username=test;Password=test";

    [TestMethod]
    public void PostgreSqlFinancialLocks_UseQuotedForUpdateQueries()
    {
        var id = Guid.NewGuid();

        AssertPostgreSqlLock(FinancialRowLocks.PostgreSqlCompany(id), "\"dbo\".\"Companies\"", "\"Id\"");
        AssertPostgreSqlLock(FinancialRowLocks.PostgreSqlMachine(id), "\"dbo\".\"Machines\"", "\"Id\"");
        AssertPostgreSqlLock(FinancialRowLocks.PostgreSqlWallet(id), "\"dbo\".\"CompanyWallets\"", "\"CompanyId\"");
    }

    [TestMethod]
    public void SqlServerFinancialLocks_ArePreserved()
    {
        var id = Guid.NewGuid();

        foreach (var query in new[]
                 {
                     FinancialRowLocks.SqlServerCompany(id),
                     FinancialRowLocks.SqlServerMachine(id),
                     FinancialRowLocks.SqlServerWallet(id)
                 })
        {
            StringAssert.Contains(query.Format, "WITH (UPDLOCK, HOLDLOCK)");
            Assert.IsFalse(query.Format.Contains("FOR UPDATE", StringComparison.Ordinal));
        }
    }

    [TestMethod]
    [DataRow(PostgresErrorCodes.SerializationFailure)]
    [DataRow(PostgresErrorCodes.DeadlockDetected)]
    public async Task PostgreSqlExecutionStrategy_RetriesWholeOperation(string sqlState)
    {
        var options = new DbContextOptionsBuilder<DiagLinkDbContext>();
        DiagLinkDatabaseConfiguration.Configure(options, ConnectionString, "PostgreSql");
        await using var db = new DiagLinkDbContext(options.Options);
        var attempts = 0;

        var result = await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await Task.Yield();
            if (Interlocked.Increment(ref attempts) == 1)
                throw new PostgresException("simulated transient failure", "ERROR", "ERROR", sqlState);
            return 42;
        });

        Assert.AreEqual(42, result);
        Assert.AreEqual(2, attempts);
    }

    [TestMethod]
    public void PostgreSqlProvider_IsAcceptedByFinancialServices()
    {
        var options = new DbContextOptionsBuilder<DiagLinkDbContext>();
        DiagLinkDatabaseConfiguration.Configure(options, ConnectionString, "PostgreSql");
        using var db = new DiagLinkDbContext(options.Options);

        Assert.IsTrue(FinancialRowLocks.IsSupported(db));
    }

    private static void AssertPostgreSqlLock(FormattableString query, string table, string key)
    {
        StringAssert.Contains(query.Format, $"FROM {table}");
        StringAssert.Contains(query.Format, $"WHERE {key} =");
        StringAssert.EndsWith(query.Format, "FOR UPDATE");
        Assert.IsFalse(query.Format.Contains("UPDLOCK", StringComparison.Ordinal));
    }
}
