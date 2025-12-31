targetScope = 'resourceGroup'

@description('Location for all resources')
param location string = 'eastus'

@description('Name prefix for resources')
param namePrefix string = 'alpacamock'

@description('Environment (dev/prod)')
@allowed(['dev', 'prod'])
param environment string = 'prod'

@description('Polygon API key for market data')
@secure()
param polygonApiKey string

@description('API key for authentication')
@secure()
param apiKey string

@description('API secret for authentication')
@secure()
param apiSecret string

@description('PostgreSQL administrator password')
@secure()
param postgresPassword string

@description('Container image tag')
param imageTag string = 'latest'

@description('Data ingestion cron schedule (UTC)')
param ingestionCronSchedule string = '0 6 * * *'

// Variables
var envSuffix = environment == 'dev' ? 'dev' : ''
var resourcePrefix = empty(envSuffix) ? namePrefix : '${namePrefix}${envSuffix}'

// Container Registry
module acr 'modules/container-registry.bicep' = {
  name: 'acr-deployment'
  params: {
    namePrefix: replace(resourcePrefix, '-', '')
    location: location
  }
}

// Log Analytics
module logAnalytics 'modules/log-analytics.bicep' = {
  name: 'log-analytics-deployment'
  params: {
    name: '${resourcePrefix}-logs'
    location: location
    retentionInDays: 30
    dailyQuotaGb: 1
  }
}

// Container Apps Environment
module containerAppsEnv 'modules/container-apps-environment.bicep' = {
  name: 'container-apps-env-deployment'
  params: {
    name: '${resourcePrefix}-env'
    location: location
    logAnalyticsCustomerId: logAnalytics.outputs.customerId
    logAnalyticsSharedKey: logAnalytics.outputs.primarySharedKey
  }
}

// Cosmos DB
module cosmos 'modules/cosmos-db.bicep' = {
  name: 'cosmos-deployment'
  params: {
    accountName: '${resourcePrefix}-cosmos'
    location: location
    databaseName: 'alpacamock'
    sessionTtlSeconds: 86400
  }
}

// PostgreSQL
module postgres 'modules/postgresql.bicep' = {
  name: 'postgres-deployment'
  params: {
    serverName: '${resourcePrefix}-postgres'
    location: location
    databaseName: 'alpacamock'
    administratorPassword: postgresPassword
    version: '16'
    storageSizeGB: 32
  }
}

// API Container App
module api 'modules/container-app.bicep' = {
  name: 'api-deployment'
  params: {
    name: '${resourcePrefix}-api'
    location: location
    environmentId: containerAppsEnv.outputs.id
    acrLoginServer: acr.outputs.loginServer
    acrUsername: acr.outputs.username
    acrPassword: acr.outputs.password
    imageTag: imageTag
    cosmosConnectionString: cosmos.outputs.connectionString
    postgresConnectionString: postgres.outputs.connectionString
    polygonApiKey: polygonApiKey
    apiKey: apiKey
    apiSecret: apiSecret
    minReplicas: 1
    maxReplicas: 3
  }
}

// Data Ingestion Job (Daily bars)
module ingestionJob 'modules/container-app-job.bicep' = {
  name: 'ingestion-job-deployment'
  params: {
    name: '${resourcePrefix}-ingestion'
    location: location
    environmentId: containerAppsEnv.outputs.id
    acrLoginServer: acr.outputs.loginServer
    acrUsername: acr.outputs.username
    acrPassword: acr.outputs.password
    imageTag: imageTag
    postgresConnectionString: postgres.outputs.connectionString
    polygonApiKey: polygonApiKey
    cronExpression: ingestionCronSchedule
    command: 'load-bars'
    resolution: 'daily'
  }
}

// Outputs
@description('API URL')
output apiUrl string = api.outputs.url

@description('ACR login server')
output acrLoginServer string = acr.outputs.loginServer

@description('ACR name')
output acrName string = acr.outputs.name

@description('Cosmos DB account name')
output cosmosAccountName string = cosmos.outputs.name

@description('PostgreSQL server name')
output postgresServerName string = postgres.outputs.name

@description('PostgreSQL server FQDN')
output postgresServerFqdn string = postgres.outputs.fqdn

@description('Ingestion job name')
output ingestionJobName string = ingestionJob.outputs.name
