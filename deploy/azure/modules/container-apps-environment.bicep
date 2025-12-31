@description('Name of the Container Apps environment')
param name string

@description('Location for the resource')
param location string = resourceGroup().location

@description('Log Analytics workspace customer ID')
param logAnalyticsCustomerId string

@description('Log Analytics workspace shared key')
@secure()
param logAnalyticsSharedKey string

resource containerAppsEnv 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: name
  location: location
  properties: {
    zoneRedundant: false
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalyticsCustomerId
        sharedKey: logAnalyticsSharedKey
      }
    }
    workloadProfiles: [
      {
        name: 'Consumption'
        workloadProfileType: 'Consumption'
      }
    ]
  }
}

@description('Resource ID of the environment')
output id string = containerAppsEnv.id

@description('Name of the environment')
output name string = containerAppsEnv.name

@description('Default domain for the environment')
output defaultDomain string = containerAppsEnv.properties.defaultDomain
