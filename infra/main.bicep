targetScope = 'subscription'

@description('Short name used as a prefix for every resource, e.g. shopforge-prod.')
param namePrefix string
param location string = 'westeurope'
param databaseAdministrator string = 'shopforge'
@secure()
param databasePassword string
@description('Container image the app runs, e.g. myregistry.azurecr.io/shopforge-api:1234.')
param image string
param registryServer string
@description('Host name the built storefront is served from (the storage account static website).')
param storefrontFqdn string
@secure()
param stripeSecretKey string = ''
@secure()
param stripeWebhookSecret string = ''
@description('Which e-mail provider carries transactional mail; "log" sends nothing.')
param emailProvider string = 'log'
param emailSenderAddress string = ''
param mailgunDomain string = ''
@secure()
param mailgunApiKey string = ''

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

module api 'modules/api.bicep' = {
  scope: group
  name: 'api'
  params: {
    location: location
    namePrefix: namePrefix
    workspaceName: observability.outputs.workspaceName
    image: image
    registryServer: registryServer
    keyVaultUri: 'https://${namePrefix}-kv${environment().suffixes.keyvaultDns}/'
    providerVaultUri: 'https://${namePrefix}-kv-prov${environment().suffixes.keyvaultDns}/'
    frontDoorId: edgeProfile.outputs.frontDoorId
    emailProvider: emailProvider
    emailSenderAddress: emailSenderAddress
    mailgunDomain: mailgunDomain
  }
}

module edgeRoutes 'modules/edge-routes.bicep' = {
  scope: group
  name: 'edge-routes'
  params: {
    profileName: edgeProfile.outputs.profileName
    endpointName: edgeProfile.outputs.endpointName
    apiFqdn: api.outputs.fqdn
    storefrontFqdn: storefrontFqdn
  }
}

module vault 'modules/vault.bicep' = {
  scope: group
  name: 'vault'
  params: {
    location: location
    namePrefix: namePrefix
    readerPrincipalId: api.outputs.principalId
    storageAccountName: data.outputs.storageAccountName
    insightsName: observability.outputs.insightsName
    postgresHost: data.outputs.postgresHost
    databaseName: data.outputs.databaseName
    databaseAdministrator: databaseAdministrator
    databasePassword: databasePassword
    stripeSecretKey: stripeSecretKey
    stripeWebhookSecret: stripeWebhookSecret
    mailgunApiKey: mailgunApiKey
  }
}

output apiFqdn string = api.outputs.fqdn
output publicHostName string = edgeProfile.outputs.endpointHostName
output vaultUri string = vault.outputs.vaultUri
