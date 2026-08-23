targetScope = 'subscription'

// RepoPulse Azure staging — PHASE A (bootstrap infrastructure) ONLY.
//
// Creates a DEDICATED resource group, a Container Apps managed environment,
// an empty Key Vault, and a user-assigned managed identity already granted
// the Key Vault Secrets User role on that Key Vault. Deliberately does NOT
// create the Container App itself — see infra/azure/app.bicep (Phase B) for
// that, and docs/adr/004-production-hosting.md /
// docs/deployment/azure-staging-runbook.md for why the two phases are
// separate and the exact order to run them in.
//
// Why this template does not create the Container App: doing so here would
// require either (a) creating it with the real GHCR image and no client
// secret, which crash-loops from the very first deployment because
// GitHubOAuthOptionsValidator.ValidateOnStart() fails fast, and forces the
// Key Vault role assignment to depend on a system-assigned identity that
// does not exist until that same doomed deployment finishes; or (b)
// embedding a placeholder secret value, which this project's rules forbid
// outright. Splitting into Phase A (this file: environment + Key Vault +
// identity + role, no app) and Phase B (app.bicep: the Container App,
// created only once the identity and role from Phase A already exist)
// avoids both problems entirely.
//
// This template intentionally does NOT create:
//   - An Azure Container Registry (a public GHCR image is used instead, in
//     Phase B).
//   - A Log Analytics workspace, Application Insights, or any
//     Microsoft.Insights diagnosticSettings resource — the Container Apps
//     environment's appLogsConfiguration.destination is set to
//     'azure-monitor', a routing mode that requires none of those; see
//     modules/containerAppsEnvironment.bicep.
//   - Any Key Vault secret (the real GitHubOAuth ClientSecret is added by a
//     human, manually, in a separate step — see the runbook — after this
//     Phase A deployment succeeds and before Phase B is deployed).
//   - The Container App itself (see infra/azure/app.bicep).
// All are cost/scope/sequencing decisions, not omissions — see the
// runbook's "Maliyet korumaları" section and ADR-004 before changing any of
// them.

@description('Azure region for all staging resources. Must be a region this subscription\'s "Allowed resource deployment regions" policy (if any) permits — check with `az policy assignment list` / `az policy assignment show` before choosing a value; West Europe and North Europe were found to be blocked by such a policy on the subscription this was developed against.')
param location string = 'westeurope'

@description('Name of the DEDICATED RepoPulse staging resource group. Must never be set to an existing, unrelated resource group (e.g. this subscription\'s pre-existing "TranslatorAppGrubu").')
param resourceGroupName string = 'rg-repopulse-staging'

@description('Azure AD tenant ID that owns the target subscription. Supply this at deploy time (e.g. from `az account show --query tenantId -o tsv`) — never hardcode a real tenant ID in any committed file.')
param tenantId string

@description('Globally-unique Key Vault name (3-24 chars, alphanumeric/hyphen).')
@minLength(3)
@maxLength(24)
param keyVaultName string

@description('Name of the user-assigned managed identity that RepoPulse.AuthApi will use (once deployed in Phase B, infra/azure/app.bicep) to read its GitHub OAuth client secret from the Key Vault created here.')
param identityName string = 'id-repopulse-authapi-staging'

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

module userAssignedIdentity 'modules/userAssignedIdentity.bicep' = {
  scope: stagingResourceGroup
  name: 'userAssignedIdentity'
  params: {
    location: location
    identityName: identityName
  }
}

// Grants the user-assigned identity's principal read-only access to secret
// VALUES in the Key Vault — nothing more, and scoped to this Key Vault
// only (never subscription or resource-group scope). Safe to include in
// this bootstrap deployment even though no secret exists yet: an RBAC
// grant does not require the target secret to already exist. Because this
// runs in Phase A, the role exists well before Phase B ever creates the
// Container App that will use this identity.
module keyVaultAccess 'modules/keyVaultAccess.bicep' = {
  scope: stagingResourceGroup
  name: 'keyVaultAccess'
  params: {
    keyVaultName: keyVault.outputs.name
    principalId: userAssignedIdentity.outputs.principalId
  }
}

// Outputs are deliberately limited to non-sensitive identifiers/names —
// no secret, connection string, or credential is ever output here. These
// are exactly the values infra/azure/app.example.bicepparam (Phase B)
// needs to reference this Phase A deployment's resources by name.
output resourceGroupName string = stagingResourceGroup.name
output containerAppsEnvironmentName string = containerAppsEnvironment.outputs.name
output keyVaultName string = keyVault.outputs.name
output identityName string = userAssignedIdentity.outputs.name
