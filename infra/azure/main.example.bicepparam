using 'main.bicep'

// EXAMPLE ONLY. Copy this file to main.bicepparam (already .gitignore'd —
// see .gitignore) and fill in real, subscription-specific values there.
// NEVER put a real tenant ID, a real unique Key Vault name choice, or a
// real container image tag directly into this tracked example file.
//
// Before ever running a real deployment from main.bicepparam, read
// docs/deployment/azure-staging-runbook.md in full — it explains the
// required manual prerequisites (provider registration approval, budget
// alert, portal credit check) that this file alone does not cover.

param location = 'westeurope'
param resourceGroupName = 'rg-repopulse-staging'

// Replace with the real tenant ID for the target subscription, obtained
// at deploy time (e.g. `az account show --query tenantId -o tsv`).
// Never commit the real value.
param tenantId = '<AZURE_TENANT_ID>'

// Key Vault names are a GLOBAL Azure namespace (not just per-subscription
// or per-resource-group) — pick something unique to you, 3-24 characters.
param keyVaultName = '<UNIQUE_KEY_VAULT_NAME>'

// Must be the real SHA-256 digest GHCR reports after the RepoPulse.AuthApi
// image has actually been built and pushed there (see
// .github/workflows/authapi-container-build.yml for the build-only CI
// check; pushing to GHCR itself is a separate, not-yet-implemented step).
// Format: "sha256:<64 lowercase hex characters>" — this is a DIGEST, not a
// tag, and "latest" cannot be expressed here at all. The GHCR repository
// itself (ghcr.io/mustafanazli/repopulse-authapi) is fixed inside
// modules/containerApp.bicep and is not set via a parameter.
param containerImageDigest = '<GHCR_IMAGE_DIGEST_SHA256_HEX>'
