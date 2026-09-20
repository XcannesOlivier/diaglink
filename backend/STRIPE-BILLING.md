# Minimal Stripe billing service

The separate [mid-cycle machine-addition workflow](STRIPE-MACHINE-ADDITION.md)
now handles first-period allocation and one-off invoicing. The methods documented
below retain their original scope; they do not automatically call that workflow.

Server-side only; no HTTP endpoints, webhooks, scheduled synchronization or chat
integration. No wallet writes, CreditLedger writes, machine-period creation or
renewal. Stripe.net 52.1.1 is pinned; no migration is needed.

## Configuration

Set through environment variables or local .NET user-secrets, never committed JSON:

- STRIPE_ENABLED=true (disabled by default)
- STRIPE_SECRET_KEY: Stripe secret/restricted key; never a publishable key
- STRIPE_PRICE_ID: existing price, not created by this service
- STRIPE_ALLOW_LIVE=true only when intentionally enabling a live key (default false)

Price validation requires active EUR 2990 cents, monthly interval count 1,
licensed quantity, per-unit billing, no quantity transformation, exclusive tax,
and a price mode matching the secret key. No Product/Price is created.
No keys or request/response payloads are logged by the service.

## API

- StripeBillingService.GetOrCreateCustomerAsync(companyId, ct): Task<string>
- StripeBillingService.GetOrCreateSubscriptionAsync(companyId, ct):
  Task<StripeSubscriptionSnapshot>

The caller is trusted server-side code. A future HTTP entry point must authorize
company access; do not expose this method directly to untrusted company IDs.
StripeBillingOptions, IStripeBillingGateway and the service are registered in DI.
The gateway is independently testable with the SDK's HTTP transport replaced.

Provisional billing eligibility: company Status=active, machines belonging to that
company with Status=active. No caller-supplied quantity. Zero machines is rejected.
This interpretation must be confirmed before real use; the model has no dedicated
billable flag. SQL comparisons follow the configured database collation.

Creation uses the configured price and the current machine count. An existing
subscription is retrieved and its state synchronized, never recreated, including
when canceled or incomplete. Quantity changes on an existing subscription are NOT
applied here; the returned Quantity is the Stripe quantity, not a changed local count.

BillingAccounts stores customer/subscription IDs, actual subscription status and
current period dates from the single subscription item. No local dates are invented.
Customer and subscription metadata carry diaglink_company_id and are validated on
retrieval. Existing manually-created Stripe objects without this metadata require
explicit reconciliation rather than automatic adoption.

## Creation, idempotence and partial failures

Short Serializable SQL transactions lock the company row with UPDLOCK/HOLDLOCK,
using an EF execution strategy and a new context per attempt. They reserve the
BillingAccount before the remote call and save identifiers after it. No Stripe
call occurs inside a SQL transaction or its retry delegate.

Stable keys: diaglink:customer:{BillingAccountId:N} and
diaglink:subscription:{BillingAccountId:N}. Customer parameters use stable company
metadata, avoiding name-change parameter drift. Concurrent requests share keys;
Stripe may return a conflict while the first request is in progress. Errors and
cancellation propagate; no automatic new object with a different key is attempted.

Before subscription creation, SubscriptionStatus=creation_pending is a LOCAL marker,
not a Stripe status; UpdatedAtUtc records the start of the pending attempt and is
not refreshed on retry. Successful synchronization replaces it with the actual
Stripe status. For customer creation, CreatedAtUtc bounds the pending attempt.
Existing accounts with no customer and old CreatedAtUtc also require reconciliation.
These existing fields suffice for this minimal flow; they are not an event journal.

Stripe can prune idempotency keys after 24 hours. Unconfirmed creation older than
23 hours is refused, rather than risking duplicate creation. If machine count or
price changes during an unresolved attempt, reusing the same key may be rejected
by Stripe because parameters differ; do not invent a new key. Reconcile first.
SQL and Stripe are not atomic; a successful remote operation followed by SQL failure
is recoverable with the same key within the window. Beyond it, manual investigation
is required. There is no automatic remote-object deletion or reconciliation tool.

## Before real use

Confirm machine eligibility, payment collection, tax treatment and the handling of
quantity changes/proration/cancellation. Creation uses charge_automatically and
default_incomplete: without a usable payment method the subscription may be
incomplete. This service does not collect payment details, expose a client secret,
confirm payment or grant any service rights. Creating a subscription is not proof
of payment. Customer billing address and tax IDs are not populated by this foundation.
Paid subscription cycles now synchronize through the signed invoice webhook; see
[subscription payments](STRIPE-SUBSCRIPTION-PAYMENTS.md) for the hosted payment page,
durable processing and remaining limits. The creation service itself never grants budgets.

Local tests use SQLite in memory, fake gateway and mocked SDK HTTP transport.
No real Stripe or Azure SQL calls and no deployment were performed. Validate with
Stripe test mode and a controlled database environment before any live activation.

References: [Stripe subscription creation](https://docs.stripe.com/api/subscriptions/create),
[Stripe idempotency](https://docs.stripe.com/api/idempotent_requests),
[official SDK](https://github.com/stripe/stripe-dotnet).
