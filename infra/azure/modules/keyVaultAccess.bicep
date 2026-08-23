@description('Name of an existing, already-deployed Key Vault to grant access on.')
param keyVaultName string

@description('Principal ID of the identity to grant access to — the user-assigned managed identity created in this same Phase A deployment (see modules/userAssignedIdentity.bicep), which the Container App created later in Phase B (infra/azure/app.bicep) will be assigned.')
param principalId string

// Built-in "Key Vault Secrets User" role definition ID. Read-only access
// to secret VALUES only (no list/manage/delete of the vault itself, no
// key/certificate access) — deliberately not Contributor, not Key Vault
// Administrator. See docs/adr/004-production-hosting.md.
var keyVaultSecretsUserRoleId = '4633458b-17de-408a-b874-0445c86b69e6'

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}

resource keyVaultSecretsUserAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, principalId, keyVaultSecretsUserRoleId)
  scope: keyVault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultSecretsUserRoleId)
    principalId: principalId
    principalType: 'ServicePrincipal'
  }
}
