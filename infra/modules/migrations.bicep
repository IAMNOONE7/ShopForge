param location string
param namePrefix string
param environmentId string
param identityId string
param registryServer string
param image string
param settings array

// A release's schema is migrated by its own image, inside the environment and with the app's identity and settings,
// before its revision serves (D-205). The deployment starts it once per release and reads how it ended; the database
// is never opened to the runner, and its credentials never leave the vault.
resource job 'Microsoft.App/jobs@2024-03-01' = {
  name: '${namePrefix}-migrate'
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${identityId}': {}
    }
  }
  properties: {
    environmentId: environmentId
    configuration: {
      triggerType: 'Manual'
      manualTriggerConfig: {
        parallelism: 1
        replicaCompletionCount: 1
      }
      // A migration that fails is not tried again behind the deployment's back: it stops the release and says why.
      replicaRetryLimit: 0
      replicaTimeout: 1800
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
          name: 'migrate'
          image: image
          args: ['migrate']
          env: settings
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
        }
      ]
    }
  }
}

output name string = job.name
