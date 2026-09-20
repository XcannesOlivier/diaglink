# Explicit transactions and SQL Server retries

Program.cs configures SQL Server with EnableRetryOnFailure, hence
SqlServerRetryingExecutionStrategy. Every explicit transaction must be created
inside Database.CreateExecutionStrategy().ExecuteAsync(...), never before it.
The entire transactional unit (reads, locks, mutations, SaveChanges and commit)
must be replayable. This does not replace business locks or isolation.

## Audit and targeted correction

The backend has five explicit transaction sites. No UseTransaction or
TransactionScope sites were found.

| Service | Before correction | Action |
| --- | --- | --- |
| AiCreditConsumptionService | Incompatible: direct Serializable transaction | Whole attempt inside execution strategy |
| CompanyWalletDebitService | Incompatible: direct Serializable transaction | Whole attempt inside execution strategy |
| MachineBillingPeriodService | Incompatible: direct Serializable transaction | Whole attempt inside execution strategy |
| MachineAssignmentService.ReplaceUserMachineAccessAsync | Already inside ExecuteAsync | Unchanged |
| CompanyOnboardingService.OnboardCompanyAsync | Already inside ExecuteAsync | Unchanged |

The two existing nonfinancial wrappers are compatible with the transaction-entry
requirement, but this is not a guarantee of replay safety: they reuse a scoped
tracker; assignment catches DbUpdateException inside the delegate (so those
errors do not reach the strategy); onboarding re-adds the same entities and has
no explicit uncertain-commit recovery. These pre-existing limitations are outside
the targeted correction of the three incompatible financial transactions.

## Financial attempt lifetime

Each service uses a context solely to obtain the configured execution strategy.
The delegate calls a private attempt method which creates and disposes its own
DiagLinkDbContext and Serializable transaction. The delegate also recreates all
mutable result variables. No tracked entity crosses attempts. Disposing a failed
attempt rolls back its uncommitted transaction; the next attempt reloads SQL state.
This avoids retaining Added/Modified entities or applying cached increments twice.
The strategy context performs no reads or writes and opens no transaction.

```csharp
await using var strategyContext = new DiagLinkDbContext(options);
var strategy = strategyContext.Database.CreateExecutionStrategy();
return await strategy.ExecuteAsync(token => AttemptAsync(token), ct);

// Inside AttemptAsync, on EVERY attempt:
await using var db = new DiagLinkDbContext(options);
await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
// Reload, lock, check idempotence, mutate and save.
await tx.CommitAsync(ct);
```

UPDLOCK/HOLDLOCK and Serializable remain unchanged. Machine and wallet ledger
filtered unique indexes, and the unique MachineId/PeriodStartUtc index, remain
unchanged. An uncertain commit followed by a replay finds the committed ledger
(AlreadyProcessed) or period (AlreadyExists), instead of repeating its effect.
Rolled-back attempts may generate new IDs; committed IDs are rediscovered from SQL.
No external side effects are performed in these delegates.

The machine debit, wallet all-or-nothing rule and atomic close/create renewal are
unchanged. There is no global transaction spanning persistence and billing services.
The orchestrator can still resume from CreditLedger. CancellationToken is passed
to ExecuteAsync and every asynchronous database operation; no custom retry loop
or cancellation swallowing is introduced. Only failures classified as transient
by the configured EF strategy are automatically retried, within its retry limit.

## Local regression validation and limits

FinancialExecutionStrategyTests installs the actual SqlServerRetryingExecutionStrategy
through IExecutionStrategyFactory, using exclusively SQLite in-memory storage.
Transaction interceptors assert an active SQL Server strategy, Serializable isolation,
an empty tracker at transaction start and a distinct context for every retry.
Injected TimeoutExceptions cover failure with Added/Modified entities before save,
rollback after SaveChanges before commit, and lost acknowledgement after successful
commit. Tests cover included debit, wallet debit, initial period and renewal, plus
cancellation before commit without retry. Existing tests cover concurrency, rollback,
insufficient balance/recharge, idempotence and orchestration recovery.

Validation: backend solution build succeeded (0 errors, 6 existing NuGet vulnerability
warnings); complete suite: 331 passed, 0 failed, 0 skipped (311 existing + 20 new).

This exercises the real EF retry policy locally, but not SQL Server transactions,
UPDLOCK/HOLDLOCK, Azure connectivity, deadlocks or connection loss on Azure SQL.
The real SqlServerRetryingExecutionStrategy + Azure SQL combination must be validated
in the next controlled test after deployment. No Azure SQL access, deployment,
migration or production usage replay was performed for this correction.
