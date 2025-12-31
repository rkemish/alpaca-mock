# Architecture Overview

AlpacaMock is a backtesting platform that simulates the Alpaca Broker API using historical Polygon market data. This document explains the system architecture and how components interact.

## System Architecture

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                              CLIENT APPLICATIONS                             │
│                    (Trading Bots, Algorithms, Test Suites)                  │
└─────────────────────────────────────────────────────────────────────────────┘
                                       │
                                       │ HTTP/REST
                                       │ (Alpaca-Compatible API)
                                       ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                              ALPACAMOCK API                                  │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐  ┌─────────────────────┐ │
│  │   Session   │  │   Account   │  │   Trading   │  │    Market Data      │ │
│  │  Endpoints  │  │  Endpoints  │  │  Endpoints  │  │     Endpoints       │ │
│  └──────┬──────┘  └──────┬──────┘  └──────┬──────┘  └──────────┬──────────┘ │
│         │                │                │                     │           │
│  ┌──────┴────────────────┴────────────────┴─────────────────────┴────────┐  │
│  │                         MIDDLEWARE LAYER                               │  │
│  │              Basic Auth │ Session Context │ Rate Limiting              │  │
│  └────────────────────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────────────────┘
                                       │
                    ┌──────────────────┼──────────────────┐
                    │                  │                  │
                    ▼                  ▼                  ▼
┌───────────────────────┐  ┌───────────────────┐  ┌───────────────────────────┐
│    DOMAIN LAYER       │  │  INFRASTRUCTURE   │  │     INFRASTRUCTURE        │
│                       │  │   (Cosmos DB)     │  │     (PostgreSQL)          │
│  ┌─────────────────┐  │  │                   │  │                           │
│  │ SimulationClock │  │  │  ┌─────────────┐  │  │  ┌─────────────────────┐  │
│  │  (Time Control) │  │  │  │  Sessions   │  │  │  │   bars_minute       │  │
│  └─────────────────┘  │  │  ├─────────────┤  │  │  │   (TimescaleDB)     │  │
│                       │  │  │  Accounts   │  │  │  ├─────────────────────┤  │
│  ┌─────────────────┐  │  │  ├─────────────┤  │  │  │   bars_daily        │  │
│  │ MatchingEngine  │  │  │  │   Orders    │  │  │  │   (TimescaleDB)     │  │
│  │ (Order Fills)   │  │  │  ├─────────────┤  │  │  ├─────────────────────┤  │
│  └─────────────────┘  │  │  │  Positions  │  │  │  │   symbols           │  │
│                       │  │  └─────────────┘  │  │  └─────────────────────┘  │
└───────────────────────┘  └───────────────────┘  └───────────────────────────┘
                                                              ▲
                                                              │
                                                              │ Data Ingestion
                                                              │
                                              ┌───────────────┴───────────────┐
                                              │      POLYGON.IO API           │
                                              │   (Historical Bar Data)       │
                                              └───────────────────────────────┘
```

## Data Flow

### 1. Session Creation

```
Client                    API                     Cosmos DB
  │                        │                          │
  │  POST /v1/sessions     │                          │
  │───────────────────────>│                          │
  │                        │  Create Session Doc      │
  │                        │─────────────────────────>│
  │                        │                          │
  │  { session_id, ... }   │                          │
  │<───────────────────────│                          │
```

### 2. Order Execution

```
Client                API              MatchingEngine          PostgreSQL
  │                    │                     │                      │
  │  POST /orders      │                     │                      │
  │───────────────────>│                     │                      │
  │                    │  Get current bar    │                      │
  │                    │─────────────────────────────────────────-->│
  │                    │                     │      Bar data        │
  │                    │<───────────────────────────────────────────│
  │                    │  TryFill(order,bar) │                      │
  │                    │────────────────────>│                      │
  │                    │     FillResult      │                      │
  │                    │<────────────────────│                      │
  │  { order_id, ... } │                     │                      │
  │<───────────────────│                     │                      │
```

### 3. Time Advancement

```
Client                API              SimulationClock        MatchingEngine
  │                    │                     │                      │
  │  POST /time/advance│                     │                      │
  │───────────────────>│                     │                      │
  │                    │  AdvanceBy(1 min)   │                      │
  │                    │────────────────────>│                      │
  │                    │  TimeAdvanceResult  │                      │
  │                    │<────────────────────│                      │
  │                    │  Process pending orders                    │
  │                    │───────────────────────────────────────────>│
  │                    │                     │      Fill results    │
  │                    │<───────────────────────────────────────────│
  │  { new_time, ... } │                     │                      │
  │<───────────────────│                     │                      │
```

## Component Responsibilities

| Component | Responsibility |
|-----------|----------------|
| **API Layer** | HTTP routing, request/response mapping, authentication |
| **SimulationClock** | Manages per-session simulation time, playback modes |
| **MatchingEngine** | Executes orders against OHLCV bar data |
| **SessionRepository** | CRUD operations for session state in Cosmos DB |
| **BarRepository** | Time-series queries against TimescaleDB |
| **PolygonClient** | Fetches historical data from Polygon.io |

## Database Design

### Cosmos DB (Session State)
- **Partition Strategy**: Session ID as partition key
- **Purpose**: Low-latency access to trading state
- **Collections**: sessions, accounts, orders, positions

### PostgreSQL + TimescaleDB (Market Data)
- **Partition Strategy**: Time-based hypertables (monthly chunks)
- **Purpose**: Efficient time-range queries on bar data
- **Tables**: bars_minute, bars_daily, symbols

## Scalability

```
                    ┌─────────────────────┐
                    │   Load Balancer     │
                    └──────────┬──────────┘
                               │
         ┌─────────────────────┼─────────────────────┐
         │                     │                     │
         ▼                     ▼                     ▼
┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐
│  API Instance   │  │  API Instance   │  │  API Instance   │
│   (Stateless)   │  │   (Stateless)   │  │   (Stateless)   │
└────────┬────────┘  └────────┬────────┘  └────────┬────────┘
         │                     │                     │
         └─────────────────────┼─────────────────────┘
                               │
         ┌─────────────────────┴─────────────────────┐
         │                                           │
         ▼                                           ▼
┌─────────────────────────┐            ┌─────────────────────────┐
│      Cosmos DB          │            │   PostgreSQL/Timescale  │
│   (Auto-scaling RUs)    │            │    (Read replicas)      │
└─────────────────────────┘            └─────────────────────────┘
```

- **API Layer**: Stateless, horizontally scalable
- **Cosmos DB**: Serverless with automatic scaling
- **PostgreSQL**: Read replicas for query distribution

## Next Steps

- [Components Deep Dive](components.md) - Detailed component documentation
- [API Reference](../api/README.md) - Complete endpoint documentation
- [Deployment Guide](../guides/deployment.md) - Azure deployment instructions
