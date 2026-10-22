param(
    [ValidateSet('Preview', 'Apply')]
    [string]$Mode = 'Preview',
    [switch]$ConfirmProductionChange
)

$ErrorActionPreference = 'Stop'

$expectedBranch = 'migration/postgresql'
$resourceGroup = 'rg-localdev'
$containerAppName = 'ca-web-yhobipnkhkqdy'
$functionResourceGroup = 'rg-ai-knowledge-dev'
$functionAppName = 'func-olivier-schematics'
$roleId = 'b7ed8619-c233-4da1-8ff6-55cd46a6e013'
$roleValue = 'DocumentIngestion.Finalize'
$backendBaseUrl = 'https://app.diaglink.com'
$requiredRedirectUris = @(
    'http://localhost:5173',
    'http://localhost:8080',
    'https://app.diaglink.com',
    'https://ca-web-yhobipnkhkqdy.livelyhill-27d72b6f.swedencentral.azurecontainerapps.io'
)

function Invoke-AzJson {
    param([Parameter(Mandatory)][string[]]$Arguments)

    $output = & az @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Azure CLI command failed: az $($Arguments[0]) ..."
    }
    return $output | ConvertFrom-Json
}

function Get-AzdValue {
    param([Parameter(Mandatory)][string]$Name)

    $value = azd env get-value $Name 2>$null | Select-Object -First 1
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($value)) {
        throw "Required azd setting '$Name' is missing."
    }
    return [string]$value
}

if ($Mode -eq 'Apply' -and -not $ConfirmProductionChange) {
    throw 'Apply mode requires -ConfirmProductionChange.'
}

$branch = git branch --show-current
if ($LASTEXITCODE -ne 0 -or $branch -cne $expectedBranch) {
    throw "Safety stop: expected branch '$expectedBranch', found '$branch'."
}

$expectedSubscription = Get-AzdValue 'AZURE_SUBSCRIPTION_ID'
$activeSubscription = az account show --query id --output tsv
if ($LASTEXITCODE -ne 0 -or $activeSubscription -cne $expectedSubscription) {
    throw 'Safety stop: the active Azure subscription does not match the azd environment.'
}

$clientId = Get-AzdValue 'ENTRA_SPA_CLIENT_ID'
$app = Invoke-AzJson @('ad', 'app', 'show', '--id', $clientId, '--output', 'json')
$backendServicePrincipal = Invoke-AzJson @('ad', 'sp', 'show', '--id', $clientId, '--output', 'json')
$functionApp = Invoke-AzJson @(
    'functionapp', 'show',
    '--resource-group', $functionResourceGroup,
    '--name', $functionAppName,
    '--output', 'json'
)
$functionPrincipalId = [string]$functionApp.identity.principalId
if ([string]::IsNullOrWhiteSpace($functionPrincipalId) -or $functionApp.identity.type -notmatch 'SystemAssigned') {
    throw 'Safety stop: the expected system-assigned Function identity is unavailable.'
}

$actualRedirects = @($app.spa.redirectUris | Sort-Object -Unique)
$missingRedirects = @($requiredRedirectUris | Where-Object { $_ -notin $actualRedirects })
if ($missingRedirects.Count -gt 0) {
    throw "Safety stop: production Entra redirect URIs are missing: $($missingRedirects -join ', ')."
}

$existingRoleByValue = @($app.appRoles | Where-Object value -eq $roleValue)
$existingRoleById = @($app.appRoles | Where-Object id -eq $roleId)
if ($existingRoleByValue.Count -gt 1 -or $existingRoleById.Count -gt 1) {
    throw 'Safety stop: duplicate documentary-ingestion app roles detected.'
}
if ($existingRoleByValue.Count -eq 1 -and $existingRoleByValue[0].id -ne $roleId) {
    throw 'Safety stop: DocumentIngestion.Finalize already exists with an unexpected ID.'
}
if ($existingRoleById.Count -eq 1 -and $existingRoleById[0].value -ne $roleValue) {
    throw 'Safety stop: the configured role ID is already used by another role.'
}

$assignments = Invoke-AzJson @(
    'rest', '--method', 'GET',
    '--url', "https://graph.microsoft.com/v1.0/servicePrincipals/$functionPrincipalId/appRoleAssignments",
    '--output', 'json'
)
$matchingAssignments = @($assignments.value | Where-Object {
    $_.resourceId -eq $backendServicePrincipal.id -and $_.appRoleId -eq $roleId
})

$containerApp = Invoke-AzJson @(
    'containerapp', 'show',
    '--resource-group', $resourceGroup,
    '--name', $containerAppName,
    '--output', 'json'
)
$webContainer = @($containerApp.properties.template.containers | Where-Object name -eq $containerAppName)
$tunnelContainer = @($containerApp.properties.template.containers | Where-Object name -eq 'postgres-tunnel')
if ($webContainer.Count -ne 1 -or $tunnelContainer.Count -ne 1) {
    throw 'Safety stop: the expected web and PostgreSQL tunnel containers were not both found.'
}
$provider = @($webContainer[0].env | Where-Object name -eq 'Database__Provider')
if ($provider.Count -ne 1 -or $provider[0].value -cne 'PostgreSql') {
    throw 'Safety stop: the production Container App is not configured for PostgreSQL.'
}
$allowedCaller = @($webContainer[0].env | Where-Object name -eq 'DocumentIngestion__AllowedCallerObjectId')
if ($allowedCaller.Count -gt 0 -and -not [string]::IsNullOrWhiteSpace($allowedCaller[0].value) -and
    $allowedCaller[0].value -cne $functionPrincipalId) {
    throw 'Safety stop: the backend allowlist contains an unexpected principal.'
}
$stripeStateBefore = @(
    $webContainer[0].env |
        Where-Object { $_.name -match '^STRIPE_' } |
        Sort-Object name |
        ForEach-Object { [ordered]@{ name = $_.name; value = $_.value; secretRef = $_.secretRef } }
) | ConvertTo-Json -Compress
$tunnelImageBefore = [string]$tunnelContainer[0].image

