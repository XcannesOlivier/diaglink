# DiagLink Azure infrastructure

The `infra/` directory contains subscription-scope Bicep templates used by Azure Developer CLI.
It provisions the hosting, identity, email and observability resources for the DiagLink web
application. It does not provision every external service consumed by the application.

## Resources provisioned by Bicep

- resource group;
- Azure Container Registry, Basic tier, with admin credentials disabled;
- user-assigned managed identity and ACR pull assignment;
- Log Analytics workspace with 30-day retention;
- separate backend and frontend Application Insights resources connected to that workspace;
- Azure Container Apps environment and one externally accessible Container App;
- Microsoft Entra SPA registration and service principal;
- optional Entra backend registration for the retained OBO compatibility path;
- Azure Communication Services, Email Communication Service, Azure-managed email domain and a
  least-privilege custom sender role;
- Foundry role assignment for the managed identity when the external Foundry resource ID is known.

The Container App runs one image containing both the React bundle and ASP.NET Core API. It uses
0.5 vCPU, 1 GiB memory, zero to three replicas, HTTPS ingress on port 8080 and `/api/health`
startup/liveness probes.

## Existing or separately managed dependencies

The templates refer to, but do not create:

- the Azure SQL server/database named by the managed-identity connection string;
- the Storage account supplied through `AZURE_STORAGE_CONNECTION_STRING`;
- the Microsoft Foundry account, its projects and machine Vector Stores;
- Stripe products, prices, customers, subscriptions and webhook destinations;
- Container Apps managed certificates supplied by name for custom domains.

The external document-ingestion function is not present in these templates. Its compute,
observability and retention settings cannot be inferred from this repository.

## Files

| File | Purpose |
|---|---|
| `main.bicep` | Subscription-scope orchestration and parameters. |
| `main-infrastructure.bicep` | Shared identity, registry, monitoring, Container Apps environment and email modules. |
| `main-app.bicep` | Container App environment variables, secrets, domains, health probes and image. |
| `entra-app.bicep` | Entra registrations and service principals. |
| `foundry-role-assignment.bicep` | Managed-identity role assignment on an existing Foundry resource. |
| `core/host/*.bicep` | Reusable resource modules. |
| `main.parameters.json` | Mapping from `azd` environment values to Bicep parameters. |

The checked-in `*.json` ARM files are compiled representations of the corresponding Bicep
templates. Bicep is the maintainable source.

## Important parameters

| Parameter/environment value | Use |
|---|---|
| `AZURE_ENV_NAME`, `AZURE_LOCATION` | Resource naming and location. |
| `ENTRA_TENANT_ID` | Entra registration tenant. |
| `AUTH_OTP_PEPPER` | Secure HMAC pepper for login codes. |
| `AZURE_STORAGE_CONNECTION_STRING` | Secure Blob Storage configuration. |
| `CONTACT_RECIPIENT_ADDRESS` | Contact destination stored as a Container App secret. |
| `STRIPE_SECRET_KEY` | Stripe test-mode server credential. |
| `STRIPE_MACHINE_REQUEST_WEBHOOK_SECRET` | Signed machine-request webhook verification. |
| `STRIPE_ENABLED` | Enables or disables payment workflows. |
| `CUSTOM_DOMAIN_NAME` and certificate values | Existing managed-certificate bindings. |

The backend additionally reads `STRIPE_PRICE_ID`, `STRIPE_WEBHOOK_SECRET`,
`STRIPE_TOPUP_WEBHOOK_SECRET` and `STRIPE_TOPUP_RETURN_URL` for subscription,
machine-addition and wallet-top-up flows. The current Bicep does not inject those four values.
They must be verified in the approved runtime configuration before those flows are tested; this
documentation does not assume that they are present remotely.

`AI_AGENT_ENDPOINT`, `AI_AGENT_ID` and the optional OBO registration remain in the templates for
deployment compatibility, but the current application chat runtime is Claude Direct and does not
select or call a Foundry Agent.

Machine-specific `ProjectEndpoint`, `VectorStoreId` and `BlobPrefix` values are application data
stored in SQL, not Bicep parameters.

## Deployment

Use the lifecycle hooks declared in `azure.yaml`:

```powershell
azd up
azd provision
azd deploy
```

`preprovision.ps1` selects the existing Foundry account. `postprovision.ps1` updates generated
Entra configuration, assigns RBAC and writes ignored local environment files. See
[deployment/README.md](../deployment/README.md).

For direct template validation without changing Azure:

```powershell
az bicep build --file infra/main.bicep
```

A direct `az deployment sub create` additionally requires every mandatory secure parameter and is
not equivalent to the complete `azd` hook workflow.

## Identity and secrets

- A user-assigned managed identity pulls from ACR, accesses the existing SQL database and receives
  the declared Foundry/email roles.
- OTP, Storage and Stripe values are secure Bicep parameters stored as Container App secrets.
- ACR admin credentials are disabled.
- The production container runs as the non-root `app` user.
- Secret values must be stored in the selected `azd` environment or an approved secret-management
  process; they must not be committed.

## Logs and cost controls

The Container Apps environment sends platform/application console logs to Log Analytics.
Application Insights sends backend OpenTelemetry and browser telemetry to the same workspace.
`core/host/log-analytics.bicep` configures 30-day retention. The templates do not configure
sampling or a daily ingestion cap, so telemetry volume should be monitored during real trials.
