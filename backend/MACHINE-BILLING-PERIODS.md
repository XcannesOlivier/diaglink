# Explicit company cycles

SQL Server retry compatibility and fresh contexts per attempt are documented in
[SQL transaction retries](SQL-TRANSACTION-RETRIES.md).

MachineBillingPeriodService receives authoritative cycleStartUtc and cycleEndUtc.
It does not compute anchors, AddMonths, month lengths or Stripe dates. The administrative
workflow (and later contractual integration) must supply the same company cycle to all
machines. Machine activation is an explicit argument, never Machine.CreatedAtUtc.
Dates must form a nonempty [start,end) interval. Local DateTime values are rejected;
Unspecified values represent UTC, consistent with SQL datetime2 materialization.

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
`Active` and `Closed`. Closed means the contractual cycle ended: historical usage within
its bounds may still consume its own remaining budget. AiCreditConsumptionService permits
both statuses and never reopens Closed. Other consumption rules remain unchanged.

Both operations use a dedicated context, Serializable transaction and SQL Server
UPDLOCK,HOLDLOCK on the machine row (the same locking convention as machine consumption).
Overlap checks, closure and insertion run under that lock and commit together. Existing
unique (MachineId,PeriodStartUtc) remains the final duplicate-start protection. It does
not independently prohibit arbitrary overlap writes outside this service; future period
workflows must use this service. Database errors propagate with transaction rollback.

Exact effective start/end match returns AlreadyExists without updates, even for an old
Closed period. Same start with different end returns DataInconsistency. Other overlaps
return OverlappingPeriod. No silent date changes. Results include Success, Status,
FailureReason, MachineId, BillingPeriodId, dates and included/used budgets.
Additional failures: InvalidCycleDates, ActivationOutsideCycle, MachineNotFound,
MachineInactive, InitialPeriodAlreadyExists, PreviousPeriodNotFound, UnsupportedDatabaseProvider.

SQLite in-memory tests validate creation, renewal, rollback, duplicate and overlap concurrency,
and late consumption. They do not execute SQL Server locking hints. No Azure access or
automatic chat integration is involved. No schema or migration change is required.
