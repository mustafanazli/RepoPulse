using 'main.bicep'

// EXAMPLE ONLY — this is PHASE A (bootstrap: resource group, Container Apps
// environment, Key Vault, user-assigned identity + its Key Vault role — NO
// Container App). Copy this file to main.bicepparam (already .gitignore'd —
// see .gitignore) and fill in real, subscription-specific values there.
// NEVER put a real tenant ID or a real unique Key Vault name choice
// directly into this tracked example file.
//
// Before ever running a real deployment from main.bicepparam, read
// docs/deployment/azure-staging-runbook.md in full — it explains the
// required manual prerequisites (provider registration approval, budget
// alert, portal credit check, and this subscription's own "allowed
// deployment regions" policy) that this file alone does not cover.

param location = 'westeurope'
param resourceGroupName = 'rg-repopulse-staging'

// Replace with the real tenant ID for the target subscription, obtained
// at deploy time (e.g. `az account show --query tenantId -o tsv`).
// Never commit the real value.
param tenantId = '<AZURE_TENANT_ID>'

// Key Vault names are a GLOBAL Azure namespace (not just per-subscription
// or per-resource-group) — pick something unique to you, 3-24 characters.
param keyVaultName = '<UNIQUE_KEY_VAULT_NAME>'

// Has a sensible default ('id-repopulse-authapi-staging') — only override
// if you need a different name. This identity is created here in Phase A
// and reused, by name, in Phase B (see app.example.bicepparam).
// param identityName = 'id-repopulse-authapi-staging'
