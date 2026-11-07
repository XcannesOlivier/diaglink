# Explicit company cycles

PostgreSQL 17 transaction retries, locking and fresh contexts per attempt are documented in
[SQL transaction retries](SQL-TRANSACTION-RETRIES.md).

`MachineBillingPeriodService` receives authoritative `cycleStartUtc` and `cycleEndUtc`
boundaries from its caller. It does not compute anchors, call `AddMonths`, derive month lengths
or calculate Stripe dates. The administrative or contractual workflow is responsible for
supplying consistent company cycles to its machines; this service does not verify synchronization
between different machines. Machine activation is an explicit argument, never
`Machine.CreatedAtUtc`.

Dates must form a nonempty `[start, end)` interval, so `end` must be greater than `start`.
`DateTimeKind.Local` values are rejected. `DateTimeKind.Unspecified` currently passes the
application validation, while the PostgreSQL columns use `timestamp with time zone`. Its real
compatibility with Npgsql persistence has not been demonstrated and it must not be treated as a
recommended input; callers should supply authoritative UTC boundaries.

CreateInitialPeriodAsync(machineId, machineActivationUtc, cycleStartUtc, cycleEndUtc, ct)
starts at max(activation,cycleStart) and ends at cycleEnd. Activation at/after cycleEnd
is rejected. Every period receives a full 10 EUR real-cost budget, used=0, status Active.
No prorata, wallet, grant ledger or financial invoice is created.

RenewPeriodAsync(machineId, cycleStartUtc, cycleEndUtc, ct) requires an existing previous
period and a chronological nonoverlapping new cycle. Explicit gaps are not fabricated
or filled. The latest previous period is set Closed if Active, with UpdatedAtUtc updated;
its dates, budget and consumed cost remain intact. Earlier history must already be Closed.
The new period receives 10 EUR independently of the previous balance. No carry-over.
An initial creation on a machine with different existing periods fails rather than becoming
an implicit renewal. The caller controls when the supplied renewal should take effect.

Machine status must equal the existing lowercase `active` convention. Period status uses
`Active` and `Closed`. A renewal closes the latest period when it is still `Active`; the caller
controls when that renewal takes effect. Historical usage within a `Closed` period's bounds may
still consume its own remaining budget. `AiCreditConsumptionService` permits both statuses and
never reopens `Closed`. Other consumption rules remain unchanged.

## PostgreSQL transaction and locking

Both operations obtain the configured Npgsql execution strategy with
`CreateExecutionStrategy` and execute the complete attempt through `ExecuteAsync`. Every
attempt creates a new `DiagLinkDbContext`, starts a `Serializable` transaction, reloads the
machine and its periods, checks idempotence and overlap, performs the closure and insertion,
calls `SaveChangesAsync`, and commits. Closing an existing `Active` period and creating its
replacement are atomic within this transaction. Database errors roll back uncommitted changes,
and the `CancellationToken` is propagated through the asynchronous operations.

The service acquires a parameterized PostgreSQL `FOR UPDATE` lock on the relevant row in
`Machines`. The lock is held until commit or rollback and serializes service operations for the
same machine. No direct `FOR UPDATE` is issued against rows in `MachineBillingPeriods`.

## Npgsql retries

Npgsql `EnableRetryOnFailure` allows at most five retries with a maximum delay of ten seconds.
It explicitly includes SQLSTATE `40001` (serialization failure) and `40P01` (deadlock detected),
and Npgsql may classify other provider errors as transient. This is not a universal retry
guarantee: the complete transactional attempt must remain replay-safe, and arbitrary failures
must not be retried blindly.

Exact effective start/end match returns AlreadyExists without updates, even for an old
Closed period. Same start with different end returns DataInconsistency. Other overlaps
return OverlappingPeriod. No silent date changes. Results include Success, Status,
FailureReason, MachineId, BillingPeriodId, dates and included/used budgets.
Additional failures: InvalidCycleDates, ActivationOutsideCycle, MachineNotFound,
MachineInactive, MachineCompanyMismatch, InitialPeriodAlreadyExists, PreviousPeriodNotFound,
UnsupportedDatabaseProvider.

The PostgreSQL unique index on `(MachineId, PeriodStartUtc)` remains the final duplicate-start
protection. It lets a replay after an uncertain commit rediscover the same period as
`AlreadyExists`, provided the stored boundaries are coherent. The index does not independently
forbid arbitrary overlaps, guarantee only one `Active` period, or protect writes performed
outside this service. Future period workflows must preserve the same locking and validation
rules.

## Validation coverage

SQLite in-memory tests validate creation, renewal, rollback, duplicate and overlap handling,
late consumption and concurrency simulations. They use an ordinary LINQ fallback and do not
reproduce PostgreSQL `FOR UPDATE` or PostgreSQL isolation behavior.

Provider-independent deterministic retry tests validate a fresh context and `Serializable`
transaction per attempt, rollback, cancellation, and replay around save and commit boundaries.
Offline Npgsql tests validate parameterized `FOR UPDATE` generation and retry classification
for SQLSTATE `40001` and `40P01` without opening a database connection.

Controlled real-PostgreSQL coverage runs only against the guarded `diaglink_test` database and
validates concurrent initial creation of exactly one period. It is not part of the offline
reference suite and must never target production. It does not currently prove every concurrent
renewal scenario, all uncertain-commit behavior, or persistence of
`DateTimeKind.Unspecified` values.

No automatic chat integration is involved. No schema or migration change is required for this
documentation update.
