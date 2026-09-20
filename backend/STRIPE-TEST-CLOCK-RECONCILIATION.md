# Subscription payments with Stripe Test Clocks

Only the existing test-only Stripe subscription gateway supports this override.
Both subscription and invoice must be non-live, reference the same nonempty
`test_clock`, and the clock must be retrieved server-side, non-live and `ready`.
Its `frozen_time` must fall inside the current subscription cycle. This verified
instant replaces UTC wall-clock time for payment/cycle validation only; audit
timestamps remain real UTC. Test objects without a clock retain UTC validation.
Live objects remain rejected by the existing test-only integration.

PaidAt cannot precede the invoiced cycle or exceed the reference instant.
The regular webhook still requires the current active subscription cycle.
An advanced clock does not authorize processing arbitrary historical invoices.
See https://docs.stripe.com/api/test_clocks/object.

## Explicit October reconciliation (not executed)

Run only from an operator-controlled backend scope using the normal DI services,
after separately authorizing the SQL writes. No new endpoint is exposed. Do not
replay the webhook or update SQL manually. Use the test Stripe configuration and
the intended DiagLink database. Read the account and periods before execution.

```csharp
var request = new SubscriptionReconciliationRequest(
    Guid.Parse("705bdf56-ac03-471f-8188-09d1e401084f"),
    Guid.Parse("59096db3-fb2e-4788-a49c-85648e7a458d"),
    "sub_1UFecZIXepHLmuWhUnZL00b6",
    "in_1UGJaEIXepHLmuWhlTyJOreq",
    "MQ83I6VC-0002",
    "evt_1UGJduIXepHLmuWh4ZnLlU9G",
    new DateTime(2026, 10, 14, 18, 28, 51, DateTimeKind.Utc),
    new DateTime(2026, 11, 14, 18, 28, 51, DateTimeKind.Utc),
    2990);
var result = await service.ReconcileTestClockPaymentAsync(request, cancellationToken);
```

The service rereads the invoice, subscription, Test Clock, successful invoice
payments/PaymentIntents and original Stripe event. Company, invoice number/ID,
event, subscription, EUR amount, quantity=1, explicit active recipient and cycle
must match. This path accepts only a historical test-clock renewal ending no
later than the current subscription cycle start. An unknown/expired Stripe event
or deleted clock fails closed; do not bypass verification.

Existing unique invoice/event constraints, company serialization and idempotent
MachineBillingPeriodService renewal protect crash/retry. The frozen recipient is
checked on replay. Expected result: Completed, then AlreadyCompleted on replay.
Stop on ReconciliationRequired or any exception; do not patch data automatically.

Verify one subscription payment receipt and one h4immo period for October-November
(budget 10 EUR, initial consumption zero). No November-December period is created.
BillingAccount is deliberately untouched: past_due, the newer unpaid invoice and
its cycle remain as they were. No wallet, AI usage or ledger is written. Renewal
closes the preceding paid period using the existing period service, preserving
its dates, consumption and ledger history. No migration is needed.
