param namePrefix string
@description('The platform\'s own host for the admin, e.g. admin.example.cz. Empty serves the admin on the endpoint\'s own host name (D-206).')
param adminHost string = ''

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

// A platform domain, so the template's to own (D-207). Front Door issues and renews its certificate once a TXT record
// at _dnsauth.<host> carries the token below, which is an output for whoever publishes the record.
resource adminDomain 'Microsoft.Cdn/profiles/customDomains@2024-02-01' = if (!empty(adminHost)) {
  parent: profile
  name: 'admin'
  properties: {
    hostName: adminHost
    tlsSettings: {
      certificateType: 'ManagedCertificate'
      minimumTlsVersion: 'TLS12'
    }
  }
}

output profileName string = profile.name
output endpointName string = endpoint.name
// The app trusts forwarded headers only from this profile, and answers nothing else (D-075, D-207).
output frontDoorId string = profile.properties.frontDoorId
output endpointHostName string = endpoint.properties.hostName
output endpointId string = endpoint.id
output adminHost string = empty(adminHost) ? endpoint.properties.hostName : adminHost
output platformDomainIds array = empty(adminHost) ? [] : [adminDomain.id]
output adminValidationToken string = empty(adminHost) ? '' : adminDomain!.properties.validationProperties.validationToken
