param namePrefix string

resource profile 'Microsoft.Cdn/profiles@2024-02-01' = {
  name: '${namePrefix}-fd'
  location: 'global'
  sku: {
    name: 'Standard_AzureFrontDoor'
  }
}

resource endpoint 'Microsoft.Cdn/profiles/afdEndpoints@2024-02-01' = {
  parent: profile
  name: '${namePrefix}-endpoint'
  location: 'global'
  properties: {
    enabledState: 'Enabled'
  }
}

output profileName string = profile.name
output endpointName string = endpoint.name
// The app trusts forwarded headers only from this profile (D-075).
output frontDoorId string = profile.properties.frontDoorId
output endpointHostName string = endpoint.properties.hostName
