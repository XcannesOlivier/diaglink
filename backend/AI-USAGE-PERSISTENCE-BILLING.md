# Post-stream usage persistence and billing

POST /api/chat/stream collects ChatResponse and ConversationSummary in the same
measurement dictionary. The summary callback uses its own EventId. Both pass through
one finally block and one coordinator, AiUsagePersistenceBillingService.ProcessAsync.
There is no per-type billing branch. Historical VisionTool records remain readable and
billable through the common SQL, pricing and reporting pipeline.

The normal path sends and flushes WriteDoneEvent before entering finally. Error and
disconnect paths also enter finally to retain identified/completed technical usages.
The coordinator first awaits all repository writes, each with the existing independent
10-second timeout, then awaits billing for each confirmed SQL record with a separate
30-second timeout. RequestAborted is deliberately not used: client disconnect should
not discard persisted usage or cancel its accounting immediately.

AiUsageRepository.RecordAsync now returns AiUsageRecordWriteResult(UsageRecordId, Status):
Persisted after SaveChanges succeeds, AlreadyExists after confirmed EventId lookup,
Failed after a caught write error, Skipped for an uninitiated placeholder. Only Persisted
and AlreadyExists permit ProcessAsync. A concurrent duplicate insert that fails is not
assumed successful; a later retry can confirm AlreadyExists. SQL logic stays in the repository.

No transaction wraps persistence and billing. A crash between them leaves the technical
usage in SQL; retrying the same event can resume billing through AlreadyExists. Financial
services retain their own transactions and unique indexes. No durable retry queue or
automatic historical sweep is added: best-effort failures require a later explicit replay.

All calls are awaited. No Task.Run, detached task or fire-and-forget is used. Billing
exceptions including timeout/cancellation are caught and logged; subsequent usages still
run. Business failures are warning logs, DataInconsistency is Error, successful results
are Information. Technical failures log the exception and identifiers. Logs contain no
prompts, documents, access tokens or connection strings introduced by this integration.

Each billing log includes UsageRecordId, UsageType, CompanyId, MachineId, BillingStatus
and business FailureReason where available, plus real EUR coverage on returned results.
Missing period, missing wallet and insufficient wallet never create resources or reject
the already-produced chat answer. No frontend or commercial access-blocking policy changes.

The normal SSE answer and done event are flushed before financial processing, but the
HTTP handler and DI scope remain alive until finally finishes. Total server request
duration can grow by the awaited per-usage work; timeout cancellation is cooperative,
not a hard background execution cutoff. This tradeoff keeps all exceptions observed.

Validation uses local fake orchestrator outcomes and real repository persistence for
ordering, all usage types and error isolation, plus SQLite replay with the real financial
services. No deployment, Azure SQL test, schema change, wallet creation or period creation.
