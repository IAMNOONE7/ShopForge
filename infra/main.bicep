targetScope = 'subscription'

@description('Short name used as a prefix for every resource, e.g. shopforge-prod.')
param namePrefix string
param location string = 'westeurope'
param databaseAdministrator string = 'shopforge'
@secure()
param databasePassword string
@description('The release\'s container image, e.g. myregistry.azurecr.io/shopforge-api:1234. The migration job runs it in both of a release\'s deployments; the app only in the second.')
param image string
@description('The container registry the release images are pushed to, which the app and its migration job pull from with their own identity.')
param registryName string
@description('The resource group the registry is in.')
param registryResourceGroup string
@description('False for the first of a release\'s two deployments: everything the app needs, and the job that migrates for the new image, while the app that is serving is left alone. The release migrates, then deploys again with true (D-205).')
param deployApp bool = true
@description('Host name the built storefront is served from (the storage account static website).')
param storefrontFqdn string
@description('The zone store subdomains are composed under, e.g. shops.example.cz (D-189).')
param storeHostSuffix string
@secure()
param stripeSecretKey string = ''
@secure()
param stripeWebhookSecret string = ''
@description('Which provider carries transactional mail. A deployment always sends mail: "log", which only writes it down, is for development (D-205).')
@allowed(['mailgun'])
param emailProvider string = 'mailgun'
@minLength(3)
param emailSenderAddress string
@minLength(1)
param mailgunDomain string
@secure()
@minLength(1)
param mailgunApiKey string
@description('The key the delivery events Mailgun posts back are signed with, which the suppression list trusts (19d).')
@secure()
@minLength(1)
param mailgunWebhookSigningKey string

resource group 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: '${namePrefix}-rg'
  location: location
}

module observability 'modules/observability.bicep' = {
  scope: group
  name: 'observability'
  params: {
    location: location
    namePrefix: namePrefix
  }
}

module data 'modules/data.bicep' = {
  scope: group
  name: 'data'
  params: {
    location: location
    namePrefix: namePrefix
    databaseAdministrator: databaseAdministrator
    databasePassword: databasePassword
  }
}

// The profile exists before the app, because the app has to be told which edge to trust; its routes come afterwards,
// once the app has a host name to send traffic to.
module edgeProfile 'modules/edge-profile.bicep' = {
  scope: group
  name: 'edge-profile'
  params: {
    namePrefix: namePrefix
  }
}

// Made first and granted everything before the app or its job exists, so neither starts unable to pull its image or
// read its vault (D-205).
module identity 'modules/identity.bicep' = {
  scope: group
  name: 'identity'
  params: {
    location: location
    namePrefix: namePrefix
  }
}

module registryAccess 'modules/registry-access.bicep' = {
  scope: resourceGroup(registryResourceGroup)
  name: '${namePrefix}-registry-access'
  params: {
    registryName: registryName
    principalId: identity.outputs.principalId
  }
}

module vault 'modules/vault.bicep' = {
  scope: group
  name: 'vault'
  params: {
    location: location
    namePrefix: namePrefix
    appPrincipalId: identity.outputs.principalId
    storageAccountName: data.outputs.storageAccountName
    insightsName: observability.outputs.insightsName
    postgresHost: data.outputs.postgresHost
    databaseName: data.outputs.databaseName
    databaseAdministrator: databaseAdministrator
    databasePassword: databasePassword
    stripeSecretKey: stripeSecretKey
    stripeWebhookSecret: stripeWebhookSecret
    mailgunApiKey: mailgunApiKey
    mailgunWebhookSigningKey: mailgunWebhookSigningKey
  }
}

module environment 'modules/environment.bicep' = {
  scope: group
  name: 'environment'
  params: {
    location: location
    namePrefix: namePrefix
    workspaceName: observability.outputs.workspaceName
  }
}

// What the app and the job that migrates for it are both told. Everything secret each reads from the vault with the
// identity named here (D-076).
var settings = [
  { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
  { name: 'AZURE_CLIENT_ID', value: identity.outputs.clientId }
  { name: 'KeyVault__Uri', value: vault.outputs.vaultUri }
  { name: 'ProviderSecrets__VaultUri', value: vault.outputs.providerVaultUri }
  { name: 'Edge__FrontDoorId', value: edgeProfile.outputs.frontDoorId }
  { name: 'Email__Provider', value: emailProvider }
  { name: 'Email__SenderAddress', value: emailSenderAddress }
  { name: 'Email__Mailgun__Domain', value: mailgunDomain }
  { name: 'StoreHosts__Suffix', value: storeHostSuffix }
]

module migrations 'modules/migrations.bicep' = {
  scope: group
  name: 'migrations'
  params: {
    location: location
    namePrefix: namePrefix
    environmentId: environment.outputs.id
    identityId: identity.outputs.id
    registryServer: registryAccess.outputs.loginServer
    image: image
    settings: settings
  }
}

module api 'modules/api.bicep' = if (deployApp) {
  scope: group
  name: 'api'
  params: {
    location: location
    namePrefix: namePrefix
    environmentId: environment.outputs.id
    identityId: identity.outputs.id
    registryServer: registryAccess.outputs.loginServer
    image: image
    settings: settings
  }
}

module edgeRoutes 'modules/edge-routes.bicep' = if (deployApp) {
  scope: group
  name: 'edge-routes'
  params: {
    profileName: edgeProfile.outputs.profileName
    endpointName: edgeProfile.outputs.endpointName
    apiFqdn: api!.outputs.fqdn
    storefrontFqdn: storefrontFqdn
  }
}

output apiFqdn string = deployApp ? api!.outputs.fqdn : ''
output publicHostName string = edgeProfile.outputs.endpointHostName
output vaultUri string = vault.outputs.vaultUri
output migrationJob string = migrations.outputs.name
