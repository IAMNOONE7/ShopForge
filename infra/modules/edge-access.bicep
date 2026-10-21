@description('The app\'s identity, which binds merchants\' domains at the edge.')
param principalId string
@description('The bootstrap\'s edge domains role, the same id in this subscription for every release (D-212).')
param roleId string

// Over the environment's resource group, which holds the profile and its firewall policy. The role's own actions are what
// keep the app to merchants' domains, the route and policy that carry them, and joining the firewall policy (D-212).
resource edgeDomains 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(resourceGroup().id, principalId, roleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleId)
    principalId: principalId
    principalType: 'ServicePrincipal'
  }
}
