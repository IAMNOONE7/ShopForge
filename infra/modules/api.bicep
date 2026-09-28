param location string
param namePrefix string
param workspaceName string
param image string
param registryServer string
param keyVaultUri string
param frontDoorId string
@description('Which e-mail provider carries transactional mail; "log" sends nothing.')
param emailProvider string = 'log'
param emailSenderAddress string = ''
param mailgunDomain string = ''

resource workspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' existing = {
  name: workspaceName
}

resource environment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: '${namePrefix}-env'
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: workspace.properties.customerId
        sharedKey: workspace.listKeys().primarySharedKey
      }
    }
  }
}

resource api 'Microsoft.App/containerApps@2024-03-01' = {
  name: '${namePrefix}-api'
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    managedEnvironmentId: environment.id
    configuration: {
      ingress: {
        external: true
        targetPort: 8080
        transport: 'http'
        // Only Front Door reaches the app; it proves that with its profile id on every request (D-075).
        ipSecurityRestrictions: []
      }
      registries: [
        {
          server: registryServer
          identity: 'system'
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'api'
          image: image
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: [
            { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
            { name: 'KeyVault__Uri', value: keyVaultUri }
            { name: 'Edge__FrontDoorId', value: frontDoorId }
            { name: 'Email__Provider', value: emailProvider }
            { name: 'Email__SenderAddress', value: emailSenderAddress }
            { name: 'Email__Mailgun__Domain', value: mailgunDomain }
          ]
          probes: [
            {
              type: 'Liveness'
              httpGet: { path: '/health/live', port: 8080 }
              periodSeconds: 30
            }
            {
              type: 'Readiness'
              httpGet: { path: '/health/ready', port: 8080 }
              periodSeconds: 10
            }
          ]
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 5
        rules: [
          {
            name: 'http'
            http: {
              metadata: {
                concurrentRequests: '50'
              }
            }
          }
        ]
      }
    }
  }
}

output principalId string = api.identity.principalId
output fqdn string = api.properties.configuration.ingress.fqdn
