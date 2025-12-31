# AlpacaMock

A backtesting platform that provides an **Alpaca-compatible API** powered by **Polygon historical data**. Run realistic backtests against 20+ years of market data with full API fidelity.

## Features

- **Alpaca API Compatibility** - Drop-in replacement for Alpaca Broker API
- **Polygon Historical Data** - Real minute/daily bars for accurate backtesting
- **Time-Travel Simulation** - Step through history or run accelerated playback
- **Multiple Sessions** - Run concurrent backtests with isolated state
- **Order Matching** - Realistic fills against OHLCV data with slippage modeling

## Alpaca API Fidelity

AlpacaMock implements Alpaca trading rules for realistic backtesting:

### Order Validation
- **Price decimal precision** - Max 2 decimals for prices ≥$1, max 4 for <$1
- **Buying power validation** - Rejects orders exceeding available funds
- **Extended hours rules** - Only limit orders with TIF=DAY allowed
- **Stop order direction** - Buy stops must be above market, sell stops below

### Time-in-Force Support
- **Day** - Expires at market close
- **GTC** - Good till cancelled (auto-expires after 90 days per Alpaca rules)
- **IOC** - Immediate or cancel with partial fills
- **FOK** - Fill or kill (all or nothing)
- **OPG/CLS** - Market on open/close orders

### Pattern Day Trader (PDT) Rules
- Tracks day trades in rolling 5-business-day window
- PDT status triggered by 4+ day trades
- $25,000 minimum equity requirement for PDT accounts
- 4x day trading buying power for PDT accounts
- Warnings when approaching day trade limits

### Order Execution
- Market orders fill at bar open with realistic slippage
- Limit orders fill when bar range touches limit price
- Volume-based partial fills (1% participation rate)
- Stop orders trigger and convert to market orders

## Quick Start

```bash
# 1. Clone and start with Docker Compose (includes Cosmos DB emulator for Apple Silicon)
git clone https://github.com/rkemish/alpaca-mock.git
cd alpaca-mock
docker compose -f deploy/docker-compose.yml up -d

# 2. Load historical data from Polygon
dotnet run --project src/AlpacaMock.DataIngestion -- load-bars \
  -c "Host=localhost;Database=alpacamock;Username=postgres;Password=postgres" \
  -k "YOUR_POLYGON_KEY" -s AAPL --from 2023-01-01 --to 2023-12-31 -r minute

# 3. Create a backtest session and trade!
curl -X POST http://localhost:5050/v1/sessions \
  -H "Authorization: Basic dGVzdC1hcGkta2V5OnRlc3QtYXBpLXNlY3JldA==" \
  -H "Content-Type: application/json" \
  -d '{"startTime": "2023-01-03T09:30:00Z", "endTime": "2023-12-31T16:00:00Z"}'
```

Docker Compose provides:
- **PostgreSQL + TimescaleDB** - Market data (persisted)
- **Azure Cosmos DB Emulator** - Session storage (persisted, supports Apple Silicon M1/M2/M3)
- **AlpacaMock API** - Available at `http://localhost:5050`

## Documentation

| Document | Description |
|----------|-------------|
| [Getting Started](docs/guides/getting-started.md) | Setup and first backtest |
| [API Reference](docs/api/README.md) | Complete endpoint documentation |
| [Data Ingestion](docs/guides/data-ingestion.md) | Loading historical data from Polygon |
| [Functional Tests](docs/testing/functional-tests.md) | Postman/Newman API testing |
| [Architecture Overview](docs/architecture/overview.md) | System design and component diagram |
| [Components](docs/architecture/components.md) | Deep dive into each component |
| [Deployment](docs/guides/deployment.md) | Azure deployment guide |

## Project Structure

```
alpaca-mock/
├── src/
│   ├── AlpacaMock.Api/           # REST API (ASP.NET Core Minimal APIs)
│   ├── AlpacaMock.Domain/        # Business logic and models
│   ├── AlpacaMock.Infrastructure/# Database access (Cosmos DB, PostgreSQL)
│   └── AlpacaMock.DataIngestion/ # CLI tool for loading Polygon data
├── docs/                          # Documentation
├── deploy/                        # Docker and Azure Bicep files
└── tests/                         # Unit and integration tests
```

## Technology Stack

| Component | Technology |
|-----------|------------|
| API | C# / .NET 10 Minimal APIs |
| Session State | Azure Cosmos DB (Serverless) |
| Market Data | PostgreSQL + TimescaleDB |
| Data Source | Polygon.io API |
| Testing | xUnit, FluentAssertions |
| Deployment | Azure Container Apps |

## Testing

The project includes comprehensive unit tests and functional API tests:

### Unit Tests (94.5% coverage)
- **Order validation** - Price precision, buying power, extended hours, stop orders
- **Matching engine** - Market/limit/stop fills, IOC/FOK behavior, slippage, volume limits
- **Position management** - Average price calculations, P&L tracking, position flipping
- **PDT tracking** - Day trade counting, PDT status, validation rules
- **Simulation clock** - Time advancement, market hours, playback controls

```bash
# Run unit tests
dotnet test

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"
```

### Functional Tests (Postman/Newman)
```bash
# Run API functional tests
newman run postman/AlpacaMock.postman_collection.json \
  -e postman/Local.postman_environment.json \
  --folder "Workflows"
```

See [Functional Tests](docs/testing/functional-tests.md) for details.

## How It Works

1. **Create a Session** - Define a historical time range for your backtest
2. **Advance Time** - Step forward minute-by-minute or run accelerated playback
3. **Place Orders** - Use Alpaca-compatible endpoints to submit trades
4. **Orders Fill** - Matching engine executes against real Polygon bar data
5. **Track Results** - Monitor positions, P&L, and portfolio value

## License

MIT
