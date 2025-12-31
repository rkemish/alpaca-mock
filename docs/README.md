# AlpacaMock Documentation

Welcome to the AlpacaMock documentation. This platform provides an Alpaca-compatible backtesting API powered by Polygon historical data.

## Getting Started

New to AlpacaMock? Start here:

1. [Getting Started Guide](guides/getting-started.md) - Set up locally and run your first backtest
2. [Architecture Overview](architecture/overview.md) - Understand the system design
3. [API Reference](api/README.md) - Explore the available endpoints

## Documentation Structure

```
docs/
├── guides/                    # Step-by-step tutorials
│   ├── getting-started.md     # Local setup and first backtest
│   ├── data-ingestion.md      # Loading Polygon data
│   └── deployment.md          # Azure deployment
├── architecture/              # System design
│   ├── overview.md            # Architecture diagram and data flow
│   └── components.md          # Component deep-dive
├── api/                       # API reference
│   ├── README.md              # Endpoint index
│   ├── sessions.md            # Session management
│   ├── accounts.md            # Account operations
│   ├── trading.md             # Orders and positions
│   └── market-data.md         # Assets and bars
└── testing/                   # Test documentation
    └── functional-tests.md    # Postman/Newman functional tests
```

## Quick Links

### Guides

| Guide | Description |
|-------|-------------|
| [Getting Started](guides/getting-started.md) | Set up locally and run your first backtest |
| [Data Ingestion](guides/data-ingestion.md) | Load historical market data from Polygon |
| [Deployment](guides/deployment.md) | Deploy to Azure |

### Architecture

| Document | Description |
|----------|-------------|
| [Overview](architecture/overview.md) | System architecture and data flow |
| [Components](architecture/components.md) | Detailed component documentation |

### API Reference

| Endpoint Group | Description |
|----------------|-------------|
| [Sessions](api/sessions.md) | Backtest session management |
| [Accounts](api/accounts.md) | Trading account operations |
| [Trading](api/trading.md) | Orders and positions |
| [Market Data](api/market-data.md) | Assets and historical bars |

### Testing

| Document | Description |
|----------|-------------|
| [Functional Tests](testing/functional-tests.md) | Postman/Newman API testing guide |

## Key Concepts

### Sessions
A session represents an isolated backtest environment with its own simulation time. Sessions contain accounts, which contain orders and positions.

### Simulation Time
Unlike live trading, backtesting uses simulated time that you control. Advance time step-by-step or run accelerated playback.

### Order Matching
Orders fill against real Polygon OHLCV data:
- Market orders fill at the next bar's open
- Limit orders fill when price touches the limit
- Stop orders trigger based on bar high/low

### Session Isolation
Multiple sessions run concurrently without interference. Each session has its own accounts, orders, and positions.

## Need Help?

- Check the [API Reference](api/README.md) for endpoint details
- Review [Architecture](architecture/overview.md) to understand data flow
- See [Troubleshooting](guides/getting-started.md#troubleshooting) for common issues
