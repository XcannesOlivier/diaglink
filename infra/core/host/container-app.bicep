param name string
param location string
param tags object
param containerAppsEnvironmentId string
param containerRegistryName string
param containerImage string
param targetPort int
param env array = []
param enableIngress bool = true
param external bool = true
param healthProbePath string = ''
param readinessProbePath string = ''
param cpu string = '0.5'
param memory string = '1Gi'
param minReplicas int = 0
param maxReplicas int = 3
param userAssignedIdentityId string
param secrets array = []
param customDomains array = []
param additionalContainers array = []
param volumes array = []

resource containerRegistry 'Microsoft.ContainerRegistry/registries@2023-07-01' existing = {
  name: containerRegistryName
}

resource containerApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: name
  location: location
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${userAssignedIdentityId}': {}
    }
  }
  properties: {
    managedEnvironmentId: containerAppsEnvironmentId
    workloadProfileName: 'Consumption'
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: enableIngress ? {
        external: external
        targetPort: targetPort
        transport: 'auto'
        allowInsecure: false
        traffic: [
          {
            latestRevision: true
            weight: 100
          }
        ]
        customDomains: customDomains
      } : null
      registries: [
        {
          server: containerRegistry.properties.loginServer
          identity: !empty(userAssignedIdentityId) ? userAssignedIdentityId : 'system'
        }
      ]
      secrets: secrets
    }
    template: {
      containers: concat([
        {
          name: name
          image: containerImage
          env: env
          resources: {
            cpu: json(cpu)
            memory: memory
          }
          probes: !empty(healthProbePath) ? [
            {
              type: 'Liveness'
              httpGet: {
                path: healthProbePath
                port: targetPort
              }
              periodSeconds: 30
              failureThreshold: 3
            }
            {
              type: 'Startup'
              httpGet: {
                path: healthProbePath
                port: targetPort
              }
              periodSeconds: 10
              failureThreshold: 30
              initialDelaySeconds: 5
            }
            {
              type: 'Readiness'
              httpGet: {
                path: !empty(readinessProbePath) ? readinessProbePath : healthProbePath
                port: targetPort
              }
              periodSeconds: 10
              failureThreshold: 3
              successThreshold: 1
              initialDelaySeconds: 5
            }
          ] : []
        }
      ], additionalContainers)
      volumes: volumes
      scale: {
        minReplicas: minReplicas
        maxReplicas: maxReplicas
      }
    }
  }
}

output id string = containerApp.id
output name string = containerApp.name
output fqdn string = enableIngress ? containerApp.properties.configuration.ingress.fqdn : ''
