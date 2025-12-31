# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

AlpacaMock is a backtesting platform that simulates the Alpaca Broker API using historical Polygon market data. It enables realistic backtests against 20+ years of market data with full Alpaca API fidelity.

## Build & Development Commands

```bash
# Build
dotnet restore
dotnet build

# Run tests
dotnet test
dotnet test --collect:"XPlat Code Coverage"   # with coverage

# Run a single test
dotnet test --filter "FullyQualifiedName~MatchingEngineTests.Market_order_fills_at_open"

# Run the API (requires POSTGRES_CONNECTION_STRING and optionally COSMOS_CONNECTION_STRING)
dotnet run --project src/AlpacaMock.Api

# Data ingestion commands
dotnet run --project src/AlpacaMock.DataIngestion -- init-db -c "Host=localhost;Database=alpaca_mock;Username=postgres;Password=postgres"
dotnet run --project src/AlpacaMock.DataIngestion -- load-symbols -c "..." -k "POLYGON_API_KEY"
dotnet run --project src/AlpacaMock.DataIngestion -- load-bars -c "..." -k "..." -s AAPL --from 2023-01-01 --to 2024-01-01 -r minute
dotnet run --project src/AlpacaMock.DataIngestion -- stats -c "..."

# Local development setup (Docker + load FAANG+ stocks)
cp .env.example .env  # Add your POLYGON_API_KEY
./scripts/setup-local.sh

# Or start Docker manually
docker compose -f deploy/docker-compose.yml up -d

# Load additional stocks
./scripts/load-tech-stocks.sh -s AAPL -s MSFT

# API runs on http://localhost:5050
# Postman functional tests
newman run postman/AlpacaMock.postman_collection.json -e postman/Local.postman_environment.json --folder "Workflows"
```

## Architecture

**Clean Architecture with 4 projects:**

- **AlpacaMock.Api** - ASP.NET Core Minimal APIs REST layer. Endpoints organized by domain area in `Endpoints/` folder.
- **AlpacaMock.Domain** - Pure business logic with no external dependencies. Contains:
  - `MatchingEngine` - Order fill simulation against OHLCV bars
  - `SimulationClock` - Time control and playback management
  - `OrderValidator` - Alpaca trading rule enforcement
  - `DayTradeTracker` - Pattern Day Trader (PDT) rule tracking
- **AlpacaMock.Infrastructure** - Database and external API implementations:
  - `Cosmos/` - Azure Cosmos DB for session state (cloud only)
  - `InMemory/` - In-memory storage for local development
  - `Postgres/` - TimescaleDB for market bar data
  - `Polygon/` - Polygon.io API client
- **AlpacaMock.DataIngestion** - CLI tool for loading historical data from Polygon

**Data Flow:** Client request → BasicAuthMiddleware → Endpoint → Domain logic → Infrastructure (Cosmos for state, PostgreSQL for bars)

## Key Domain Concepts

**Sessions** have independent simulation clocks and isolated state. Each session tracks its own time range, accounts, orders, and positions.

**Matching Engine fill logic:**
- Market orders: Fill at bar open with 10% slippage based on bar range
- Limit orders: Fill when bar range touches limit price
- Stop orders: Trigger and convert to market when price reached
- Volume-based partial fills at 1% participation rate

**Order validation enforces Alpaca rules:**
- Price decimal precision (2 decimals for prices ≥$1, 4 for <$1)
- Buying power validation
- Extended hours: only limit orders with TIF=DAY
- Stop order direction enforcement

## Testing

Tests use xUnit + FluentAssertions + Moq. Test projects:
- `AlpacaMock.Domain.Tests` - Unit tests for business logic (94.5% coverage)
- `AlpacaMock.Api.Tests` - Integration tests

Test data builder at `tests/AlpacaMock.Domain.Tests/Fixtures/TestDataBuilder.cs` provides factory methods for creating test objects.

## Configuration

**Docker Compose (default):** Runs PostgreSQL and API. Session data stored in-memory locally.

**Environment variables:**
- `USE_INMEMORY_COSMOS=true` - Use in-memory storage (default for local dev)
- `COSMOS_CONNECTION_STRING` - Azure Cosmos DB connection (for cloud deployment)
- `POSTGRES_CONNECTION_STRING` - PostgreSQL/TimescaleDB connection
- `ApiKeys__0__Key` / `ApiKeys__0__Secret` - API credentials

**API authentication:** Basic Auth with `Authorization: Basic base64(API_KEY:API_SECRET)`

**Test credentials:** `test-api-key:test-api-secret` (base64: `dGVzdC1hcGkta2V5OnRlc3QtYXBpLXNlY3JldA==`)
