param resourceToken string
param location string = 'global'
param dataLocation string = 'Europe'
param managedIdentityPrincipalId string
param tags object = {}

// Email Communication Service — holds the sending domain(s).
resource emailService 'Microsoft.Communication/emailServices@2023-04-01' = {
  name: 'acs-email-${resourceToken}'
  location: location
  tags: tags
  properties: {
    dataLocation: dataLocation
  }
}

// AzureManaged domain — free Azure-provided sending domain, no DNS setup required.
// 'AzureManagedDomain' is the fixed name Azure requires for this domain type.
resource domain 'Microsoft.Communication/emailServices/domains@2023-04-01' = {
  parent: emailService
  name: 'AzureManagedDomain'
  location: location
  properties: {
    domainManagement: 'AzureManaged'
    userEngagementTracking: 'Disabled'
  }
}

// Communication Service — the resource the app authenticates against; linked to the email domain.
resource communicationService 'Microsoft.Communication/communicationServices@2023-04-01' = {
  name: 'acs-${resourceToken}'
  location: location
  tags: tags
  properties: {
    dataLocation: dataLocation
    linkedDomains: [
      domain.id
    ]
  }
}

// Custom role — least privilege: only what's needed to send email via Entra ID auth, scoped to this resource only.
resource emailSenderRole 'Microsoft.Authorization/roleDefinitions@2022-04-01' = {
  name: guid(communicationService.id, 'DiagLinkCommunicationEmailSender')
  properties: {
    roleName: 'DiagLink Communication Email Sender'
    description: 'Minimal permissions to send email via Azure Communication Services using Entra ID auth (no connection string/access key).'
    type: 'CustomRole'
    permissions: [
      {
        actions: [
          'Microsoft.Communication/CommunicationServices/Read'
          'Microsoft.Communication/CommunicationServices/Write'
          'Microsoft.Communication/EmailServices/write'
        ]
        notActions: []
      }
    ]
    assignableScopes: [
      communicationService.id
    ]
  }
}

resource emailSenderRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(communicationService.id, managedIdentityPrincipalId, emailSenderRole.id)
  scope: communicationService
  properties: {
    roleDefinitionId: emailSenderRole.id
    principalId: managedIdentityPrincipalId
    principalType: 'ServicePrincipal'
  }
}

output acsEndpoint string = 'https://${communicationService.properties.hostName}'
output communicationServiceId string = communicationService.id
output emailServiceId string = emailService.id
output domainId string = domain.id
output domainName string = domain.name
// The actual Azure-generated sender FQDN/address (e.g. DoNotReply@<guid>.azurecomm.net) is not exposed
// as a documented, readable Bicep property on this resource/API version — it must be read post-deployment
// (Azure Portal or `az communication email domain show`) and is intentionally NOT output here.
