@description('Name of the Log Analytics workspace')
param name string

@description('Location for the resource')
param location string = resourceGroup().location

@description('Retention period in days')
@minValue(30)
@maxValue(730)
param retentionInDays int = 30

@description('Daily ingestion cap in GB (0 for unlimited)')
@minValue(0)
param dailyQuotaGb int = 1

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: name
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: retentionInDays
    features: {
      enableLogAccessUsingOnlyResourcePermissions: true
    }
    workspaceCapping: {
      dailyQuotaGb: dailyQuotaGb > 0 ? dailyQuotaGb : -1
    }
    publicNetworkAccessForIngestion: 'Enabled'
    publicNetworkAccessForQuery: 'Enabled'
  }
}

@description('Resource ID of the workspace')
output id string = logAnalytics.id

@description('Name of the workspace')
output name string = logAnalytics.name

@description('Customer ID for the workspace')
output customerId string = logAnalytics.properties.customerId

@description('Primary shared key for the workspace')
#disable-next-line outputs-should-not-contain-secrets
output primarySharedKey string = logAnalytics.listKeys().primarySharedKey
