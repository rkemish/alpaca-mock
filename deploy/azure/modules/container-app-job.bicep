@description('Name of the job')
param name string

@description('Location for the resource')
param location string = resourceGroup().location

@description('Container Apps Environment ID')
param environmentId string

@description('ACR login server')
param acrLoginServer string

@description('ACR username')
param acrUsername string

@description('ACR password')
@secure()
param acrPassword string

@description('Container image name')
param imageName string = 'alpacamock-ingestion'

@description('Container image tag')
param imageTag string = 'latest'

@description('PostgreSQL connection string')
@secure()
param postgresConnectionString string

@description('Polygon API key')
@secure()
param polygonApiKey string

@description('Cron schedule expression (UTC)')
param cronExpression string = '0 6 * * *'

@description('Command to run')
@allowed(['init-db', 'load-symbols', 'load-bars'])
param command string = 'load-bars'

@description('Bar resolution for load-bars command')
@allowed(['minute', 'daily'])
param resolution string = 'daily'

@description('Job timeout in seconds')
param replicaTimeout int = 3600

var commandArgs = command == 'init-db' ? [
  command
] : command == 'load-symbols' ? [
  command
] : [
  command
  '-r'
  resolution
]

resource dataIngestionJob 'Microsoft.App/jobs@2024-03-01' = {
  name: name
  location: location
  properties: {
    environmentId: environmentId
    configuration: {
      triggerType: 'Schedule'
      scheduleTriggerConfig: {
        cronExpression: cronExpression
        parallelism: 1
        replicaCompletionCount: 1
      }
      replicaTimeout: replicaTimeout
      replicaRetryLimit: 1
      registries: [
        {
          server: acrLoginServer
          username: acrUsername
          passwordSecretRef: 'acr-password'
        }
      ]
      secrets: [
        { name: 'acr-password', value: acrPassword }
        { name: 'postgres-connection', value: postgresConnectionString }
        { name: 'polygon-api-key', value: polygonApiKey }
      ]
    }
    template: {
      containers: [
        {
          name: 'data-ingestion'
          image: '${acrLoginServer}/${imageName}:${imageTag}'
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          args: commandArgs
          env: [
            { name: 'POSTGRES_CONNECTION_STRING', secretRef: 'postgres-connection' }
            { name: 'POLYGON_API_KEY', secretRef: 'polygon-api-key' }
          ]
        }
      ]
    }
  }
}

@description('Resource ID of the job')
output id string = dataIngestionJob.id

@description('Name of the job')
output name string = dataIngestionJob.name
