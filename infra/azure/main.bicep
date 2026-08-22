targetScope = 'subscription'

// RepoPulse Azure staging bootstrap — creates a DEDICATED resource group and
// the supporting infrastructure for RepoPulse.AuthApi on Azure Container
// Apps Consumption. See docs/adr/004-production-hosting.md for the
// architecture decision and docs/deployment/azure-staging-runbook.md for
// how and when to actually run this deployment.
//
// This template intentionally does NOT create:
//   - An Azure Container Registry (a public GHCR image is used instead).
//   - A Log Analytics workspace (the Container Apps environment explicitly
//     sets appLogsConfiguration.destination: 'none' — see
//     modules/containerAppsEnvironment.bicep — rather than relying on any
//     default behavior).
//   - Any Key Vault secret (the real GitHubOAuth ClientSecret is added by a
//     human, manually, in a separate step — see the runbook).
// All three are cost/scope decisions, not omissions — see the runbook's
// "Maliyet korumaları" section before changing any of them.

@description('Azure region for all staging resources. Must be a region where Azure Container Apps Consumption is available in the target subscription (West Europe and North Europe were confirmed available for this subscription in a prior read-only check).')
param location string = 'westeurope'

@description('Name of the DEDICATED RepoPulse staging resource group. Must never be set to an existing, unrelated resource group (e.g. this subscription\'s pre-existing "TranslatorAppGrubu").')
param resourceGroupName string = 'rg-repopulse-staging'

@description('Azure AD tenant ID that owns the target subscription. Supply this at deploy time (e.g. from `az account show --query tenantId -o tsv`) — never hardcode a real tenant ID in any committed file.')
param tenantId string

@description('Globally-unique Key Vault name (3-24 chars, alphanumeric/hyphen).')
@minLength(3)
@maxLength(24)
param keyVaultName string

@description('SHA-256 digest of the RepoPulse.AuthApi image already built and pushed to GHCR, in the exact format "sha256:<64 lowercase hex characters>". This is a digest, not a tag — "latest" and mutable branch/commit tags cannot be expressed by this parameter at all. The GHCR repository itself is fixed in modules/containerApp.bicep, not parameterized here.')
@minLength(71)
@maxLength(71)
param containerImageDigest string

@description('Public GitHub OAuth App Client ID — not a secret.')
param gitHubOAuthClientId string = 'Ov23likVt8K7YO1aqnfo'

@description('GitHub OAuth redirect URI — not a secret.')
param gitHubOAuthRedirectUri string = 'repopulse://oauth/callback'

@description('GitHub OAuth token endpoint — not a secret.')
param gitHubOAuthTokenEndpoint string = 'https://github.com/login/oauth/access_token'

resource stagingResourceGroup 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: resourceGroupName
  location: location
}

module containerAppsEnvironment 'modules/containerAppsEnvironment.bicep' = {
  scope: stagingResourceGroup
  name: 'containerAppsEnvironment'
  params: {
    location: location
    environmentName: 'cae-repopulse-staging'
  }
}

module keyVault 'modules/keyVault.bicep' = {
  scope: stagingResourceGroup
  name: 'keyVault'
  params: {
    location: location
    keyVaultName: keyVaultName
    tenantId: tenantId
  }
}

module containerApp 'modules/containerApp.bicep' = {
  scope: stagingResourceGroup
  name: 'containerApp'
  params: {
    location: location
    containerAppName: 'ca-repopulse-authapi-staging'
    containerAppsEnvironmentId: containerAppsEnvironment.outputs.id
    containerImageDigest: containerImageDigest
    gitHubOAuthClientId: gitHubOAuthClientId
    gitHubOAuthRedirectUri: gitHubOAuthRedirectUri
    gitHubOAuthTokenEndpoint: gitHubOAuthTokenEndpoint
  }
}

// Grants the Container App's system-assigned identity read-only access to
// secret VALUES in the Key Vault — nothing more. Safe to include in this
// bootstrap deployment even though no secret exists yet: an RBAC grant
// does not require the target secret to already exist.
module keyVaultAccess 'modules/keyVaultAccess.bicep' = {
  scope: stagingResourceGroup
  name: 'keyVaultAccess'
  params: {
    keyVaultName: keyVault.outputs.name
    principalId: containerApp.outputs.principalId
  }
}

// Outputs are deliberately limited to non-sensitive identifiers/names —
// no secret, connection string, or credential is ever output here.
output resourceGroupName string = stagingResourceGroup.name
output containerAppsEnvironmentName string = containerAppsEnvironment.outputs.name
output keyVaultName string = keyVault.outputs.name
output containerAppName string = containerApp.outputs.name
output containerAppFqdn string = containerApp.outputs.fqdn
