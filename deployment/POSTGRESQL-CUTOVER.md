# PostgreSQL production cutover

This runbook is intentionally separate from the live `diaglink_test` tests. Every production
database command is fixed to `diaglink` and requires an explicit confirmation switch.

## Safety invariants

- Work from `migration/postgresql`; never deploy `main` for this cutover.
- Do not put passwords, private keys or connection strings in Git or `azd` values.
- PostgreSQL listens on `127.0.0.1:5432` on Vultr. Port 5432 stays closed in UFW and the Vultr
  firewall. Azure reaches it only through the SSH sidecar.
- Keep the current Azure SQL database until the PostgreSQL revision and administrator login have
  passed the smoke checks.
- The local file named `-` must remain untracked and is excluded from Git and Docker build contexts.

## 1. Harden the dedicated SSH account on Vultr

Create a dedicated `diaglink_tunnel` account with a locked password. Install only its public key.
Use these authorized-key restrictions before the public key itself:

```text
restrict,port-forwarding,permitopen="127.0.0.1:5432" ssh-ed25519 PUBLIC_KEY
```

Apply an sshd `Match User diaglink_tunnel` block:

```text
PasswordAuthentication no
PermitTTY no
X11Forwarding no
AllowAgentForwarding no
AllowTcpForwarding local
PermitOpen 127.0.0.1:5432
GatewayPorts no
```

Validate the sshd configuration before reloading it. Capture the server host key through a trusted
administrative channel; do not use `StrictHostKeyChecking=accept-new` in production.

## 2. Store the three runtime secrets in Azure Key Vault

Use versionless Key Vault secret URIs so rotation does not require a Bicep change:

- complete PostgreSQL connection string;
- dedicated OpenSSH private key;
- pinned `known_hosts` line.

The connection string must be equivalent to:

```text
Host=127.0.0.1;Port=15432;Database=diaglink;Username=diaglink_app;Password=SECRET;Maximum Pool Size=10;Minimum Pool Size=0;Timeout=15;Command Timeout=30;Keepalive=30;SSL Mode=Disable
```

`SSL Mode=Disable` applies only to the loopback leg inside the Container App replica. The external
leg is encrypted and authenticated by SSH. Grant the existing Container App user-assigned identity
the `Key Vault Secrets User` role on these secrets or on the dedicated vault.

The checked-in parameter mapping contains the versionless URIs for `kv-diaglink-pg-2026` and the
Vultr SSH endpoint `65.20.98.165:22`. They remain overridable through these non-secret azd values:

```powershell
azd env set POSTGRESQL_CONNECTION_STRING_SECRET_URI 'https://kv-diaglink-pg-2026.vault.azure.net/secrets/diaglink-postgresql'
azd env set VULTR_SSH_PRIVATE_KEY_SECRET_URI 'https://kv-diaglink-pg-2026.vault.azure.net/secrets/diaglink-vultr-ssh-key'
azd env set VULTR_SSH_KNOWN_HOSTS_SECRET_URI 'https://kv-diaglink-pg-2026.vault.azure.net/secrets/diaglink-vultr-known-hosts'
azd env set VULTR_SSH_HOST '65.20.98.165'
azd env set VULTR_SSH_PORT '22'
azd env set VULTR_SSH_USER 'diaglink_tunnel'
```

Also reconcile the existing Stripe and Claude values before provisioning:

```text
STRIPE_SECRET_KEY
STRIPE_MACHINE_REQUEST_WEBHOOK_SECRET
STRIPE_WEBHOOK_SECRET
STRIPE_TOPUP_WEBHOOK_SECRET
STRIPE_PRICE_ID
STRIPE_TOPUP_RETURN_URL
STRIPE_ENABLED
STRIPE_MACHINE_REQUEST_RETURN_URL
STRIPE_MACHINE_REQUEST_APP_RETURN_URL
CLAUDE_DIRECT_DEPLOYMENT
CLAUDE_FILE_SEARCH_DIAGNOSTICS_ENABLED
```

## 3. Build without changing the running Container App

```powershell
pwsh ./deployment/hooks/predeploy.ps1 -BuildOnly
```

This builds and pushes immutable API and tunnel images and records both image names. It does not
change the running revision. Inspect the Bicep diff next:

Because `infra/main.bicep` is subscription-scoped and its parameters use azd substitutions, run the
preview through azd rather than `az deployment group what-if`:

```powershell
azd provision --preview
```

Do not provision yet. The production schema must exist before a PostgreSQL-configured revision can
start, because background workers access the database immediately.

## 4. Migrate only `diaglink`

Use the already-established local administrative SSH tunnel. Inject the password only in the
current PowerShell process, then run:

```powershell
pwsh ./deployment/scripts/migrate-postgresql-production.ps1 -ConfirmProductionMigration
```

The script refuses any database other than `diaglink`, requires zero application tables, applies
only `20261020002202_InitialPostgreSql`, and checks 25 tables, 25 primary keys, 26 foreign keys,
49 CHECK constraints, 31 non-primary unique indexes, the migration-history row and the immutable
TopUp trigger.

## 5. Create the initial super administrator

After independently verifying the intended email and optional Entra object ID:

```powershell
pwsh ./deployment/scripts/create-postgresql-super-admin.ps1 `
  -Email 'ADMIN_EMAIL' `
  -FirstName 'FIRST_NAME' `
  -LastName 'LAST_NAME' `
  -EntraObjectId 'ENTRA_OBJECT_ID_OR_EMPTY' `
  -ConfirmProductionWrite
```

The script creates a dedicated `DiagLink Administration` company and an active
`diaglink_super_admin` in one transaction. A uniqueness failure rolls back both inserts.

## 6. Provision and deploy

After migration and administrator creation are validated:

```powershell
azd provision
azd deploy
```

The deploy hook refuses an image-only update unless the running template already contains both the
`postgres-tunnel` container and `Database__Provider=PostgreSql`. This prevents the old SQL Server
revision from receiving a PostgreSQL-only image, and prevents a PostgreSQL revision from starting
without its tunnel.

Validate:

1. The tunnel sidecar is ready and has no host-key/authentication errors.
2. `GET /api/health` returns 200.
3. `GET /api/health/ready` returns 200; it performs a read-only database query.
4. API and outbox logs contain no Npgsql connection or schema errors.
5. Super-administrator authentication succeeds.
6. No real Stripe payment is initiated during the smoke test.

Only after these checks and explicit approval may the Azure SQL database be deleted.
