# Paid subscription cycles (Stripe test)

`StripeBillingService` still creates/retrieves the Customer and Subscription.
New subscriptions save the successful payment method on the subscription for
future automatic collection. In Super Admin, **Actualiser le paiement abonnement**
reads the latest invoice and exposes its Stripe-hosted payment page in the same tab.
This read does not grant any budget or initiate a payment.

## Signed notification

Keep the existing destination `POST /api/stripe/webhooks/machine-additions`,
event `invoice.payment_succeeded`, secret `STRIPE_WEBHOOK_SECRET`.
`StripeSubscriptionWebhook` dispatches addition invoices to the existing handler;
subscription invoices must reference the linked subscription through
`parent.subscription_details.subscription` and have billing reason
`subscription_create` or `subscription_cycle`.
Wallet Checkout notifications remain separate and unchanged.

The server re-reads the subscription, invoice, lines and all settled InvoicePayments.
It validates ownership, test mode, EUR, configured price, non-prorated quantity,
29.90 EUR/unit/month excluding tax, the complete paid amount and successful
PaymentIntents. Manual/out-of-band payment or credit balance alone is not accepted.
No budget is granted for an open, processing or otherwise unverified invoice.

## Durable completion

Migration `20260914134817_AddStripeSubscriptionPayments` adds one table only.
It is generated locally and **not applied** by this implementation.
`StripeSubscriptionPayments` stores the paid invoice, first event, settled payment
references/time, amount in EUR cents, cycle and frozen machine IDs.
Unique indexes protect invoice ID, event ID and subscription/cycle start.

The workflow is `PaymentConfirmed → periods created/renewed → Completed`.
Machine periods receive the full 10 EUR included budget. Existing period-service
transactions make each machine idempotent. A crash between machines or before the
final account update leaves a resumable receipt, with the same recipient list.
Completion updates BillingAccount status/cycle without moving it backwards.
No wallet or CreditLedger writes are performed.

Transient failures return 503 for Stripe retries; already completed invoices have
no effect even when delivered with another event ID. An unlinked subscription also
returns 503 (e.g. the notification arrived before provisioning persisted its ID).
Incoherent quantities/cycles or period conflicts require reconciliation, without
automatic repair. An interrupted cycle can contain some already created periods;
replay reuses these periods rather than granting additional budgets.

## Super Admin

`GET /api/companies/{companyId}/stripe/subscription-payment` requires SuperAdmin.
It reads the latest invoice and up to twelve recorded cycles, displaying the paid
amount, dates, `PaymentConfirmed` or `Completed`, and a payment link when open.

## Limits / controlled test still required

- Apply the migration separately before deployment/use.
- Test hosted invoice payment, SCA and a paid renewal with Stripe test clocks.
- For an existing incomplete subscription, use **Créer / récupérer l’abonnement Stripe**
  before payment: it also configures saving the successful payment method for renewal,
  without recreating the subscription or changing its quantity or anchor.
- Only the current active cycle is processed; late/out-of-order historical cycles,
  changed recipients/quantity, discounts and invoice credits require reconciliation.
- A renewal requires existing machine periods; it does not silently bootstrap a
  missing first period. Payment-confirmed receipts freeze active recipients.
- Unpaid invoices, cancellation status and machine activation/deactivation are
  covered by [Stripe lifecycle](STRIPE-LIFECYCLE.md). Refunds and credit notes
  remain outside this implementation.
- No Stripe network call or Azure SQL access is made by the local test suite.
