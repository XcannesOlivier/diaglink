#!/usr/bin/env pwsh
# Pre-provision: discovers the AI Foundry resource used by Claude Direct, Toolbox MCP and File Search.
# Entra app registration is handled declaratively by Bicep (infra/entra-app.bicep).

$ErrorActionPreference = "Stop"
$env:PYTHONIOENCODING = "utf-8"
. "$PSScriptRoot/modules/HookLogging.ps1"
Start-HookLog -HookName "preprovision" -EnvironmentName $env:AZURE_ENV_NAME

Write-Host "Pre-Provision: AI Foundry resource discovery" -ForegroundColor Cyan

foreach ($cmd in @('pwsh', 'az', 'azd')) {
    if (-not (Get-Command $cmd -ErrorAction SilentlyContinue)) {
        Write-Host "[ERROR] $cmd not found." -ForegroundColor Red
        exit 1
    }
}

$account = az account show 2>$null | ConvertFrom-Json
if (-not $account) {
    Write-Host "[ERROR] Not logged in to Azure. Run 'azd auth login'." -ForegroundColor Red
    exit 1
}
Write-Host "[OK] Azure CLI: $($account.user.name)" -ForegroundColor Green

$environmentName = (azd env get-value AZURE_ENV_NAME 2>&1) |
    Where-Object { $_ -notmatch 'ERROR' } |
    Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($environmentName)) { $environmentName = $env:AZURE_ENV_NAME }
if ([string]::IsNullOrWhiteSpace($environmentName)) {
    Write-Host "[ERROR] AZURE_ENV_NAME not set. Run 'azd init' first." -ForegroundColor Red
    exit 1
}

$tenantId = (azd env get-value ENTRA_TENANT_ID 2>&1) |
    Where-Object { $_ -notmatch 'ERROR' } |
    Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($tenantId)) {
    $tenantId = $account.tenantId
    azd env set ENTRA_TENANT_ID $tenantId
}
Write-Host "[OK] Tenant: $tenantId" -ForegroundColor Green
Write-Host "[OK] Environment: $environmentName" -ForegroundColor Green

$authOtpPepper = (azd env get-value AUTH_OTP_PEPPER 2>&1) |
    Where-Object { $_ -notmatch 'ERROR|WARNING' } |
    Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($authOtpPepper)) { $authOtpPepper = $env:AUTH_OTP_PEPPER }
if ([string]::IsNullOrWhiteSpace($authOtpPepper)) {
    Write-Host "[ERROR] AUTH_OTP_PEPPER is not set. Run:" -ForegroundColor Red
    Write-Host "        azd env set AUTH_OTP_PEPPER <value> --environment $environmentName" -ForegroundColor Yellow
    exit 1
}
Write-Host "[OK] AUTH_OTP_PEPPER configured" -ForegroundColor Green

# The Foundry portal can provide the resource ARM ID. Machine-specific project endpoints
# remain in SQL and toolbox markers.
$portalResourceId = (azd env get-value AZURE_EXISTING_RESOURCE_ID 2>&1) |
    Where-Object { $_ -notmatch 'ERROR' } |
    Select-Object -First 1
$rootEnvFile = Join-Path $PSScriptRoot "../../.env"
if (-not $portalResourceId -and (Test-Path $rootEnvFile)) {
    $resourceLine = Get-Content $rootEnvFile |
        Where-Object { $_ -match '^\s*AZURE_EXISTING_RESOURCE_ID\s*=' } |
        Select-Object -First 1
    if ($resourceLine) { $portalResourceId = ($resourceLine -split '=', 2)[1].Trim().Trim('"') }
}
if ($portalResourceId) {
    $resourceName = (($portalResourceId -split '/accounts/')[-1] -split '/' | Select-Object -First 1).Trim()
    if ($resourceName) { azd env set AI_FOUNDRY_RESOURCE_NAME $resourceName }
}

$configuredResourceName = (azd env get-value AI_FOUNDRY_RESOURCE_NAME 2>&1) |
    Where-Object { $_ -notmatch 'ERROR' } |
    Select-Object -First 1
$resources = @(az cognitiveservices account list --query "[?kind=='AIServices']" | ConvertFrom-Json)
if ($resources.Count -eq 0) {
    Write-Host "[ERROR] No AI Foundry resource found. Create one at https://ai.azure.com." -ForegroundColor Red
    exit 1
}

$selected = if (-not [string]::IsNullOrWhiteSpace($configuredResourceName)) {
    $resources | Where-Object { $_.name -eq $configuredResourceName.Trim() } | Select-Object -First 1
} else {
    $resources | Select-Object -First 1
}
if (-not $selected) {
    Write-Host "[ERROR] AI Foundry resource '$configuredResourceName' was not found." -ForegroundColor Red
    exit 1
}

azd env set AI_FOUNDRY_RESOURCE_GROUP $selected.resourceGroup
azd env set AI_FOUNDRY_RESOURCE_NAME $selected.name
azd env set AI_FOUNDRY_LOCATION $selected.location
Write-Host "[OK] AI Foundry resource: $($selected.name)" -ForegroundColor Green

$deploymentLocation = (azd env get-value AZURE_LOCATION 2>&1) |
    Where-Object { $_ -notmatch 'ERROR' } |
    Select-Object -First 1
if ($deploymentLocation -and $selected.location -and
    $deploymentLocation.Replace(' ', '').ToLowerInvariant() -ne $selected.location.Replace(' ', '').ToLowerInvariant()) {
    Write-Host "[WARN] Region mismatch: app '$deploymentLocation', AI Foundry '$($selected.location)'." -ForegroundColor Yellow
}

Write-Host "[OK] Pre-provision complete" -ForegroundColor Green
if ($script:HookLogFile) { Write-Host "[LOG] Log file: $script:HookLogFile" -ForegroundColor DarkGray }
Stop-HookLog
