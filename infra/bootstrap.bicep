targetScope = 'subscription'

// What every environment's template assumes, made once per subscription by somebody who may assign roles: the shared
// resource group, the container registry, and the identity GitHub's Deploy workflow signs in as (D-208). Each
// environment is then main.bicep with its own parameter file in infra/environments.

@description('Short name used as a prefix for the shared resources, e.g. shopforge.')
param namePrefix string = 'shopforge'
param location string = 'westeurope'
@description('The container registry\'s name: 5 to 50 letters and digits, unique across Azure.')
@minLength(5)
@maxLength(50)
param registryName string
@description('The GitHub repository whose Deploy workflow may sign in, as owner/name.')
param repository string
@description('The GitHub environments that workflow deploys to; each gets a federated credential of its own.')
param environments array = ['staging', 'production']

// Contributor: an environment's resource group and everything in it.
var contributor = 'b24988ac-6180-42a0-ab88-20f7382dd24c'

// Role Based Access Control Administrator, held to the three roles a release grants its app's identity.
var rbacAdministrator = 'f58310d9-a9f6-439a-9e8d-f62e7b41a168'

// AcrPull, Key Vault Secrets User, Key Vault Secrets Officer.
var grantable = [
  '7f951dda-4ed3-4680-a7ca-43fe172d538d'
  '4633458b-17de-408a-b874-0445c86b69e6'
  'b86a8fe4-44ce-4948-aee7-eccb2c155cd7'
]

resource shared 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: '${namePrefix}-shared-rg'
  location: location
}

module deployer 'modules/deployer.bicep' = {
  scope: shared
  name: 'deployer'
  params: {
    location: location
    namePrefix: namePrefix
    repository: repository
    environments: environments
  }
}

module registry 'modules/registry.bicep' = {
  scope: shared
  name: 'registry'
  params: {
    location: location
    registryName: registryName
    pusherPrincipalId: deployer.outputs.principalId
  }
}

resource deployerContributes 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(subscription().id, namePrefix, 'deployer', contributor)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', contributor)
    principalId: deployer.outputs.principalId
    principalType: 'ServicePrincipal'
  }
}

// The deployer can grant, and take back, only what a release grants: a stolen workflow token cannot make itself Owner.
resource deployerGrants 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(subscription().id, namePrefix, 'deployer', rbacAdministrator)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', rbacAdministrator)
    principalId: deployer.outputs.principalId
    principalType: 'ServicePrincipal'
    conditionVersion: '2.0'
    condition: '((!(ActionMatches{\'Microsoft.Authorization/roleAssignments/write\'})) OR (@Request[Microsoft.Authorization/roleAssignments:RoleDefinitionId] ForAnyOfAnyValues:GuidEquals {${join(grantable, ', ')}})) AND ((!(ActionMatches{\'Microsoft.Authorization/roleAssignments/delete\'})) OR (@Resource[Microsoft.Authorization/roleAssignments:RoleDefinitionId] ForAnyOfAnyValues:GuidEquals {${join(grantable, ', ')}}))'
  }
}

// What the GitHub environments are told: the deployer's client id and the tenant and subscription as secrets, the
// registry as variables.
output deployerClientId string = deployer.outputs.clientId
output tenantId string = tenant().tenantId
output subscriptionId string = subscription().subscriptionId
output registryName string = registry.outputs.name
output registryServer string = registry.outputs.loginServer
output registryResourceGroup string = shared.name
