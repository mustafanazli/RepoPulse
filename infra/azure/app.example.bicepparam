using 'app.bicep'

// EXAMPLE ONLY — this is PHASE B (the Container App itself). Copy this file
// to app.bicepparam (already .gitignore'd — see .gitignore) and fill in
// real values there. Only run this AFTER infra/azure/main.bicep (Phase A)
// has succeeded AND a human has already added the real GitHubOAuth
// client_secret value to the Key Vault below — see
// docs/deployment/azure-staging-runbook.md, Faz B.
//
// NEVER put a real container image digest directly into this tracked
// example file.

param location = 'westeurope'

// Must match the resourceGroupName/environment name actually produced by
// your Phase A deployment (main.bicep's default is 'cae-repopulse-staging').
param containerAppsEnvironmentName = 'cae-repopulse-staging'

// Must match the Key Vault name you chose in Phase A's main.bicepparam.
param keyVaultName = '<UNIQUE_KEY_VAULT_NAME_FROM_PHASE_A>'

// Must match the identityName you used (or the default) in Phase A.
param identityName = 'id-repopulse-authapi-staging'

// Must be the real SHA-256 digest GHCR reports after the RepoPulse.AuthApi
// image has actually been built and pushed there (see
// .github/workflows/authapi-publish-ghcr.yml). Format:
// "sha256:<64 lowercase hex characters>" — this is a DIGEST, not a tag,
// and "latest" cannot be expressed here at all. The GHCR repository itself
// (ghcr.io/mustafanazli/repopulse-authapi) is fixed inside
// modules/containerApp.bicep and is not set via a parameter.
param containerImageDigest = '<GHCR_IMAGE_DIGEST_SHA256_HEX>'
