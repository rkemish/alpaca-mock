# Functional Testing Guide

This document describes the functional testing approach for AlpacaMock using Postman and Newman.

## Overview

AlpacaMock uses **Postman collections** for functional API testing, executed via **Newman** (Postman's CLI runner) for automation. Tests validate end-to-end workflows that simulate real backtesting scenarios.

```
┌─────────────────┐     ┌─────────────────┐     ┌─────────────────┐
│  Postman        │     │  Newman CLI     │     │  CI/CD Pipeline │
│  (Interactive)  │────▶│  (Automation)   │────▶│  (GitHub Actions)│
└─────────────────┘     └─────────────────┘     └─────────────────┘
```

---

## Test Infrastructure

### Files

| File | Purpose |
|------|---------|
| `postman/AlpacaMock.postman_collection.json` | Test collection with all endpoints and workflows |
| `postman/Local.postman_environment.json` | Environment variables for local testing (localhost:5050) |
| `postman/Cloud.postman_environment.json` | Environment variables for cloud testing (Azure) |

### Prerequisites

- Docker Compose running (`docker compose -f deploy/docker-compose.yml up -d`)
- Historical data loaded for test symbols (at minimum: AAPL for January 2023)
- Newman installed (`npm install -g newman`)

---

## Running Tests

### Interactive (Postman GUI)

1. Import `AlpacaMock.postman_collection.json`
2. Import `Local.postman_environment.json`
3. Select "AlpacaMock - Local" environment
4. Run the "Workflows" folder using Collection Runner

### Automated (Newman CLI)

```bash
# Run all workflow tests
newman run postman/AlpacaMock.postman_collection.json \
  -e postman/Local.postman_environment.json \
  --folder "Workflows"

# Run specific workflow
newman run postman/AlpacaMock.postman_collection.json \
  -e postman/Local.postman_environment.json \
  --folder "1. Setup Backtest"

# Generate HTML report
newman run postman/AlpacaMock.postman_collection.json \
  -e postman/Local.postman_environment.json \
  --folder "Workflows" \
  --reporters cli,html \
  --reporter-html-export reports/functional-tests.html
```

---

## Functional Test Workflows

### Workflow 1: Setup Backtest

**Purpose:** Validate the fundamental setup flow required before any trading can occur.

| Step | Request | Assertions | Rationale |
|------|---------|------------|-----------|
| 1.1 | Create Session | 201 Created, returns session ID | Sessions provide isolated backtest environments with their own time, accounts, and state. This is the entry point for all backtesting. |
| 1.2 | Create Account | 201 Created, returns account ID, initial cash = $100,000 | Accounts hold positions and track P&L. Validates that accounts are correctly initialized with starting capital. |
| 1.3 | Check Session Status | 200 OK, status = "active", playback_state = "paused" | Confirms session is ready for trading. Paused state means time won't advance automatically. |

**What This Tests:**
- Session isolation and creation
- Account initialization with correct starting balance
- Session-account relationship (X-Session-Id header)
- Basic authentication flow

---

### Workflow 2: Execute Trade

**Purpose:** Validate the core trading loop - getting market data, placing orders, advancing time, and verifying fills.

| Step | Request | Assertions | Rationale |
|------|---------|------------|-----------|
| 2.1 | Get Quote | 200 OK, bid price > 0 | Validates market data is available for the symbol at the current simulation time. Quote data drives trading decisions. |
| 2.2 | Place Market Order | 201 Created, status = "accepted" or "filled" | Market orders should be accepted immediately. If bar data exists, they fill instantly at the bar's open price. |
| 2.3 | Advance Time | 200 OK, currentTime > previousTime | Time advancement triggers the matching engine to process pending orders against historical bar data. |
| 2.4 | Check Position | 200 OK, position exists with qty = 10 | Confirms the order was filled and a position was created with correct quantity and entry price. |

**What This Tests:**
- Market data retrieval at simulation time
- Order submission and validation
- Matching engine execution (market orders fill at bar open)
- Position creation and tracking
- Time advancement mechanics

**Expected Behavior:**
- Market orders fill immediately when bar data is available
- Fill price = bar open price + slippage (10% of bar range)
- Position reflects the filled quantity and average entry price

---

### Workflow 3: Limit Order Flow

**Purpose:** Validate conditional order types that only fill when price conditions are met.

| Step | Request | Assertions | Rationale |
|------|---------|------------|-----------|
| 3.1 | Get Current Price | 200 OK (or 404 if no data) | Establishes baseline price for setting limit. 404 is acceptable if symbol data not loaded. |
| 3.2 | Place Limit Order | 201 Created, status = "accepted", type = "limit" | Limit orders remain pending until the bar's price range touches the limit price. |
| 3.3 | Advance Time (30 min) | 200 OK, time advanced by 30 minutes | Extended time advancement gives the limit order multiple bars to potentially fill. |
| 3.4 | Check Order Status | 200 OK, status logged | Order may be "accepted" (still pending), "filled", or "expired" depending on price action. |

**What This Tests:**
- Limit order acceptance and persistence
- Conditional fill logic (bar low ≤ limit price for buys)
- Order state transitions over time
- Day order expiration at market close

**Expected Behavior:**
- Limit orders remain "accepted" until fill conditions are met
- Buy limit fills when bar low ≤ limit price
- Sell limit fills when bar high ≥ limit price
- Day orders expire at 16:00 ET if unfilled

---

### Workflow 4: End-of-Day Summary

**Purpose:** Validate portfolio reporting and trade history retrieval.

| Step | Request | Assertions | Rationale |
|------|---------|------------|-----------|
| 4.1 | List All Positions | 200 OK, array of positions | Confirms all open positions are tracked with current market values and P&L. |
| 4.2 | Get Account Balance | 200 OK, cash/equity/buying_power returned | Validates account reflects trades and position values. Essential for strategy performance tracking. |
| 4.3 | List Filled Orders | 200 OK, array of filled orders | Trade history is critical for performance analysis and audit trail. |

**What This Tests:**
- Position aggregation and P&L calculation
- Account equity and buying power tracking
- Order history filtering by status
- Data consistency between positions, orders, and account

**Expected Behavior:**
- Positions show unrealized P&L based on current bar price
- Account equity = cash + long_market_value - short_market_value
- Filled orders include fill price and fill timestamp

---

## Test Data Requirements

### Minimum Data for Full Test Suite

| Symbol | Date Range | Resolution | Purpose |
|--------|------------|------------|---------|
| AAPL | 2023-01-01 to 2023-01-31 | minute | Workflow 1, 2, 4 (core trading) |
| MSFT | 2023-01-01 to 2023-01-31 | minute | Workflow 3 (limit orders) - optional |

### Loading Test Data

```bash
# Required: AAPL minute bars for January 2023
dotnet run --project src/AlpacaMock.DataIngestion -- load-bars \
  -c "Host=localhost;Database=alpacamock;Username=postgres;Password=postgres" \
  -k "YOUR_POLYGON_API_KEY" \
  -s AAPL \
  --from 2023-01-01 \
  --to 2023-01-31 \
  -r minute

# Optional: MSFT for limit order tests
dotnet run --project src/AlpacaMock.DataIngestion -- load-bars \
  -c "Host=localhost;Database=alpacamock;Username=postgres;Password=postgres" \
  -k "YOUR_POLYGON_API_KEY" \
  -s MSFT \
  --from 2023-01-01 \
  --to 2023-01-31 \
  -r minute
```

---

## Assertion Patterns

### Response Status Assertions

```javascript
pm.test('Resource created', function() {
    pm.response.to.have.status(201);
});
```

### Data Extraction and Chaining

```javascript
// Save IDs for subsequent requests
if (pm.response.code === 201) {
    var jsonData = pm.response.json();
    pm.environment.set('sessionId', jsonData.id);
}
```

### Flexible Status Validation

```javascript
// Accept multiple valid states
pm.test('Order accepted or filled', function() {
    pm.expect(['accepted', 'filled']).to.include(jsonData.status);
});
```

### Numeric Assertions

```javascript
pm.test('Quote received', function() {
    var jsonData = pm.response.json();
    pm.expect(jsonData.quote.bp).to.be.above(0);
});
```

---

## Environment Variables

Variables are automatically populated during test execution:

| Variable | Set By | Used By |
|----------|--------|---------|
| `baseUrl` | Environment file | All requests |
| `apiKey` | Environment file | Auth header |
| `apiSecret` | Environment file | Auth header |
| `sessionId` | Create Session test | All subsequent requests |
| `accountId` | Create Account test | Trading requests |
| `orderId` | Place Order tests | Order status checks |

---

## Troubleshooting

### 403 Forbidden

- Check `baseUrl` matches running API port (5050 for Docker Compose)
- Verify `apiKey` and `apiSecret` match Docker Compose environment

### 404 No Quote Data

- Symbol data not loaded for the simulation time period
- Load data using the DataIngestion CLI

### Order Not Filling

- Check if bar data exists for the symbol and time range
- Limit orders only fill when price conditions are met
- Advance time to process pending orders

### Stale Session/Account IDs

- Re-run "1. Setup Backtest" workflow first
- Environment variables persist between runs

---

## CI/CD Integration

### GitHub Actions Example

```yaml
functional-tests:
  runs-on: ubuntu-latest
  services:
    postgres:
      image: timescale/timescaledb:latest-pg15
      env:
        POSTGRES_PASSWORD: postgres
        POSTGRES_DB: alpacamock
      ports:
        - 5432:5432
  steps:
    - uses: actions/checkout@v4

    - name: Setup .NET
      uses: actions/setup-dotnet@v4
      with:
        dotnet-version: '10.0.x'

    - name: Build and start API
      run: |
        dotnet build
        dotnet run --project src/AlpacaMock.Api &
        sleep 10
      env:
        POSTGRES_CONNECTION_STRING: "Host=localhost;Database=alpacamock;..."
        USE_INMEMORY_COSMOS: true

    - name: Install Newman
      run: npm install -g newman newman-reporter-htmlextra

    - name: Run functional tests
      run: |
        newman run postman/AlpacaMock.postman_collection.json \
          -e postman/Local.postman_environment.json \
          --folder "Workflows" \
          --reporters cli,htmlextra \
          --reporter-htmlextra-export reports/functional-tests.html

    - name: Upload test report
      uses: actions/upload-artifact@v4
      with:
        name: functional-test-report
        path: reports/functional-tests.html
```

---

## Extending Tests

### Adding New Test Cases

1. Create request in appropriate folder in Postman
2. Add test script with assertions
3. Use environment variables for dynamic data
4. Export updated collection to `postman/AlpacaMock.postman_collection.json`

### Test Naming Convention

```
{Workflow Number}.{Step Number} {Action Description}
```

Examples:
- `1.1 Create Session`
- `2.2 Place Market Order`
- `3.4 Check Order Status`

---

## Coverage Matrix

| Feature | Workflow | Covered |
|---------|----------|---------|
| Session lifecycle | 1 | ✅ |
| Account creation | 1 | ✅ |
| Market orders | 2 | ✅ |
| Limit orders | 3 | ✅ |
| Time advancement | 2, 3 | ✅ |
| Quote retrieval | 2, 3 | ✅ |
| Position tracking | 2, 4 | ✅ |
| Order history | 4 | ✅ |
| Account balance | 4 | ✅ |
| Stop orders | - | ❌ (TODO) |
| Stop-limit orders | - | ❌ (TODO) |
| Order cancellation | - | ❌ (TODO) |
| PDT rules | - | ❌ (TODO) |
| Extended hours | - | ❌ (TODO) |

---

## See Also

- [API Reference](../api/README.md) - Complete endpoint documentation
- [Getting Started](../guides/getting-started.md) - Initial setup guide
- [Data Ingestion](../guides/data-ingestion.md) - Loading historical data
