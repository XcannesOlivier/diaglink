# Resumable usage billing

AiUsageBillingOrchestrator.ProcessAsync(usageRecordId, ct) loads the persisted usage,
calls AiCostCalculator once, then AiCostCurrencyConverter once using usage.CreatedAtUtc.
An unvaluable usage or failed conversion stops before any debit, preserving the underlying
FailureReason. No pricing or rate is changed and no period or wallet is created.

Before consumption and after each service call, an AsNoTracking query reloads both AiUsage
ledger buckets. Coverage is the sum of RealAiCost in MachineIncluded and CompanyWallet,
not CommercialCreditAmount. Remaining = converted total - machine coverage - wallet coverage.
Overcoverage, duplicate bucket entries, invalid attribution/currency or an incomplete existing
wallet settlement return DataInconsistency without attempting repair. Zero remainder before
debit returns AlreadyFullyProcessed, including a wallet-only payment with no machine marker.

AiCreditConsumptionService receives the full converted cost. Its own idempotence prevents
repeating the machine debit. AlreadyProcessed does not end the orchestration: the refreshed
ledger determines whether a wallet remainder exists. CompanyWalletDebitService receives only
that positive remainder, retaining its all-or-nothing rule. Missing period is propagated;
missing wallet returns WalletNotFound; insufficient credit returns AwaitingWalletCredit.
After recharge, retrying the original usage settles the outstanding wallet amount only.

There is no enclosing transaction. Each debit commits independently inside its existing
Serializable transaction and targeted lock, with a unique filtered ledger index per bucket.
If wallet processing fails after the machine commit, that machine debit remains valid and
the next invocation resumes from it. Infrastructure exceptions/cancellation propagate;
the caller may retry the whole ProcessAsync. The final ledger read can observe another
invocation's completed payment and reports a settled result rather than a stale shortfall.

These guarantees assume historical rates, prices, usage attribution and period budgets
are not retroactively edited during processing. The service is not a reconciliation engine.
Out-of-band financial/cache modifications require a separate controlled workflow. Without
a transaction across both buckets, a reported pending result is an observation at that read;
a concurrent invocation may complete payment immediately afterwards. Retrying is safe under
normal service operation. Both filtered indexes must be deployed before enabling billing.

Result monetary fields represent cumulative coverage for this usage, not the amounts newly
debited by this invocation. CommercialCreditDebitedEur is the wallet ledger's commercial debit;
WalletBalanceAfterEur is its historical BalanceAfter when paid, or the observed service balance
when awaiting credit. Missing wallet leaves it null. Failed valuation leaves RealAiCostEur
null rather than treating the error as a zero-cost usage.

Successful statuses: ProcessedByMachine, ProcessedByMachineAndWallet, ProcessedByWallet,
AlreadyFullyProcessed. Failures/pending: UsageNotValuable, CurrencyConversionFailed,
BillingPeriodNotFound, AwaitingWalletCredit, WalletNotFound, DataInconsistency and other
explicit debit-service validation statuses. Flags identify missing period/credit.

The chat finally now invokes AiUsagePersistenceBillingService, which persists all captured
usages before invoking this orchestrator for confirmed Persisted or AlreadyExists rows.
No period renewal, recharge or Stripe integration is added. Local SQLite tests run the real services, including
concurrent requests and transaction failures. They do not exercise SQL Server locking hints.
