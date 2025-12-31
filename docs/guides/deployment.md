# Deployment Guide

This guide covers deploying AlpacaMock to Azure using the provided Bicep templates.

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────┐
│                    Azure Container Apps                      │
│  ┌─────────────────────────────────────────────────────┐    │
│  │              AlpacaMock API                         │    │
│  │         (Container Apps Environment)                │    │
│  └─────────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────────┘
                    │                        │
                    ▼                        ▼
┌────────────────────────┐    ┌────────────────────────────────┐
│   Azure Cosmos DB      │    │  Azure Database for PostgreSQL │
│     (Serverless)       │    │    Flexible Server             │
│                        │    │    + TimescaleDB Extension     │
│  • Sessions            │    │                                │
│  • Accounts            │    │  • bars_minute (hypertable)    │
│  • Orders              │    │  • bars_daily (hypertable)     │
│  • Positions           │    │  • symbols                     │
└────────────────────────┘    └────────────────────────────────┘
```

**Estimated Monthly Cost**: $30-80/month (consumption-based)

---

## Prerequisites

- [Azure CLI](https://docs.microsoft.com/cli/azure/install-azure-cli)
- [Docker](https://www.docker.com/products/docker-desktop)
- Azure subscription with permissions to create resources
- Polygon API key (for data ingestion)

---

## 1. Azure Setup

### Login and set subscription

```bash
az login
az account set --subscription "Your Subscription Name"
```

### Create resource group

```bash
RESOURCE_GROUP="alpaca-mock-rg"
LOCATION="eastus"

az group create --name $RESOURCE_GROUP --location $LOCATION
```

---

## 2. Build and Push Docker Image

### Create Azure Container Registry

```bash
ACR_NAME="alpacamockacr"  # Must be globally unique

az acr create \
  --resource-group $RESOURCE_GROUP \
  --name $ACR_NAME \
  --sku Basic \
  --admin-enabled true
```

### Build and push the image

```bash
# Login to ACR
az acr login --name $ACR_NAME

# Build and push
docker build -t $ACR_NAME.azurecr.io/alpaca-mock:latest -f deploy/Dockerfile .
docker push $ACR_NAME.azurecr.io/alpaca-mock:latest
```

---

## 3. Deploy Infrastructure

### Deploy with Bicep

```bash
# Get ACR credentials
ACR_PASSWORD=$(az acr credential show -n $ACR_NAME --query "passwords[0].value" -o tsv)

# Deploy infrastructure
az deployment group create \
  --resource-group $RESOURCE_GROUP \
  --template-file deploy/azure/main.bicep \
  --parameters \
    containerRegistryName=$ACR_NAME \
    containerRegistryPassword=$ACR_PASSWORD \
    postgresAdminPassword="YourSecurePassword123!" \
    apiKey="your-production-api-key" \
    apiSecret="your-production-api-secret"
```

This creates:
- Azure Container Apps environment
- Azure Cosmos DB account (serverless)
- Azure Database for PostgreSQL Flexible Server
- Managed identities and networking

### Get deployment outputs

```bash
# Get the API URL
API_URL=$(az deployment group show \
  --resource-group $RESOURCE_GROUP \
  --name main \
  --query "properties.outputs.apiUrl.value" -o tsv)

echo "API URL: $API_URL"

# Get PostgreSQL connection string
PG_HOST=$(az deployment group show \
  --resource-group $RESOURCE_GROUP \
  --name main \
  --query "properties.outputs.postgresHost.value" -o tsv)
```

---

## 4. Configure TimescaleDB

Azure PostgreSQL Flexible Server supports TimescaleDB as an extension.

### Enable TimescaleDB extension

```bash
az postgres flexible-server parameter set \
  --resource-group $RESOURCE_GROUP \
  --server-name alpaca-mock-postgres \
  --name shared_preload_libraries \
  --value timescaledb

# Restart the server to apply
az postgres flexible-server restart \
  --resource-group $RESOURCE_GROUP \
  --name alpaca-mock-postgres
```

### Initialize the database

```bash
# Connect and run init script
PG_CONNECTION="Host=$PG_HOST;Database=alpaca_mock;Username=pgadmin;Password=YourSecurePassword123!"

dotnet run --project src/AlpacaMock.DataIngestion -- init-db -c "$PG_CONNECTION"
```

---

## 5. Load Market Data

Load historical data from Polygon:

```bash
POLYGON_KEY="your-polygon-api-key"

# Load symbol catalog
dotnet run --project src/AlpacaMock.DataIngestion -- load-symbols \
  -c "$PG_CONNECTION" \
  -k $POLYGON_KEY

# Load minute bars for key symbols
for SYMBOL in AAPL MSFT GOOGL AMZN TSLA; do
  dotnet run --project src/AlpacaMock.DataIngestion -- load-bars \
    -c "$PG_CONNECTION" \
    -k $POLYGON_KEY \
    -s $SYMBOL \
    --from 2020-01-01 \
    --to 2024-01-01 \
    -r minute
