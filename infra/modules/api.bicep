param location string
param namePrefix string
param environmentId string
param identityId string
param registryServer string
param image string
param settings array
param minReplicas int
param maxReplicas int

resource api 'Microsoft.App/containerApps@2024-03-01' = {
  name: '${namePrefix}-api'
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${identityId}': {}
    }
  }
  properties: {
    managedEnvironmentId: environmentId
    configuration: {
      ingress: {
        external: true
        targetPort: 8080
        transport: 'http'
        // Container Apps can restrict ingress by address range only, not by Front Door's service tag, and Front Door's
        // addresses change too often to write down. So the app refuses every request without this profile's id itself,
        // health probes apart (D-207).
        ipSecurityRestrictions: []
      }
      registries: [
        {
          server: registryServer
          identity: identityId
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
          env: settings
          probes: [
            {
              type: 'Liveness'
              httpGet: { path: '/health/live', port: 8080 }
              periodSeconds: 30
            }
            {
              // Not ready until the database has every migration the image carries, so the revision it replaces keeps
              // serving until then (D-205).
              type: 'Readiness'
              httpGet: { path: '/health/ready', port: 8080 }
              periodSeconds: 10
            }
          ]
        }
      ]
      scale: {
        minReplicas: minReplicas
        maxReplicas: maxReplicas
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

output fqdn string = api.properties.configuration.ingress.fqdn
