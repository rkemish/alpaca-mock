@description('Name of the PostgreSQL server')
param serverName string

@description('Location for the resource')
param location string = resourceGroup().location

@description('Database name')
param databaseName string = 'alpacamock'

@description('Administrator login')
param administratorLogin string = 'alpacaadmin'

@description('Administrator password')
@secure()
param administratorPassword string

@description('PostgreSQL version')
@allowed(['14', '15', '16'])
param version string = '16'

@description('Storage size in GB')
@minValue(32)
@maxValue(16384)
param storageSizeGB int = 32

resource postgresServer 'Microsoft.DBforPostgreSQL/flexibleServers@2024-08-01' = {
  name: serverName
  location: location
  sku: {
    name: 'Standard_B1ms'
    tier: 'Burstable'
  }
  properties: {
    version: version
    administratorLogin: administratorLogin
    administratorLoginPassword: administratorPassword
    storage: {
      storageSizeGB: storageSizeGB
      autoGrow: 'Disabled'
    }
    backup: {
      backupRetentionDays: 7
      geoRedundantBackup: 'Disabled'
    }
    highAvailability: {
      mode: 'Disabled'
    }
    network: {
      publicNetworkAccess: 'Enabled'
    }
  }
}

resource database 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2024-08-01' = {
  parent: postgresServer
  name: databaseName
  properties: {
    charset: 'UTF8'
    collation: 'en_US.utf8'
  }
}

resource firewallAzureServices 'Microsoft.DBforPostgreSQL/flexibleServers/firewallRules@2024-08-01' = {
  parent: postgresServer
  name: 'AllowAzureServices'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource timescaleConfig 'Microsoft.DBforPostgreSQL/flexibleServers/configurations@2024-08-01' = {
  parent: postgresServer
  name: 'shared_preload_libraries'
  properties: {
    value: 'pg_stat_statements,timescaledb'
    source: 'user-override'
  }
}

resource azureExtensions 'Microsoft.DBforPostgreSQL/flexibleServers/configurations@2024-08-01' = {
  parent: postgresServer
  name: 'azure.extensions'
  properties: {
    value: 'TIMESCALEDB'
    source: 'user-override'
  }
}

@description('Resource ID of the PostgreSQL server')
output id string = postgresServer.id

@description('Name of the PostgreSQL server')
output name string = postgresServer.name

@description('Fully qualified domain name')
output fqdn string = postgresServer.properties.fullyQualifiedDomainName

@description('Connection string for the database')
#disable-next-line outputs-should-not-contain-secrets
output connectionString string = 'Host=${postgresServer.properties.fullyQualifiedDomainName};Database=${databaseName};Username=${administratorLogin};Password=${administratorPassword};SSL Mode=Require;Trust Server Certificate=true'
