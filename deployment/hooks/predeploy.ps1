#!/usr/bin/env pwsh
# Pre-deploy: build the application image on every deployment and rebuild the
# PostgreSQL tunnel image only when its source files change.

param(
    [switch]$BuildOnly
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot/modules/HookLogging.ps1"
Start-HookLog -HookName "predeploy" -EnvironmentName $env:AZURE_ENV_NAME

Write-Host "Pre-Deploy: Building DiagLink" -ForegroundColor Cyan

# azd injects environment values into hooks. The fallback keeps the hook usable
# when an azd subprocess cannot locate azure.yaml from its current directory.
function Get-AzdValue([string]$Name) {
    $value = $null
    $output = azd env get-value $Name 2>$null
    if ($LASTEXITCODE -eq 0) {
        $value = $output | Select-Object -First 1
    }
    if ([string]::IsNullOrWhiteSpace($value)) {
        $value = [Environment]::GetEnvironmentVariable($Name)
    }
    return $value
}

function Set-AzdValue([string]$Name, [string]$Value) {
    azd env set $Name $Value 2>$null
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to persist azd environment value '$Name'."
    }
}

function Get-SourceFingerprint([string]$Root, [string[]]$Paths) {
    $resolvedRoot = (Resolve-Path -LiteralPath $Root).Path.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $rootPrefix = $resolvedRoot + [IO.Path]::DirectorySeparatorChar
    $manifestLines = foreach ($path in ($Paths | Sort-Object)) {
        $resolvedPath = (Resolve-Path -LiteralPath $path).Path
        if (-not $resolvedPath.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Fingerprint source '$resolvedPath' is outside '$resolvedRoot'."
        }
        $relativePath = $resolvedPath.Substring($rootPrefix.Length).Replace('\', '/')
        $fileHash = (Get-FileHash -LiteralPath $resolvedPath -Algorithm SHA256).Hash.ToLowerInvariant()
        "$relativePath`:$fileHash"
    }

    $bytes = [Text.Encoding]::UTF8.GetBytes(($manifestLines -join "`n"))
    $stream = [IO.MemoryStream]::new($bytes, $false)
    try {
        return (Get-FileHash -InputStream $stream -Algorithm SHA256).Hash.ToLowerInvariant()
    } finally {
        $stream.Dispose()
    }
}

function Test-AcrImageExists([string]$RegistryName, [string]$ImageName) {
    $registryPrefix = "$RegistryName.azurecr.io/"
    if (-not $ImageName.StartsWith($registryPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        return $false
    }

    $repositoryAndTag = $ImageName.Substring($registryPrefix.Length)
    az acr repository show --name $RegistryName --image $repositoryAndTag `
        --query name --output tsv --only-show-errors 1>$null 2>$null
    return $LASTEXITCODE -eq 0
}

$clientId = Get-AzdValue 'ENTRA_SPA_CLIENT_ID'
$tenantId = Get-AzdValue 'ENTRA_TENANT_ID'
$backendClientId = Get-AzdValue 'ENTRA_BACKEND_CLIENT_ID'
$appInsightsConnStr = Get-AzdValue 'APPLICATIONINSIGHTS_FRONTEND_CONNECTION_STRING'
# ACR treats unescaped semicolons in build arguments as shell separators.
if ($appInsightsConnStr) { $appInsightsConnStrEscaped = $appInsightsConnStr -replace ';', '\;' } else { $appInsightsConnStrEscaped = '' }
$acrName = Get-AzdValue 'AZURE_CONTAINER_REGISTRY_NAME'
$resourceGroup = Get-AzdValue 'AZURE_RESOURCE_GROUP_NAME'
$containerApp = Get-AzdValue 'AZURE_CONTAINER_APP_NAME'
$existingTunnelImage = Get-AzdValue 'POSTGRES_TUNNEL_IMAGE_NAME'
$recordedTunnelSourceHash = Get-AzdValue 'POSTGRES_TUNNEL_SOURCE_HASH'

if (-not $clientId -or -not $tenantId) {
    throw "ENTRA_SPA_CLIENT_ID or ENTRA_TENANT_ID is not set."
}
if (-not $acrName) {
    throw "AZURE_CONTAINER_REGISTRY_NAME is not set."
}

$projectRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$tunnelSources = @(
    (Join-Path $projectRoot 'deployment/docker/postgres-tunnel.Dockerfile'),
    (Join-Path $projectRoot 'deployment/docker/postgres-tunnel-entrypoint.sh')
)
$tunnelSourceHash = Get-SourceFingerprint -Root $projectRoot -Paths $tunnelSources
$tunnelContentTag = "source-$($tunnelSourceHash.Substring(0, 16))"
$contentAddressedTunnelImage = "$acrName.azurecr.io/postgres-tunnel:$tunnelContentTag"

$buildTunnelImage = $false
$updateTunnelContainer = $false
if ($existingTunnelImage -and -not $recordedTunnelSourceHash) {
    # One-time adoption of the image already deployed before source tracking was added.
    $tunnelImageName = $existingTunnelImage
    Write-Host "[OK] Reusing the existing tunnel image and recording its source baseline." -ForegroundColor Green
} elseif ($existingTunnelImage -and $recordedTunnelSourceHash -eq $tunnelSourceHash) {
    $tunnelImageName = $existingTunnelImage
    Write-Host "[OK] Tunnel sources unchanged; existing image will be reused." -ForegroundColor Green
} else {
    $tunnelImageName = $contentAddressedTunnelImage
    $buildTunnelImage = -not (Test-AcrImageExists -RegistryName $acrName -ImageName $tunnelImageName)
    $updateTunnelContainer = $existingTunnelImage -ne $tunnelImageName
    if ($buildTunnelImage) {
        Write-Host "Tunnel sources are new; the sidecar image will be built." -ForegroundColor Cyan
    } else {
        Write-Host "[OK] Matching tunnel image already exists in ACR; build skipped." -ForegroundColor Green
    }
}

$imageTag = "deploy-$([DateTimeOffset]::UtcNow.ToUnixTimeSeconds())"
$imageName = "$acrName.azurecr.io/web:$imageTag"

# Prefer local Docker when its daemon is available; otherwise use ACR cloud builds.
$dockerAvailable = $false
if (Get-Command docker -ErrorAction SilentlyContinue) {
    $dockerVersion = docker version --format '{{.Server.Version}}' 2>$null
    if ($LASTEXITCODE -eq 0 -and $dockerVersion) {
        $dockerAvailable = $true
        Write-Host "[OK] Docker v$dockerVersion" -ForegroundColor Green
    }
}

Push-Location $projectRoot
try {
    if ($dockerAvailable) {
        Write-Host "Building the DiagLink image with local Docker..." -ForegroundColor Cyan
        $buildArgs = @(
            "--platform", "linux/amd64",
            "--build-arg", "ENTRA_SPA_CLIENT_ID=$clientId",
            "--build-arg", "ENTRA_TENANT_ID=$tenantId"
        )
        if ($backendClientId) { $buildArgs += @("--build-arg", "ENTRA_BACKEND_CLIENT_ID=$backendClientId") }
        if ($appInsightsConnStr) { $buildArgs += @("--build-arg", "APPLICATIONINSIGHTS_FRONTEND_CONNECTION_STRING=$appInsightsConnStr") }
        $buildArgs += @("-f", "deployment/docker/frontend.Dockerfile", "-t", $imageName, ".")
        docker build @buildArgs 2>&1 | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "Docker build failed." }

        if ($buildTunnelImage) {
            docker build --platform linux/amd64 `
                --file deployment/docker/postgres-tunnel.Dockerfile `
                --tag $tunnelImageName . 2>&1 | Out-Host
            if ($LASTEXITCODE -ne 0) { throw "PostgreSQL tunnel image build failed." }
        }

        Write-Host "Pushing new images to ACR..." -ForegroundColor Cyan
        az acr login --name $acrName | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "ACR login failed." }
        docker push $imageName 2>&1 | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "Docker push failed." }
        if ($buildTunnelImage) {
            docker push $tunnelImageName 2>&1 | Out-Host
            if ($LASTEXITCODE -ne 0) { throw "PostgreSQL tunnel image push failed." }
        }
    } else {
        Write-Host "Using an ACR cloud build for DiagLink (3-5 min)..." -ForegroundColor Yellow
        $acrBuildArgs = @("--build-arg", "ENTRA_SPA_CLIENT_ID=$clientId", "--build-arg", "ENTRA_TENANT_ID=$tenantId")
        if ($backendClientId) { $acrBuildArgs += @("--build-arg", "ENTRA_BACKEND_CLIENT_ID=$backendClientId") }
        if ($appInsightsConnStrEscaped) { $acrBuildArgs += @("--build-arg", "APPLICATIONINSIGHTS_FRONTEND_CONNECTION_STRING=$appInsightsConnStrEscaped") }
        $buildOutput = az acr build --registry $acrName --image "web:$imageTag" `
            @acrBuildArgs `
            --file deployment/docker/frontend.Dockerfile . `
            --no-logs --only-show-errors 2>&1
        if ($LASTEXITCODE -ne 0) {
            Write-Host "Build output: $buildOutput" -ForegroundColor Red
            throw "ACR build failed."
        }

        if ($buildTunnelImage) {
            $tunnelBuildOutput = az acr build --registry $acrName --image "postgres-tunnel:$tunnelContentTag" `
                --file deployment/docker/postgres-tunnel.Dockerfile . `
                --no-logs --only-show-errors 2>&1
            if ($LASTEXITCODE -ne 0) {
                Write-Host "Tunnel build output: $tunnelBuildOutput" -ForegroundColor Red
                throw "PostgreSQL tunnel ACR build failed."
            }
        }
    }

    Write-Host "[OK] DiagLink image built: $imageName" -ForegroundColor Green
    if ($buildTunnelImage) {
        Write-Host "[OK] Tunnel image built: $tunnelImageName" -ForegroundColor Green
    }

    # Persist image metadata only. No infrastructure, secret, or database operation is performed.
    Set-AzdValue -Name 'SERVICE_WEB_IMAGE_NAME' -Value $imageName
    Set-AzdValue -Name 'POSTGRES_TUNNEL_IMAGE_NAME' -Value $tunnelImageName
    Set-AzdValue -Name 'POSTGRES_TUNNEL_SOURCE_HASH' -Value $tunnelSourceHash

    if ($BuildOnly) {
        Write-Host "[OK] Build-only mode: image metadata recorded; Container Apps was not changed." -ForegroundColor Green
    }

    if (-not $BuildOnly -and $containerApp -and $resourceGroup) {
        $exists = az containerapp show --name $containerApp --resource-group $resourceGroup --query name -o tsv 2>$null
        if ($exists) {
            $currentApp = az containerapp show --name $containerApp --resource-group $resourceGroup --output json 2>$null | ConvertFrom-Json
            $tunnelContainer = $currentApp.properties.template.containers |
                Where-Object { $_.name -eq 'postgres-tunnel' } |
                Select-Object -First 1
            $webContainer = $currentApp.properties.template.containers |
                Where-Object { $_.name -eq $containerApp } |
                Select-Object -First 1
            $databaseProvider = $webContainer.env |
                Where-Object { $_.name -eq 'Database__Provider' } |
                Select-Object -ExpandProperty value -First 1
            if (-not $tunnelContainer -or $databaseProvider -ne 'PostgreSql') {
                throw "Safety stop: the Container App is not configured with the PostgreSQL tunnel and provider. No running revision was changed."
            }

            Write-Host "Updating the DiagLink application container..." -ForegroundColor Cyan
            az containerapp update --name $containerApp --resource-group $resourceGroup `
                --container-name $containerApp --image $imageName --output none
            if ($LASTEXITCODE -ne 0) { throw "Container App update failed." }

            if ($updateTunnelContainer) {
                Write-Host "Updating the PostgreSQL tunnel sidecar image..." -ForegroundColor Cyan
                az containerapp update --name $containerApp --resource-group $resourceGroup `
                    --container-name postgres-tunnel --image $tunnelImageName --output none
                if ($LASTEXITCODE -ne 0) { throw "PostgreSQL tunnel Container App update failed." }
            } else {
                Write-Host "[OK] PostgreSQL tunnel container and SSH configuration preserved unchanged." -ForegroundColor Green
            }
            Write-Host "[OK] Container App updated." -ForegroundColor Green
        } else {
            Write-Host "[SKIP] Container App not yet provisioned." -ForegroundColor Yellow
        }
    }
} finally {
    Pop-Location
}

if ($script:HookLogFile) {
    Write-Host "[LOG] Log file: $script:HookLogFile" -ForegroundColor DarkGray
}
Stop-HookLog
