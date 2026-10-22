param(
    [ValidateSet('Preview', 'Download')]
    [string]$Mode = 'Preview',
    [string]$DestinationDirectory = (Join-Path $env:LOCALAPPDATA 'DiagLink\Backups\Functions')
)

$ErrorActionPreference = 'Stop'
$functionResourceGroup = 'rg-ai-knowledge-dev'
$functionAppName = 'func-olivier-schematics'
$connectionSettingName = 'DEPLOYMENT_STORAGE_CONNECTION_STRING'

$functionApp = az functionapp show `
    --resource-group $functionResourceGroup `
    --name $functionAppName --output json | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or $functionApp.identity.type -notmatch 'SystemAssigned') {
    throw 'Safety stop: the expected Function App was not found.'
}

$subscriptionId = az account show --query id --output tsv
$resourceUrl = "https://management.azure.com/subscriptions/$subscriptionId/resourceGroups/$functionResourceGroup/providers/Microsoft.Web/sites/${functionAppName}?api-version=2024-04-01"
$resource = az rest --method GET --url $resourceUrl --output json | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) { throw 'Unable to inspect the Function deployment storage.' }

$storageUri = [Uri]([string]$resource.properties.functionAppConfig.deployment.storage.value)
$containerName = $storageUri.AbsolutePath.Trim('/').Split('/')[0]
if ([string]::IsNullOrWhiteSpace($containerName)) {
    throw 'Safety stop: the Function deployment container is unavailable.'
}

$settings = az functionapp config appsettings list `
    --resource-group $functionResourceGroup `
    --name $functionAppName --output json | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) { throw 'Unable to read the Function deployment configuration.' }
$connection = @($settings | Where-Object name -eq $connectionSettingName)
if ($connection.Count -ne 1 -or [string]::IsNullOrWhiteSpace($connection[0].value)) {
    throw "Safety stop: $connectionSettingName is unavailable."
}

$previousConnection = [Environment]::GetEnvironmentVariable('AZURE_STORAGE_CONNECTION_STRING', 'Process')
try {
    [Environment]::SetEnvironmentVariable('AZURE_STORAGE_CONNECTION_STRING', $connection[0].value, 'Process')
    if ($Mode -eq 'Preview') {
        $packageName = az storage blob show `
            --container-name $containerName `
            --name 'released-package.zip' `
            --query name --output tsv --only-show-errors
        if ($LASTEXITCODE -ne 0 -or $packageName -cne 'released-package.zip') {
            throw 'The currently deployed Function package could not be verified.'
        }
        Write-Host 'Backup source package available: True'
        Write-Host 'Preview complete. No file was downloaded and no Azure resource was modified.'
        exit 0
    }

    New-Item -ItemType Directory -Path $DestinationDirectory -Force | Out-Null
    $timestamp = [DateTimeOffset]::UtcNow.ToString('yyyyMMdd-HHmmss')
    $destination = Join-Path $DestinationDirectory "$functionAppName-$timestamp.zip"
    az storage blob download `
        --container-name $containerName `
        --name 'released-package.zip' `
        --file $destination `
        --no-progress --only-show-errors --output none
    if ($LASTEXITCODE -ne 0) { throw 'Function package download failed.' }
} finally {
    [Environment]::SetEnvironmentVariable('AZURE_STORAGE_CONNECTION_STRING', $previousConnection, 'Process')
    $connection = $null
    $settings = $null
}

$item = Get-Item -LiteralPath $destination
if ($item.Length -le 0) { throw 'The downloaded Function package is empty.' }
$hash = Get-FileHash -LiteralPath $destination -Algorithm SHA256

Write-Host "BackupPath=$destination"
Write-Host "SizeBytes=$($item.Length)"
Write-Host "SHA256=$($hash.Hash)"
Write-Host 'The Azure Function and its deployment package were not modified.'
