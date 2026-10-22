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
@secure()
param stripeSecretKey string
@secure()
param stripeMachineRequestWebhookSecret string
@secure()
param stripeWebhookSecret string
@secure()
param stripeTopUpWebhookSecret string
param stripePriceId string
param stripeTopUpReturnUrl string
@description('Enable Stripe payment flows.')
param stripeEnabled bool
@description('Stripe Checkout return URL for the public first-machine request flow.')
param stripeMachineRequestReturnUrl string
@description('Stripe Checkout return URL for authenticated application machine requests.')
param stripeMachineRequestAppReturnUrl string
@description('Custom domain bound to the Container App ingress.')
param customDomainName string
@description('Name of the existing Container Apps managed certificate.')
param customDomainCertificateName string
@description('Public custom domain bound to the same Container App ingress.')
param publicCustomDomainName string
@description('Name of the existing Container Apps managed certificate for the public domain.')
param publicCustomDomainCertificateName string = ''
param claudeDirectDeployment string = 'claude-sonnet-5'
param claudeFileSearchDiagnosticsEnabled bool = false
@description('Existing non-secret deployment identities used to resolve AI pricing.')
param aiPricingIdentityDeployments array
@description('Immutable ACR image for the autossh sidecar.')
param postgresTunnelImageName string
@description('SSH hostname or IP of the Vultr server. PostgreSQL itself remains private.')
param vultrSshHost string
param vultrSshPort int = 22
param vultrSshUser string = 'diaglink_tunnel'
@description('Versionless Key Vault secret URI containing the complete PostgreSQL connection string.')
param postgresqlConnectionStringSecretUri string
@description('Versionless Key Vault secret URI containing the dedicated OpenSSH private key.')
param vultrSshPrivateKeySecretUri string
@description('Versionless Key Vault secret URI containing the pinned Vultr SSH known_hosts line.')
param vultrSshKnownHostsSecretUri string

var abbrs = loadJsonContent('./abbreviations.json')

resource containerAppsEnvironment 'Microsoft.App/managedEnvironments@2024-03-01' existing = {
  name: last(split(containerAppsEnvironmentId, '/'))
}

resource customDomainCertificate 'Microsoft.App/managedEnvironments/managedCertificates@2024-03-01' existing = {
  parent: containerAppsEnvironment
  name: customDomainCertificateName
}

resource publicCustomDomainCertificate 'Microsoft.App/managedEnvironments/managedCertificates@2024-03-01' existing = if (!empty(publicCustomDomainCertificateName)) {
  parent: containerAppsEnvironment
  name: publicCustomDomainCertificateName
}

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

