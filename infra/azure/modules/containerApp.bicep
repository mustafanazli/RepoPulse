@description('Azure region for the Container App.')
param location string

@description('Name of the Container App.')
param containerAppName string

@description('Resource ID of the Container Apps managed environment this app runs in.')
param containerAppsEnvironmentId string

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

resource containerApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: containerAppName
  location: location
  identity: {
    // System-assigned only (no user-assigned identity) — its principalId
    // becomes available only once this resource exists, which is why the
    // Key Vault role assignment (modules/keyVaultAccess.bicep) is a
    // separate module that depends on this one's output.
    type: 'SystemAssigned'
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
      // Deliberately no `secrets` array here. The real GitHubOAuth
      // ClientSecret is wired in a separate, later, manual step (see
      // docs/deployment/azure-staging-runbook.md) once a human has placed
      // it in Key Vault — this bootstrap template never embeds a
      // placeholder or fake value for it.
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
            // GitHubOAuth__ClientSecret is intentionally absent. Until the
            // separate secret-wiring step in the runbook is completed,
            // GitHubOAuthOptionsValidator.ValidateOnStart() will correctly
            // make the container fail to start — that fail-fast behavior
            // is by design (see src/RepoPulse.AuthApi/Configuration), not
            // a defect in this template.
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

output principalId string = containerApp.identity.principalId
output name string = containerApp.name
output fqdn string = containerApp.properties.configuration.ingress.fqdn
