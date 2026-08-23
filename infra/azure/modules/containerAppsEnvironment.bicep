@description('Azure region for the Container Apps managed environment.')
param location string

@description('Name of the Container Apps managed environment.')
param environmentName string

// Log routing: appLogsConfiguration.destination is set to 'azure-monitor'.
//
// This was NOT the first choice — 'none' was tried first and was REJECTED
// by the live Microsoft.App/managedEnvironments (2024-03-01) resource
// provider during a real `--validation-level Provider` what-if preflight
// check in Poland Central, with the error "App Logs destination 'none' not
// supported. Supported values: 'log-analytics', 'azure-monitor'". So unlike
// the earlier assumption, 'none' passes Bicep's static type-check but is
// NOT accepted by the actual resource provider at deploy time — this file
// now uses the value that the live provider actually accepts.
//
// 'azure-monitor' is a ROUTING MODE, not a storage destination: unlike
// 'log-analytics' (which requires a Log Analytics workspace with a
// customerId/sharedKey), 'azure-monitor' needs no additional workspace,
// diagnostic settings, or Application Insights resource — none of those
// are created anywhere in this template, and no Microsoft.Insights or
// Microsoft.OperationalInsights provider registration is performed by or
// for this template. This is NOT a "zero-cost" guarantee — it only means
// no additional, ongoing-cost log STORAGE resource (workspace) is
// provisioned here. During staging, real-time log streaming
// (`az containerapp logs show ... --follow`) remains available without
// this destination. Adding a persistent log destination later (Log
// Analytics workspace + diagnostic settings) is a deliberate, separate
// cost/security decision for a future task, not an oversight here.
// See docs/adr/004-production-hosting.md and
// docs/deployment/azure-staging-runbook.md.
resource containerAppsEnvironment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: environmentName
  location: location
  properties: {
    zoneRedundant: false
    appLogsConfiguration: {
      destination: 'azure-monitor'
    }
  }
}

output id string = containerAppsEnvironment.id
output name string = containerAppsEnvironment.name
