using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace WebApp.Api.Tests;

[TestClass]
public sealed class DeterministicTestRetryingExecutionStrategyTests
{
    [TestMethod]
    public void SyncOperationWithoutFailureRunsOnce()
    {
        using var fixture = new Fixture();
        var attempts = 0;

        var result = fixture.Strategy.Execute(() =>
        {
            attempts++;
            return 42;
        });

        Assert.AreEqual(42, result);
        Assert.AreEqual(1, attempts);
    }

    [TestMethod]
    public async Task AsyncTimeoutIsRetriedAndThenSucceeds()
    {
        using var fixture = new Fixture();
        var attempts = 0;

        var result = await fixture.Strategy.ExecuteAsync(async () =>
        {
            await Task.Yield();
            if (++attempts == 1)
            {
                throw new TimeoutException("Simulated transient failure.");
            }

            return 42;
        });

        Assert.AreEqual(42, result);
        Assert.AreEqual(2, attempts);
    }

    [TestMethod]
    public void TimeoutStopsAtConfiguredRetryLimit()
    {
        using var fixture = new Fixture();
        var attempts = 0;

        var exception = Assert.ThrowsExactly<RetryLimitExceededException>(() =>
            fixture.Strategy.Execute<int>(() =>
            {
                attempts++;
                throw new TimeoutException("Simulated persistent transient failure.");
            }));

        Assert.AreEqual(DeterministicTestRetryingExecutionStrategy.ConfiguredMaxRetryCount + 1, attempts);
        Assert.IsInstanceOfType<TimeoutException>(exception.InnerException);
    }

    [TestMethod]
    public void InvalidOperationExceptionIsNotRetried()
    {
        using var fixture = new Fixture();
        var attempts = 0;

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            fixture.Strategy.Execute<int>(() =>
            {
                attempts++;
                throw new InvalidOperationException("Simulated non-transient failure.");
            }));

        Assert.AreEqual(1, attempts);
    }

    [TestMethod]
    public void OperationCanceledExceptionIsNotRetried()
    {
        using var fixture = new Fixture();
        var attempts = 0;

        Assert.ThrowsExactly<OperationCanceledException>(() =>
            fixture.Strategy.Execute<int>(() =>
            {
                attempts++;
                throw new OperationCanceledException("Simulated cancellation.");
            }));

        Assert.AreEqual(1, attempts);
    }

    [TestMethod]
    public async Task CanceledTokenIsRespected()
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var attempts = 0;

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            fixture.Strategy.ExecuteAsync(async token =>
            {
                attempts++;
                await Task.Delay(TimeSpan.FromSeconds(1), token);
                return 42;
            }, cancellation.Token));

        Assert.IsTrue(attempts <= 1);
    }

    [TestMethod]
    public void SyncTimeoutIsRetriedAndThenSucceeds()
    {
        using var fixture = new Fixture();
        var attempts = 0;

        var result = fixture.Strategy.Execute(() =>
        {
            if (++attempts == 1)
            {
                throw new TimeoutException("Simulated transient failure.");
            }

            return 42;
        });

        Assert.AreEqual(42, result);
        Assert.AreEqual(2, attempts);
    }

    [TestMethod]
    public async Task AsyncOperationWithoutFailureRunsOnce()
    {
        using var fixture = new Fixture();
        var attempts = 0;

        var result = await fixture.Strategy.ExecuteAsync(async () =>
        {
            attempts++;
            await Task.Yield();
            return 42;
        });

        Assert.AreEqual(42, result);
        Assert.AreEqual(1, attempts);
    }

    [TestMethod]
    public void FactoryCreatesIndependentInstancesWithoutSharedState()
    {
        using var fixture = new Fixture();

        var first = fixture.Context.Database.CreateExecutionStrategy();
        var second = fixture.Context.Database.CreateExecutionStrategy();

        Assert.IsInstanceOfType<DeterministicTestRetryingExecutionStrategy>(first);
        Assert.IsInstanceOfType<DeterministicTestRetryingExecutionStrategy>(second);
        Assert.AreNotSame(first, second);
    }

    [TestMethod]
    public void StrategyWorksWithSqliteInMemory()
    {
        using var fixture = new Fixture();
        fixture.Context.Database.EnsureCreated();

        fixture.Strategy.Execute(() =>
        {
            fixture.Context.Items.Add(new RetryTestEntity { Name = "created" });
            fixture.Context.SaveChanges();
        });

        Assert.AreEqual(1, fixture.Context.Items.Count());
        Assert.AreEqual("created", fixture.Context.Items.Single().Name);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly SqliteConnection _connection;

        public Fixture()
        {
            _connection = new SqliteConnection("Data Source=:memory:");
            _connection.Open();
            var options = new DbContextOptionsBuilder<RetryTestDbContext>()
                .UseSqlite(_connection)
                .ReplaceService<IExecutionStrategyFactory, DeterministicTestRetryingExecutionStrategyFactory>()
                .Options;
            Context = new RetryTestDbContext(options);
            Strategy = Context.Database.CreateExecutionStrategy();
        }

        public RetryTestDbContext Context { get; }

        public IExecutionStrategy Strategy { get; }

        public void Dispose()
        {
            Context.Dispose();
            _connection.Dispose();
        }
    }

    private sealed class RetryTestDbContext(DbContextOptions<RetryTestDbContext> options) : DbContext(options)
    {
        public DbSet<RetryTestEntity> Items => Set<RetryTestEntity>();
    }

    private sealed class RetryTestEntity
    {
        public int Id { get; set; }

        public required string Name { get; set; }
    }
}
