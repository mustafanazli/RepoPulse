@description('Azure region for the Key Vault.')
param location string

@description('Globally-unique Key Vault name (3-24 chars, alphanumeric/hyphen). Key Vault names are a global Azure namespace, not just subscription- or resource-group-scoped.')
@minLength(3)
@maxLength(24)
param keyVaultName string

@description('Azure AD tenant ID that owns the target subscription. Supplied by the caller at deploy time — never hardcoded here, so no real tenant ID is ever committed to source control.')
param tenantId string

// Standard SKU (not Premium — a single OAuth client secret needs no
// HSM-backed keys) with RBAC authorization (not legacy vault access
// policies), per docs/adr/004-production-hosting.md.
//
// This module creates an EMPTY Key Vault. No secret is created, set, or
// referenced anywhere in this file, this module, or anywhere else in
// infra/azure/ — adding the real GitHubOAuth ClientSecret value is a
// separate, manual, human-only step documented in
// docs/deployment/azure-staging-runbook.md, run only after this
// bootstrap deployment succeeds.
resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: keyVaultName
  location: location
  properties: {
    sku: {
      family: 'A'
      name: 'standard'
    }
    tenantId: tenantId
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 7
  }
}

output id string = keyVault.id
output name string = keyVault.name
