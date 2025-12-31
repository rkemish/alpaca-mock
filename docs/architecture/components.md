# Component Documentation

This document provides detailed documentation for each component in the AlpacaMock system.

## Table of Contents

- [Domain Layer](#domain-layer)
  - [SimulationClock](#simulationclock)
  - [MatchingEngine](#matchingengine)
  - [Session](#session)
  - [Order](#order)
  - [Position](#position)
  - [Account](#account)
- [Infrastructure Layer](#infrastructure-layer)
  - [BarRepository](#barrepository)
  - [PolygonClient](#polygonclient)
  - [CosmosDbContext](#cosmosdbcontext)
- [API Layer](#api-layer)
  - [Endpoints](#endpoints)
  - [Middleware](#middleware)

---

## Domain Layer

The domain layer contains business logic and is independent of infrastructure concerns.

### SimulationClock

**Location**: `src/AlpacaMock.Domain/Sessions/SimulationClock.cs`

**Purpose**: Manages simulation time for a backtest session. Each session has its own independent clock.

**Key Features**:
- **Step Mode**: Advance time by specific durations (1 min, 1 hour, 1 day)
- **Play Mode**: Real-time or accelerated playback (1x, 10x, 100x)
- **Pause Mode**: Freeze time for analysis
- **Market Hours**: Respects NYSE market hours (9:30 AM - 4:00 PM ET)

**Methods**:

| Method | Description |
|--------|-------------|
| `AdvanceBy(TimeSpan)` | Move forward by duration |
| `AdvanceTo(DateTimeOffset)` | Jump to specific time |
| `Play()` | Start continuous playback |
| `Pause()` | Stop playback |
| `SetSpeed(double)` | Set playback multiplier |
| `Tick()` | Update time based on real elapsed time |

**Example**:
```csharp
var clock = new SimulationClock(session);

// Step forward 5 minutes
clock.AdvanceBy(TimeSpan.FromMinutes(5));

// Or jump to specific time
clock.AdvanceTo(new DateTimeOffset(2023, 6, 15, 10, 30, 0, TimeSpan.Zero));

// Start accelerated playback at 100x speed
clock.SetSpeed(100);
clock.Play();
```

---

### MatchingEngine

**Location**: `src/AlpacaMock.Domain/Trading/MatchingEngine.cs`

**Purpose**: Simulates order execution against historical OHLCV bar data.

**Key Features**:
- Fills orders based on bar price conditions
- Applies realistic slippage based on bar range
- Considers volume for partial fills
- Supports market, limit, stop, and stop-limit orders

**Order Fill Logic**:

| Order Type | Fill Condition | Execution Price |
|------------|----------------|-----------------|
| Market | Always fills | Bar's open price |
| Limit (Buy) | Bar low ≤ limit price | Limit price |
| Limit (Sell) | Bar high ≥ limit price | Limit price |
| Stop (Buy) | Bar high ≥ stop price | Max(open, stop) |
| Stop (Sell) | Bar low ≤ stop price | Min(open, stop) |

**Slippage Model**:
```
slippage = (bar.High - bar.Low) × 0.10  // 10% of bar range
execution_price = price ± slippage      // adverse direction
```

**Example**:
```csharp
var engine = new MatchingEngine();

var fill = engine.TryFill(order, currentBar);

if (fill.Filled)
{
    order.FilledQty = fill.FillQty;
    order.FilledAvgPrice = fill.FillPrice;
    order.Status = OrderStatus.Filled;
}
```

---

### Session

**Location**: `src/AlpacaMock.Domain/Sessions/Session.cs`

**Purpose**: Represents a backtest session with isolated state.

**Properties**:

| Property | Type | Description |
|----------|------|-------------|
| `Id` | string | Unique session identifier |
| `SimulationStart` | DateTimeOffset | Start of backtest period |
| `SimulationEnd` | DateTimeOffset | End of backtest period |
| `CurrentSimulationTime` | DateTimeOffset | Current position in simulation |
| `PlaybackState` | enum | Paused, Playing, or StepPending |
| `PlaybackSpeed` | double | Speed multiplier (1.0 = real-time) |
| `InitialCash` | decimal | Starting cash balance |
| `TotalRealizedPnL` | decimal | Sum of closed position P&L |
| `TotalUnrealizedPnL` | decimal | Sum of open position P&L |

---

### Order

**Location**: `src/AlpacaMock.Domain/Trading/Order.cs`

**Purpose**: Represents a trading order (Alpaca-compatible).

**Order Types**:
- `Market` - Execute immediately at market price
- `Limit` - Execute at specified price or better
- `Stop` - Trigger market order when price reached
- `StopLimit` - Trigger limit order when price reached

**Order Statuses**:
```
New → Accepted → PartiallyFilled → Filled
                                 → Cancelled
                                 → Expired
```

---

### Position

**Location**: `src/AlpacaMock.Domain/Trading/Position.cs`

**Purpose**: Tracks holdings in a security.

**Key Methods**:

| Method | Description |
|--------|-------------|
| `ApplyFill(qty, price, side)` | Update position after order fill |
| `UpdatePrices(currentPrice)` | Recalculate P&L with new price |

**P&L Calculation**:
```
CostBasis = |Qty| × AvgEntryPrice
MarketValue = |Qty| × CurrentPrice
UnrealizedPnL = MarketValue - CostBasis
```

---

### Account

**Location**: `src/AlpacaMock.Domain/Accounts/Account.cs`

**Purpose**: Represents a trading account (Alpaca-compatible).

**Key Properties**:
- `Cash` - Available cash for trading
- `BuyingPower` - Total purchasing power
- `Equity` - Cash + LongMarketValue - ShortMarketValue
- `PortfolioValue` - Total account value

---

## Infrastructure Layer

### BarRepository

**Location**: `src/AlpacaMock.Infrastructure/Postgres/BarRepository.cs`

**Purpose**: Accesses Polygon bar data stored in TimescaleDB.

**Key Methods**:

| Method | Description |
|--------|-------------|
| `GetBarAsync(symbol, time)` | Get single bar at or before time |
| `GetBarsAsync(symbol, start, end)` | Get bars in time range |
| `GetLatestBarsAsync(symbols, asOf)` | Get latest bar for multiple symbols |
| `InsertBarsAsync(bars)` | Bulk insert using COPY protocol |

**Query Optimization**:
- Uses TimescaleDB hypertables for time-based partitioning
- Indexes on `(symbol, time DESC)` for efficient lookups
- DISTINCT ON for latest-per-symbol queries

**Example**:
```csharp
await using var repo = new BarRepository(connectionString);

// Get bars for a time range
var bars = await repo.GetBarsAsync("AAPL", startTime, endTime);

// Get latest bar for current simulation time
var bar = await repo.GetBarAsync("AAPL", session.CurrentSimulationTime);
```

---

### PolygonClient

**Location**: `src/AlpacaMock.Infrastructure/Polygon/PolygonClient.cs`

**Purpose**: REST client for Polygon.io API.

**Key Methods**:

| Method | Description |
|--------|-------------|
| `GetMinuteBarsAsync(symbol, from, to)` | Fetch minute-level bars |
| `GetDailyBarsAsync(symbol, from, to)` | Fetch daily bars |
| `GetTickersAsync()` | Get all available symbols |
| `GetPreviousCloseAsync(symbol)` | Get previous day's bar |

**Pagination**: Automatically handles `next_url` pagination for large date ranges.

---

### CosmosDbContext

**Location**: `src/AlpacaMock.Infrastructure/Cosmos/CosmosDbContext.cs`

**Purpose**: Manages Cosmos DB connection and container access.

**Containers**:

| Container | Partition Key | Purpose |
|-----------|---------------|---------|
| sessions | /id | Session metadata |
| accounts | /sessionId | Account data per session |
| orders | /sessionId | Order history |
| positions | /sessionId | Current holdings |
| events | /sessionId | Trade events |

---

## API Layer

### Endpoints

| File | Route Group | Purpose |
|------|-------------|---------|
| `SessionEndpoints.cs` | `/v1/sessions` | Session management |
| `AccountEndpoints.cs` | `/v1/accounts` | Account CRUD |
| `TradingEndpoints.cs` | `/v1/trading/accounts/{id}` | Orders & positions |
| `MarketDataEndpoints.cs` | `/v1/assets` | Asset & bar data |

### Middleware

#### BasicAuthMiddleware

**Location**: `src/AlpacaMock.Api/Middleware/BasicAuthMiddleware.cs`

**Purpose**: Validates API key credentials using Basic auth.

**Format**: `Authorization: Basic base64(API_KEY:API_SECRET)`

**Test Credentials**:
| API Key | API Secret |
|---------|------------|
| `test-api-key` | `test-api-secret` |
| `demo-api-key` | `demo-api-secret` |

---

## See Also

- [Architecture Overview](overview.md)
- [API Reference](../api/README.md)
- [Getting Started](../guides/getting-started.md)
