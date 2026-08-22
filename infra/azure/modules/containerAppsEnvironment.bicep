@description('Azure region for the Container Apps managed environment.')
param location string

@description('Name of the Container Apps managed environment.')
param environmentName string

// Deliberately workspace-less: no Log Analytics workspace is created, and
// logging is explicitly turned OFF via appLogsConfiguration.destination:
// 'none' — the same value the `az containerapp env create
// --logs-destination none` CLI flag sends to ARM. This is an explicit,
// supported value of this property in the 2024-03-01 API version, not an
// assumption about what omitting the property would default to. This is a
// conscious cost decision for a time-limited Azure for Students credit, not
// an oversight — Log Analytics ingestion/retention has an ongoing cost this
// staging environment does not need to take on, and 'none' is deliberately
// NOT 'azure-monitor' either (no Azure Monitor destination is wired up).
// See docs/adr/004-production-hosting.md and
// docs/deployment/azure-staging-runbook.md.
resource containerAppsEnvironment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: environmentName
  location: location
  properties: {
    zoneRedundant: false
    appLogsConfiguration: {
      destination: 'none'
    }
  }
}

output id string = containerAppsEnvironment.id
output name string = containerAppsEnvironment.name
