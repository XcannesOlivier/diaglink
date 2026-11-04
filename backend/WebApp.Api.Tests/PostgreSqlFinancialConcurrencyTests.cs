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
    public void PostgreSqlFinancialLocks_ExcludeSqlServerSyntaxAndRemainParameterized()
    {
        var id = Guid.NewGuid();

        foreach (var query in new[]
                 {
                     FinancialRowLocks.PostgreSqlCompany(id),
                     FinancialRowLocks.PostgreSqlMachine(id),
                     FinancialRowLocks.PostgreSqlWallet(id)
                 })
        {
            StringAssert.EndsWith(query.Format, "FOR UPDATE");
            Assert.IsFalse(query.Format.Contains("UPDLOCK", StringComparison.Ordinal));
            Assert.IsFalse(query.Format.Contains("HOLDLOCK", StringComparison.Ordinal));
            Assert.IsFalse(query.Format.Contains("[dbo]", StringComparison.Ordinal));
            Assert.AreEqual(1, query.ArgumentCount);
            Assert.AreEqual(id, query.GetArgument(0));
            StringAssert.Contains(query.Format, "{0}");
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
