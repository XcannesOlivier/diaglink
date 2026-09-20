param location string
param tags object
param resourceToken string

var abbrs = loadJsonContent('./abbreviations.json')

var defaultTags = {
  'azd-env-name': resourceToken
}

var allTags = union(tags, defaultTags)

// Log Analytics Workspace
module logAnalytics './core/host/log-analytics.bicep' = {
  name: 'log-analytics'
  params: {
    name: '${abbrs.operationalInsightsWorkspaces}${resourceToken}'
    location: location
    tags: allTags
  }
}

// Application Insights (backend)
module appInsights './core/host/application-insights.bicep' = {
  name: 'appInsights'
  params: {
    name: '${abbrs.insightsComponents}${resourceToken}'
    location: location
    tags: allTags
    logAnalyticsWorkspaceId: logAnalytics.outputs.id
  }
}

// Application Insights (frontend) — separate resource so browser telemetry doesn't pollute server metrics
module appInsightsFrontend './core/host/application-insights.bicep' = {
  name: 'appInsightsFrontend'
  params: {
    name: '${abbrs.insightsComponents}fe-${resourceToken}'
    location: location
    tags: allTags
    logAnalyticsWorkspaceId: logAnalytics.outputs.id
  }
}

// Container Registry
module containerRegistry './core/host/container-registry.bicep' = {
  name: 'container-registry'
  params: {
    name: '${abbrs.containerRegistryRegistries}${resourceToken}'
    location: location
    tags: allTags
    acrPullPrincipalId: managedIdentity.properties.principalId
  }
}

// Container Apps Environment
module containerAppsEnvironment './core/host/container-apps-environment.bicep' = {
  name: 'container-apps-environment'
  params: {
    name: '${abbrs.appManagedEnvironments}${resourceToken}'
    location: location
    tags: allTags
    logAnalyticsWorkspaceId: logAnalytics.outputs.id
  }
}

output appInsightsConnectionString string = appInsights.outputs.connectionString
output appInsightsFrontendConnectionString string = appInsightsFrontend.outputs.connectionString
output containerRegistryName string = containerRegistry.outputs.name
output containerRegistryLoginServer string = containerRegistry.outputs.loginServer
output containerAppsEnvironmentId string = containerAppsEnvironment.outputs.id

// User-assigned managed identity — created independently so its principalId
// is available for both Entra FIC and Container App/ACR assignment without circular dependency.
// isolationScope: Regional ensures the identity can only be used in the deployment region.
resource managedIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: '${abbrs.managedIdentityUserAssignedIdentities}web-${resourceToken}'
  location: location
  tags: allTags
  properties: {
    isolationScope: 'Regional'
  }
}
module foundryRoleAssignment './foundry-role-assignment.bicep' = {
  name: 'foundry-role-assignment'
  scope: resourceGroup('rg-ai-knowledge-dev')
  params: {
    principalId: managedIdentity.properties.principalId
    foundryAccountName: 'diaglink-foundry-prod'
  }
}

output managedIdentityId string = managedIdentity.id
output managedIdentityPrincipalId string = managedIdentity.properties.principalId
output managedIdentityClientId string = managedIdentity.properties.clientId

// Azure Communication Services Email — OTP login codes (Entra ID auth via existing Managed Identity, no keys)
module communicationEmail './core/host/communication-email.bicep' = {
  name: 'communication-email'
  params: {
    resourceToken: resourceToken
    dataLocation: 'Europe'
    tags: allTags
    managedIdentityPrincipalId: managedIdentity.properties.principalId
  }
}

output acsEndpoint string = communicationEmail.outputs.acsEndpoint
output communicationServiceId string = communicationEmail.outputs.communicationServiceId
output emailServiceId string = communicationEmail.outputs.emailServiceId
output domainId string = communicationEmail.outputs.domainId
