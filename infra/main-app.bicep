param location string
param tags object
param resourceToken string
param containerAppsEnvironmentId string
param containerRegistryName string
param aiAgentEndpoint string
param aiAgentId string
param entraSpaClientId string
param entraTenantId string
param entraBackendClientId string = ''
param webImageName string
param userAssignedIdentityId string = ''
param oboManagedIdentityClientId string = ''
param appInsightsConnectionString string = ''
param appInsightsFrontendConnectionString string = ''
param acsEndpoint string = ''
param senderAddress string = ''
@secure()
param authOtpPepper string
@secure()
param azureStorageConnectionString string
@secure()
param contactRecipientAddress string

var abbrs = loadJsonContent('./abbreviations.json')

// Base env vars always present
var baseEnv = [
  {
    name: 'ASPNETCORE_ENVIRONMENT'
    value: 'Production'
  }
  {
    name: 'ASPNETCORE_URLS'
    value: 'http://+:8080'
  }
  {
    name: 'ENTRA_SPA_CLIENT_ID'
    value: entraSpaClientId
  }
  {
    name: 'ENTRA_TENANT_ID'
    value: entraTenantId
  }
  {
    name: 'AI_AGENT_ENDPOINT'
    value: aiAgentEndpoint
  }
  {
    name: 'AI_AGENT_ID'
    value: aiAgentId
  }
  {
    name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
    value: appInsightsConnectionString
  }
  {
    name: 'APPLICATIONINSIGHTS_FRONTEND_CONNECTION_STRING'
    value: appInsightsFrontendConnectionString
  }
]

// User-assigned MI client ID — always needed since RBAC is assigned to this MI
var miEnv = [
  {
    name: 'MANAGED_IDENTITY_CLIENT_ID'
    value: oboManagedIdentityClientId
  }
]

// OBO env vars only injected when configured
var oboEnv = !empty(entraBackendClientId) ? [
  {
    name: 'ENTRA_BACKEND_CLIENT_ID'
    value: entraBackendClientId
  }
] : []

// SQL connection string for conversation-history persistence — existing 'diaglink' Azure SQL DB,
// Managed Identity auth (no secret). Reuses the same user-assigned MI as MANAGED_IDENTITY_CLIENT_ID.
var diagLinkEnv = [
  {
    name: 'ConnectionStrings__DiagLink'
    value: 'Server=tcp:sql-diaglink.database.windows.net,1433;Database=diaglink;Authentication=Active Directory Managed Identity;User Id=${oboManagedIdentityClientId};Encrypt=True;TrustServerCertificate=False;Connection Timeout=60;'
  }
]

// ACS endpoint + sender address for sending OTP emails — both non-secret
var emailEnv = [
  {
    name: 'Email__AcsEndpoint'
    value: acsEndpoint
  }
  {
    name: 'Email__SenderAddress'
    value: senderAddress
  }
]

var authEnv = [
  {
    name: 'Auth__OtpPepper'
    secretRef: 'auth-otp-pepper'
  }
]

var storageEnv = [
  {
    name: 'AZURE_STORAGE_CONNECTION_STRING'
    secretRef: 'azure-storage-connection-string'
  }
]

var contactEnv = [
  {
    name: 'Contact__RecipientAddress'
    secretRef: 'contact-recipient-address'
  }
]

var containerSecrets = [
  {
    name: 'auth-otp-pepper'
    value: authOtpPepper
  }
  {
    name: 'azure-storage-connection-string'
    value: azureStorageConnectionString
  }
  {
    name: 'contact-recipient-address'
    value: contactRecipientAddress
  }
]

// Share the non-secret tariff identities with local/backend configuration.
var pricingIdentities = loadJsonContent('../backend/WebApp.Api/appsettings.json').AiPricingIdentity.Deployments
var pricingIdentityEnvGroups = [for (identity, index) in pricingIdentities: [
  {
    name: 'AiPricingIdentity__Deployments__${index}__ProjectEndpoint'
    value: identity.ProjectEndpoint
  }
  {
    name: 'AiPricingIdentity__Deployments__${index}__Deployment'
    value: identity.Deployment
  }
  {
    name: 'AiPricingIdentity__Deployments__${index}__Provider'
    value: identity.Provider
  }
]]
var pricingIdentityEnv = flatten(pricingIdentityEnvGroups)

var containerEnv = concat(baseEnv, miEnv, oboEnv, diagLinkEnv, emailEnv, authEnv, storageEnv, contactEnv, pricingIdentityEnv)

// Single Container App - serves both frontend and backend
module webApp './core/host/container-app.bicep' = {
  name: 'web-container-app'
  params: {
    name: '${abbrs.appContainerApps}web-${resourceToken}'
    location: location
    tags: union(tags, { 'azd-service-name': 'web' })
    containerAppsEnvironmentId: containerAppsEnvironmentId
    containerRegistryName: containerRegistryName
    containerImage: webImageName
    targetPort: 8080
    env: containerEnv
    enableIngress: true
    external: true
    healthProbePath: '/api/health'
    userAssignedIdentityId: userAssignedIdentityId
    secrets: containerSecrets
  }
}

output webEndpoint string = 'https://${webApp.outputs.fqdn}'
output webAppName string = webApp.outputs.name
