@description('Name of the container app')
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
param imageName string = 'alpacamock-api'

@description('Container image tag')
param imageTag string = 'latest'

@description('Cosmos DB connection string')
@secure()
param cosmosConnectionString string

@description('PostgreSQL connection string')
@secure()
param postgresConnectionString string

@description('Polygon API key')
@secure()
param polygonApiKey string

@description('API authentication key')
@secure()
param apiKey string

@description('API authentication secret')
@secure()
param apiSecret string

@description('Minimum replicas')
@minValue(0)
@maxValue(10)
param minReplicas int = 1

@description('Maximum replicas')
@minValue(1)
@maxValue(30)
param maxReplicas int = 3

resource containerApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: name
  location: location
  properties: {
    managedEnvironmentId: environmentId
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: 8080
        transport: 'http'
        allowInsecure: false
        corsPolicy: {
          allowedOrigins: ['*']
          allowedMethods: ['GET', 'POST', 'PUT', 'DELETE', 'PATCH']
          allowedHeaders: ['*']
        }
      }
      registries: [
        {
          server: acrLoginServer
          username: acrUsername
          passwordSecretRef: 'acr-password'
        }
      ]
      secrets: [
        { name: 'acr-password', value: acrPassword }
        { name: 'cosmos-connection', value: cosmosConnectionString }
        { name: 'postgres-connection', value: postgresConnectionString }
        { name: 'polygon-api-key', value: polygonApiKey }
        { name: 'api-key', value: apiKey }
        { name: 'api-secret', value: apiSecret }
      ]
    }
    template: {
      containers: [
        {
          name: 'api'
          image: '${acrLoginServer}/${imageName}:${imageTag}'
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
          env: [
            { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
            { name: 'COSMOS_CONNECTION_STRING', secretRef: 'cosmos-connection' }
            { name: 'POSTGRES_CONNECTION_STRING', secretRef: 'postgres-connection' }
            { name: 'POLYGON_API_KEY', secretRef: 'polygon-api-key' }
            { name: 'ApiKeys__0__Key', secretRef: 'api-key' }
            { name: 'ApiKeys__0__Secret', secretRef: 'api-secret' }
            { name: 'ApiKeys__0__Name', value: 'Production User' }
          ]
          probes: [
            {
              type: 'Liveness'
              httpGet: {
                path: '/health'
                port: 8080
              }
              initialDelaySeconds: 10
              periodSeconds: 30
              failureThreshold: 3
            }
            {
              type: 'Readiness'
              httpGet: {
                path: '/health'
                port: 8080
              }
              initialDelaySeconds: 5
              periodSeconds: 10
              failureThreshold: 3
            }
          ]
        }
      ]
      scale: {
        minReplicas: minReplicas
        maxReplicas: maxReplicas
        rules: [
          {
            name: 'http-scaling'
            http: {
              metadata: {
                concurrentRequests: '50'
              }
            }
          }
        ]
      }
    }
  }
}

@description('Resource ID of the container app')
output id string = containerApp.id

@description('Name of the container app')
output name string = containerApp.name

@description('FQDN of the container app')
output fqdn string = containerApp.properties.configuration.ingress.fqdn

@description('URL of the container app')
output url string = 'https://${containerApp.properties.configuration.ingress.fqdn}'
