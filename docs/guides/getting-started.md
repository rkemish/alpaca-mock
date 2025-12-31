# Getting Started

This guide walks you through setting up AlpacaMock locally and running your first backtest.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Docker Desktop](https://www.docker.com/products/docker-desktop) (for local infrastructure)
- [Polygon.io subscription](https://polygon.io/pricing) (for market data)

---

## Quick Start with Docker Compose

The fastest way to get started is using Docker Compose, which sets up:
- **PostgreSQL + TimescaleDB** - Market data storage (persisted)
- **Azure Cosmos DB Emulator** - Session storage (persisted, supports Apple Silicon M1/M2/M3)
- **AlpacaMock API** - The backtesting API

### 1. Clone and Start

```bash
git clone https://github.com/rkemish/alpaca-mock.git
cd alpaca-mock

# Start all services (first run may take a few minutes to pull images)
docker compose -f deploy/docker-compose.yml up -d

# Check services are healthy
docker compose -f deploy/docker-compose.yml ps
```

Wait for all services to show "healthy" status. The Cosmos DB emulator takes ~60 seconds to initialize.

### 2. Load Market Data

You need historical bar data to run backtests:

```bash
# Load AAPL minute bars for January 2023
dotnet run --project src/AlpacaMock.DataIngestion -- load-bars \
  -c "Host=localhost;Database=alpacamock;Username=postgres;Password=postgres" \
  -k "YOUR_POLYGON_API_KEY" \
  -s AAPL \
  --from 2023-01-01 \
  --to 2023-01-31 \
  -r minute
```

### 3. Verify the API

```bash
curl http://localhost:5050/health
# {"status":"healthy","timestamp":"..."}
```

### 4. Run Your First Backtest

```bash
# Set up authentication
AUTH="Authorization: Basic dGVzdC1hcGkta2V5OnRlc3QtYXBpLXNlY3JldA=="

# Create a session
curl -X POST http://localhost:5050/v1/sessions \
  -H "$AUTH" \
  -H "Content-Type: application/json" \
  -d '{
    "startTime": "2023-01-03T09:30:00Z",
    "endTime": "2023-01-31T16:00:00Z",
    "name": "My First Backtest",
    "initialCash": 100000
  }'
```

See the [API Reference](../api/README.md) for complete endpoint documentation.

---

## Data Persistence

With Docker Compose, all data persists across restarts:

| Data | Storage | Volume |
|------|---------|--------|
| Market bars (OHLCV) | PostgreSQL/TimescaleDB | `postgres_data` |
| Sessions, accounts, orders | Cosmos DB Emulator | `cosmos_data` |

To reset everything:
```bash
docker compose -f deploy/docker-compose.yml down -v
```

---

## Manual Setup (Alternative)

If you prefer not to use Docker Compose, you can set up each component manually.

### 1. Start PostgreSQL with TimescaleDB

```bash
docker run -d \
  --name alpaca-timescale \
  -p 5432:5432 \
  -e POSTGRES_PASSWORD=postgres \
  -e POSTGRES_DB=alpacamock \
  timescale/timescaledb:latest-pg15
```

### 2. Initialize Database Schema

```bash
dotnet run --project src/AlpacaMock.DataIngestion -- init-db \
  -c "Host=localhost;Database=alpacamock;Username=postgres;Password=postgres"
```

### 3. Start Cosmos DB Emulator (Optional)

For persistent session storage on Apple Silicon:

```bash
docker run -d \
  --name cosmos-emulator \
  -p 8081:8081 \
  -p 1234:1234 \
  mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator:vnext-preview
```

### 4. Run the API

```bash
# With Cosmos DB emulator
COSMOS_CONNECTION_STRING="AccountEndpoint=http://localhost:8081/;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==" \
POSTGRES_CONNECTION_STRING="Host=localhost;Database=alpacamock;Username=postgres;Password=postgres" \
dotnet run --project src/AlpacaMock.Api

# Or with in-memory session storage (data lost on restart)
USE_INMEMORY_COSMOS=true \
POSTGRES_CONNECTION_STRING="Host=localhost;Database=alpacamock;Username=postgres;Password=postgres" \
dotnet run --project src/AlpacaMock.Api
```

The API runs on `http://localhost:5000` when started manually.

---

## Loading Market Data

### Load Symbols Catalog

```bash
dotnet run --project src/AlpacaMock.DataIngestion -- load-symbols \
  -c "Host=localhost;Database=alpacamock;Username=postgres;Password=postgres" \
  -k "YOUR_POLYGON_API_KEY"
```

### Load Minute Bars

```bash
dotnet run --project src/AlpacaMock.DataIngestion -- load-bars \
  -c "Host=localhost;Database=alpacamock;Username=postgres;Password=postgres" \
  -k "YOUR_POLYGON_API_KEY" \
  -s AAPL \
  --from 2023-01-01 \
  --to 2023-12-31 \
  -r minute
```

### Check Data Coverage

```bash
dotnet run --project src/AlpacaMock.DataIngestion -- stats \
  -c "Host=localhost;Database=alpacamock;Username=postgres;Password=postgres"
```

---

## Running a Backtest

### Authentication

All API requests (except `/health`) require Basic Auth:

```bash
AUTH="Authorization: Basic dGVzdC1hcGkta2V5OnRlc3QtYXBpLXNlY3JldA=="
```

### Create a Session

```bash
SESSION=$(curl -s -X POST http://localhost:5050/v1/sessions \
  -H "$AUTH" \
  -H "Content-Type: application/json" \
  -d '{
    "startTime": "2023-01-03T09:30:00Z",
    "endTime": "2023-01-31T16:00:00Z",
    "name": "My First Backtest",
    "initialCash": 100000
  }' | jq -r '.id')

echo "Session ID: $SESSION"
```

### Create an Account

```bash
ACCOUNT=$(curl -s -X POST http://localhost:5050/v1/accounts \
  -H "$AUTH" \
  -H "X-Session-Id: $SESSION" \
  -H "Content-Type: application/json" \
  -d '{"initialCash": 100000}' | jq -r '.id')

echo "Account ID: $ACCOUNT"
```

### Place a Market Order

```bash
curl -X POST "http://localhost:5050/v1/trading/accounts/$ACCOUNT/orders" \
  -H "$AUTH" \
  -H "X-Session-Id: $SESSION" \
  -H "Content-Type: application/json" \
  -d '{
    "symbol": "AAPL",
    "side": "buy",
    "qty": 10,
    "type": "market"
  }'
```

### Advance Time (Orders Fill on Next Bar)

```bash
curl -X POST "http://localhost:5050/v1/sessions/$SESSION/time/advance" \
  -H "$AUTH" \
  -H "Content-Type: application/json" \
  -d '{"duration": 1}'
```

### Check Position

```bash
curl -s "http://localhost:5050/v1/trading/accounts/$ACCOUNT/positions" \
  -H "$AUTH" \
  -H "X-Session-Id: $SESSION" | jq
```

---

## Using Postman

Import the Postman collection for easier testing:

1. Import `postman/AlpacaMock.postman_collection.json`
2. Import `postman/Local.postman_environment.json`
3. Select "AlpacaMock - Local" environment
4. Run the "Workflows" folder

See [Functional Tests](../testing/functional-tests.md) for automated testing with Newman.

---

## Next Steps

- [Data Ingestion Guide](data-ingestion.md) - Load more historical data
- [API Reference](../api/README.md) - Complete endpoint documentation
- [Functional Tests](../testing/functional-tests.md) - Automated API testing
- [Deployment Guide](deployment.md) - Deploy to Azure

---

## Troubleshooting

### "No bar data found for symbol"

Load data for that symbol:
```bash
dotnet run --project src/AlpacaMock.DataIngestion -- load-bars \
  -c "..." -k "..." -s SYMBOL --from 2023-01-01 --to 2023-12-31 -r minute
```

### "Session not found"

Ensure you're passing the `X-Session-Id` header with a valid session ID.

### "Authentication failed"

Verify your Authorization header:
```bash
echo -n "test-api-key:test-api-secret" | base64
# dGVzdC1hcGkta2V5OnRlc3QtYXBpLXNlY3JldA==
```

### Cosmos DB emulator not starting

The emulator takes ~60 seconds to initialize. Check logs:
```bash
docker logs alpaca-mock-cosmosdb-1
```

### Services not healthy

Check all service logs:
```bash
docker compose -f deploy/docker-compose.yml logs
```

### Reset everything

```bash
docker compose -f deploy/docker-compose.yml down -v
docker compose -f deploy/docker-compose.yml up -d
```
