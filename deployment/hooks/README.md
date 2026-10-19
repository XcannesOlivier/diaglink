# Azure Developer CLI hooks

The scripts in this directory implement the `azd` lifecycle declared in `azure.yaml`.

## Execution order

| Command | Hooks |
|---|---|
| `azd up` | `preprovision` → Bicep provision → `postprovision` → `predeploy` |
| `azd provision` | `preprovision` → Bicep provision → `postprovision` |
| `azd deploy` | `predeploy` |
| `azd down` | Azure removal → `postdown` |

## Hook responsibilities

### `preprovision.ps1`

- validates PowerShell, Azure CLI and `azd` availability;
- verifies the active Azure account and resolves the tenant/environment;
- requires `AUTH_OTP_PEPPER` without printing its value;
- selects an existing Cognitive Services account of kind `AIServices` and records its resource
  group, name and location;
- warns when the application and Foundry regions differ.

It does not discover or select a Foundry Agent. Machine project endpoints, Blob prefixes and Vector
Store IDs remain server-owned SQL configuration.

### `postprovision.ps1`

- reads Bicep outputs;
- updates Entra identifier/redirect configuration that depends on generated IDs and FQDNs;
- configures optional OBO compatibility when enabled;
- assigns the managed identity's Foundry RBAC roles;
- writes ignored `frontend/.env.local` and `backend/WebApp.Api/.env` files;
- opens the deployed URL when the environment permits it.

### `predeploy.ps1`

- builds with local Docker, or uses an ACR cloud build as fallback;
- passes only frontend build-time configuration as Docker arguments;
- pushes the tagged image;
- updates the existing Container App image.

### `postdown.ps1`

Removes identity configuration that is not automatically tied to resource-group deletion. Review
the target `azd` environment before running a destructive teardown.

## Logging and retention

`modules/HookLogging.ps1` creates one PowerShell transcript per run:

```text
.azure/<environment>/logs/yyyyMMdd-HHmmss-<hook>.log
```

At the start of a hook, it deletes files older than 30 days only when their names exactly match one
of the current module-owned suffixes: `preprovision`, `postprovision`, `predeploy` or `postdown`.
Other files and directories are untouched. Cleanup is best-effort; an access or deletion failure is
reported as a warning and does not abort the Azure operation.

Transcripts can contain operational identifiers and command output. They are ignored by Git and
should not be published.

## Modules

- `HookLogging.ps1`: transcript creation, bounded retention and shutdown.
- `Get-AIFoundryAgents.ps1`: standalone paginated REST helper for listing agents from a supplied
  project endpoint. It is not part of the current hook workflow.

## Troubleshooting

| Problem | Check |
|---|---|
| Azure session missing | `az account show` and `azd auth login` |
| OTP pepper rejected | Configure `AUTH_OTP_PEPPER` in the selected `azd` environment. |
| Foundry account not found | Set `AI_FOUNDRY_RESOURCE_NAME` or verify access to an `AIServices` account. |
| Foundry RBAC assignment fails | Verify the deployer can create role assignments on the Foundry resource. |
| Local Docker build fails | Check `docker version`; the hook otherwise attempts an ACR cloud build. |
| Entra policy blocks provisioning | Configure `ENTRA_SERVICE_MANAGEMENT_REFERENCE` when required by the tenant. |
