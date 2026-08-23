targetScope = 'resourceGroup'

// RepoPulse Azure staging — PHASE B (application deployment) ONLY.
//
// Run this ONLY after infra/azure/main.bicep (Phase A) has already
// succeeded AND a human has already added the real GitHubOAuth
// client_secret VALUE to the Key Vault referenced below (see
// docs/deployment/azure-staging-runbook.md, Faz B, steps 5-6) — this
// template never creates, reads, or sets that secret value itself, only a
// reference to it (a Key Vault URL + the identity used to resolve it).
//
// Deploy at RESOURCE-GROUP scope (unlike main.bicep, which is
// subscription-scoped), e.g.:
//   az deployment group create --resource-group rg-repopulse-staging ...
// into the SAME resource group Phase A created.
//
// This template intentionally does NOT create a Container Registry, a Log
// Analytics workspace/Application Insights/diagnosticSettings resource, or
// any Key Vault secret — see infra/azure/main.bicep and
// docs/adr/004-production-hosting.md.

@description('Azure region for the Container App. Must match the region used for the Phase A Container Apps environment.')
param location string = 'westeurope'

@description('Name of the existing Container Apps environment created in Phase A (infra/azure/main.bicep).')
param containerAppsEnvironmentName string

@description('Name of the existing Key Vault created in Phase A, which must already contain the real GitHubOAuth client secret (added manually, out of band, by a human — never by this template or any agent).')
param keyVaultName string

@description('Name of the existing user-assigned managed identity created in Phase A, already granted Key Vault Secrets User on the Key Vault above.')
param identityName string

@description('Name of the Container App to create.')
param containerAppName string = 'ca-repopulse-authapi-staging'

@description('Name of the secret that must already exist in the Key Vault above. This deployment does not create, read, or set its value — only references it by name/URL.')
param keyVaultSecretName string = 'github-oauth-client-secret'

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

resource containerAppsEnvironment 'Microsoft.App/managedEnvironments@2024-03-01' existing = {
  name: containerAppsEnvironmentName
}

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}

resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' existing = {
  name: identityName
}

module containerApp 'modules/containerApp.bicep' = {
  name: 'containerApp'
  params: {
    location: location
    containerAppName: containerAppName
    containerAppsEnvironmentId: containerAppsEnvironment.id
    userAssignedIdentityId: identity.id
    // Versionless Key Vault secret URI — Container Apps resolves the
    // current version at runtime. Never a secret VALUE, only a reference.
    clientSecretKeyVaultUrl: '${keyVault.properties.vaultUri}secrets/${keyVaultSecretName}'
    clientSecretName: keyVaultSecretName
    containerImageDigest: containerImageDigest
    gitHubOAuthClientId: gitHubOAuthClientId
    gitHubOAuthRedirectUri: gitHubOAuthRedirectUri
    gitHubOAuthTokenEndpoint: gitHubOAuthTokenEndpoint
  }
}

// Outputs limited to non-sensitive identifiers — no digest, no secret URI,
// no credential is ever output here.
output containerAppName string = containerApp.outputs.name
output containerAppFqdn string = containerApp.outputs.fqdn
