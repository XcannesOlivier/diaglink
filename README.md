# DiagLink

DiagLink is a technical-assistance web application for industrial equipment. It combines a
React frontend, an ASP.NET Core API, SQL persistence, Azure Blob Storage and a Claude Sonnet
direct runtime. The application is packaged as one container and deployed to Azure Container Apps.

## Current architecture

- **Frontend:** React 19, TypeScript, Vite and Fluent UI.
- **Backend:** ASP.NET Core on .NET 10, Minimal APIs and Entity Framework Core SQL Server.
- **AI runtime:** Claude Sonnet through the direct Messages API. There is no runtime selector or
  Agent Service fallback.
- **Machine tools:** a machine-owned Foundry project endpoint, Vector Store ID, Blob prefix and
  `toolbox.json` marker configure Toolbox MCP, File Search and `get_page_image`.
- **Persistence:** Azure SQL stores users, companies, machines, conversations, usage and billing
  state. Blob Storage holds uploaded PDFs, branding and technical visual artefacts.
- **Observability:** backend OpenTelemetry and frontend Application Insights feed a shared Log
  Analytics workspace with 30-day retention.

See [ARCHITECTURE-FLOW.md](ARCHITECTURE-FLOW.md) for the verified request and document flows.

## Authentication and roles

The API accepts two authentication mechanisms:

1. Microsoft Entra bearer tokens, used through MSAL when an Entra account is available.
2. DiagLink sessions created after a six-digit email OTP has been verified.

OTP codes expire after ten minutes. The backend stores their HMAC hash, not the plaintext code.
The OTP pepper is deployment configuration and must never be committed.

Exactly three application roles are stored in `dbo.Users.Role`:

| Stored value | Purpose |
|---|---|
| `technician` | Uses assigned machines and their technical assistant. |
| `company_admin` | Manages the users, machines, branding and finances of their own company. |
| `diaglink_super_admin` | Administers companies and platform-wide operational/financial views. |

Authorization remains server-side. Frontend visibility helpers do not replace backend policies.

## Claude Direct and machine documents

`POST /api/chat/stream` authenticates the user, verifies machine access and AI credit, rebuilds
SQL conversation context, resolves the machine configuration and calls
`IClaudeDirectChatRuntime`. The response is streamed with the shared SSE contract.

Toolbox MCP exposes machine-scoped File Search and the local `get_page_image` tool. File Search is
bound to the machine Vector Store; the image tool reads only validated Blob artefacts belonging to
documents visible through the toolbox marker. User chat attachments are PNG or JPEG images only,
with at most five images and 5 MiB per image.

The ingestion implementation that creates the Foundry Vector Store content and Blob derivatives is
external to this repository. The application code verifies only the artefacts it consumes:
`.foundry/toolbox.json`, `page-map.json`, source PDFs, full-page PNGs and optional 3 × 3 page
tiles. Page descriptions can appear in MCP/File Search results, but the existence and naming of
description TXT files cannot be confirmed from this repository. The external generation algorithm,
trigger and retention policy are likewise not confirmed.

## Billing

Claude chat and summary usage is persisted before billing. The billing services consume the
machine's included period budget first and then the company EUR wallet. Stripe test-mode workflows
cover initial machine requests, additional machines, subscriptions and wallet top-ups. Signed
webhooks and server-side Stripe re-reads confirm payments before service rights, periods or wallet
credit are granted.

The detailed invariants and recovery rules are documented in the focused files under `backend/`.

## Prerequisites

- PowerShell 7
- Azure Developer CLI (`azd`)
- Azure CLI
- .NET 10 SDK
- Node.js 22 recommended
- Docker, optional; deployment falls back to an ACR cloud build
- Access to the existing Foundry, Azure SQL, Blob Storage and Stripe test configuration used by the
  target environment

## Initial configuration and deployment

Initialize an `azd` environment, configure the required environment values, then deploy:

```powershell
azd init
azd env set AUTH_OTP_PEPPER <secret-value>
azd env set AZURE_STORAGE_CONNECTION_STRING <secret-value>
azd env set CONTACT_RECIPIENT_ADDRESS <address>
azd env set STRIPE_SECRET_KEY <test-secret>
azd env set STRIPE_MACHINE_REQUEST_WEBHOOK_SECRET <test-webhook-secret>
azd up
```

Do not commit these values. `preprovision.ps1` selects the existing Microsoft Foundry resource
and records its resource group, name and location. Machine-specific project endpoints and Vector
Store IDs remain in SQL; they are not discovered by the deployment hook.

The subscription, machine-addition and wallet-top-up services also read
`STRIPE_PRICE_ID`, `STRIPE_WEBHOOK_SECRET`, `STRIPE_TOPUP_WEBHOOK_SECRET` and
`STRIPE_TOPUP_RETURN_URL`. These values are not injected by the current Bicep template; verify
their approved runtime configuration before exercising those flows. Keep `STRIPE_ALLOW_LIVE`
unset/false: the documented DiagLink workflows are test-mode only.

Useful commands:

| Command | Purpose |
|---|---|
| `azd up` | Provision the declared Azure resources and deploy the application. |
| `azd provision` | Reapply Bicep and post-provision configuration. |
| `azd deploy` | Build and deploy application code. |
| `.\deployment\scripts\start-local-dev.ps1` | Start backend and frontend locally. |
| `.\deployment\scripts\validate-config.ps1` | Validate generated local Entra configuration. |
| `azd down --force --purge` | Remove resources managed by the current `azd` environment. |

No `list-agents.ps1` command exists in the current repository.

## Local development

`azd up` or `postprovision.ps1` generates ignored local authentication files:

- `frontend/.env.local`
- `backend/WebApp.Api/.env`

Start both applications:

```powershell
.\deployment\scripts\start-local-dev.ps1
```

The frontend listens on `http://localhost:5173`; the backend listens on
`http://localhost:8080`. VS Code tasks provide the same two development processes.

Run validation directly when needed:

```powershell
dotnet build backend/WebApp.sln
dotnet test backend/WebApp.Api.Tests/WebApp.Api.Tests.csproj
cd frontend
npm ci
npm run test:run
npm run build
```

## Azure resources and external dependencies

The Bicep templates provision a resource group, Container Registry, Container Apps environment and
application, user-assigned managed identity, two Application Insights resources, one Log Analytics
workspace, Azure Communication Services Email resources and Entra application registrations.

The deployment also uses existing or separately managed services:

- Microsoft Foundry resources and machine projects;
- Azure SQL;
- Azure Blob Storage supplied through deployment configuration;
- Stripe test-mode resources and webhook destinations;
- custom-domain managed certificates named by deployment parameters.

See [infra/README.md](infra/README.md) and [deployment/README.md](deployment/README.md).

## Repository layout

```text
backend/                 ASP.NET Core API, data model, migrations and tests
frontend/                React application and tests
infra/                   Bicep templates
deployment/docker/       production multi-stage Dockerfile
deployment/hooks/        azd lifecycle hooks
deployment/scripts/      local/deployment helper scripts
```

## License

MIT — see [LICENSE](LICENSE). Runtime dependencies retain their own licenses.
