using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using WebApp.Api.Data;
using WebApp.Api.Models.Entities;
using WebApp.Api.Services;

namespace WebApp.Api.Tests;

[TestClass]
[TestCategory("LivePostgreSql")]
[DoNotParallelize]
public sealed class PostgreSqlLiveBackendIntegrationTests
{
    private const string PasswordEnvironmentVariable = "DIAGLINK_TEST_DB_PASSWORD";
    private static readonly DateTime PeriodStart = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
    private readonly HashSet<Guid> companyIds = [];
    private readonly HashSet<Guid> outboxIds = [];
    private readonly HashSet<Guid> paymentIds = [];
    private string connectionString = null!;
    private DbContextOptions<DiagLinkDbContext> options = null!;

    [TestInitialize]
    public async Task VerifyExclusiveTestTargetAsync()
    {
        var password = Environment.GetEnvironmentVariable(PasswordEnvironmentVariable);
        Assert.IsFalse(string.IsNullOrWhiteSpace(password),
            $"Set {PasswordEnvironmentVariable} in the terminal that starts this test.");

        connectionString = new NpgsqlConnectionStringBuilder
        {
            Host = "127.0.0.1",
            Port = 15432,
            Database = "diaglink_test",
            Username = "diaglink_app",
            Password = password,
            Pooling = true,
            Timeout = 5,
            CommandTimeout = 15,
            ApplicationName = "DiagLink PostgreSQL integration tests"
        }.ConnectionString;

        var builder = new DbContextOptionsBuilder<DiagLinkDbContext>();
        DiagLinkDatabaseConfiguration.Configure(builder, connectionString, "PostgreSql");
        options = builder.Options;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT current_database()";
        Assert.AreEqual("diaglink_test", (string?)await command.ExecuteScalarAsync(),
            "Safety stop: integration tests resolved to an unexpected database.");

    }

    [TestCleanup]
    public async Task RemoveOnlyRowsCreatedByThisTestAsync()
    {
        if (string.IsNullOrWhiteSpace(connectionString)) return;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await AssertExpectedDatabaseAsync(connection);
        await using var transaction = await connection.BeginTransactionAsync();

        foreach (var id in outboxIds)
            await DeleteByIdAsync(connection, transaction, "EmailOutbox", id);
        foreach (var id in paymentIds)
            await DeleteByIdAsync(connection, transaction, "MachineRequestPayments", id);

        foreach (var companyId in companyIds)
            await RemoveCompanyRowsAsync(connection, transaction, companyId);

        await transaction.CommitAsync();
    }

    [TestMethod]
    public async Task ConcurrentAiCredits_DoNotExceedBudgetOrDuplicateUsageLedger()
    {
        var seed = await SeedFinancialContextAsync(includePeriod: true, walletBalance: null);
        var secondUsageId = Guid.NewGuid();
        await AddUsageAsync(seed.CompanyId, seed.MachineId, secondUsageId);
        var service = new AiCreditConsumptionService(options);

        await Task.WhenAll(
            service.ConsumeAsync(seed.UsageId, 0.7m, "EUR"),
            service.ConsumeAsync(seed.UsageId, 0.7m, "EUR"),
            service.ConsumeAsync(secondUsageId, 0.7m, "EUR"));

        await using var db = new DiagLinkDbContext(options);
        var period = await db.MachineBillingPeriods.SingleAsync(p => p.MachineId == seed.MachineId);
        var ledger = await db.CreditLedger
            .Where(entry => entry.CompanyId == seed.CompanyId && entry.BucketType == "MachineIncluded")
            .ToListAsync();

        Assert.AreEqual(1m, period.IncludedAiUsedRealCost);
        Assert.AreEqual(2, ledger.Count);
        Assert.AreEqual(2, ledger.Select(entry => entry.AiUsageRecordId).Distinct().Count());
        Assert.AreEqual(1m, ledger.Sum(entry => entry.RealAiCost ?? 0m));
    }

