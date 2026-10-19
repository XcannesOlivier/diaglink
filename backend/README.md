# DiagLink backend

The backend is an ASP.NET Core .NET 10 Minimal API. It serves the production React bundle and owns
authentication, authorization, SQL persistence, Blob access, Claude Direct orchestration, usage
billing, machine requests, email delivery and Stripe workflows.

## Authentication and authorization

The API accepts Microsoft Entra JWT bearer tokens and DiagLink session tokens created by the email
OTP flow. `DiagLinkUserClaimsTransformation` and `DiagLinkSessionAuthenticationHandler` resolve
the active SQL user and produce the same role/company claims for both mechanisms.

The only roles are defined by `Models/DiagLinkRoles.cs`:

- `technician`;
- `company_admin`;
- `diaglink_super_admin`.

Endpoint policies such as `TechnicianOrAbove`, `CompanyAdminOnly` and `SuperAdminOnly` enforce
the role boundary. Machine and company services perform their own ownership checks as well.

## AI runtime

`POST /api/chat/stream` uses Claude Sonnet directly through `IClaudeDirectChatRuntime`. There is no
runtime selector or Agent Service fallback. Machine-scoped `ProjectEndpoint`, `BlobPrefix`,
`VectorStoreId` and `toolbox.json` resolve:

- Toolbox MCP;
- Foundry File Search and the machine Vector Store;
- the local `get_page_image` tool over validated full-page Blob images and 3 × 3 tiles.

SQL is the source of truth for DiagLink conversation history, summaries, usage records and persisted
source/visual references. `/api/agent` returns local Claude Direct metadata.

## Documents

This project uploads request PDFs and reads the technical artefacts used by chat. It does not contain
the external ingestion function that populates File Search or generates document derivatives.
Consequently, only the consumer contract is verifiable here: machine Blob prefixes,
`.foundry/toolbox.json`, `page-map.json`, source PDFs and visual keys following
`{documentId}/page-{page:00000}/{documentId}_page-{page:00000}-full.png` or
`...-tile-rNN-cNN.png`. Page descriptions can be returned through the machine tools, but
description-TXT generation, its filenames, the external trigger and the image-processing algorithm
are not defined in this repository.

## Usage and billing

Provider usage is persisted before the shared billing workflow runs. Billing uses an applicable
machine period's included budget, then the company EUR wallet. Stripe integrations are test-mode
only and use signed webhooks plus server-side verification before recording payments or granting
rights. The focused Markdown files in this directory document transaction and recovery invariants.

## Configuration

`azd` generates `WebApp.Api/.env` for local Entra configuration. Runtime settings also come from
`appsettings*.json`, Container App environment variables and secrets. Important configuration areas
include:

- `ConnectionStrings:DiagLink`;
- `Auth:OtpPepper`;
- `ClaudeDirectChat`;
- Azure Blob Storage;
- email/contact delivery;
- commercial policy and Stripe.

Never commit local `.env` files, connection strings, Stripe secrets or the OTP pepper.

### Temporary File Search diagnostics

`ClaudeDirectChat:FileSearchDiagnostics:Enabled` is `false` by default. When temporarily enabled,
the backend logs only File Search result counts, recognized physical page numbers, the two
diagnostic section identifiers, and document/page pair associations. Document identifiers are
represented by deterministic truncated SHA-256 keys; document content, excerpts, prompts and
responses are never logged. In Azure Container Apps, enable it without changing code:

```powershell
az containerapp update `
  --name <container-app-name> `
  --resource-group <resource-group> `
  --set-env-vars ClaudeDirectChat__FileSearchDiagnostics__Enabled=true
```

After collecting the required request, disable it again with the same command and `false`. The
diagnostic events are `ClaudeDirectFileSearchDiagnostics` and
`ClaudeDirectFileSearchDiagnosticResult`; correlate them with the existing `Trace` field.

## Development and tests

```powershell
dotnet restore backend/WebApp.sln
dotnet build backend/WebApp.sln --no-restore
dotnet test backend/WebApp.Api.Tests/WebApp.Api.Tests.csproj --no-build
```

Tests that require Azure services are opt-in through explicit environment flags. The normal test
suite uses local doubles or in-memory databases and does not deploy resources.
