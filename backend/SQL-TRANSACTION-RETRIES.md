# Explicit transactions and PostgreSQL retries

DiagLink configures EF Core for PostgreSQL 17 through Npgsql. The active
`EnableRetryOnFailure` policy allows at most five retries, with a maximum delay
of ten seconds. It explicitly adds PostgreSQL SQLSTATE `40001` (serialization
failure) and `40P01` (deadlock detected); Npgsql can also classify other provider
exceptions as transient through its own internal rules. The two explicit codes
must therefore not be interpreted as the complete set of retryable failures.

Every explicit transaction that may be retried must be created inside
`Database.CreateExecutionStrategy().ExecuteAsync(...)`, never before it. The
entire transactional unit (reads, locks, mutations, `SaveChanges` and commit)
must be replay-safe. An EF execution strategy does not make an arbitrary
transaction safe automatically and does not replace isolation, row locks,
idempotency or uniqueness constraints.

## Initial scoped audit and targeted correction

The following table records the initial, deliberately scoped inventory that
motivated corrections to three financial services. It is historical context,
not an exhaustive inventory of every transaction currently present in the
backend.

| Service | Initial finding | Retained pattern |
| --- | --- | --- |
| `AiCreditConsumptionService` | Direct `Serializable` transaction was incompatible with retry | Whole attempt runs inside the configured execution strategy |
| `CompanyWalletDebitService` | Direct `Serializable` transaction was incompatible with retry | Whole attempt runs inside the configured execution strategy |
| `MachineBillingPeriodService` | Direct `Serializable` transaction was incompatible with retry | Whole attempt runs inside the configured execution strategy |
| `MachineAssignmentService.ReplaceUserMachineAccessAsync` | Already wrapped in `ExecuteAsync` | Existing behavior retained |
| `CompanyOnboardingService.OnboardCompanyAsync` | Already wrapped in `ExecuteAsync` | Existing behavior retained |

The two nonfinancial wrappers satisfy the transaction-entry requirement, but
that alone does not prove replay safety. `MachineAssignmentService` reuses a
scoped change tracker and catches `DbUpdateException` inside the delegate, so
those exceptions do not reach the execution strategy.
`CompanyOnboardingService` also reuses a scoped tracker, re-adds the same
entities and has no explicit uncertain-commit recovery. These limitations were
outside the targeted correction of the three financial services and remain
important when those paths are changed.

## Financial transaction attempt lifetime

The pattern below applies specifically to `AiCreditConsumptionService`,
`CompanyWalletDebitService` and `MachineBillingPeriodService`. Each service uses
one context only to obtain the configured execution strategy. The delegate then
creates a fresh `DiagLinkDbContext`, starts a `Serializable` transaction, reloads
the relevant state, acquires the required row locks, performs idempotency checks,
mutates state, calls `SaveChanges` and commits.

No tracked entity crosses attempts. Disposing a failed attempt rolls back its
uncommitted transaction, and a later attempt reloads database state. Mutable
result state is recreated as part of the attempt. The strategy context performs
no reads or writes and opens no transaction.

```csharp
await using var strategyContext = new DiagLinkDbContext(options);
var strategy = strategyContext.Database.CreateExecutionStrategy();
return await strategy.ExecuteAsync(token => AttemptAsync(token), ct);

// Inside AttemptAsync, on every attempt:
await using var db = new DiagLinkDbContext(options);
await using var tx = await db.Database.BeginTransactionAsync(
    IsolationLevel.Serializable,
    ct);
// Reload, lock, check idempotence, mutate and save.
await tx.CommitAsync(ct);
```

`CancellationToken` is passed to `ExecuteAsync` and to the asynchronous database
operations. Cancellation is not converted into a transient failure or swallowed
by a custom retry loop. The three delegates perform no external side effect that
would be repeated by an EF retry.

## PostgreSQL locks, idempotency and concurrency

`FinancialRowLocks` issues parameterized PostgreSQL `FOR UPDATE` queries for
rows in `Companies`, `Machines` and `CompanyWallets`. Callers already own a
transaction, so each acquired lock remains held until commit or rollback. The
SQLite fallback used by local tests performs ordinary LINQ queries and does not
reproduce PostgreSQL row-locking semantics.

The financial paths combine these locks and `Serializable` isolation with
idempotency records and database uniqueness constraints. A replay after an
uncertain commit can rediscover a committed ledger entry as `AlreadyProcessed`
or a billing period as `AlreadyExists`, instead of applying the same debit or
period creation twice. Filtered unique ledger indexes protect the machine and
company-wallet entries for an AI usage record, while the unique
`MachineId`/`PeriodStartUtc` index protects billing-period creation. This design
preserves the documented machine debit, wallet all-or-nothing debit and atomic
period close/create operations; it does not make unrelated operations globally
atomic.

There is deliberately no global transaction in `AiUsageBillingOrchestrator`.
Each debit service owns its transaction, and the orchestrator refreshes
`CreditLedger` state so an interrupted usage can resume from already committed
work.

PostgreSQL optimistic concurrency uses the system `xmin` value for
`EmailOutbox` and `MachineRequestPayment`. SQLite and InMemory test models retain
their `RowVersion` fallback. `xmin` is not the protection mechanism for wallet
balances: wallet safety depends on the explicit transactions, `FOR UPDATE`
locks, idempotency checks and uniqueness constraints described above.

`EmailOutboxStore` is a separate, nonfinancial concurrency path. Eligible rows
are claimed with `FOR UPDATE SKIP LOCKED` inside a `ReadCommitted` transaction,
allowing concurrent workers to reserve disjoint batches. Its lease, status and
retry scheduling rules must not be confused with the `Serializable` financial
transactions.

## Validation layers and known limits

### SQLite offline retry tests

`FinancialExecutionStrategyTests` uses SQLite in memory with
`DeterministicTestRetryingExecutionStrategy`. That test-only strategy retries
only simulated `TimeoutException` failures, allows at most two retries and uses
zero delay. The tests verify a fresh context and `Serializable` transaction per
attempt, cancellation without retry, and no duplicate persisted effect for
failures injected before `SaveChanges`, before commit and after a successful
commit whose acknowledgement is treated as uncertain.

These tests protect retry boundaries, rollback behavior and idempotent replay,
but SQLite does not validate PostgreSQL locks, isolation behavior, SQLSTATE
classification, deadlocks or connection-loss behavior.

### Npgsql offline tests

`PostgreSqlFinancialConcurrencyTests` builds the Npgsql model without opening a
database connection. It verifies parameterized `FOR UPDATE` SQL, the absence of
legacy SQL Server lock syntax, selection of the PostgreSQL provider, and retry
classification for SQLSTATE `40001` and `40P01`.

These tests validate generated configuration and SQL shape only. They do not
prove that two real PostgreSQL sessions block, deadlock, serialize or recover in
the same way under load.

### Controlled PostgreSQL integration tests

`PostgreSqlLiveBackendIntegrationTests` contains the live coverage for real
financial concurrency, `xmin` conflicts and concurrent Outbox claims. It is
categorized as `LivePostgreSql`, requires an explicitly configured test
environment and guards the database name as `diaglink_test`. It is not part of
offline validation and must never be run against the production database.

A controlled PostgreSQL test database is still required to validate actual lock
contention, serialization failures, deadlocks, connection loss, retry delays and
uncertain-commit recovery. Passing offline tests must not be presented as proof
of those production behaviors. Changes to financial transaction boundaries,
lock ordering, idempotency keys or Outbox claiming require review of both the
offline safeguards and the applicable controlled PostgreSQL scenarios.
