param profileName string
param endpointName string
param apiFqdn string
@description('The platform\'s own domains this route serves besides the endpoint\'s host name. Merchants\' domains are never listed here (D-207).')
param platformDomainIds array = []

resource profile 'Microsoft.Cdn/profiles@2024-02-01' existing = {
  name: profileName
}

resource endpoint 'Microsoft.Cdn/profiles/afdEndpoints@2024-02-01' existing = {
  parent: profile
  name: endpointName
}

resource appOrigins 'Microsoft.Cdn/profiles/originGroups@2024-02-01' = {
  parent: profile
  name: 'app'
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

resource appOrigin 'Microsoft.Cdn/profiles/originGroups/origins@2024-02-01' = {
  parent: appOrigins
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

// Only files whose names change when their content does are cached at the edge, for as long as the app says. Pages,
// the API and everything private reach the app every time (D-206).
resource caching 'Microsoft.Cdn/profiles/ruleSets@2024-02-01' = {
  parent: profile
  name: 'caching'
}

resource hashedAssets 'Microsoft.Cdn/profiles/ruleSets/rules@2024-02-01' = {
  parent: caching
  name: 'hashedAssets'
  properties: {
    order: 1
    conditions: [
      {
        name: 'UrlPath'
        parameters: {
          typeName: 'DeliveryRuleUrlPathMatchConditionParameters'
          operator: 'BeginsWith'
          matchValues: ['/assets/', '/admin/assets/']
          negateCondition: false
          transforms: []
        }
      }
    ]
    actions: [
      {
        name: 'RouteConfigurationOverride'
        parameters: {
          typeName: 'DeliveryRuleRouteConfigurationOverrideActionParameters'
          cacheConfiguration: {
            cacheBehavior: 'HonorOrigin'
            queryStringCachingBehavior: 'IgnoreQueryString'
            isCompressionEnabled: 'Enabled'
          }
        }
      }
    ]
    matchProcessingBehavior: 'Stop'
  }
}

// One route to one origin: the browser sees the pages and the API at one address, which is what the session cookies
// and the store's own host need (D-206). The route is the template's and names only platform domains. A merchant's
// domain goes on a route the runtime owns, so redeploying this cannot unbind any shop (D-207).
resource platformRoute 'Microsoft.Cdn/profiles/afdEndpoints/routes@2024-02-01' = {
  parent: endpoint
  name: 'platform'
  properties: {
    originGroup: { id: appOrigins.id }
    customDomains: [for id in platformDomainIds: { id: id }]
    ruleSets: [{ id: caching.id }]
    patternsToMatch: ['/*']
    supportedProtocols: ['Http', 'Https']
    forwardingProtocol: 'HttpsOnly'
    httpsRedirect: 'Enabled'
    linkToDefaultDomain: 'Enabled'
    enabledState: 'Enabled'
  }
  dependsOn: [appOrigin, hashedAssets]
}
