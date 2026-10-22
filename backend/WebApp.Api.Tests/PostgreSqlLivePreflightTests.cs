using Npgsql;

namespace WebApp.Api.Tests;

[TestClass]
[TestCategory("LivePostgreSql")]
public sealed class PostgreSqlLivePreflightTests
{
    private const string PasswordEnvironmentVariable = "DIAGLINK_TEST_DB_PASSWORD";

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task TestDatabase_IsExpectedTargetAndContainsNoApplicationTables()
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync();

        await using (var databaseCommand = connection.CreateCommand())
        {
            databaseCommand.CommandText = "SELECT current_database()";
            var databaseName = (string?)await databaseCommand.ExecuteScalarAsync();
            Assert.AreEqual(
                "diaglink_test",
                databaseName,
                "Safety stop: the PostgreSQL connection resolved to an unexpected database.");
        }

        await using (var tableCommand = connection.CreateCommand())
        {
            tableCommand.CommandText =
                """
                SELECT count(*)
                FROM information_schema.tables
                WHERE table_schema IN ('dbo', 'chat')
                  AND table_type = 'BASE TABLE'
                  AND table_name <> '__EFMigrationsHistory'
                """;

            var applicationTableCount = Convert.ToInt32(await tableCommand.ExecuteScalarAsync());
            Assert.AreEqual(
                0,
                applicationTableCount,
                "Safety stop: diaglink_test already contains application tables.");
        }
    }

    [TestMethod]
    public async Task MigratedDatabase_HasExpectedPostgreSqlStructure()
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync();

        Assert.AreEqual("diaglink_test", await ExecuteTextAsync(connection, "SELECT current_database()"));

        var tableCount = await ExecuteCountAsync(
            connection,
            """
            SELECT count(*)
            FROM information_schema.tables
            WHERE table_schema IN ('dbo', 'chat')
              AND table_type = 'BASE TABLE'
              AND table_name <> '__EFMigrationsHistory'
            """);
        var historyCount = await ExecuteCountAsync(
            connection,
            """
            SELECT count(*)
            FROM chat."__EFMigrationsHistory"
            WHERE "MigrationId" = '20261020002202_InitialPostgreSql'
            """);
        var primaryKeyCount = await ExecuteCountAsync(
            connection,
            """
            SELECT count(*)
            FROM information_schema.table_constraints
            WHERE table_schema IN ('dbo', 'chat')
              AND table_name <> '__EFMigrationsHistory'
              AND constraint_type = 'PRIMARY KEY'
            """);
        var foreignKeyCount = await ExecuteCountAsync(
            connection,
            """
            SELECT count(*)
            FROM pg_constraint c
            JOIN pg_namespace n ON n.oid = c.connamespace
            WHERE n.nspname IN ('dbo', 'chat') AND c.contype = 'f'
            """);
        var cascadeForeignKeyCount = await ExecuteCountAsync(
            connection,
            """
            SELECT count(*)
            FROM pg_constraint c
            JOIN pg_namespace n ON n.oid = c.connamespace
            WHERE n.nspname IN ('dbo', 'chat')
              AND c.contype = 'f'
              AND c.confdeltype = 'c'
            """);
        var checkConstraintCount = await ExecuteCountAsync(
            connection,
            """
            SELECT count(*)
            FROM pg_constraint c
            JOIN pg_namespace n ON n.oid = c.connamespace
            WHERE n.nspname IN ('dbo', 'chat')
              AND c.contype = 'c'
              AND c.conname LIKE 'CK_%'
            """);
        var uniqueIndexCount = await ExecuteCountAsync(
            connection,
            """
            SELECT count(*)
            FROM pg_index i
            JOIN pg_class idx ON idx.oid = i.indexrelid
            JOIN pg_class tbl ON tbl.oid = i.indrelid
            JOIN pg_namespace n ON n.oid = tbl.relnamespace
            WHERE n.nspname IN ('dbo', 'chat')
              AND tbl.relname <> '__EFMigrationsHistory'
              AND i.indisunique
              AND NOT i.indisprimary
            """);
        var criticalUniqueIndexCount = await ExecuteCountAsync(
            connection,
            """
            SELECT count(*)
            FROM pg_class idx
            JOIN pg_namespace n ON n.oid = idx.relnamespace
            WHERE n.nspname = 'dbo'
              AND idx.relkind = 'i'
              AND idx.relname IN (
                  'UX_CreditLedger_CompanyWalletUsage',
                  'UX_CreditLedger_MachineIncludedUsage',
                  'IX_MachineRequestPayments_AuthorizationEventId',
                  'IX_StripeWalletTopUps_ExternalEventId',
                  'IX_Users_Email_Normalized',
                  'IX_Users_EntraObjectId_NotNull')
            """);
        var triggerCount = await ExecuteCountAsync(
            connection,
            """
            SELECT count(*)
            FROM pg_trigger t
            JOIN pg_class tbl ON tbl.oid = t.tgrelid
            JOIN pg_namespace n ON n.oid = tbl.relnamespace
            WHERE n.nspname = 'dbo'
              AND tbl.relname = 'CreditLedger'
              AND t.tgname = 'TR_CreditLedger_TopUpImmutable'
              AND NOT t.tgisinternal
            """);

        Assert.AreEqual(25, tableCount, "Unexpected application table count.");
        Assert.AreEqual(1, historyCount, "The expected migration history row is missing.");
        Assert.AreEqual(25, primaryKeyCount, "Every application table must have a primary key.");
        Assert.AreEqual(26, foreignKeyCount, "Unexpected foreign-key count.");
        Assert.AreEqual(5, cascadeForeignKeyCount, "Unexpected cascade-delete foreign-key count.");
        Assert.AreEqual(49, checkConstraintCount, "Unexpected CHECK constraint count.");
        Assert.AreEqual(31, uniqueIndexCount, "Unexpected unique-index count.");
        Assert.AreEqual(6, criticalUniqueIndexCount, "A critical idempotency index is missing.");
        Assert.AreEqual(1, triggerCount, "The immutable TopUp trigger is missing.");

        TestContext.WriteLine(
            $"Tables={tableCount}; PK={primaryKeyCount}; FK={foreignKeyCount}; " +
            $"CascadeFK={cascadeForeignKeyCount}; CHECK={checkConstraintCount}; " +
            $"UniqueIndexes={uniqueIndexCount}; Trigger={triggerCount}; History={historyCount}");
    }

    private static NpgsqlConnection CreateConnection()
    {
        var password = Environment.GetEnvironmentVariable(PasswordEnvironmentVariable);
        Assert.IsFalse(
            string.IsNullOrWhiteSpace(password),
            $"Set {PasswordEnvironmentVariable} in the terminal that starts this test.");

        return new NpgsqlConnection(new NpgsqlConnectionStringBuilder
        {
            Host = "127.0.0.1",
            Port = 15432,
            Database = "diaglink_test",
            Username = "diaglink_app",
            Password = password,
            Pooling = false,
            Timeout = 5,
            CommandTimeout = 10
        }.ConnectionString);
    }

    private static async Task<int> ExecuteCountAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<string?> ExecuteTextAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (string?)await command.ExecuteScalarAsync();
    }
}