done
```

**Note**: For large data loads, consider running the ingestion tool from an Azure VM in the same region to minimize latency and egress costs.

---

## 6. Verify Deployment

### Health check

```bash
curl $API_URL/health
# {"status":"Healthy"}
```

### Test authentication

```bash
# Base64 encode your credentials
AUTH=$(echo -n "your-production-api-key:your-production-api-secret" | base64)

curl -H "Authorization: Basic $AUTH" $API_URL/v1/sessions
# []
```

### Create a test session

```bash
curl -X POST $API_URL/v1/sessions \
  -H "Authorization: Basic $AUTH" \
  -H "Content-Type: application/json" \
  -d '{
    "startTime": "2023-01-03T09:30:00Z",
    "endTime": "2023-12-29T16:00:00Z",
    "name": "Production Test"
  }'
```

---

## 7. Monitoring

### Enable Application Insights

The Bicep template includes Application Insights. View metrics in the Azure portal:

1. Go to your resource group
2. Open the Application Insights resource
3. View:
   - Request rates and latencies
   - Failed requests
   - Dependency calls (Cosmos DB, PostgreSQL)

### View container logs

```bash
az containerapp logs show \
  --resource-group $RESOURCE_GROUP \
  --name alpaca-mock-api \
  --follow
```

### Scale configuration

The Container App is configured for consumption-based scaling:

```bicep
scale: {
  minReplicas: 0    // Scale to zero when idle
  maxReplicas: 10   // Scale up under load
  rules: [{
    name: 'http-requests'
    http: {
      metadata: {
        concurrentRequests: '100'
      }
    }
  }]
}
```

---

## 8. Cost Management

### Estimated costs (consumption-based)

| Resource | Est. Monthly Cost |
|----------|-------------------|
| Container Apps | $5-20 (based on usage) |
| Cosmos DB Serverless | $5-15 (based on RUs consumed) |
| PostgreSQL Flexible (B1ms) | $15-25 |
| Application Insights | $0-5 |
| Container Registry (Basic) | $5 |
| **Total** | **$30-70/month** |

### Cost optimization tips

1. **Scale to zero**: Container Apps scales to 0 when idle
2. **Cosmos DB serverless**: Only pay for consumed RUs
3. **PostgreSQL**: Start with B1ms tier, scale as needed
4. **Data retention**: Use TTL on Cosmos DB events collection
5. **Compression**: TimescaleDB compression reduces storage costs

### Set up budget alerts

```bash
az consumption budget create \
  --resource-group $RESOURCE_GROUP \
  --budget-name "alpaca-mock-budget" \
  --amount 100 \
  --category Cost \
  --time-grain Monthly
```

---

## 9. Security Best Practices

### API keys

- Generate strong, unique API keys for production
- Rotate keys periodically
- Store secrets in Azure Key Vault

### Network security

```bash
# Restrict PostgreSQL access to Container Apps
az postgres flexible-server firewall-rule create \
  --resource-group $RESOURCE_GROUP \
  --name alpaca-mock-postgres \
  --rule-name "AllowContainerApps" \
  --start-ip-address <CONTAINER_APPS_OUTBOUND_IP> \
  --end-ip-address <CONTAINER_APPS_OUTBOUND_IP>
```

### Enable HTTPS only

The Container App is configured for HTTPS by default. Verify:

```bash
az containerapp ingress show \
  --resource-group $RESOURCE_GROUP \
  --name alpaca-mock-api \
  --query "transport"
# "https"
```

---

## 10. Updating the Application

### Deploy new version

```bash
# Build and push new image
docker build -t $ACR_NAME.azurecr.io/alpaca-mock:v2 -f deploy/Dockerfile .
docker push $ACR_NAME.azurecr.io/alpaca-mock:v2

# Update Container App
az containerapp update \
  --resource-group $RESOURCE_GROUP \
  --name alpaca-mock-api \
  --image $ACR_NAME.azurecr.io/alpaca-mock:v2
```

### Rollback

```bash
az containerapp revision list \
  --resource-group $RESOURCE_GROUP \
  --name alpaca-mock-api

az containerapp ingress traffic set \
  --resource-group $RESOURCE_GROUP \
  --name alpaca-mock-api \
  --revision-weight alpaca-mock-api--<previous-revision>=100
```

---

## Cleanup

To delete all resources:

```bash
az group delete --name $RESOURCE_GROUP --yes --no-wait
```

---

## Next Steps

- [Data Ingestion Guide](data-ingestion.md) - Bulk load historical data
- [API Reference](../api/README.md) - Complete endpoint documentation
- [Getting Started](getting-started.md) - Local development setup
