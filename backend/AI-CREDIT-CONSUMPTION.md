# Included machine budget consumption

`AiCreditConsumptionService.ConsumeAsync(usageRecordId, realAiCostEur, currency, ct)`
accepts a positive EUR amount with at most six decimals, already successfully valued and
converted by trusted server-side code. Do not pass a client-provided amount, a failed valuation, or a
conversation aggregate: each call accounts for one AiUsageRecord. Retries must use the
same valued amount.

The active flow is `AiUsagePersistenceBillingService` -> `AiUsageBillingOrchestrator` ->
`AiCreditConsumptionService` -> `CompanyWalletDebitService` when a wallet remainder must
be settled. `AiCreditConsumptionService` consumes only the included machine budget; it
never reads or debits the company wallet itself.

The service reloads the persisted usage, verifies its machine/company ownership and selects
one period using the usage timestamp and [start,end). Status must be `Active` or `Closed`;
overlapping periods are rejected even if one is inactive. No period is created or renewed.
Late usage within a Closed period can debit its historical budget without reopening it.
The normal 10 EUR budget is set by `MachineBillingPeriodService`, not hardcoded here.

Available budget = budget - used. Debit = min(available, cost). Both the increase in used
and the AiUsage/MachineIncluded ledger entry commit in one transaction. BalanceAfter is
the remaining included real EUR budget, not the amount used or a commercial balance.
No zero entry is written. Any remainder returns WalletDebitRequired without accessing
or updating a wallet. CommercialCreditAmount, ExternalEventId and Notes remain NULL.

## Atomicity and concurrency

PostgreSQL retry behavior and fresh contexts per attempt are documented in
[SQL transaction retries](SQL-TRANSACTION-RETRIES.md).

With Npgsql and PostgreSQL 17, `ConsumeAsync` obtains the configured EF execution strategy
and runs the complete attempt inside `ExecuteAsync`. Every attempt creates a new dedicated
`DiagLinkDbContext`, reloads database state and starts a `Serializable` transaction. This
prevents request-local tracked changes or entities from a failed attempt from leaking into a
later attempt.

For this operation, `FinancialRowLocks` issues a parameterized `FOR UPDATE` query only for
the existing `Machines` row. The period and ledger rows do not have their own direct
`FOR UPDATE` query. Cooperating calls for the same machine serialize through the machine
row, and the lock remains held until commit or rollback. SQLite and InMemory test providers
do not reproduce this PostgreSQL row-locking behavior.

The period update and `AiUsage`/`MachineIncluded` ledger insertion share `SaveChanges` and
the same explicit transaction, making those two writes atomic. Exceptions or cancellation
before a successful commit dispose and roll back the attempt. There is no process-local lock.
This transaction does not make the later wallet operation part of the same atomic unit.

The Npgsql configuration enables `EnableRetryOnFailure` with at most five retries and a
maximum delay of ten seconds. PostgreSQL SQLSTATE `40001` (serialization failure) and
`40P01` (deadlock detected) are explicitly retryable, in addition to other transient errors
recognized by Npgsql's classification. A unique-constraint violation is not automatically
replayed, and callers must not retry every infrastructure exception blindly.

After an uncertain commit, replaying the same valued cost can rediscover the committed
ledger and return `AlreadyProcessed`. This limits duplicate machine debits but is not an
absolute recovery guarantee for every connection failure or external writer.

The PostgreSQL partial unique index `UX_CreditLedger_MachineIncludedUsage` applies only to
non-null `AiUsageRecordId` values where `EntryType = AiUsage` and
`BucketType = MachineIncluded`. It protects that combination while allowing a later
`CompanyWallet` entry for the same usage. The corresponding PostgreSQL migration must be
present in the deployed schema; conflicting pre-existing rows would prevent creation of the
index, and this service does not repair historical data.

AlreadyProcessed performs no debit and returns MachineDebitEur=0 plus the existing ledger ID.
RemainingIncludedBudgetEur then reflects that ledger's historical BalanceAfter, not a fresh
balance. The wallet remainder is the caller's original cost minus the recorded machine debit.
An exhausted budget creates no processed marker, as requested; repeating it again returns the
full wallet remainder. Machine-budget idempotence is separate from wallet idempotence, which
is implemented by `CompanyWalletDebitService` and coordinated by the orchestrator.

Success means the included-budget decision completed, not that wallet settlement occurred.
Statuses: Processed, WalletDebitRequired, AlreadyProcessed; business failures include
UsageNotFound, MachineMissing, CompanyMissing, InvalidCurrency, InvalidCost,
BillingPeriodNotFound, BillingPeriodInactive, AmbiguousBillingPeriod, DataInconsistency,
UnsupportedDatabaseProvider. Failures leave supplied cost visible rather than replacing it with zero.

## Orchestration and recovery

The machine and wallet services own distinct transactions. `AiUsageBillingOrchestrator`
reloads committed `CreditLedger` entries between the machine and wallet stages, so a known
intermediate state can be resumed without repeating an already recorded debit. There is no
global machine-wallet transaction, and resumability must not be interpreted as a guarantee
against every failure mode or inconsistent external write.

## Local validation

### SQLite offline tests

SQLite in-memory tests cover amount calculations, statuses, budget limits, rollback, unique
enforcement, idempotence and simulated concurrent calls. The provider-independent retry tests
also inject failures before save, before commit and after commit. SQLite transactions and
constraints are useful regression safeguards, but they do not reproduce PostgreSQL
`FOR UPDATE`, lock scheduling or serialization behavior.

### Npgsql offline tests

`PostgreSqlFinancialConcurrencyTests` validates the generated parameterized `FOR UPDATE`
queries without opening a database connection. It also verifies the configured execution
strategy for SQLSTATE `40001` and `40P01`. These tests validate SQL shape and retry
classification, not real multi-session locking.

### Controlled PostgreSQL tests

`PostgreSqlLiveBackendIntegrationTests` is restricted to the dedicated `diaglink_test`
database and includes real concurrent machine-budget consumption. A controlled PostgreSQL
test database is the appropriate layer for validating real constraints, locks and
serialization failures. These live tests are not part of the reference offline validation
and must never be run against the production database.