    [TestMethod]
    public async Task ConcurrentWalletDebits_NeverProduceNegativeBalanceOrDuplicateLedger()
    {
        var seed = await SeedFinancialContextAsync(includePeriod: false, walletBalance: 6m);
        var secondUsageId = Guid.NewGuid();
        await AddUsageAsync(seed.CompanyId, seed.MachineId, secondUsageId);
        var service = new CompanyWalletDebitService(options);

        var results = await Task.WhenAll(
            service.DebitAsync(seed.UsageId, 1m),
            service.DebitAsync(seed.UsageId, 1m),
            service.DebitAsync(secondUsageId, 1m));

        await using var db = new DiagLinkDbContext(options);
        var wallet = await db.CompanyWallets.SingleAsync(row => row.CompanyId == seed.CompanyId);
        var ledger = await db.CreditLedger
            .Where(entry => entry.CompanyId == seed.CompanyId && entry.BucketType == "CompanyWallet")
            .ToListAsync();

        Assert.AreEqual(0m, wallet.Balance);
        Assert.AreEqual(2, ledger.Count);
        Assert.AreEqual(2, ledger.Select(entry => entry.AiUsageRecordId).Distinct().Count());
        Assert.IsTrue(results.All(result => result.Status is "Processed" or "AlreadyProcessed"));
    }

    [TestMethod]
    public async Task ConcurrentBillingPeriodCreation_CreatesExactlyOnePeriod()
    {
        var seed = await SeedFinancialContextAsync(includePeriod: false, walletBalance: null);
        var service = new MachineBillingPeriodService(options);
        var end = PeriodStart.AddMonths(1);

        var results = await Task.WhenAll(
            service.CreateInitialPeriodAsync(seed.MachineId, PeriodStart, PeriodStart, end),
            service.CreateInitialPeriodAsync(seed.MachineId, PeriodStart, PeriodStart, end));

        await using var db = new DiagLinkDbContext(options);
        Assert.AreEqual(1, await db.MachineBillingPeriods.CountAsync(p => p.MachineId == seed.MachineId));
        CollectionAssert.AreEquivalent(
            new[] { "Created", "AlreadyExists" },
            results.Select(result => result.Status).ToArray());
    }

    [TestMethod]
    public async Task Xmin_RejectsStaleUpdatesForOutboxAndMachineRequestPayment()
    {
        var outboxId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        outboxIds.Add(outboxId);
        paymentIds.Add(paymentId);
        var now = DateTime.UtcNow;

        await using (var seed = new DiagLinkDbContext(options))
        {
            seed.EmailOutbox.Add(new EmailOutbox
            {
                Id = outboxId,
                MachineRequestId = Guid.NewGuid().ToString("N"),
                NotificationType = EmailNotificationType.RequestReceived,
                RecipientEmail = "integration@example.test",
                PayloadJson = "{}",
                Status = EmailOutboxStatus.Pending,
                NextAttemptAtUtc = now,
                CreatedAtUtc = now
            });
            seed.MachineRequestPayments.Add(new WebApp.Api.Models.Entities.MachineRequestPayment
            {
                Id = paymentId,
                Status = MachineRequestPaymentStatus.Pending,
                EstimatedTotalPages = 1,
                AmountCents = 1000,
                Currency = "EUR",
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });
            await seed.SaveChangesAsync();
        }

        await AssertXminConflictAsync(
            db => db.EmailOutbox.SingleAsync(row => row.Id == outboxId),
            row => row.RecipientName = "first",
            row => row.RecipientName = "stale");
        await AssertXminConflictAsync(
            db => db.MachineRequestPayments.SingleAsync(row => row.Id == paymentId),
            row => { row.Email = "first@example.test"; row.UpdatedAtUtc = now.AddSeconds(1); },
            row => { row.Email = "stale@example.test"; row.UpdatedAtUtc = now.AddSeconds(2); });
    }

    [TestMethod]
    public async Task OutboxConcurrentClaims_AreDisjointAndRequireNoEmailSender()
    {
        var requestA = Guid.NewGuid();
        var requestB = Guid.NewGuid();
        await using (var db = new DiagLinkDbContext(options))
        {
            var store = new EmailOutboxStore(db, TimeProvider.System);
            var first = await store.EnqueueAsync(new(
                requestA.ToString("N"), null, EmailNotificationType.RequestReceived,
                "first@example.test", null, new { Test = true }));
            var second = await store.EnqueueAsync(new(
                requestB.ToString("N"), null, EmailNotificationType.RequestReceived,
                "second@example.test", null, new { Test = true }));
            outboxIds.UnionWith([first.Id, second.Id]);
        }

        async Task<ClaimedEmailOutbox> ClaimOneAsync()
        {
            await using var db = new DiagLinkDbContext(options);
            return (await new EmailOutboxStore(db, TimeProvider.System)
                .ClaimEligibleAsync(1, TimeSpan.FromMinutes(5))).Single();
        }

        var claims = await Task.WhenAll(ClaimOneAsync(), ClaimOneAsync());
        Assert.AreEqual(2, claims.Select(claim => claim.Entry.Id).Distinct().Count());
        Assert.IsTrue(claims.All(claim => claim.Entry.LeaseId == claim.LeaseId));
    }

