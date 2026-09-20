# Machine addition payment webhook

`POST /api/stripe/webhooks/machine-additions` handles only `invoice.payment_succeeded`.
It is anonymous at the application authentication layer but requires a valid
`Stripe-Signature` over the exact body. The SDK's default 300-second tolerance and
API version compatibility check remain enabled. Configure the destination with
the API version expected by the installed Stripe.net SDK (52.1.1).

Required secret: `STRIPE_WEBHOOK_SECRET=whsec_...`, supplied through the existing
secret/environment configuration, never source control. Existing `STRIPE_ENABLED`,
`STRIPE_SECRET_KEY`, `STRIPE_PRICE_ID` and live opt-in still apply. Live/test mismatch
and connected-account events are rejected. Missing configuration returns 503.

The signed invoice is matched by its unique StripeInvoiceId. Company, machine,
BillingAccount/customer/subscription, metadata, EUR, frozen subtotal/excluding-tax
amount, paid status, paid_at and full total payment are checked. The existing
gateway then re-reads the invoice and settled InvoicePayments using read-only
Stripe API calls. No charge is initiated. PaymentReference and PaymentConfirmedAtUtc
are durably saved before the existing period workflow runs. Late payment returns
ReconciliationRequired, preserving proof but granting no expired period.

The first verified event ID is bound under the company transaction to
StripeMachineAdditions.ExternalEventId (nvarchar(200), nullable, unique filtered
index). This requires the new AddStripeMachineAdditionPaymentEvent migration;
the previously applied operation-table migration is unchanged. No financial
ledger entry is fabricated to record webhook delivery. Apply the new migration
in a separately authorized deployment before enabling this endpoint.

An event binding is NOT a completion marker: retries resume the same invoice and
monotonic checkpoints after a crash. A different event for the same invoice also
resumes that operation; the first ID is retained. Completed operations return
AlreadyCompleted without writes or provider calls. Machine-period transactions
and operation uniqueness continue to prevent duplicate budgets under concurrency.
`ResumeInvoicePaymentAsync` also recovers a lost finalization checkpoint after
23 hours by reading confirmed payment; it never retries invoice creation,
finalization or subscription quantity writes.

HTTP responses contain `status`: invalid signature/payload/mode returns 400;
unknown invoice, unrelated event, incomplete signed invoice, inconsistency or late
payment return 200 with UnknownInvoice, Ignored, PaymentIncomplete or
ReconciliationRequired and are logged. Operations not yet quantity-updated, provider
payment not yet confirmed, and transient failures return 503 so Stripe retries.
No payload, signature, secret or raw provider exception is logged. Review terminal
reconciliation logs; they do not trigger automatic data repair.

No wallet top-ups, monthly renewals, payment initiation or frontend integration.
Tests use signed synthetic events, local SQLite and fake provider responses only.

Signature/retry guidance: [Stripe webhooks](https://docs.stripe.com/webhooks).
