param location string
param namePrefix string
param databaseAdministrator string
@secure()
param databasePassword string
param skuName string
param tier string
param storageGb int
param backupDays int
@description('Where the server\'s logs go (D-208).')
param workspaceId string

resource postgres 'Microsoft.DBforPostgreSQL/flexibleServers@2024-08-01' = {
  name: '${namePrefix}-postgres'
  location: location
  sku: {
    name: skuName
    tier: tier
  }
  properties: {
    version: '18'
    administratorLogin: databaseAdministrator
    administratorLoginPassword: databasePassword
    storage: { storageSizeGB: storageGb }
    backup: {
      backupRetentionDays: backupDays
      geoRedundantBackup: 'Disabled'
    }
    highAvailability: { mode: 'Disabled' }
  }
}

resource postgresLogs 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  scope: postgres
  name: 'logs'
  properties: {
    workspaceId: workspaceId
    logs: [
      {
        categoryGroup: 'allLogs'
        enabled: true
      }
    ]
  }
}

resource database 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2024-08-01' = {
  parent: postgres
  name: 'shopforge'
  properties: {
    charset: 'UTF8'
    collation: 'en_US.utf8'
  }
}

// The extensions the migrations create, and only those: Azure lets the database's owner create what this lists and
// nothing else. A release deploys this before its migration job runs (D-224).
resource extensions 'Microsoft.DBforPostgreSQL/flexibleServers/configurations@2024-08-01' = {
  parent: postgres
  name: 'azure.extensions'
  properties: {
    value: 'unaccent'
    source: 'user-override'
  }
}

// The app and its migration job reach the server from Container Apps; nothing outside Azure does, so a release never
// opens the database to the machine that runs it (D-205).
resource allowAzureServices 'Microsoft.DBforPostgreSQL/flexibleServers/firewallRules@2024-08-01' = {
  parent: postgres
  name: 'allow-azure-services'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource storage 'Microsoft.Storage/storageAccounts@2024-01-01' = {
  name: replace('${namePrefix}media', '-', '')
  location: location
  sku: { name: 'Standard_LRS' }
  kind: 'StorageV2'
  properties: {
    allowBlobPublicAccess: false
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2024-01-01' = {
  parent: storage
  name: 'default'
}

resource mediaContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2024-01-01' = {
  parent: blobService
  name: 'media'
}

output postgresHost string = postgres.properties.fullyQualifiedDomainName
output databaseName string = database.name
output storageAccountName string = storage.name
