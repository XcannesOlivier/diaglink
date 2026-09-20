# Included machine budget consumption

`AiCreditConsumptionService.ConsumeAsync(usageRecordId, realAiCostEur, currency, ct)`
accepts a positive six-decimal EUR amount already successfully valued and converted by
trusted server-side code. Do not pass a client-provided amount, a failed valuation, or a
conversation aggregate: each call accounts for one AiUsageRecord. Retries must use the
same valued amount. No calculator/converter changes, HTTP endpoint, chat hook or worker
are included here. Registering the service does not activate production consumption.

The service reloads the persisted usage, verifies its machine/company ownership and selects
one period using the usage timestamp and [start,end). Status must be `Active` or `Closed`;
overlapping periods are rejected even if one is inactive. No period is created or renewed.
Late usage within a Closed period can debit its historical budget without reopening it.
The normal 10 EUR budget is set by the future period creation service, not hardcoded here.

Available budget = budget - used. Debit = min(available, cost). Both the increase in used
and the AiUsage/MachineIncluded ledger entry commit in one transaction. BalanceAfter is
the remaining included real EUR budget, not the amount used or a commercial balance.
No zero entry is written. Any remainder returns WalletDebitRequired without accessing
or updating a wallet. CommercialCreditAmount, ExternalEventId and Notes remain NULL.

## Atomicity and concurrency

SQL Server retry compatibility and fresh contexts per attempt are documented in
[SQL transaction retries](SQL-TRANSACTION-RETRIES.md).

A dedicated DbContext prevents flushing request-local changes. SQL Server uses a Serializable
transaction and a parameterized Machines lookup with UPDLOCK,HOLDLOCK before checking the
ledger and reading periods. Calls for one machine serialize through the same existing row.
Serializable reads protect ownership, period selection and ledger ranges against concurrent
changes; locks remain held through commit. Period update and ledger insertion share SaveChanges
and the explicit transaction. Exceptions or cancellation dispose/rollback the transaction.
There is no process-local lock. A deadlock, timeout, unique violation or connection failure is
an infrastructure exception, never a successful zero debit. Callers can retry the whole operation;
after an uncertain commit, idempotence prevents a second machine debit.

The separate SQL filtered UNIQUE index on AiUsageRecordId applies only to non-null IDs with
EntryType AiUsage and BucketType MachineIncluded. It protects against competing writers that
do not use this service, while allowing later CompanyWallet entries for the same usage.
The migration must be applied before enabling this service. Existing matching duplicate rows
would make index creation fail; this implementation does not modify historical entries.

AlreadyProcessed performs no debit and returns MachineDebitEur=0 plus the existing ledger ID.
RemainingIncludedBudgetEur then reflects that ledger's historical BalanceAfter, not a fresh
balance. The wallet remainder is the caller's original cost minus the recorded machine debit.
An exhausted budget creates no processed marker, as requested; repeating it again returns the
full wallet remainder. No wallet completion/idempotency is implemented in this stage.

Success means the included-budget decision completed, not that wallet settlement occurred.
Statuses: Processed, WalletDebitRequired, AlreadyProcessed; business failures include
UsageNotFound, MachineMissing, CompanyMissing, InvalidCurrency, InvalidCost,
BillingPeriodNotFound, BillingPeriodInactive, AmbiguousBillingPeriod, DataInconsistency,
UnsupportedDatabaseProvider. Failures leave supplied cost visible rather than replacing it with zero.

## Local validation

SQLite in-memory tests use real transactions, constraints and failure triggers, with separate
connections for concurrency. They validate rollback, unique enforcement and budget limits.
SQLite lock contention is retried at the test caller; this does not exercise SQL Server's
UPDLOCK/HOLDLOCK implementation. No Azure SQL test, production data or deployment is involved.
