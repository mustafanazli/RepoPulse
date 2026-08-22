@description('Azure region for the Container Apps managed environment.')
param location string

@description('Name of the Container Apps managed environment.')
param environmentName string

// Deliberately workspace-less: no Log Analytics workspace is created, and
// appLogsConfiguration is omitted entirely, so no logging destination is
// wired up. This is a conscious cost decision for a time-limited Azure for
// Students credit, not an oversight — Log Analytics ingestion/retention
// has an ongoing cost this staging environment does not need to take on.
// See docs/adr/004-production-hosting.md and
// docs/deployment/azure-staging-runbook.md.
resource containerAppsEnvironment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: environmentName
  location: location
  properties: {
    zoneRedundant: false
  }
}

output id string = containerAppsEnvironment.id
output name string = containerAppsEnvironment.name
