@description('Azure region for the Container App. Must match the region used for the existing Container Apps environment (created in Phase A).')
param location string

@description('Name of the Container App.')
param containerAppName string

@description('Resource ID of the existing Container Apps managed environment (created in Phase A, infra/azure/main.bicep) this app runs in.')
param containerAppsEnvironmentId string

@description('Resource ID of the existing user-assigned managed identity (created in Phase A, already granted Key Vault Secrets User on the Key Vault) to assign to this Container App. NOT a system-assigned identity — see docs/adr/004-production-hosting.md for why this template no longer uses SystemAssigned.')
param userAssignedIdentityId string

@description('Name of the existing Key Vault (created in Phase A) that already contains the real GitHubOAuth client secret. This is an IDENTITY only — a plain Key Vault name, never a URL or a secret path. The caller cannot supply a different host or path: this module builds the exact secret URL itself, below, from this name and a fixed secret name.')
param keyVaultName string

@description('SHA-256 digest of the RepoPulse.AuthApi image already built and pushed to GHCR, in the exact format "sha256:<64 lowercase hex characters>". Obtain this from the real GHCR push output (the digest GHCR/`docker push` reports, or a registry API digest lookup) after the image has actually been pushed — never fabricate or guess this value. This parameter accepts ONLY a digest, never a tag: neither "latest" nor any mutable branch/commit tag can be expressed here at all, because the repository is fixed below and addressed exclusively by @<digest>.')
@minLength(71)
@maxLength(71)
param containerImageDigest string

@description('Public GitHub OAuth App Client ID — not a secret, already committed in src/RepoPulse.AuthApi/appsettings.json.')
param gitHubOAuthClientId string = 'Ov23likVt8K7YO1aqnfo'

@description('GitHub OAuth redirect URI configured on the OAuth App — not a secret.')
param gitHubOAuthRedirectUri string = 'repopulse://oauth/callback'

@description('GitHub OAuth token endpoint — not a secret, it is GitHub\'s own public endpoint.')
param gitHubOAuthTokenEndpoint string = 'https://github.com/login/oauth/access_token'

// Consumption-plan sizing per docs/adr/004-production-hosting.md: scales to
// zero when idle (minReplicas: 0) and never runs more than one replica
// (maxReplicas: 1) — both limits exist specifically to bound cost against a
// time-limited Azure for Students credit and must not be raised without
// re-reading that ADR and the staging runbook's cost section.
var minReplicas = 0
var maxReplicas = 1
var cpuCores = json('0.25')
var memorySize = '0.5Gi'
var containerTargetPort = 8080

// Repository is fixed and NOT parameterized — this template only ever
// pulls RepoPulse.AuthApi from this single GHCR repository, addressed
// exclusively by immutable digest (never a mutable tag such as "latest" or
// a branch/commit tag). See docs/deployment/azure-staging-runbook.md §4.
var containerImageRepository = 'ghcr.io/mustafanazli/repopulse-authapi'
var containerImage = '${containerImageRepository}@${containerImageDigest}'

// Secret NAME is fixed in Bicep, not a parameter — no caller (bicepparam or
// otherwise) can point this at a different secret name.
var clientSecretName = 'github-oauth-client-secret'

// The Key Vault secret URL is built entirely inside this module, from the
// Key Vault's own canonical `vaultUri` property (via an `existing` lookup)
// plus the fixed secret name above — never accepted as a free-form
// parameter. `vaultUri` is used rather than manually assembling
// `https://${keyVaultName}.vault.azure.net/` or
// `environment().suffixes.keyvaultDns`: it is the vault resource's own,
// always-correct URI, with no assumption about which Azure cloud
// (Public/Government/China) this deploys into baked in here.
resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}

var clientSecretKeyVaultUrl = '${keyVault.properties.vaultUri}secrets/${clientSecretName}'

resource containerApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: containerAppName
  location: location
  identity: {
    // User-assigned only — NOT SystemAssigned. The identity already exists
    // and already holds the Key Vault Secrets User role (both from Phase
    // A) before this Container App is ever created, so there is no window
    // where the app starts without an identity that can already resolve
    // its Key Vault secret reference.
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${userAssignedIdentityId}': {}
    }
  }
  properties: {
    managedEnvironmentId: containerAppsEnvironmentId
    configuration: {
      ingress: {
        external: true
        targetPort: containerTargetPort
        transport: 'auto'
        // No plain-HTTP traffic is ever accepted at the ingress — external
        // HTTPS is enforced by Azure Container Apps itself, independent of
        // Hosting:BehindTlsTerminatingProxy inside the app (see
        // src/RepoPulse.AuthApi/Program.cs and ADR-004).
        allowInsecure: false
      }
      // The only "secret" here is a Container Apps secret NAME plus a
      // reference (Key Vault URL + identity to use to resolve it) — the
      // actual secret VALUE is never present in this template, this
      // deployment, or any Bicep output. The runbook requires the real
      // value to already exist in Key Vault (added manually, by a human,
      // out of band) before this Phase B deployment is ever run.
      secrets: [
        {
          name: clientSecretName
          keyVaultUrl: clientSecretKeyVaultUrl
          identity: userAssignedIdentityId
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'authapi'
          image: containerImage
          resources: {
            cpu: cpuCores
            memory: memorySize
          }
          env: [
            {
              name: 'ASPNETCORE_URLS'
              value: 'http://+:${containerTargetPort}'
            }
            {
              // Azure Container Apps ingress terminates TLS; the app must
              // not also try to redirect HTTP->HTTPS itself, or every
              // request would loop (see Program.cs and ADR-004).
              name: 'Hosting__BehindTlsTerminatingProxy'
              value: 'true'
            }
            {
              name: 'GitHubOAuth__ClientId'
              value: gitHubOAuthClientId
            }
            {
              name: 'GitHubOAuth__RedirectUri'
              value: gitHubOAuthRedirectUri
            }
            {
              name: 'GitHubOAuth__TokenEndpoint'
              value: gitHubOAuthTokenEndpoint
            }
            {
              // Bound via Container Apps secretRef, never a plain `value`
              // — the actual secret text never appears in this template,
              // this deployment's parameters, or any ARM/Bicep output.
              name: 'GitHubOAuth__ClientSecret'
              secretRef: clientSecretName
            }
          ]
        }
      ]
      scale: {
        minReplicas: minReplicas
        maxReplicas: maxReplicas
      }
    }
  }
}

output name string = containerApp.name
output fqdn string = containerApp.properties.configuration.ingress.fqdn
