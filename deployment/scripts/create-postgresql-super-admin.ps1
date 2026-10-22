#!/usr/bin/env pwsh
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [ValidateNotNullOrEmpty()] [string]$Email,
    [Parameter(Mandatory)] [ValidateNotNullOrEmpty()] [string]$FirstName,
    [Parameter(Mandatory)] [ValidateNotNullOrEmpty()] [string]$LastName,
    [string]$EntraObjectId = '',
    [Parameter(Mandatory)] [switch]$ConfirmProductionWrite
)

$ErrorActionPreference = 'Stop'
$passwordVariable = 'DIAGLINK_PROD_DB_PASSWORD'
$migration = '20261020002202_InitialPostgreSql'
$project = 'backend/WebApp.Api.Migrations.PostgreSql/WebApp.Api.Migrations.PostgreSql.csproj'
$migrationDirectory = 'backend/WebApp.Api.Migrations.PostgreSql/Migrations'
$executable = 'backend/WebApp.Api.Migrations.PostgreSql/bin/Release/net10.0/WebApp.Api.Migrations.PostgreSql.dll'
$locationPushed = $false

if (-not $ConfirmProductionWrite) {
    throw 'Safety stop: pass -ConfirmProductionWrite after checking the administrator identity.'
}
if ([string]::IsNullOrWhiteSpace($env:DIAGLINK_PROD_DB_PASSWORD)) {
    throw "$passwordVariable must be set in this PowerShell process."
}
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'dotnet 10 is required.'
}
if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    throw 'git is required for the branch safety check.'
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '../..')

try {
    Push-Location $repoRoot
    $locationPushed = $true

    $branch = (git branch --show-current).Trim()
    if ($LASTEXITCODE -ne 0 -or $branch -cne 'migration/postgresql') {
        throw "Safety stop: expected branch 'migration/postgresql', found '$branch'."
    }

    $migrationFiles = @(
        Get-ChildItem -LiteralPath $migrationDirectory -Filter '*.cs' |
            Where-Object {
                $_.Name -notlike '*.Designer.cs' -and
                $_.Name -ne 'DiagLinkDbContextModelSnapshot.cs'
            }
    )
    if ($migrationFiles.Count -ne 1 -or $migrationFiles[0].BaseName -cne $migration) {
        throw "Safety stop: the PostgreSQL assembly must contain only migration '$migration'."
    }

    if (-not (Test-NetConnection -ComputerName '127.0.0.1' -Port 15434 -InformationLevel Quiet)) {
        throw 'Safety stop: the local SSH tunnel is not listening on 127.0.0.1:15434.'
    }

    dotnet build $project --configuration Release --nologo
    if ($LASTEXITCODE -ne 0) {
        throw 'PostgreSQL bootstrap project build failed.'
    }

    $env:DIAGLINK_SUPER_ADMIN_EMAIL = $Email.Trim()
    $env:DIAGLINK_SUPER_ADMIN_FIRST_NAME = $FirstName.Trim()
    $env:DIAGLINK_SUPER_ADMIN_LAST_NAME = $LastName.Trim()
    $env:DIAGLINK_SUPER_ADMIN_ENTRA_OBJECT_ID = $EntraObjectId.Trim()

    dotnet $executable create-super-admin --confirm-production-write
    if ($LASTEXITCODE -ne 0) {
        throw 'PostgreSQL super-administrator creation or non-personal verification failed.'
    }
}
finally {
    Remove-Item Env:\DIAGLINK_PROD_DB_PASSWORD -ErrorAction SilentlyContinue
    Remove-Item Env:\DIAGLINK_SUPER_ADMIN_EMAIL -ErrorAction SilentlyContinue
    Remove-Item Env:\DIAGLINK_SUPER_ADMIN_FIRST_NAME -ErrorAction SilentlyContinue
    Remove-Item Env:\DIAGLINK_SUPER_ADMIN_LAST_NAME -ErrorAction SilentlyContinue
    Remove-Item Env:\DIAGLINK_SUPER_ADMIN_ENTRA_OBJECT_ID -ErrorAction SilentlyContinue
    if ($locationPushed) {
        Pop-Location
    }
}
