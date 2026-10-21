param namePrefix string
param profileName string
@description('The endpoint\'s own domain and the platform\'s domains. Merchants\' domains join a security policy the runtime owns (D-207).')
param domainIds array

resource profile 'Microsoft.Cdn/profiles@2024-02-01' existing = {
  name: profileName
}

// Defined now and switched from detection to prevention in V19, once a real domain's traffic shows what it would
// block. The Standard tier carries custom rules only; the managed rule sets need Premium (D-207). The app keeps its
// own limits whatever this does (D-126).
resource policy 'Microsoft.Network/FrontDoorWebApplicationFirewallPolicies@2024-02-01' = {
  name: replace('${namePrefix}waf', '-', '')
  location: 'Global'
  sku: {
    name: 'Standard_AzureFrontDoor'
  }
  properties: {
    policySettings: {
      enabledState: 'Enabled'
      mode: 'Detection'
      requestBodyCheck: 'Enabled'
    }
    customRules: {
      rules: [
        {
          name: 'SignInsPerAddress'
          priority: 100
          enabledState: 'Enabled'
          ruleType: 'RateLimitRule'
          rateLimitDurationInMinutes: 1
          rateLimitThreshold: 60
          matchConditions: [
            {
              matchVariable: 'RequestUri'
              operator: 'Contains'
              negateCondition: false
              matchValue: ['/auth/login', '/account/login']
              transforms: ['Lowercase']
            }
          ]
          action: 'Block'
        }
      ]
    }
  }
}

// The template's own security policy covers the platform's domains only, for the same reason the route does (D-207).
resource platform 'Microsoft.Cdn/profiles/securityPolicies@2024-02-01' = {
  parent: profile
  name: 'platform'
  properties: {
    parameters: {
      type: 'WebApplicationFirewall'
      wafPolicy: {
        id: policy.id
      }
      associations: [
        {
          domains: [for id in domainIds: { id: id }]
          patternsToMatch: ['/*']
        }
      ]
    }
  }
}

output wafPolicyName string = policy.name
// The runtime's security policy applies the same firewall policy to merchants' domains (D-207, D-212).
output wafPolicyId string = policy.id
