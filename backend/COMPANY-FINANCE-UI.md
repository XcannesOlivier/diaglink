# Client financial overview

Company Admin navigation: **Crédits et abonnement**. Uses the current machine
selected in the application. Without a selection, included credit is not inferred
from another machine; the company wallet/subscription remain visible.

Endpoints require `CompanyAdminOnly` and derive company identity from the
authenticated `company_id` claim:

- GET `/api/company/finance?machineId=...`: client-safe summary; an out-of-company
  machine returns 404. No provider cost, tokens, model or Stripe identifiers.
- GET `/api/company/finance/topups`: client-safe recharge amount, state and link;
  operation ID is retained for idempotent retries but never rendered.
- POST `/api/company/finance/topups`: `{requestId,amount,currency}` passed to the
  existing CompanyWalletTopUpService with server-derived company identity.

The existing financial rules, payment confirmation, test-only Stripe guard,
amount validation and idempotence are unchanged. The browser persists the request
ID and amount before sending, reuses them after uncertain responses, and opens
only HTTPS checkout.stripe.com links in the same tab. A recharge does not settle
an unpaid subscription invoice; the two amounts are displayed separately.

Super Admin screens/endpoints retain their technical detail. No migration,
deployment, remote Stripe call or Azure SQL change accompanies this UI change.
Stripe return URL remains the existing configured `STRIPE_TOPUP_RETURN_URL`;
no live configuration is enabled here. After payment, use Actualiser to reread
the confirmed wallet balance and credit admission status.