// PostgreSQL production connection resolved from Key Vault. The application connects to the
// loopback port exposed by the SSH sidecar; PostgreSQL is never exposed directly to Azure.
var diagLinkEnv = [
  {
    name: 'Database__Provider'
    value: 'PostgreSql'
  }
  {
    name: 'ConnectionStrings__DiagLink'
    secretRef: 'diaglink-postgresql'
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

var stripeEnv = [
  {
    name: 'STRIPE_SECRET_KEY'
    secretRef: 'stripe-secret-key'
  }
  {
    name: 'STRIPE_MACHINE_REQUEST_WEBHOOK_SECRET'
    secretRef: 'stripe-machine-request-webhook-secret'
  }
  {
    name: 'STRIPE_WEBHOOK_SECRET'
    secretRef: 'stripe-webhook-secret'
  }
  {
    name: 'STRIPE_TOPUP_WEBHOOK_SECRET'
    secretRef: 'stripe-topup-webhook-secret'
  }
  {
    name: 'STRIPE_PRICE_ID'
    value: stripePriceId
  }
  {
    name: 'STRIPE_TOPUP_RETURN_URL'
    value: stripeTopUpReturnUrl
  }
  {
    name: 'STRIPE_ENABLED'
    value: string(stripeEnabled)
  }
  {
    name: 'STRIPE_MACHINE_REQUEST_RETURN_URL'
    value: stripeMachineRequestReturnUrl
  }
  {
    name: 'STRIPE_MACHINE_REQUEST_APP_RETURN_URL'
    value: stripeMachineRequestAppReturnUrl
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
  {
    name: 'stripe-secret-key'
    value: stripeSecretKey
  }
  {
    name: 'stripe-machine-request-webhook-secret'
    value: stripeMachineRequestWebhookSecret
  }
  {
    name: 'stripe-webhook-secret'
    value: stripeWebhookSecret
  }
  {
    name: 'stripe-topup-webhook-secret'
    value: stripeTopUpWebhookSecret
  }
  {
    name: 'diaglink-postgresql'
    keyVaultUrl: postgresqlConnectionStringSecretUri
    identity: userAssignedIdentityId
  }
  {
    name: 'vultr-ssh-private-key'
    keyVaultUrl: vultrSshPrivateKeySecretUri
    identity: userAssignedIdentityId
  }
  {
    name: 'vultr-ssh-known-hosts'
    keyVaultUrl: vultrSshKnownHostsSecretUri
    identity: userAssignedIdentityId
  }
]

var claudeEnv = [
  {
    name: 'ClaudeDirectChat__Deployment'
    value: claudeDirectDeployment
  }
  {
    name: 'ClaudeDirectChat__FileSearchDiagnostics__Enabled'
    value: string(claudeFileSearchDiagnosticsEnabled)
  }
]

var pricingIdentityEnvGroups = [for (identity, index) in aiPricingIdentityDeployments: [
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

var containerEnv = concat(baseEnv, miEnv, oboEnv, diagLinkEnv, emailEnv, authEnv, storageEnv, contactEnv, stripeEnv, claudeEnv, pricingIdentityEnv)

var tunnelContainers = [
  {
    name: 'postgres-tunnel'
    image: postgresTunnelImageName
    env: [
      {
        name: 'VULTR_SSH_HOST'
        value: vultrSshHost
      }
      {
        name: 'VULTR_SSH_PORT'
        value: string(vultrSshPort)
      }
      {
        name: 'VULTR_SSH_USER'
        value: vultrSshUser
      }
      {
        name: 'POSTGRES_TUNNEL_LOCAL_PORT'
        value: '15432'
      }
      {
        name: 'POSTGRES_REMOTE_HOST'
        value: '127.0.0.1'
      }
      {
        name: 'POSTGRES_REMOTE_PORT'
        value: '5432'
      }
    ]
    resources: {
      cpu: json('0.25')
      memory: '0.5Gi'
    }
    volumeMounts: [
      {
        volumeName: 'postgres-tunnel-secrets'
        mountPath: '/mnt/ssh-secrets'
      }
    ]
    probes: [
      {
        type: 'Liveness'
        tcpSocket: {
          port: 15432
        }
        periodSeconds: 30
        failureThreshold: 3
        initialDelaySeconds: 10
      }
      {
        type: 'Startup'
        tcpSocket: {
          port: 15432
        }
        periodSeconds: 5
        failureThreshold: 24
        initialDelaySeconds: 2
      }
    ]
  }
]

var tunnelVolumes = [
  {
    name: 'postgres-tunnel-secrets'
    storageType: 'Secret'
    secrets: [
      {
        secretRef: 'vultr-ssh-private-key'
        path: 'id_ed25519'
      }
      {
        secretRef: 'vultr-ssh-known-hosts'
        path: 'known_hosts'
      }
    ]
  }
]

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
    readinessProbePath: '/api/health/ready'
    userAssignedIdentityId: userAssignedIdentityId
    secrets: containerSecrets
    additionalContainers: tunnelContainers
    volumes: tunnelVolumes
    customDomains: concat([
      {
        name: customDomainName
        bindingType: 'SniEnabled'
        certificateId: customDomainCertificate.id
      }
    ], !empty(publicCustomDomainCertificateName) ? [
      {
        name: publicCustomDomainName
        bindingType: 'SniEnabled'
        certificateId: publicCustomDomainCertificate!.id
      }
    ] : [])
  }
}

output webEndpoint string = 'https://${webApp.outputs.fqdn}'
output webAppName string = webApp.outputs.name
