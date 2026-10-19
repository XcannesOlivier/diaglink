# Deployment

The deployment directory contains the production Docker build, Azure Developer CLI lifecycle hooks
and operator-invoked helper scripts.

## Layout

```text
deployment/
├── docker/
│   └── frontend.Dockerfile       React build + .NET 10 publish + ASP.NET runtime
├── hooks/
│   ├── preprovision.ps1          Validate environment and select Foundry resource
│   ├── postprovision.ps1         Entra updates, RBAC and local environment files
│   ├── predeploy.ps1             Build/push image and update Container App
│   ├── postdown.ps1              Post-removal identity cleanup
│   └── modules/
│       ├── Get-AIFoundryAgents.ps1
│       └── HookLogging.ps1
└── scripts/
    ├── deploy.ps1
    ├── smoke-test.js
    ├── start-local-dev.ps1
    └── validate-config.ps1
```

`Get-AIFoundryAgents.ps1` remains available as a standalone helper but is not called by the current
hooks. The current Claude Direct deployment does not select a Foundry Agent.

## Hook workflow

```text
azd up
  ├─ preprovision: validate CLI/session, OTP pepper and existing Foundry account
  ├─ provision: deploy Bicep resources and Entra registrations
  ├─ postprovision: update Entra redirect URIs, assign RBAC, generate local env files
  └─ predeploy: build/push the image and update the Container App

azd deploy
  └─ predeploy

azd down
  └─ postdown after managed resource removal
```

The Entra applications are declared in `infra/entra-app.bicep`; no
`New-EntraAppRegistration.ps1` module exists.

## Build strategy

`predeploy.ps1` uses local Docker when it is available. Otherwise it submits the same build context
to Azure Container Registry. The multi-stage Dockerfile:

1. runs `npm ci` and builds the Vite frontend;
2. restores and publishes the .NET 10 API;
3. copies both into the ASP.NET runtime image and runs as the built-in non-root `app` user.

Frontend Entra and Application Insights values are build arguments because Vite replaces them at
build time. Backend secrets are Container App secrets/environment values and are not baked into the
image.

## Commands

| Command | Purpose |
|---|---|
| `azd up` | Initial provision and deployment. |
| `azd provision` | Reapply infrastructure and post-provision setup. |
| `azd deploy` | Build and deploy code. |
| `.\deployment\scripts\start-local-dev.ps1` | Start local frontend/backend processes. |
| `.\deployment\scripts\validate-config.ps1` | Check generated local auth configuration. |
| `node deployment/scripts/smoke-test.js` | Run the scripted smoke test when its target is configured. |
| `azd down --force --purge` | Remove resources managed by the environment. |

## Local hook logs

Every hook writes a timestamped PowerShell transcript below `.azure/<environment>/logs/`. The
directory is ignored by Git. `HookLogging.ps1` performs a best-effort deletion of transcripts older
than 30 days, restricted to the four hook filename patterns it creates. Cleanup failures emit a
warning and do not stop provisioning or deployment.
