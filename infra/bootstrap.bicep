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

// Role Based Access Control Administrator, held to the four roles a release grants its app's identity.
var rbacAdministrator = 'f58310d9-a9f6-439a-9e8d-f62e7b41a168'

// The edge domains role below, the same id in this subscription for every release that grants it (D-212).
var edgeDomains = guid(subscription().id, 'shopforge-edge-domains')

// AcrPull, Key Vault Secrets User, Key Vault Secrets Officer, and the edge domains role.
var grantable = [
  '7f951dda-4ed3-4680-a7ca-43fe172d538d'
  '4633458b-17de-408a-b874-0445c86b69e6'
  'b86a8fe4-44ce-4948-aee7-eccb2c155cd7'
  edgeDomains
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

// What a release's app may do at the edge, and no more (D-212): merchants' custom domains and their validation tokens,
// the route and the security policy that carry them, reading what those refer to, and joining the template's firewall
// policy to that security policy. The profile itself, its platform route's origin and the firewall policy's rules are
// out of its reach. No built-in role is this narrow: CDN Profile Contributor may delete the profile, and Network
// Contributor rewrite the firewall. Whether a security policy needs the join action at all is UNVERIFIED until V19.
resource edgeDomainsRole 'Microsoft.Authorization/roleDefinitions@2022-04-01' = {
  name: edgeDomains
  properties: {
    roleName: '${namePrefix} edge domains (${subscription().subscriptionId})'
    description: 'Binds merchants\' domains at Front Door: custom domains, the runtime\'s route and security policy, and joining the firewall policy.'
    type: 'CustomRole'
    permissions: [
      {
        actions: [
          'Microsoft.Cdn/profiles/read'
          'Microsoft.Cdn/profiles/afdendpoints/read'
          'Microsoft.Cdn/profiles/afdendpoints/routes/read'
          'Microsoft.Cdn/profiles/afdendpoints/routes/write'
          'Microsoft.Cdn/profiles/afdendpoints/routes/delete'
          'Microsoft.Cdn/profiles/origingroups/read'
          'Microsoft.Cdn/profiles/rulesets/read'
          'Microsoft.Cdn/profiles/customdomains/read'
          'Microsoft.Cdn/profiles/customdomains/write'
          'Microsoft.Cdn/profiles/customdomains/delete'
          'Microsoft.Cdn/profiles/customdomains/RefreshValidationToken/action'
          'Microsoft.Cdn/profiles/securitypolicies/read'
          'Microsoft.Cdn/profiles/securitypolicies/write'
          'Microsoft.Cdn/profiles/securitypolicies/delete'
          'Microsoft.Cdn/operationresults/profileresults/afdendpointresults/routeresults/read'
          'Microsoft.Cdn/operationresults/profileresults/customdomainresults/read'
          'Microsoft.Cdn/operationresults/profileresults/securitypolicyresults/read'
          'Microsoft.Network/frontDoorWebApplicationFirewallPolicies/read'
          'Microsoft.Network/frontDoorWebApplicationFirewallPolicies/join/action'
        ]
        notActions: []
      }
    ]
    assignableScopes: [
      subscription().id
    ]
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
