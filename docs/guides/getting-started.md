# Getting Started

This guide walks you through setting up AlpacaMock locally and running your first backtest.

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Docker](https://www.docker.com/products/docker-desktop) (for local PostgreSQL)
- [Polygon.io Advanced subscription](https://polygon.io/pricing) (for market data)

---

## 1. Clone and Build

```bash
git clone https://github.com/your-org/alpaca-mock.git
cd alpaca-mock

# Restore and build
dotnet restore
dotnet build
```

---

## 2. Start PostgreSQL with TimescaleDB

AlpacaMock stores historical bar data in PostgreSQL with the TimescaleDB extension.

```bash
# Start TimescaleDB container
docker run -d \
  --name alpaca-timescale \
  -p 5432:5432 \
  -e POSTGRES_PASSWORD=postgres \
  -e POSTGRES_DB=alpaca_mock \
  timescale/timescaledb:latest-pg16

# Wait for it to be ready
docker logs -f alpaca-timescale
# Look for: "database system is ready to accept connections"
```

---

## 3. Initialize the Database

Use the data ingestion tool to create the schema:

```bash
dotnet run --project src/AlpacaMock.DataIngestion -- init-db \
  -c "Host=localhost;Database=alpaca_mock;Username=postgres;Password=postgres"
```

Expected output:
```
Database initialized successfully.
Created tables: bars_minute, bars_daily, symbols, data_coverage
TimescaleDB hypertables configured.
```

---

## 4. Load Market Data

You need historical bar data to run backtests. Load data for the symbols you want to test:

```bash
# Set your Polygon API key
export POLYGON_API_KEY="your-polygon-api-key"

# Load symbols catalog
dotnet run --project src/AlpacaMock.DataIngestion -- load-symbols \
  -c "Host=localhost;Database=alpaca_mock;Username=postgres;Password=postgres" \
  -k $POLYGON_API_KEY

# Load minute bars for a symbol (this may take a while)
dotnet run --project src/AlpacaMock.DataIngestion -- load-bars \
  -c "Host=localhost;Database=alpaca_mock;Username=postgres;Password=postgres" \
  -k $POLYGON_API_KEY \
  -s AAPL \
  --from 2023-01-01 \
  --to 2024-01-01 \
  -r minute

# Check what data is available
dotnet run --project src/AlpacaMock.DataIngestion -- stats \
  -c "Host=localhost;Database=alpaca_mock;Username=postgres;Password=postgres"
```

**Tip**: Load a few popular symbols first (AAPL, MSFT, GOOGL) to get started quickly.

---

## 5. Configure the API

Create a local settings file:

```bash
# Copy example settings
cp src/AlpacaMock.Api/appsettings.Development.json.example \
   src/AlpacaMock.Api/appsettings.Development.json
```

Edit `appsettings.Development.json`:

```json
{
  "ConnectionStrings": {
    "PostgreSQL": "Host=localhost;Database=alpaca_mock;Username=postgres;Password=postgres",
    "CosmosDB": "AccountEndpoint=https://localhost:8081/;AccountKey=..."
  },
  "Authentication": {
    "ApiKey": "test-api-key",
    "ApiSecret": "test-api-secret"
  }
}
```

**For local development without Azure Cosmos DB**, you can use the [Cosmos DB Emulator](https://learn.microsoft.com/en-us/azure/cosmos-db/local-emulator) or configure an in-memory session store.

---

## 6. Start the API

```bash
dotnet run --project src/AlpacaMock.Api
```

The API starts on `http://localhost:5000`.

Verify it's running:
```bash
curl http://localhost:5000/health
# {"status":"Healthy"}
```

---

## 7. Run Your First Backtest

Now let's create a session and execute some trades.

### Set up authentication

```bash
# Base64 encoded test-api-key:test-api-secret
AUTH="Authorization: Basic dGVzdC1hcGkta2V5OnRlc3QtYXBpLXNlY3JldA=="
```

### Create a backtest session

```bash
SESSION=$(curl -s -X POST http://localhost:5000/v1/sessions \
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

### Create a trading account

```bash
ACCOUNT=$(curl -s -X POST http://localhost:5000/v1/accounts \
  -H "$AUTH" \
  -H "X-Session-Id: $SESSION" \
  -H "Content-Type: application/json" \
  -d '{"initialCash": 100000}' | jq -r '.id')

echo "Account ID: $ACCOUNT"
```

### Check current simulation time

```bash
curl -s http://localhost:5000/v1/sessions/$SESSION \
  -H "$AUTH" | jq '{current_time, playback_state}'
```

### Place a market order

```bash
curl -X POST "http://localhost:5000/v1/trading/accounts/$ACCOUNT/orders" \
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

### Advance time to fill the order

```bash
curl -X POST "http://localhost:5000/v1/sessions/$SESSION/time/advance" \
  -H "$AUTH" \
  -H "Content-Type: application/json" \
  -d '{"duration": 1}'
```

### Check your position

```bash
curl -s "http://localhost:5000/v1/trading/accounts/$ACCOUNT/positions" \
  -H "$AUTH" \
  -H "X-Session-Id: $SESSION" | jq
```

### Check account balance

```bash
curl -s "http://localhost:5000/v1/accounts/$ACCOUNT" \
  -H "$AUTH" \
  -H "X-Session-Id: $SESSION" | jq '{cash, equity, buying_power}'
```

---

## 8. Trading Strategies

Here's a simple example of running a backtest loop:

```bash
#!/bin/bash
AUTH="Authorization: Basic dGVzdC1hcGkta2V5OnRlc3QtYXBpLXNlY3JldA=="
SESSION="your-session-id"
ACCOUNT="your-account-id"

# Advance through the trading day, one minute at a time
for i in {1..390}; do
  # Get current price
  PRICE=$(curl -s "http://localhost:5000/v1/assets/AAPL/quotes/latest" \
    -H "$AUTH" \
    -H "X-Session-Id: $SESSION" | jq -r '.quote.bp')

  echo "Minute $i: AAPL bid = $PRICE"

  # Your trading logic here...
  # Example: Buy if price drops below 150
  if (( $(echo "$PRICE < 150" | bc -l) )); then
    curl -s -X POST "http://localhost:5000/v1/trading/accounts/$ACCOUNT/orders" \
      -H "$AUTH" \
      -H "X-Session-Id: $SESSION" \
      -H "Content-Type: application/json" \
      -d '{"symbol": "AAPL", "side": "buy", "qty": 1, "type": "market"}'
  fi

  # Advance time by 1 minute
  curl -s -X POST "http://localhost:5000/v1/sessions/$SESSION/time/advance" \
    -H "$AUTH" \
    -H "Content-Type: application/json" \
    -d '{"duration": 1}'
done

# Check final results
curl -s "http://localhost:5000/v1/accounts/$ACCOUNT" \
  -H "$AUTH" \
  -H "X-Session-Id: $SESSION" | jq
```

---

## Next Steps

- [Data Ingestion Guide](data-ingestion.md) - Load more historical data
- [Deployment Guide](deployment.md) - Deploy to Azure
- [API Reference](../api/README.md) - Complete endpoint documentation
- [Architecture Overview](../architecture/overview.md) - Understand the system design

---

## Troubleshooting

### "No bar data found for symbol"

You need to load data for that symbol first:
```bash
dotnet run --project src/AlpacaMock.DataIngestion -- load-bars \
  -c "..." -k "..." -s SYMBOL --from 2023-01-01 --to 2024-01-01 -r minute
```

### "Session not found"

Make sure you're passing the `X-Session-Id` header with a valid session ID.

### "Authentication failed"

Verify your Authorization header is correctly base64-encoded:
```bash
echo -n "test-api-key:test-api-secret" | base64
# dGVzdC1hcGkta2V5OnRlc3QtYXBpLXNlY3JldA==
```

### "Connection refused" on PostgreSQL

Check that the TimescaleDB container is running:
```bash
docker ps | grep timescale
docker logs alpaca-timescale
```

### "Order not filling"

Orders fill on the next bar after they're placed. Make sure to advance time:
```bash
curl -X POST "http://localhost:5000/v1/sessions/$SESSION/time/advance" \
  -H "$AUTH" -d '{"duration": 1}'
```