    [TestMethod]
    public async Task StripeTopUpReservation_IsIdempotentWithoutRealStripeCall()
    {
        var seed = await SeedFinancialContextAsync(includePeriod: false, walletBalance: null);
        await using (var db = new DiagLinkDbContext(options))
        {
            db.BillingAccounts.Add(new BillingAccount
            {
                Id = Guid.NewGuid(),
                CompanyId = seed.CompanyId,
                StripeCustomerId = "cus_integration_" + seed.CompanyId.ToString("N"),
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var gateway = new FakeStripeGateway();
        var settings = new StripeBillingOptions
        {
            Enabled = true,
            SecretKey = "sk_test_integration_fake",
            PriceId = "price_integration_fake",
            TopUpWebhookSecret = "whsec_integration_fake",
            TopUpReturnUrl = "http://localhost:5173/"
        };
        var service = new CompanyWalletTopUpService(
            options,
            new StripeBillingService(options, gateway, settings),
            gateway,
            settings);
        var operationId = Guid.NewGuid();

        var results = await Task.WhenAll(
            service.StartAsync(seed.CompanyId, operationId, 20m),
            service.StartAsync(seed.CompanyId, operationId, 20m));

        await using var check = new DiagLinkDbContext(options);
        Assert.AreEqual(1, await check.StripeWalletTopUps.CountAsync(row => row.Id == operationId));
        Assert.AreEqual(1, gateway.CreateCalls);
        Assert.IsTrue(results.All(result => result.Id == operationId && result.Stage == "AwaitingPayment"));
        Assert.AreEqual(0, await check.CreditLedger.CountAsync(row => row.CompanyId == seed.CompanyId));
    }

    [TestMethod]
    public async Task TopUpLedgerTrigger_RejectsMutationAndTransactionRollsBack()
    {
        var seed = await SeedFinancialContextAsync(includePeriod: false, walletBalance: null);
        var ledgerId = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await AssertExpectedDatabaseAsync(connection);
        await using var transaction = await connection.BeginTransactionAsync();

        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText =
                """
                INSERT INTO "dbo"."CreditLedger"
                    ("Id", "CompanyId", "EntryType", "BucketType", "CommercialCreditAmount",
                     "BalanceAfter", "Currency", "ExternalEventId", "CreatedAtUtc")
                VALUES
                    (@id, @companyId, 'TopUp', 'CompanyWallet', 10.000000,
                     10.000000, 'EUR', @eventId, @createdAtUtc)
                """;
            insert.Parameters.AddWithValue("id", ledgerId);
            insert.Parameters.AddWithValue("companyId", seed.CompanyId);
            insert.Parameters.AddWithValue("eventId", "evt_integration_" + ledgerId.ToString("N"));
            insert.Parameters.AddWithValue("createdAtUtc", DateTime.UtcNow);
            await insert.ExecuteNonQueryAsync();
        }

        try
        {
            await using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText =
                "UPDATE \"dbo\".\"CreditLedger\" SET \"CommercialCreditAmount\" = 20 WHERE \"Id\" = @id";
            update.Parameters.AddWithValue("id", ledgerId);
            await update.ExecuteNonQueryAsync();
            Assert.Fail("The PostgreSQL TopUp immutability trigger did not reject the update.");
        }
        catch (PostgresException exception)
        {
            Assert.AreEqual(PostgresErrorCodes.RaiseException, exception.SqlState);
            StringAssert.Contains(exception.MessageText, "TopUp ledger entries are immutable");
        }
        finally
        {
            await transaction.RollbackAsync();
        }

        await using var db = new DiagLinkDbContext(options);
        Assert.IsFalse(await db.CreditLedger.AnyAsync(row => row.Id == ledgerId));
    }

    private async Task<(Guid CompanyId, Guid MachineId, Guid UsageId)> SeedFinancialContextAsync(
        bool includePeriod,
        decimal? walletBalance)
    {
        var companyId = Guid.NewGuid();
        var machineId = Guid.NewGuid();
        var usageId = Guid.NewGuid();
        companyIds.Add(companyId);
        var now = DateTime.UtcNow;

        await using var db = new DiagLinkDbContext(options);
        db.Companies.Add(new Company
        {
            Id = companyId,
            Name = "PostgreSQL integration " + companyId.ToString("N"),
            Status = "active",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });
        db.Machines.Add(new Machine
        {
            Id = machineId,
            CompanyId = companyId,
            Name = "Integration machine",
            Status = "active",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });
        db.AiUsageRecords.Add(CreateUsage(companyId, machineId, usageId));
        if (includePeriod)
        {
            db.MachineBillingPeriods.Add(new MachineBillingPeriod
            {
                Id = Guid.NewGuid(),
                MachineId = machineId,
                PeriodStartUtc = PeriodStart,
                PeriodEndUtc = PeriodStart.AddMonths(1),
                IncludedAiBudgetRealCost = 1m,
                IncludedAiUsedRealCost = 0m,
                Status = "Active",
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });
        }
        if (walletBalance.HasValue)
        {
            db.CompanyWallets.Add(new CompanyWallet
            {
                CompanyId = companyId,
                Balance = walletBalance.Value,
                Currency = "EUR",
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });
        }
        await db.SaveChangesAsync();
        return (companyId, machineId, usageId);
    }

    private async Task AddUsageAsync(Guid companyId, Guid machineId, Guid usageId)
    {
        await using var db = new DiagLinkDbContext(options);
        db.AiUsageRecords.Add(CreateUsage(companyId, machineId, usageId));
        await db.SaveChangesAsync();
    }

    private static AiUsageRecord CreateUsage(Guid companyId, Guid machineId, Guid usageId) => new()
    {
        Id = usageId,
        CompanyId = companyId,
        MachineId = machineId,
        CreatedAtUtc = PeriodStart.AddDays(1)
    };

    private async Task AssertXminConflictAsync<TEntity>(
        Func<DiagLinkDbContext, Task<TEntity>> load,
        Action<TEntity> changeFirst,
        Action<TEntity> changeStale)
        where TEntity : class
    {
        await using var first = new DiagLinkDbContext(options);
        await using var stale = new DiagLinkDbContext(options);
        var firstEntity = await load(first);
        var staleEntity = await load(stale);
        changeFirst(firstEntity);
        await first.SaveChangesAsync();
        changeStale(staleEntity);
        await Assert.ThrowsExactlyAsync<DbUpdateConcurrencyException>(() => stale.SaveChangesAsync());
    }

    private static async Task AssertExpectedDatabaseAsync(NpgsqlConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT current_database()";
        if (!string.Equals((string?)await command.ExecuteScalarAsync(), "diaglink_test", StringComparison.Ordinal))
            throw new InvalidOperationException("Safety stop: cleanup resolved to an unexpected database.");
    }

    private static async Task DeleteByIdAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string table,
        Guid id)
    {
        var allowed = table is "EmailOutbox" or "MachineRequestPayments";
        if (!allowed) throw new ArgumentOutOfRangeException(nameof(table));
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"DELETE FROM \"dbo\".\"{table}\" WHERE \"Id\" = @id";
        command.Parameters.AddWithValue("id", id);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task ExecuteCompanyDeleteAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string table,
        Guid companyId)
    {
        string[] allowedTables =
        [
            "StripeWalletTopUps", "StripeMachineAdditions", "StripeSubscriptionPayments",
            "StripeLifecycleEvents", "CreditLedger", "MachineRequestPayments", "AiUsageRecords",
            "CompanyWallets", "BillingAccounts", "Machines", "CompanyBrandings"
        ];
        if (!allowedTables.Contains(table, StringComparer.Ordinal))
            throw new ArgumentOutOfRangeException(nameof(table));
        var schema = table is "AiUsageRecords" or "CompanyBrandings" ? "chat" : "dbo";
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"DELETE FROM \"{schema}\".\"{table}\" WHERE \"CompanyId\" = @companyId";
        command.Parameters.AddWithValue("companyId", companyId);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task RemoveCompanyRowsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid companyId)
    {
        await ExecuteCompanyDeleteAsync(connection, transaction, "StripeWalletTopUps", companyId);
        await ExecuteCompanyDeleteAsync(connection, transaction, "StripeMachineAdditions", companyId);
        await ExecuteCompanyDeleteAsync(connection, transaction, "StripeSubscriptionPayments", companyId);
        await ExecuteCompanyDeleteAsync(connection, transaction, "StripeLifecycleEvents", companyId);
        await ExecuteCompanyDeleteAsync(connection, transaction, "CreditLedger", companyId);
        await ExecuteCompanyDeleteAsync(connection, transaction, "MachineRequestPayments", companyId);
        await ExecuteCompanyDeleteAsync(connection, transaction, "AiUsageRecords", companyId);
        await ExecuteCompanyDeleteAsync(connection, transaction, "CompanyWallets", companyId);
        await ExecuteCompanyDeleteAsync(connection, transaction, "BillingAccounts", companyId);

        await using (var periods = connection.CreateCommand())
        {
            periods.Transaction = transaction;
            periods.CommandText =
                """
                DELETE FROM "dbo"."MachineBillingPeriods"
                WHERE "MachineId" IN (SELECT "Id" FROM "dbo"."Machines" WHERE "CompanyId" = @companyId)
                """;
            periods.Parameters.AddWithValue("companyId", companyId);
            await periods.ExecuteNonQueryAsync();
        }

        await ExecuteCompanyDeleteAsync(connection, transaction, "Machines", companyId);
        await ExecuteCompanyDeleteAsync(connection, transaction, "CompanyBrandings", companyId);
        await using var company = connection.CreateCommand();
        company.Transaction = transaction;
        company.CommandText = "DELETE FROM \"dbo\".\"Companies\" WHERE \"Id\" = @companyId";
        company.Parameters.AddWithValue("companyId", companyId);
        await company.ExecuteNonQueryAsync();
    }

    private sealed class FakeStripeGateway : IStripeWalletTopUpGateway, IStripeBillingGateway
    {
        private readonly ConcurrentDictionary<Guid, string> sessions = new();
        private int createCalls;

        public int CreateCalls => createCalls;

        public Task<StripeTopUpPayment> CreateAsync(StripeWalletTopUp operation, CancellationToken ct)
        {
            var session = sessions.GetOrAdd(operation.Id, id =>
            {
                Interlocked.Increment(ref createCalls);
                return "cs_test_integration_" + id.ToString("N");
            });
            return Task.FromResult(new StripeTopUpPayment(session, "https://checkout.stripe.com/c/pay/integration-test", "AwaitingPayment"));
        }

        public Task<StripeTopUpPayment> ReadAsync(StripeWalletTopUp operation, string sessionId, CancellationToken ct) =>
            Task.FromResult(new StripeTopUpPayment(sessionId, null, "AwaitingPayment"));

        public Task<string> GetCustomerAsync(string customerId, Guid companyId, CancellationToken ct) =>
            Task.FromResult(customerId);

        public Task<string> CreateCustomerAsync(Guid companyId, Guid accountId, CancellationToken ct) =>
            throw new AssertFailedException("A customer must never be created in this integration test.");

        public Task ValidatePriceAsync(CancellationToken ct) =>
            throw new AssertFailedException("No real Stripe price lookup is allowed.");

        public Task PrepareInitialPaymentAsync(string subscriptionId, string customerId, Guid companyId, CancellationToken ct) =>
            throw new AssertFailedException("No real Stripe payment preparation is allowed.");

        public Task<StripeSubscriptionSnapshot> CreateSubscriptionAsync(
            string customerId, Guid companyId, Guid accountId, int quantity, CancellationToken ct) =>
            throw new AssertFailedException("No real Stripe subscription is allowed.");

        public Task<StripeSubscriptionSnapshot> GetSubscriptionAsync(
            string subscriptionId, string customerId, Guid companyId, CancellationToken ct) =>
            throw new AssertFailedException("No real Stripe subscription read is allowed.");
    }
}
