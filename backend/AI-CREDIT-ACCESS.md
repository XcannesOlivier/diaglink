# AI admission check

`AiCreditAccessService` reads the machine's current [start,end) included budget,
then its company's EUR wallet. `POST /api/chat/stream` calls it after ownership
checks but before provider conversation creation, metadata, messages or streaming.
The same guard covers resumed conversations. Unbound legacy
conversations remain readable but cannot start an unscoped AI call.

No funds are reserved. An accepted request (including its tools/summary) finishes
normally even if it exceeds the balance. Subsequent requests re-read SQL, so a
wallet recharge needs no manual unblock. Already concurrent requests can pass
before their usage is persisted; this MVP is not a distributed reservation system.

Because the existing wallet debit is all-or-nothing, a failed debit can leave a
positive balance. In the wallet branch, already persisted, valuable but uncovered
company usage is calculated with the existing pricing/conversion services and
deducted *virtually* from available credit. The existing x3/6-decimal formula is
shared unchanged with CompanyWalletDebitService. Nothing is debited or retried by
this check. Unpriceable usage is not assigned an invented financial value.
Historical unbilled valuable usage also counts; this can require reconciliation
or additional funding. The included-budget branch remains first priority.

Denial: HTTP 402, `status` and `code` = `AiCreditExhausted`, recharge message.
The chat UI preserves the message, shows the reason and avoids automatic retries.
GET `/api/machines/{machineId}/ai-credit` uses existing machine authorization,
including tenant boundaries; the Machines detail panel shows the reason and a
refresh action for Company Admin/Super Admin and authorized users.
History/conversation read endpoints are not gated. Existing subscription rights
and authorization checks remain independent of financial admission.

No schema change, migration, ledger write or financial calculation change.
