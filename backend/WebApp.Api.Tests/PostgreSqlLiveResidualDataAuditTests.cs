using Npgsql;

namespace WebApp.Api.Tests;

[TestClass]
[TestCategory("LivePostgreSqlReadOnly")]
[DoNotParallelize]
public sealed class PostgreSqlLiveResidualDataAuditTests
{
    private const string PasswordEnvironmentVariable = "DIAGLINK_TEST_DB_PASSWORD";

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task ReportRowsLeftByPostgreSqlIntegrationTests()
    {
        var password = Environment.GetEnvironmentVariable(PasswordEnvironmentVariable);
        Assert.IsFalse(string.IsNullOrWhiteSpace(password),
            $"Set {PasswordEnvironmentVariable} in the terminal that starts this test.");

        var connectionString = new NpgsqlConnectionStringBuilder
        {
            Host = "127.0.0.1",
            Port = 15432,
            Database = "diaglink_test",
            Username = "diaglink_app",
            Password = password,
            Pooling = false,
            Timeout = 5,
            CommandTimeout = 15,
            ApplicationName = "DiagLink read-only residual-data audit"
        }.ConnectionString;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using (var target = connection.CreateCommand())
        {
            target.CommandText = "SELECT current_database(), current_setting('transaction_read_only')";
            await using var reader = await target.ExecuteReaderAsync();
            Assert.IsTrue(await reader.ReadAsync());
            Assert.AreEqual("diaglink_test", reader.GetString(0),
                "Safety stop: the audit resolved to an unexpected database.");
        }

        await using var transaction = await connection.BeginTransactionAsync();
        await using (var readOnly = connection.CreateCommand())
        {
            readOnly.Transaction = transaction;
            readOnly.CommandText = "SET TRANSACTION READ ONLY";
            await readOnly.ExecuteNonQueryAsync();
        }

        var companyCount = 0;
        await using (var companies = connection.CreateCommand())
        {
            companies.Transaction = transaction;
            companies.CommandText =
                """
                SELECT
                    c."Id",
                    c."Name",
                    (SELECT count(*) FROM "dbo"."Machines" m WHERE m."CompanyId" = c."Id") AS machines,
                    (SELECT count(*) FROM "chat"."AiUsageRecords" u WHERE u."CompanyId" = c."Id") AS usages,
                    (SELECT count(*) FROM "dbo"."MachineBillingPeriods" p
                        JOIN "dbo"."Machines" m ON m."Id" = p."MachineId"
                        WHERE m."CompanyId" = c."Id") AS periods,
                    (SELECT count(*) FROM "dbo"."CompanyWallets" w WHERE w."CompanyId" = c."Id") AS wallets,
                    (SELECT count(*) FROM "dbo"."CreditLedger" l WHERE l."CompanyId" = c."Id") AS ledger,
                    (SELECT count(*) FROM "dbo"."BillingAccounts" b WHERE b."CompanyId" = c."Id") AS billing_accounts,
                    (SELECT count(*) FROM "dbo"."StripeWalletTopUps" t WHERE t."CompanyId" = c."Id") AS topups,
                    (SELECT count(*) FROM "dbo"."StripeMachineAdditions" a WHERE a."CompanyId" = c."Id") AS additions,
                    (SELECT count(*) FROM "dbo"."StripeSubscriptionPayments" s WHERE s."CompanyId" = c."Id") AS subscription_payments,
                    (SELECT count(*) FROM "dbo"."StripeLifecycleEvents" e WHERE e."CompanyId" = c."Id") AS lifecycle_events,
                    (SELECT count(*) FROM "dbo"."MachineRequestPayments" r WHERE r."CompanyId" = c."Id") AS request_payments,
                    (SELECT count(*) FROM "chat"."CompanyBrandings" b WHERE b."CompanyId" = c."Id") AS brandings
                FROM "dbo"."Companies" c
                WHERE c."Name" ~ '^PostgreSQL integration [0-9a-f]{32}$'
                ORDER BY c."CreatedAt"
                """;

            await using var reader = await companies.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                companyCount++;
                TestContext.WriteLine(
                    "CompanyId={0}; Name={1}; Machines={2}; AiUsageRecords={3}; Periods={4}; " +
                    "Wallets={5}; Ledger={6}; BillingAccounts={7}; TopUps={8}; Additions={9}; " +
                    "SubscriptionPayments={10}; LifecycleEvents={11}; RequestPayments={12}; Brandings={13}",
                    reader.GetGuid(0), reader.GetString(1), reader.GetInt64(2), reader.GetInt64(3),
                    reader.GetInt64(4), reader.GetInt64(5), reader.GetInt64(6), reader.GetInt64(7),
                    reader.GetInt64(8), reader.GetInt64(9), reader.GetInt64(10), reader.GetInt64(11),
                    reader.GetInt64(12), reader.GetInt64(13));
            }
        }

        var outboxIds = await ReadIdsAsync(
            connection,
            transaction,
            """
            SELECT "Id"
            FROM "dbo"."EmailOutbox"
            WHERE "RecipientEmail" IN
                ('integration@example.test', 'first@example.test', 'second@example.test')
            ORDER BY "Id"
            """);
        var paymentIds = await ReadIdsAsync(
            connection,
            transaction,
            """
            SELECT "Id"
            FROM "dbo"."MachineRequestPayments"
            WHERE "Email" IN ('first@example.test', 'stale@example.test')
            ORDER BY "Id"
            """);

        TestContext.WriteLine("ResidualIntegrationCompanies={0}", companyCount);
        TestContext.WriteLine("ResidualOutboxIds={0}", FormatIds(outboxIds));
        TestContext.WriteLine("ResidualMachineRequestPaymentIds={0}", FormatIds(paymentIds));

        await transaction.RollbackAsync();
    }

    private static async Task<List<Guid>> ReadIdsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql)
    {
        var ids = new List<Guid>();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) ids.Add(reader.GetGuid(0));
        return ids;
    }

    private static string FormatIds(IReadOnlyCollection<Guid> ids) =>
        ids.Count == 0 ? "none" : string.Join(',', ids);
}
