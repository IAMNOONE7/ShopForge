param location string
param namePrefix string

// The app's identity, made before anything grants it a role and before the app itself, so the app starts able to pull
// its image and read its vault instead of waiting on a role assignment that waits on it (D-205).
resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: '${namePrefix}-api-identity'
  location: location
}

output id string = identity.id
output principalId string = identity.properties.principalId
output clientId string = identity.properties.clientId
