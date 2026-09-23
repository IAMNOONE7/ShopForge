param profileName string
param endpointName string
param apiFqdn string
param storefrontFqdn string

resource profile 'Microsoft.Cdn/profiles@2024-02-01' existing = {
  name: profileName
}

resource endpoint 'Microsoft.Cdn/profiles/afdEndpoints@2024-02-01' existing = {
  parent: profile
  name: endpointName
}

resource apiOrigins 'Microsoft.Cdn/profiles/originGroups@2024-02-01' = {
  parent: profile
  name: 'api'
  properties: {
    loadBalancingSettings: {
      sampleSize: 4
      successfulSamplesRequired: 3
    }
    healthProbeSettings: {
      probePath: '/health/ready'
      probeRequestType: 'GET'
      probeProtocol: 'Https'
      probeIntervalInSeconds: 30
    }
  }
}

resource apiOrigin 'Microsoft.Cdn/profiles/originGroups/origins@2024-02-01' = {
  parent: apiOrigins
  name: 'container-app'
  properties: {
    hostName: apiFqdn
    originHostHeader: apiFqdn
    httpsPort: 443
    priority: 1
    weight: 1000
    enforceCertificateNameCheck: true
  }
}

resource storefrontOrigins 'Microsoft.Cdn/profiles/originGroups@2024-02-01' = {
  parent: profile
  name: 'storefront'
  properties: {
    loadBalancingSettings: {
      sampleSize: 4
      successfulSamplesRequired: 3
    }
  }
}

resource storefrontOrigin 'Microsoft.Cdn/profiles/originGroups/origins@2024-02-01' = {
  parent: storefrontOrigins
  name: 'static-site'
  properties: {
    hostName: storefrontFqdn
    originHostHeader: storefrontFqdn
    httpsPort: 443
    priority: 1
    weight: 1000
    enforceCertificateNameCheck: true
  }
}

// The browser sees one origin: /api/* is the container app, everything else is the built storefront (D-034).
resource apiRoute 'Microsoft.Cdn/profiles/afdEndpoints/routes@2024-02-01' = {
  parent: endpoint
  name: 'api'
  properties: {
    originGroup: { id: apiOrigins.id }
    patternsToMatch: ['/api/*']
    supportedProtocols: ['Https']
    forwardingProtocol: 'HttpsOnly'
    httpsRedirect: 'Enabled'
    linkToDefaultDomain: 'Enabled'
    enabledState: 'Enabled'
  }
  dependsOn: [apiOrigin]
}

resource storefrontRoute 'Microsoft.Cdn/profiles/afdEndpoints/routes@2024-02-01' = {
  parent: endpoint
  name: 'storefront'
  properties: {
    originGroup: { id: storefrontOrigins.id }
    patternsToMatch: ['/*']
    supportedProtocols: ['Https']
    forwardingProtocol: 'HttpsOnly'
    httpsRedirect: 'Enabled'
    linkToDefaultDomain: 'Enabled'
    enabledState: 'Enabled'
  }
  dependsOn: [storefrontOrigin]
}

