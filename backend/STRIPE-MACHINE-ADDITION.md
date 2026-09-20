# Active machine added during an existing Stripe cycle

`StripeMachineAdditionService.AddActiveMachineAsync(machineId, activatedAtUtc, ct)`
is trusted backend code only. The caller supplies the authoritative UTC activation
instant, not a client-controlled date. The machine is already active; this service
does not create or activate it. No endpoint, webhook, frontend or background trigger.

## Preconditions and calculation

The company and machine must be active, with an existing linked active Stripe
subscription and no prior billing period for this machine. The service calls
StripeBillingService.GetExistingSubscriptionAsync: it never creates a subscription
or customer as a side effect. BillingAccounts' dates must match the live subscription
item dates. Stale cycles are rejected for synchronization. The original quantity
must be smaller than the company's active machine count, preventing an extra
addition when Stripe already covers all active machines.

For full cycle [S,E) and activation A, require S <= A < E and A <= UTC now:

```
service cents = RoundAwayFromZero(1990m * (E.Ticks - A.Ticks) / (E.Ticks - S.Ticks))
AI cents      = 1000
invoice HT    = (1000 + service cents) / 100 EUR
```

The denominator is the actual cycle duration, never a fixed 30-day month. Decimal
arithmetic and UTC ticks avoid intermediate rounding and local daylight-saving
time. Only the service part is rounded, once, to the nearest cent, ties away from
zero. At half-cycle: EUR 9.95 service + EUR 10 AI = EUR 19.95 HT. The full AI budget
is EUR 10 even immediately before cycle end; there is no budget proration/report.

## Workflow and durable checkpoints

1. Reserve immutable inputs in dbo.StripeMachineAdditions: activation/cycle dates,
   customer/subscription/item/price, original quantity, absolute target quantity
   (+1), and amounts. This reservation does not change BillingAccounts.
2. Update the existing subscription item to the stored target quantity with
   proration_behavior=none. Do not send billing_cycle_anchor, price or interval.
   Verify unchanged dates (Stage 1: StripeQuantityUpdated).
3. Create a standalone invoice draft with auto_advance=false and
   pending_invoice_items_behavior=exclude. Persist its ID while remaining at Stage 1.
   Add exactly two invoice items bound explicitly to that invoice: EUR 10 AI and
   the service prorata. Both are EUR, tax exclusive, non-discountable, with [A,E)
   dates and stable keys. Verify ownership, lines and subtotal, then finalize with
   auto_advance=false (Stage 2: InvoiceFinalized).
4. Persist Stage 3: AwaitingPayment. Open, processing or failed payments grant NO
   machine period and NO AI budget.
5. Re-read the same invoice and its paginated payments server-side. Require paid
   status, zero amount remaining, expected pretax total and the entire invoice
   amount settled by Stripe InvoicePayments backed by succeeded PaymentIntents.
   Check customer, currency, identity and metadata. Manual/out-of-band records or
   credit balance alone require reconciliation. Multiple settled PaymentIntents
   are supported. Never accept an arbitrary caller-supplied paid flag.
   Persist PaymentReference (JSON array of InvoicePayment IDs) and
   PaymentConfirmedAtUtc (Stripe paid_at) with Stage 4: PaymentConfirmed.
6. Only after durable payment confirmation, call MachineBillingPeriodService with
   the frozen [A,E) dates and full EUR 10 budget. Persist the period ID with
   Stage 5: MachineBillingPeriodCreated.
7. Persist Stage 6: Completed and CompletedAtUtc.

Final sequence: Reserved → StripeQuantityUpdated → InvoiceFinalized → AwaitingPayment
→ PaymentConfirmed → MachineBillingPeriodCreated → Completed.
No payment initiation, automatic collection or webhook is implemented. A future
verified webhook or reconciliation task can resume this operation; the service
re-verifies the payment with Stripe. Without a resume call, waiting operations do
not advance automatically. Completed means payment confirmed AND period created.

