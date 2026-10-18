param location string
param registryName string
param pusherPrincipalId string

// Every environment's images, in one place. Each app pulls with its own identity, granted by its environment's
// template; only the deployer pushes. No admin user, so the registry has no password at all (D-208).
resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: registryName
  location: location
  sku: {
    name: 'Basic'
  }
  properties: {
    adminUserEnabled: false
  }
}

// AcrPush.
resource push 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: registry
  name: guid(registry.id, pusherPrincipalId, '8311e382-0749-4cb8-b61a-304f252e45ec')
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '8311e382-0749-4cb8-b61a-304f252e45ec')
    principalId: pusherPrincipalId
    principalType: 'ServicePrincipal'
  }
}

output name string = registry.name
output loginServer string = registry.properties.loginServer
