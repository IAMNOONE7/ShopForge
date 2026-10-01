param location string
param namePrefix string
param readerPrincipalId string
param storageAccountName string
param insightsName string
param postgresHost string
param databaseName string
param databaseAdministrator string
@secure()
param databasePassword string
@secure()
param stripeSecretKey string
@secure()
param stripeWebhookSecret string
@secure()
param mailgunApiKey string

resource storage 'Microsoft.Storage/storageAccounts@2024-01-01' existing = {
  name: storageAccountName
}

resource insights 'Microsoft.Insights/components@2020-02-02' existing = {
  name: insightsName
}



resource vault 'Microsoft.KeyVault/vaults@2024-11-01' = {
  name: '${namePrefix}-kv'
  location: location
  properties: {
    sku: {
      family: 'A'
      name: 'standard'
    }
    tenantId: subscription().tenantId
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 30
  }
}

// Configuration binds `Section--Key` from Key Vault the way it binds `Section:Key` from a file (D-076). The values
// are written one by one because a loop would need them all known before the deployment starts.
resource databaseSecret 'Microsoft.KeyVault/vaults/secrets@2024-11-01' = {
  parent: vault
  name: 'ConnectionStrings--ShopForge'
  properties: {
    value: 'Host=${postgresHost};Database=${databaseName};Username=${databaseAdministrator};Password=${databasePassword};SSL Mode=Require'
  }
}

resource fileStorageSecret 'Microsoft.KeyVault/vaults/secrets@2024-11-01' = {
  parent: vault
  name: 'ConnectionStrings--FileStorage'
  properties: {
    value: 'DefaultEndpointsProtocol=https;AccountName=${storageAccountName};AccountKey=${storage.listKeys().keys[0].value};EndpointSuffix=${environment().suffixes.storage}'
  }
}

resource insightsSecret 'Microsoft.KeyVault/vaults/secrets@2024-11-01' = {
  parent: vault
  name: 'ApplicationInsights--ConnectionString'
  properties: {
    value: insights.properties.ConnectionString
  }
}

resource stripeKeySecret 'Microsoft.KeyVault/vaults/secrets@2024-11-01' = if (!empty(stripeSecretKey)) {
  parent: vault
  name: 'Payments--Stripe--SecretKey'
  properties: {
    value: stripeSecretKey
  }
}

resource stripeWebhookSecretValue 'Microsoft.KeyVault/vaults/secrets@2024-11-01' = if (!empty(stripeWebhookSecret)) {
  parent: vault
  name: 'Payments--Stripe--WebhookSecret'
  properties: {
    value: stripeWebhookSecret
  }
}

resource mailgunApiKeySecret 'Microsoft.KeyVault/vaults/secrets@2024-11-01' = if (!empty(mailgunApiKey)) {
  parent: vault
  name: 'Email--Mailgun--ApiKey'
  properties: {
    value: mailgunApiKey
  }
}

// Key Vault Secrets User: the container app reads what it needs and nothing writes from the outside.
resource reader 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: vault
  name: guid(vault.id, readerPrincipalId, '4633458b-17de-408a-b874-0445c86b69e6')
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6')
    principalId: readerPrincipalId
    principalType: 'ServicePrincipal'
  }
}

output vaultUri string = vault.properties.vaultUri

// A second vault, for credentials that belong to a merchant rather than to the deployment: a gateway's secret, a
// carrier's password. The application writes here, because a merchant rotates their own credentials from the
// admin, and it must not be able to write where the database connection string lives (D-152).
resource providerVault 'Microsoft.KeyVault/vaults@2024-11-01' = {
  name: '${vault.name}-prov'
  location: location
  properties: {
    sku: {
      family: 'A'
      name: 'standard'
    }
    tenantId: subscription().tenantId
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 90
    enablePurgeProtection: true
    publicNetworkAccess: 'Enabled'
  }
}

// Key Vault Secrets Officer, scoped to the provider vault alone: read, write and rotate what a merchant owns.
resource providerSecretsOfficer 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: providerVault
  name: guid(providerVault.id, readerPrincipalId, 'b86a8fe4-44ce-4948-aee7-eccb2c155cd7')
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'b86a8fe4-44ce-4948-aee7-eccb2c155cd7')
    principalId: readerPrincipalId
    principalType: 'ServicePrincipal'
  }
}

output providerVaultUri string = providerVault.properties.vaultUri