$functionSettingNames = @(
    az functionapp config appsettings list `
        --resource-group $functionResourceGroup `
        --name $functionAppName `
        --query '[].name' --output tsv
)
if ($LASTEXITCODE -ne 0) {
    throw 'Unable to inspect Function setting names.'
}

Write-Host "Mode: $Mode"
Write-Host "App role present: $($existingRoleByValue.Count -eq 1)"
Write-Host "Role assignment present: $($matchingAssignments.Count -eq 1)"
Write-Host "Backend allowlist configured: $($allowedCaller.Count -eq 1 -and $allowedCaller[0].value -ceq $functionPrincipalId)"
Write-Host "Function backend URL configured: $($functionSettingNames -contains 'DIAGLINK_BACKEND_BASE_URL')"
Write-Host "Function audience configured: $($functionSettingNames -contains 'DIAGLINK_BACKEND_AUDIENCE')"
Write-Host "PostgreSQL tunnel preserved: True"

if ($Mode -eq 'Preview') {
    Write-Host 'Preview complete. No Azure resource was modified.'
    exit 0
}

if ($existingRoleByValue.Count -eq 0) {
    $newRole = [ordered]@{
        allowedMemberTypes = @('Application')
        description = 'Allows the documentary-ingestion managed identity to finalize machine configuration.'
        displayName = 'Finalize document ingestion'
        id = $roleId
        isEnabled = $true
        value = $roleValue
    }
    $roles = @(
        $app.appRoles | ForEach-Object {
            [ordered]@{
                allowedMemberTypes = @($_.allowedMemberTypes)
                description = $_.description
                displayName = $_.displayName
                id = $_.id
                isEnabled = $_.isEnabled
                value = $_.value
            }
        }
    ) + @($newRole)
    $bodyPath = [IO.Path]::GetTempFileName()
    try {
        @{ appRoles = $roles } | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $bodyPath -Encoding utf8
        az rest --method PATCH `
            --url "https://graph.microsoft.com/v1.0/applications/$($app.id)" `
            --headers 'Content-Type=application/json' `
            --body "@$bodyPath" --output none
        if ($LASTEXITCODE -ne 0) { throw 'Failed to add the documentary-ingestion app role.' }
    } finally {
        Remove-Item -LiteralPath $bodyPath -Force -ErrorAction SilentlyContinue
    }
}

if ($matchingAssignments.Count -eq 0) {
    $assignmentPath = [IO.Path]::GetTempFileName()
    try {
        @{
            principalId = $functionPrincipalId
            resourceId = $backendServicePrincipal.id
            appRoleId = $roleId
        } | ConvertTo-Json | Set-Content -LiteralPath $assignmentPath -Encoding utf8
        az rest --method POST `
            --url "https://graph.microsoft.com/v1.0/servicePrincipals/$functionPrincipalId/appRoleAssignments" `
            --headers 'Content-Type=application/json' `
            --body "@$assignmentPath" --output none
        if ($LASTEXITCODE -ne 0) { throw 'Failed to assign the documentary-ingestion app role.' }
    } finally {
        Remove-Item -LiteralPath $assignmentPath -Force -ErrorAction SilentlyContinue
    }
}

if ($allowedCaller.Count -eq 0 -or $allowedCaller[0].value -cne $functionPrincipalId) {
    az containerapp update `
        --resource-group $resourceGroup `
        --name $containerAppName `
        --container-name $containerAppName `
        --set-env-vars "DocumentIngestion__AllowedCallerObjectId=$functionPrincipalId" `
        --output none
    if ($LASTEXITCODE -ne 0) { throw 'Failed to configure the backend caller allowlist.' }

    $updatedContainerApp = Invoke-AzJson @(
        'containerapp', 'show',
        '--resource-group', $resourceGroup,
        '--name', $containerAppName,
        '--output', 'json'
    )
    $updatedWeb = @($updatedContainerApp.properties.template.containers | Where-Object name -eq $containerAppName)
    $updatedTunnel = @($updatedContainerApp.properties.template.containers | Where-Object name -eq 'postgres-tunnel')
    $stripeStateAfter = @(
        $updatedWeb[0].env |
            Where-Object { $_.name -match '^STRIPE_' } |
            Sort-Object name |
            ForEach-Object { [ordered]@{ name = $_.name; value = $_.value; secretRef = $_.secretRef } }
    ) | ConvertTo-Json -Compress
    if ($stripeStateAfter -cne $stripeStateBefore) {
        throw 'Safety stop: Stripe environment settings changed unexpectedly.'
    }
    if ($updatedTunnel.Count -ne 1 -or $updatedTunnel[0].image -cne $tunnelImageBefore) {
        throw 'Safety stop: the PostgreSQL tunnel image changed unexpectedly.'
    }
}

az functionapp config appsettings set `
    --resource-group $functionResourceGroup `
    --name $functionAppName `
    --settings `
        "DIAGLINK_BACKEND_BASE_URL=$backendBaseUrl" `
        "DIAGLINK_BACKEND_AUDIENCE=api://$clientId" `
    --output none
if ($LASTEXITCODE -ne 0) { throw 'Failed to configure the Function callback settings.' }

Write-Host 'Targeted documentary-ingestion authorization configuration completed.'
