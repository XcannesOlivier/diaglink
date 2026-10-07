---
name: validating-local-setup
description: Diagnose local DiagLink authentication, backend, frontend and Azure setup.
---

# Local setup validation

Run `azd up` once to create the Entra applications, Container Apps infrastructure, RBAC and local
authentication files. The pre-provision hook discovers the existing AI Foundry resource used by
Claude Direct, Toolbox MCP and File Search.

Required frontend values in `frontend/.env.local`:

- `VITE_ENTRA_SPA_CLIENT_ID`
- `VITE_ENTRA_TENANT_ID`
- optional `VITE_ENTRA_BACKEND_CLIENT_ID` for OBO mode.

Required backend authentication values in `backend/WebApp.Api/.env`:

- `AzureAd__TenantId`
- `AzureAd__ClientId`
- `AzureAd__Audience`

Machine-specific project endpoints, Blob prefixes and Vector Store IDs are stored server-side in
SQL and resolved with each machine's `toolbox.json`.

Use `pwsh -File deployment/scripts/validate-config.ps1` for the standard local check. Missing
frontend values produce an undefined login URL; backend JWT mismatches produce HTTP 401/403.
