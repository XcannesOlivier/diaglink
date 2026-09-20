using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebApp.Api.Data;
using WebApp.Api.Models.Entities;
using WebApp.Api.Services;
using Fixture = WebApp.Api.Tests.AiCreditConsumptionServiceTests.Fixture;

namespace WebApp.Api.Tests;

// Real SQL Server retry policy, but exclusively SQLite in-memory connections.
// This tests EF retry boundaries, not SQL Server locking or Azure connectivity.
[TestClass]
public class FinancialExecutionStrategyTests
{
    public sealed class RetryFactory(ExecutionStrategyDependencies dependencies) : IExecutionStrategyFactory
    {
        public IExecutionStrategy Create() => new SqlServerRetryingExecutionStrategy(
            dependencies, 2, TimeSpan.Zero, null);
    }

    private sealed class Probe(string failure, CancellationTokenSource? cancellation = null) : DbTransactionInterceptor
    {
        public List<Guid> ContextIds { get; } = [];
        public int Failures { get; private set; }

        public void Inject(string stage)
        {
            if (failure != stage || Failures != 0) return;
            Failures++;
            if (cancellation != null)
            {
                cancellation.Cancel();
                cancellation.Token.ThrowIfCancellationRequested();
            }
            throw new TimeoutException("Local simulated transient failure: " + stage);
        }

        public override ValueTask<DbTransaction> TransactionStartedAsync(DbConnection connection,
            TransactionEndEventData eventData, DbTransaction result, CancellationToken cancellationToken = default)
        {
            Assert.IsInstanceOfType<SqlServerRetryingExecutionStrategy>(ExecutionStrategy.Current);
            Assert.AreEqual(IsolationLevel.Serializable, result.IsolationLevel);
            Assert.IsNotNull(eventData.Context);
            Assert.AreEqual(0, eventData.Context.ChangeTracker.Entries().Count());
            ContextIds.Add(eventData.Context.ContextId.InstanceId);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            Inject("beforeCommit");
            return ValueTask.FromResult(result);
        }

        public override Task TransactionCommittedAsync(DbTransaction transaction,
            TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            Inject("afterCommit");
            return Task.CompletedTask;
        }
    }

