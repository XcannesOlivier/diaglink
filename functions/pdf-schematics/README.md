# DiagLink documentary-ingestion Function

This directory contains the source that is deployed to `func-olivier-schematics`. The imported
baseline was verified byte-for-byte against the Azure deployment package before the PostgreSQL
callback change.

The Function still creates and reuses the existing Foundry project, machine vector store, toolbox,
page assets, and File Search files. It no longer opens Azure SQL. Once a document is fully indexed,
it calls:

`POST /api/internal/document-ingestion/finalize`

The request contains only `blobPrefix`, `projectEndpoint`, and `vectorStoreId`. The Function obtains
an app-only Entra token with its system-assigned managed identity. The backend accepts the request
only when both conditions hold:

- the token contains the `DocumentIngestion.Finalize` application role;
- its `oid` is exactly `DocumentIngestion:AllowedCallerObjectId`.

## Required Function settings

- `DIAGLINK_BACKEND_BASE_URL`: the HTTPS origin of the DiagLink backend, without a path;
- `DIAGLINK_BACKEND_AUDIENCE`: `api://<ENTRA_SPA_CLIENT_ID>`;
- the existing Storage, Foundry, Anthropic, and ingestion settings remain unchanged.

No shared secret, PostgreSQL credential, SSH key, or Azure SQL connection string is required by the
callback.

## Safe rollout order

1. Provision the Entra application role and configure the backend allowlist with the Function's
   managed-identity object ID.
2. Assign that application role to the Function managed identity.
3. Deploy the backend and verify that an unauthenticated request is rejected.
4. Add the two Function settings above and deploy this Function package.
5. Ingest a new non-production test machine and verify the callback, PostgreSQL fields, toolbox, and
   vector store.
6. Only after that validation, remove the Function identity's Azure SQL database permissions and
   the obsolete Azure SQL operational configuration.

Do not reingest an existing machine merely to validate this callback. A repeated callback with the
same values is idempotent; a different existing Foundry binding is rejected with HTTP 409.

The authorization can be prepared without a global `azd provision`:

```powershell
pwsh ./deployment/scripts/configure-document-ingestion-auth.ps1 -Mode Preview
```

After reviewing the preview, the explicit production command is:

```powershell
pwsh ./deployment/scripts/configure-document-ingestion-auth.ps1 `
  -Mode Apply `
  -ConfirmProductionChange
```

This targeted script appends the application role without replacing existing roles or redirect
URIs, assigns it to the Function managed identity, adds only the backend caller allowlist variable,
and merges the two callback settings into the Function configuration. It refuses to run outside
`migration/postgresql`, against another subscription, without the PostgreSQL sidecar, or when an
unexpected role or caller is present. It also verifies that Stripe settings and the tunnel image
remain byte-for-byte identical after the Container App update.

Before publishing the Function, download its currently deployed package outside the repository:

```powershell
pwsh ./deployment/scripts/backup-document-ingestion-function.ps1 -Mode Preview
pwsh ./deployment/scripts/backup-document-ingestion-function.ps1 -Mode Download
```

The backup script uses the deployment connection only in process memory, restores the previous
process environment afterwards, and prints only the backup path, size, and SHA-256 hash.

## Local tests

From this directory, with the dependencies installed in an isolated Python environment:

```powershell
python -m unittest discover -v
```

All HTTP, Foundry, Storage, and identity interactions in the targeted tests are mocked.
