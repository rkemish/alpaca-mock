@description('Name prefix for resources')
param namePrefix string

@description('Location for the resource')
param location string = resourceGroup().location

resource containerRegistry 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: '${namePrefix}acr'
  location: location
  sku: {
    name: 'Basic'
  }
  properties: {
    adminUserEnabled: true
    publicNetworkAccess: 'Enabled'
  }
}

@description('Resource ID of the container registry')
output id string = containerRegistry.id

@description('Name of the container registry')
output name string = containerRegistry.name

@description('Login server URL')
output loginServer string = containerRegistry.properties.loginServer

@description('Admin username')
output username string = containerRegistry.listCredentials().username

@description('Admin password')
#disable-next-line outputs-should-not-contain-secrets
output password string = containerRegistry.listCredentials().passwords[0].value
