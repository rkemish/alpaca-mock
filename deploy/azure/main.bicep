@description('Location for all resources')
param location string = resourceGroup().location

@description('Name prefix for resources')
param namePrefix string = 'alpacamock'

@description('Polygon API key for market data')
@secure()
param polygonApiKey string

@description('API key for authentication')
@secure()
param apiKey string = 'alpaca-api-key'

@description('API secret for authentication')
@secure()
param apiSecret string

// Azure Container Registry (Basic SKU - minimum cost)
resource containerRegistry 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: '${namePrefix}acr'
  location: location
  sku: {
    name: 'Basic'
  }
  properties: {
    adminUserEnabled: true
  }
}

// Log Analytics Workspace for Container Apps
resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2022-10-01' = {
  name: '${namePrefix}-logs'
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
  }
}

// Container Apps Environment
resource containerAppsEnv 'Microsoft.App/managedEnvironments@2023-05-01' = {
  name: '${namePrefix}-env'
  location: location
  properties: {
    zoneRedundant: false
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalytics.properties.customerId
        sharedKey: logAnalytics.listKeys().primarySharedKey
      }
    }
  }
}

// Cosmos DB Account (Serverless - pay per use)
resource cosmosAccount 'Microsoft.DocumentDB/databaseAccounts@2023-04-15' = {
  name: '${namePrefix}-cosmos'
  location: location
  kind: 'GlobalDocumentDB'
  properties: {
    databaseAccountOfferType: 'Standard'
    capabilities: [
      {
        name: 'EnableServerless'
      }
    ]
    locations: [
      {
        locationName: location
        failoverPriority: 0
      }
    ]
    consistencyPolicy: {
      defaultConsistencyLevel: 'Session'
    }
  }
}

// PostgreSQL Flexible Server (Burstable B1ms - minimum cost)
resource postgresServer 'Microsoft.DBforPostgreSQL/flexibleServers@2023-03-01-preview' = {
  name: '${namePrefix}-postgres'
  location: location
  sku: {
    name: 'Standard_B1ms'
    tier: 'Burstable'
  }
  properties: {
    version: '15'
    administratorLogin: 'alpacaadmin'
    administratorLoginPassword: 'P@ssw0rd${uniqueString(resourceGroup().id)}'
    storage: {
      storageSizeGB: 32
    }
    backup: {
      backupRetentionDays: 7
      geoRedundantBackup: 'Disabled'
    }
    highAvailability: {
      mode: 'Disabled'
    }
  }
}

// PostgreSQL Database
resource postgresDatabase 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2023-03-01-preview' = {
  parent: postgresServer
  name: 'alpacamock'
  properties: {
    charset: 'UTF8'
    collation: 'en_US.utf8'
  }
}

// PostgreSQL Firewall Rule (Allow Azure Services)
resource postgresFirewall 'Microsoft.DBforPostgreSQL/flexibleServers/firewallRules@2023-03-01-preview' = {
  parent: postgresServer
  name: 'AllowAzureServices'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

// Container App - API (24/7 with minimum resources)
resource containerApp 'Microsoft.App/containerApps@2023-05-01' = {
  name: '${namePrefix}-api'
  location: location
  properties: {
    managedEnvironmentId: containerAppsEnv.id
    configuration: {
      ingress: {
        external: true
        targetPort: 8080
        transport: 'http'
        corsPolicy: {
          allowedOrigins: ['*']
          allowedMethods: ['GET', 'POST', 'PUT', 'DELETE', 'PATCH']
          allowedHeaders: ['*']
        }
      }
      registries: [
        {
          server: containerRegistry.properties.loginServer
          username: containerRegistry.listCredentials().username
          passwordSecretRef: 'acr-password'
        }
      ]
      secrets: [
        {
          name: 'acr-password'
          value: containerRegistry.listCredentials().passwords[0].value
        }
        {
          name: 'cosmos-connection'
          value: cosmosAccount.listConnectionStrings().connectionStrings[0].connectionString
        }
        {
          name: 'postgres-connection'
          value: 'Host=${postgresServer.properties.fullyQualifiedDomainName};Database=alpacamock;Username=alpacaadmin;Password=P@ssw0rd${uniqueString(resourceGroup().id)};SSL Mode=Require;Trust Server Certificate=true'
        }
        {
          name: 'polygon-api-key'
          value: polygonApiKey
        }
        {
          name: 'api-key'
          value: apiKey
        }
        {
          name: 'api-secret'
          value: apiSecret
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'api'
          image: '${containerRegistry.properties.loginServer}/alpacamock-api:latest'
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
          env: [
            {
              name: 'ASPNETCORE_ENVIRONMENT'
              value: 'Production'
            }
            {
              name: 'COSMOS_CONNECTION_STRING'
              secretRef: 'cosmos-connection'
            }
            {
              name: 'POSTGRES_CONNECTION_STRING'
              secretRef: 'postgres-connection'
            }
            {
              name: 'POLYGON_API_KEY'
              secretRef: 'polygon-api-key'
            }
            {
              name: 'ApiKeys__0__Key'
              secretRef: 'api-key'
            }
            {
              name: 'ApiKeys__0__Secret'
              secretRef: 'api-secret'
            }
            {
              name: 'ApiKeys__0__Name'
              value: 'Production User'
            }
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
        minReplicas: 1  // 24/7 availability
        maxReplicas: 3
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

// Outputs
output apiUrl string = 'https://${containerApp.properties.configuration.ingress.fqdn}'
output acrLoginServer string = containerRegistry.properties.loginServer
output acrName string = containerRegistry.name
output cosmosAccountName string = cosmosAccount.name
output postgresServerName string = postgresServer.name
output postgresServerFqdn string = postgresServer.properties.fullyQualifiedDomainName
