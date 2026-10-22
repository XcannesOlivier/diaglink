#!/usr/bin/env pwsh
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [switch]$ConfirmProductionMigration
)

$ErrorActionPreference = 'Stop'
$passwordVariable = 'DIAGLINK_PROD_DB_PASSWORD'
$migration = '20261020002202_InitialPostgreSql'
$project = 'backend/WebApp.Api.Migrations.PostgreSql/WebApp.Api.Migrations.PostgreSql.csproj'
$migrationDirectory = 'backend/WebApp.Api.Migrations.PostgreSql/Migrations'
$executable = 'backend/WebApp.Api.Migrations.PostgreSql/bin/Release/net10.0/WebApp.Api.Migrations.PostgreSql.dll'
$locationPushed = $false

if (-not $ConfirmProductionMigration) {
    throw 'Safety stop: pass -ConfirmProductionMigration after the cutover checklist is approved.'
}
if ([string]::IsNullOrWhiteSpace($env:DIAGLINK_PROD_DB_PASSWORD)) {
    throw "$passwordVariable must be set in this PowerShell process."
}
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'dotnet is required.'
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

    # Refuse to run if the dedicated assembly contains anything other than the approved initial
    # PostgreSQL migration. Designer and snapshot files are deliberately excluded.
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

    # Build locally; this neither opens a database connection nor applies a migration.
    dotnet build $project --configuration Release --nologo
    if ($LASTEXITCODE -ne 0) {
        throw 'PostgreSQL migration project build failed.'
    }

    # The .NET 10 guard constructs the Npgsql connection string from fixed constants, checks
    # current_database() and emptiness, applies only the named migration, then validates schema.
    dotnet $executable apply-initial --confirm-production-migration
    if ($LASTEXITCODE -ne 0) {
        throw 'PostgreSQL production migration or validation failed.'
    }
}
finally {
    Remove-Item Env:\DIAGLINK_PROD_DB_PASSWORD -ErrorAction SilentlyContinue
    if ($locationPushed) {
        Pop-Location
    }
}
