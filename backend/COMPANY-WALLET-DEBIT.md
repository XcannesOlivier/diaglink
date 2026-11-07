# Company wallet: all-or-nothing payment

PostgreSQL transaction retries, locking and fresh contexts per attempt are documented in
[SQL transaction retries](SQL-TRANSACTION-RETRIES.md).

`CompanyWalletDebitService.DebitAsync` receives the trusted server-side real EUR remainder
after the machine-included budget has been applied. It does not calculate the provider cost.
The commercial debit remains:

```text
Math.Round(real remainder * 3m, 6, MidpointRounding.AwayFromZero)
```

## Validation and all-or-nothing debit

The service accepts EUR amounts that are strictly positive, within the supported bounds and
expressible with at most six decimal places. It also validates the usage record and company,
the optional machine-to-company relationship, and an existing EUR wallet whose balance is
valid, coherent, non-negative and expressible with at most six decimal places.

With sufficient balance, the service debits the entire commercial amount and writes exactly
one ledger entry containing the entire real remainder and commercial debit. No division by
three, partial coverage or proportional payment is performed.

With insufficient balance, the result is `Success=false` with status
`InsufficientWalletBalance`. No ledger is written, the wallet balance and timestamp remain
unchanged, `WalletDebitEur=0`, and `WalletLedgerEntryId=null`.
`WalletBalanceBeforeEur` and `WalletBalanceAfterEur` are equal. `CommercialCreditMissing` is
the commercial amount still missing after the available balance is considered.
`RemainingRealAiCostUncovered` is the full real remainder, while
`RemainingCommercialCreditRequired` is the full unpaid commercial amount. `FailureReason`
remains null for this normal business outcome.

## PostgreSQL transaction and locks

The operation obtains the configured Npgsql strategy with `CreateExecutionStrategy` and runs
the unit through `ExecuteAsync`. Each attempt creates a fresh `DiagLinkDbContext`, opens a
`Serializable` transaction, reloads the required state, checks idempotency, updates the wallet,
inserts the ledger entry, calls `SaveChangesAsync`, and then commits. The wallet update and
ledger insertion are therefore atomic within this operation. They are not part of a single
transaction shared with the machine-included debit.

The PostgreSQL lock order is fixed:

1. the company row in `Companies` with `FOR UPDATE`;
2. the wallet row in `CompanyWallets` with `FOR UPDATE`.

The machine row is not locked directly. When a machine is present, its existence and company
ownership are validated. SQLite tests use the LINQ fallback and therefore do not reproduce
PostgreSQL row-locking semantics. Exceptions roll back the transaction, and cancellation is
propagated through the supplied `CancellationToken`.

## Retry and uncertain commits

Npgsql retry is configured with at most five retries and a maximum delay of ten seconds. The
explicit transient PostgreSQL SQLSTATE values include serialization failure `40001` and
deadlock detected `40P01`; Npgsql can also classify other provider failures as transient.
Not every exception is retryable.

A unique-constraint violation is not automatically treated as `AlreadyProcessed`, and the
service does not blindly retry arbitrary failures. After an uncertain commit, a new attempt
can safely return `AlreadyProcessed` only when it rereads a coherent matching ledger entry.

## Idempotency and ledger constraints

Idempotency is keyed by the same `AiUsageRecordId` with `EntryType=AiUsage` and
`BucketType=CompanyWallet`. A matching existing entry is accepted only when its company,
optional machine, currency, real cost, commercial debit and non-negative `BalanceAfter` are
coherent with the request. The service then returns `AlreadyProcessed` without another debit
or ledger insertion. The returned before/after balances describe the wallet during the current
invocation; they are not necessarily the historical `BalanceAfter` stored by the first call.
An existing entry whose protected values do not match returns `DataInconsistency` instead of
being accepted as an idempotent success.

`UX_CreditLedger_CompanyWalletUsage` is a partial unique PostgreSQL index on non-null
`AiUsageRecordId` for `CompanyWallet/AiUsage`. It protects this debit path, not every type of
credit-ledger entry. An insufficient attempt creates no ledger, so the original input can be
retried after a separately completed recharge. A successful retry then performs the full debit.

There is no general database `CHECK` constraint guaranteeing a non-negative wallet balance.
This service enforces that invariant for its own debit path, but it cannot repair inconsistent
data written by another path.

## Orchestration and Stripe separation

The billing orchestrator does not wrap the machine-included debit and company-wallet debit in
one global transaction. It reloads committed ledger state between stages so processing can
resume from known committed entries.

`CompanyWalletDebitService` does not call Stripe, create wallets or recharge them. Its ledger
entry is `AiUsage / CompanyWallet`. Recharge is handled separately by
`CompanyWalletTopUpService` after payment proof, using `TopUp / CompanyWallet` and distinct
idempotency rules. A top-up credits its commercial amount directly and does not apply the
three-times commercial debit coefficient.

## Validation layers

The guarantees are validated at three different levels:

- Offline SQLite tests cover amount validation, insufficient balance, rollback, idempotency
  and deterministic concurrency/retry scenarios. They do not prove PostgreSQL lock behavior.
- Offline Npgsql tests inspect parameterized `FOR UPDATE` SQL and retry handling for `40001`
  and `40P01` without opening a database connection.
- Controlled real-PostgreSQL tests are required to validate actual concurrent wallet locking,
  indexes, constraints, serialization conflicts and uncertain-commit behavior. Such tests must
  use an isolated `diaglink_test` database and must never target production. They are not part
  of the offline reference suite.