    private sealed class SaveProbe(Probe probe) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Assert.IsTrue(eventData.Context!.ChangeTracker.Entries().Any(e => e.State == EntityState.Added));
            probe.Inject("beforeSave");
            return ValueTask.FromResult(result);
        }
    }

    private static async Task Prepare(Fixture f, string service)
    {
        await f.Seed();
        await using var db = f.Db();
        (await db.Machines.SingleAsync()).Status = "active";
        db.CompanyWallets.Add(new CompanyWallet { CompanyId = f.CompanyId, Balance = 10m, Currency = "EUR" });
        if (service == "initial") db.MachineBillingPeriods.Remove(await db.MachineBillingPeriods.SingleAsync());
        await db.SaveChangesAsync();
    }

    private static async Task<string> Run(Fixture f, DbContextOptions<DiagLinkDbContext> options,
        string service, CancellationToken ct = default)
    {
        var start = new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
        return service switch
        {
            "machine" => (await new AiCreditConsumptionService(options).ConsumeAsync(f.UsageId, 0.1m, "EUR", ct)).Status,
            "wallet" => (await new CompanyWalletDebitService(options).DebitAsync(f.UsageId, 0.1m, "EUR", ct)).Status,
            "initial" => (await new MachineBillingPeriodService(options).CreateInitialPeriodAsync(f.MachineId, start, start, start.AddMonths(1), ct)).Status,
            "renew" => (await new MachineBillingPeriodService(options).RenewPeriodAsync(f.MachineId, start, start.AddMonths(1), ct)).Status,
            _ => throw new ArgumentOutOfRangeException(nameof(service))
        };
    }

    [TestMethod]
    [DataRow("machine", "none")]
    [DataRow("machine", "beforeSave")]
    [DataRow("machine", "beforeCommit")]
    [DataRow("machine", "afterCommit")]
    [DataRow("wallet", "none")]
    [DataRow("wallet", "beforeSave")]
    [DataRow("wallet", "beforeCommit")]
    [DataRow("wallet", "afterCommit")]
    [DataRow("initial", "none")]
    [DataRow("initial", "beforeSave")]
    [DataRow("initial", "beforeCommit")]
    [DataRow("initial", "afterCommit")]
    [DataRow("renew", "none")]
    [DataRow("renew", "beforeSave")]
    [DataRow("renew", "beforeCommit")]
    [DataRow("renew", "afterCommit")]
    public async Task TransactionsUseRetryStrategyAndFreshContextWithoutDuplicateEffects(string service, string failure)
    {
        await using var f = new Fixture();
        await Prepare(f, service);
        var probe = new Probe(failure);
        var options = new DbContextOptionsBuilder<DiagLinkDbContext>(f.Options)
            .ReplaceService<IExecutionStrategyFactory, RetryFactory>()
            .AddInterceptors(probe, new SaveProbe(probe)).Options;
        var status = await Run(f, options, service);
        var isPeriod = service is "initial" or "renew";
        Assert.AreEqual(failure == "afterCommit" ? (isPeriod ? "AlreadyExists" : "AlreadyProcessed")
            : isPeriod ? "Created" : "Processed", status);
        var attempts = failure == "none" ? 1 : 2;
        Assert.AreEqual(attempts, probe.ContextIds.Count);
        Assert.AreEqual(attempts, probe.ContextIds.Distinct().Count());
        Assert.AreEqual(failure == "none" ? 0 : 1, probe.Failures);

        await using var db = f.Db();
        Assert.AreEqual(service == "wallet" ? 9.7m : 10m, (await db.CompanyWallets.SingleAsync()).Balance);
        Assert.AreEqual(isPeriod ? 0 : 1, await db.CreditLedger.CountAsync());
        var periods = await db.MachineBillingPeriods.OrderBy(p => p.PeriodStartUtc).ToListAsync();
        Assert.AreEqual(service == "renew" ? 2 : 1, periods.Count);
        Assert.AreEqual(service == "machine" ? 0.1m : 0m, periods.Sum(p => p.IncludedAiUsedRealCost));
        Assert.AreEqual("Active", periods[^1].Status);
        Assert.AreEqual(10m, periods[^1].IncludedAiBudgetRealCost);
        if (service == "renew") Assert.AreEqual("Closed", periods[0].Status);
        if (!isPeriod)
        {
            var ledger = await db.CreditLedger.SingleAsync();
            Assert.AreEqual(0.1m, ledger.RealAiCost);
            Assert.AreEqual(service == "machine" ? "MachineIncluded" : "CompanyWallet", ledger.BucketType);
            Assert.AreEqual(service == "machine" ? (decimal?)null : 0.3m, ledger.CommercialCreditAmount);
        }
    }

    [TestMethod]
    [DataRow("machine")]
    [DataRow("wallet")]
    [DataRow("initial")]
    [DataRow("renew")]
    public async Task CancellationRollsBackAndIsNotRetried(string service)
    {
        await using var f = new Fixture();
        await Prepare(f, service);
        using var cancellation = new CancellationTokenSource();
        var probe = new Probe("beforeCommit", cancellation);
        var options = new DbContextOptionsBuilder<DiagLinkDbContext>(f.Options)
            .ReplaceService<IExecutionStrategyFactory, RetryFactory>().AddInterceptors(probe).Options;
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => Run(f, options, service, cancellation.Token));
        Assert.AreEqual(1, probe.ContextIds.Count);
        await using var db = f.Db();
        Assert.AreEqual(0, await db.CreditLedger.CountAsync());
        Assert.AreEqual(10m, (await db.CompanyWallets.SingleAsync()).Balance);
        var periods = await db.MachineBillingPeriods.ToListAsync();
        Assert.AreEqual(service == "initial" ? 0 : 1, periods.Count);
        Assert.IsTrue(periods.All(p => p.Status == "Active" && p.IncludedAiUsedRealCost == 0m));
    }
}
