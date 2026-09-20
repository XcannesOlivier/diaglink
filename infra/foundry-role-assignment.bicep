param principalId string
param foundryAccountName string

var foundryUserRoleDefinitionId = subscriptionResourceId(
  'Microsoft.Authorization/roleDefinitions',
  '53ca6127-db72-4b80-b1b0-d745d6d5456d'
)

resource foundryAccount 'Microsoft.CognitiveServices/accounts@2025-06-01' existing = {
  name: foundryAccountName
}

resource foundryUserRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(
    foundryAccount.id,
    'foundry-user-web-managed-identity'
  )
  scope: foundryAccount
  properties: {
    roleDefinitionId: foundryUserRoleDefinitionId
    principalId: principalId
    principalType: 'ServicePrincipal'
  }
}