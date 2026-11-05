using Microsoft.EntityFrameworkCore.Storage;

namespace WebApp.Api.Tests;

internal sealed class DeterministicTestRetryingExecutionStrategy(
    ExecutionStrategyDependencies dependencies)
    : ExecutionStrategy(dependencies, ConfiguredMaxRetryCount, TimeSpan.Zero)
{
    internal const int ConfiguredMaxRetryCount = 2;

    protected override bool ShouldRetryOn(Exception exception) => exception is TimeoutException;
}

internal sealed class DeterministicTestRetryingExecutionStrategyFactory(
    ExecutionStrategyDependencies dependencies) : IExecutionStrategyFactory
{
    public IExecutionStrategy Create() => new DeterministicTestRetryingExecutionStrategy(dependencies);
}
