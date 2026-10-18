param location string
param namePrefix string
@description('The GitHub repository whose workflows may sign in, as owner/name.')
param repository string
param environments array

// What GitHub's Deploy workflow signs in as: an identity with no secret to leak, trusted for this repository's
// environments alone (D-208).
resource deployer 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: '${namePrefix}-deployer'
  location: location
}

// One credential per GitHub environment, so only a job running in that environment gets a token. Azure writes an
// identity's credentials one at a time.
@batchSize(1)
resource github 'Microsoft.ManagedIdentity/userAssignedIdentities/federatedIdentityCredentials@2023-01-31' = [
  for environment in environments: {
    parent: deployer
    name: 'github-${environment}'
    properties: {
      issuer: 'https://token.actions.githubusercontent.com'
      subject: 'repo:${repository}:environment:${environment}'
      audiences: ['api://AzureADTokenExchange']
    }
  }
]

output principalId string = deployer.properties.principalId
output clientId string = deployer.properties.clientId
