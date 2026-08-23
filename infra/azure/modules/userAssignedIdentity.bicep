@description('Azure region for the user-assigned managed identity.')
param location string

@description('Name of the user-assigned managed identity. This identity is created here, in Phase A (infra/azure/main.bicep), and is granted the Key Vault Secrets User role in this same Phase A deployment — before the Container App that will actually use it exists. The Container App itself is created later, in Phase B (infra/azure/app.bicep), and is simply assigned this already-existing, already-permissioned identity. This ordering is deliberate: it removes the previous circular risk where a Container App would be created with a real image and no secret, and its (then system-assigned) identity would not exist to receive a Key Vault role until after that first, doomed-to-crash-loop deployment.')
param identityName string

resource userAssignedIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: identityName
  location: location
}

output id string = userAssignedIdentity.id
output name string = userAssignedIdentity.name
output principalId string = userAssignedIdentity.properties.principalId
