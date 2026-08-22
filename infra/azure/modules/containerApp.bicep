@description('Azure region for the Container App.')
param location string

@description('Name of the Container App.')
param containerAppName string

@description('Resource ID of the Container Apps managed environment this app runs in.')
param containerAppsEnvironmentId string

@description('Full container image reference for RepoPulse.AuthApi, pinned to an immutable commit SHA tag (e.g. ghcr.io/mustafanazli/repopulse-authapi:<40-char-sha>). Must NEVER be "latest" — a mutable tag would let a future, unreviewed image silently replace what staging is running.')
param containerImage string

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