Returned result contains operation, machine, period and invoice IDs, target quantity,
AI and service amounts, and Completed / AlreadyCompleted / AwaitingPayment /
ReconciliationRequired. Invoice/period IDs may be null before their checkpoints.
Unexpected state, Stripe
errors, cancellation and SQL errors propagate; no compensating invoice, period
deletion, budget rollback, or manual ledger correction is performed.

## Why a migration is necessary

Migration **20260911083148_AddStripeMachineAdditionOperations** creates only
dbo.StripeMachineAdditions. This unapplied migration is edited directly, with no
second migration. BillingAccounts cannot hold one operation per machine,
an immutable activation timestamp/prorata and the invoice/checkpoints. The technical
AiUsageRecords and financial CreditLedger are not repurposed as workflow journals.
No existing columns or financial rules change.

- Unique MachineId: one first-cycle addition per machine, including across cycles.
  Reactivation/transfer is intentionally outside this workflow.
- Unique CompanyId filtered on CompletedAtUtc IS NULL: one unfinished operation per
  company. A different machine waits/retries after completion; no parallel lost
  quantity increment. A new reservation also rejects a stale quantity snapshot if
  another company addition completed during its Stripe read.
- Unique non-null StripeInvoiceId; FK links to company, machine, BillingAccount and
  machine period, all Restrict. Check constraints require payment evidence at
  stages 4–6, a period ID at stages 5–6 and CompletedAtUtc only at stage 6.

Short Serializable transactions lock the company row using UPDLOCK/HOLDLOCK.
Every transaction is inside CreateExecutionStrategy().ExecuteAsync with a new
DbContext per attempt. All Stripe calls and period-service calls are outside those
transactions. Checkpoints only advance and conflicting identifiers are rejected.

Remote keys: diaglink:machine-add:{operationId:N}:quantity|invoice|ai|service|finalize.
Replays reuse exact stored parameters, never increment again or recalculate prorata.
The existing period service's unique MachineId/PeriodStartUtc and AlreadyExists
behavior cover period success followed by checkpoint failure. Completed replays
return from SQL without calling Stripe, even long after the idempotency-key window.

Creation steps >=23h old or past cycle end fail closed: Stripe may remove keys
after 24h. AwaitingPayment has NO 23h cutoff; reads and durable confirmation can
run later. A known finalized invoice also recovers a lost finalization checkpoint
by reading its state, without recreating invoice lines.

Payment after E is persisted but returns ReconciliationRequired without creating
an expired period. The same applies when payment occurred before E but processing
resumes after E with no existing period. If a paid attempt already created the
exact period before crashing, its checkpoint is recovered without recreating it.
Frozen dates/money are never shifted to payment/recovery time. Pending operations
continue to block other additions for the company. Do not delete operations or
mint replacement keys; unresolved cases require reconciliation.

External Stripe Dashboard changes or other quantity writers are not coordinated by
the local company lock. Unexpected item, quantity, dates, status or price changes
are rejected where detected; production activation requires one controlled writer
and an explicit reconciliation procedure. Payment failures, VAT configuration,
removals/prorated refunds and renewal remain separate work.

## Validation scope

SQLite in-memory workflow tests simulate lost remote replies, failed SQL checkpoints,
replay, concurrency, expired operations, invalid inputs and exact prorata. SDK tests
replace its HTTP transport and verify quantity/anchor parameters, isolated invoice,
two amounts, tax behavior, confirmation evidence, unpaid states and late payments.
No real Stripe API,
Azure SQL, deployment or migration application is part of this implementation.

References: [Stripe prorations](https://docs.stripe.com/billing/subscriptions/prorations),
[invoice creation](https://docs.stripe.com/api/invoices/create),
[invoice items](https://docs.stripe.com/api/invoiceitems/create),
[idempotency lifetime](https://docs.stripe.com/api/idempotent_requests).
