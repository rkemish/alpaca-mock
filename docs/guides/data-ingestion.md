# Data Ingestion Guide

This guide covers loading historical market data from Polygon.io into AlpacaMock.

## Overview

AlpacaMock requires historical OHLCV bar data to simulate realistic order fills. Data is stored in PostgreSQL with TimescaleDB for efficient time-series queries.

```
┌──────────────────┐         ┌──────────────────┐         ┌──────────────────┐
│   Polygon.io     │ ──API── │  DataIngestion   │ ──SQL── │   PostgreSQL     │
│                  │         │      CLI         │         │  + TimescaleDB   │
│  • REST API      │         │                  │         │                  │
│  • Flat Files    │         │  • load-symbols  │         │  • bars_minute   │
│    (S3/Parquet)  │         │  • load-bars     │         │  • bars_daily    │
└──────────────────┘         └──────────────────┘         └──────────────────┘
```

---

## Prerequisites

- PostgreSQL with TimescaleDB extension
- [Polygon.io subscription](https://polygon.io/pricing) (Advanced recommended)
- .NET 8 SDK

---

## Polygon Subscription Tiers

| Tier | API Rate Limit | Flat Files | Best For |
|------|----------------|------------|----------|
| Basic | 5 calls/min | No | Testing only |
| Starter | Unlimited | No | Small datasets |
| Developer | Unlimited | No | Moderate datasets |
| **Advanced** | Unlimited | **Yes (S3)** | **Production** |

**Recommendation**: Advanced tier provides S3 flat file access, which is significantly faster for bulk historical data loading.

---

## CLI Commands

The data ingestion tool provides four commands:

| Command | Description |
|---------|-------------|
| `init-db` | Initialize database schema |
| `load-symbols` | Load symbol catalog from Polygon |
| `load-bars` | Load OHLCV bar data for a symbol |
| `stats` | Show data coverage statistics |

---

## 1. Initialize Database

Create the TimescaleDB schema:

```bash
dotnet run --project src/AlpacaMock.DataIngestion -- init-db \
  -c "Host=localhost;Database=alpaca_mock;Username=postgres;Password=postgres"
```

This creates:

| Table | Type | Description |
|-------|------|-------------|
| `bars_minute` | Hypertable | Minute OHLCV bars, partitioned monthly |
| `bars_daily` | Hypertable | Daily OHLCV bars, partitioned yearly |
| `symbols` | Regular | Symbol metadata (name, exchange, status) |
| `data_coverage` | Regular | Tracks which date ranges are loaded |

---

## 2. Load Symbol Catalog

Load available symbols from Polygon:

```bash
dotnet run --project src/AlpacaMock.DataIngestion -- load-symbols \
  -c "Host=localhost;Database=alpaca_mock;Username=postgres;Password=postgres" \
  -k "your-polygon-api-key"
```

This fetches all US stock symbols and stores metadata like:
- Symbol ticker (e.g., AAPL)
- Company name
- Exchange (NASDAQ, NYSE, etc.)
- Active/inactive status

---

## 3. Load Bar Data

### Load minute bars for a single symbol

```bash
dotnet run --project src/AlpacaMock.DataIngestion -- load-bars \
  -c "Host=localhost;..." \
  -k "your-polygon-api-key" \
  -s AAPL \
  --from 2023-01-01 \
  --to 2024-01-01 \
  -r minute
```

### Load daily bars

```bash
dotnet run --project src/AlpacaMock.DataIngestion -- load-bars \
  -c "Host=localhost;..." \
  -k "your-polygon-api-key" \
  -s AAPL \
  --from 2000-01-01 \
  --to 2024-01-01 \
  -r daily
```

### Parameters

| Parameter | Required | Description |
|-----------|----------|-------------|
| `-c`, `--connection` | Yes | PostgreSQL connection string |
| `-k`, `--api-key` | Yes | Polygon API key |
| `-s`, `--symbol` | Yes | Stock symbol to load |
| `--from` | Yes | Start date (YYYY-MM-DD) |
| `--to` | Yes | End date (YYYY-MM-DD) |
| `-r`, `--resolution` | No | `minute` (default) or `daily` |

---

## 4. Bulk Loading Strategies

### Load popular symbols

```bash
#!/bin/bash
CONN="Host=localhost;Database=alpaca_mock;Username=postgres;Password=postgres"
KEY="your-polygon-api-key"

SYMBOLS=(
  AAPL MSFT GOOGL AMZN META
  TSLA NVDA JPM V JNJ
  WMT PG UNH HD MA
  DIS PYPL NFLX ADBE CRM
)

for SYMBOL in "${SYMBOLS[@]}"; do
  echo "Loading $SYMBOL..."
  dotnet run --project src/AlpacaMock.DataIngestion -- load-bars \
    -c "$CONN" -k "$KEY" -s "$SYMBOL" \
    --from 2020-01-01 --to 2024-01-01 -r minute
done
```

### Load all active symbols

```bash
#!/bin/bash
CONN="Host=localhost;..."
KEY="your-polygon-api-key"

# Get active symbols from database
SYMBOLS=$(psql "$CONN" -t -c "SELECT symbol FROM symbols WHERE status = 'active'")

for SYMBOL in $SYMBOLS; do
  dotnet run --project src/AlpacaMock.DataIngestion -- load-bars \
    -c "$CONN" -k "$KEY" -s "$SYMBOL" \
    --from 2020-01-01 --to 2024-01-01 -r minute
done
```

### Parallel loading

```bash
#!/bin/bash
# Load multiple symbols in parallel (be mindful of API rate limits)

echo "AAPL MSFT GOOGL AMZN" | xargs -P 4 -n 1 bash -c '
  dotnet run --project src/AlpacaMock.DataIngestion -- load-bars \
    -c "..." -k "..." -s "$0" \
    --from 2020-01-01 --to 2024-01-01 -r minute
'
```

---

## 5. Check Data Coverage

View statistics on loaded data:

```bash
dotnet run --project src/AlpacaMock.DataIngestion -- stats \
  -c "Host=localhost;Database=alpaca_mock;Username=postgres;Password=postgres"
```

Output example:
```
Data Coverage Statistics
========================

Symbols loaded: 2,847
Total minute bars: 145,234,567
Total daily bars: 1,234,567

Top 10 symbols by minute bars:
  AAPL:  2,453,000 bars (2000-01-03 to 2024-01-01)
  MSFT:  2,451,000 bars (2000-01-03 to 2024-01-01)
  GOOGL: 1,234,000 bars (2004-08-19 to 2024-01-01)
  ...

Storage:
  bars_minute: 45.2 GB
  bars_daily:  234 MB
```

### Query coverage directly

```sql
-- Check coverage for a specific symbol
SELECT symbol, resolution, min_date, max_date, bar_count
FROM data_coverage
WHERE symbol = 'AAPL';

-- Find symbols with gaps
SELECT symbol, resolution, max_date
FROM data_coverage
WHERE max_date < CURRENT_DATE - INTERVAL '7 days'
ORDER BY max_date;
```

---

## 6. Data Storage Estimates

### Minute bars

| Timeframe | Bars per Symbol | Storage (compressed) |
|-----------|-----------------|---------------------|
| 1 year | ~98,000 | ~5 MB |
| 5 years | ~490,000 | ~25 MB |
| 10 years | ~980,000 | ~50 MB |
| 20 years | ~1,960,000 | ~100 MB |

### Total estimates

| Dataset | Symbols | Bars | Storage |
|---------|---------|------|---------|
| Top 100 stocks, 5 years | 100 | 49M | 2.5 GB |
| S&P 500, 10 years | 500 | 490M | 25 GB |
| All US stocks, 20 years | 10,000 | 19.6B | 500 GB - 1 TB |

---

## 7. TimescaleDB Optimization

### Enable compression (for old data)

```sql
-- Enable compression on minute bars older than 1 year
ALTER TABLE bars_minute SET (
  timescaledb.compress,
  timescaledb.compress_segmentby = 'symbol'
);

SELECT add_compression_policy('bars_minute', INTERVAL '1 year');
```

### Check compression status

```sql
SELECT
  hypertable_name,
  total_chunks,
  number_compressed_chunks,
  pg_size_pretty(before_compression_total_bytes) as before,
  pg_size_pretty(after_compression_total_bytes) as after
FROM timescaledb_information.compression_settings;
```

### Create continuous aggregates (optional)

Pre-compute hourly and daily aggregates from minute data:

```sql
CREATE MATERIALIZED VIEW bars_hourly
WITH (timescaledb.continuous) AS
SELECT
  time_bucket('1 hour', time) AS time,
  symbol,
  first(open, time) AS open,
  max(high) AS high,
  min(low) AS low,
  last(close, time) AS close,
  sum(volume) AS volume
FROM bars_minute
GROUP BY time_bucket('1 hour', time), symbol
WITH NO DATA;

SELECT add_continuous_aggregate_policy('bars_hourly',
  start_offset => INTERVAL '1 month',
  end_offset => INTERVAL '1 hour',
  schedule_interval => INTERVAL '1 hour');
```

---

## 8. Using Polygon Flat Files

For Advanced tier subscribers, flat files provide the fastest bulk loading.

### Download flat files

```bash
# Polygon provides S3 access
aws s3 sync s3://flatfiles/us_stocks_sip/minute_aggs_v1/2023/ ./polygon-data/2023/
```

### Load from Parquet files

```bash
dotnet run --project src/AlpacaMock.DataIngestion -- load-parquet \
  -c "Host=localhost;..." \
  --path ./polygon-data/2023/ \
  --resolution minute
```

### Flat file structure

```
us_stocks_sip/
├── minute_aggs_v1/
│   ├── 2023/
│   │   ├── 01/
│   │   │   ├── 2023-01-03.csv.gz
│   │   │   ├── 2023-01-04.csv.gz
│   │   │   └── ...
│   │   └── ...
│   └── ...
└── day_aggs_v1/
    └── ...
```

---

## 9. Troubleshooting

### "Rate limit exceeded"

With non-Advanced tiers, you may hit API limits. Add delays:

```bash
for SYMBOL in "${SYMBOLS[@]}"; do
  dotnet run --project src/AlpacaMock.DataIngestion -- load-bars ...
  sleep 60  # Wait 1 minute between symbols
done
```

### "No data found for date range"

- The symbol may not have been traded on those dates
- Check if the symbol was active: `GET https://api.polygon.io/v3/reference/tickers/{symbol}`
- For IPOs, data starts from the IPO date

### "Connection timeout" during bulk insert

Increase PostgreSQL timeouts:

```sql
SET statement_timeout = '30min';
SET lock_timeout = '10min';
```

Or use smaller batches:

```bash
# Load one year at a time
for YEAR in 2020 2021 2022 2023; do
  dotnet run --project src/AlpacaMock.DataIngestion -- load-bars \
    -c "..." -k "..." -s AAPL \
    --from $YEAR-01-01 --to $YEAR-12-31 -r minute
done
```

### "Duplicate key violation"

The ingestion tool uses UPSERT, but if you encounter issues:

```sql
-- Check for duplicates
SELECT symbol, time, COUNT(*)
FROM bars_minute
GROUP BY symbol, time
HAVING COUNT(*) > 1;

-- Remove duplicates (keep first)
DELETE FROM bars_minute a USING bars_minute b
WHERE a.ctid > b.ctid
  AND a.symbol = b.symbol
  AND a.time = b.time;
```

---

## 10. Maintenance

### Update data daily

```bash
#!/bin/bash
# cron job: 0 6 * * * /path/to/update-bars.sh

CONN="Host=localhost;..."
KEY="your-polygon-api-key"
YESTERDAY=$(date -d "yesterday" +%Y-%m-%d)
TODAY=$(date +%Y-%m-%d)

# Get active symbols
SYMBOLS=$(psql "$CONN" -t -c "SELECT DISTINCT symbol FROM data_coverage")

for SYMBOL in $SYMBOLS; do
  dotnet run --project src/AlpacaMock.DataIngestion -- load-bars \
    -c "$CONN" -k "$KEY" -s "$SYMBOL" \
    --from $YESTERDAY --to $TODAY -r minute
done
```

### Detect and fill gaps

```sql
-- Find symbols with data gaps
WITH date_range AS (
  SELECT generate_series(
    (SELECT MIN(time) FROM bars_minute),
    (SELECT MAX(time) FROM bars_minute),
    '1 day'::interval
  )::date AS trading_day
),
expected_dates AS (
  SELECT trading_day
  FROM date_range
  WHERE EXTRACT(DOW FROM trading_day) NOT IN (0, 6)  -- Exclude weekends
)
SELECT
  dc.symbol,
  ed.trading_day AS missing_date
FROM data_coverage dc
CROSS JOIN expected_dates ed
LEFT JOIN bars_minute bm ON bm.symbol = dc.symbol
  AND bm.time::date = ed.trading_day
WHERE ed.trading_day BETWEEN dc.min_date AND dc.max_date
  AND bm.time IS NULL
ORDER BY dc.symbol, ed.trading_day;
```

---

## Next Steps

- [Getting Started](getting-started.md) - Run your first backtest
- [Deployment Guide](deployment.md) - Deploy to Azure
- [Market Data API](../api/market-data.md) - Query loaded data via API
