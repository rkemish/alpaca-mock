# Market Data API

The market data API provides access to asset information and historical price data from Polygon.

---

## List Assets

Returns all available tradeable assets.

```
GET /v1/assets
```

### Query Parameters

| Parameter | Type | Description |
|-----------|------|-------------|
| `status` | string | Filter by status ("active", "inactive") |
| `asset_class` | string | Filter by class ("us_equity", "crypto") |

### Response

```json
[
  {
    "id": "asset-uuid",
    "class": "us_equity",
    "exchange": "NASDAQ",
    "symbol": "AAPL",
    "name": "AAPL",
    "status": "active",
    "tradable": true,
    "marginable": true,
    "shortable": true,
    "easy_to_borrow": true,
    "fractionable": true
  }
]
```

---

## Get Asset

Returns details for a specific asset.

```
GET /v1/assets/{symbolOrId}
```

### Response

```json
{
  "id": "asset-uuid",
  "class": "us_equity",
  "exchange": "NASDAQ",
  "symbol": "AAPL",
  "name": "Apple Inc.",
  "status": "active",
  "tradable": true,
  "marginable": true,
  "shortable": true,
  "easy_to_borrow": true,
  "fractionable": true
}
```

---

## Get Historical Bars

Returns OHLCV bar data for a symbol.

```
GET /v1/assets/{symbol}/bars
```

### Query Parameters

| Parameter | Type | Description |
|-----------|------|-------------|
| `timeframe` | string | Bar size: "1Min", "1Hour", "1Day" (default: "1Min") |
| `start` | datetime | Start time (ISO 8601) |
| `end` | datetime | End time (ISO 8601) |
| `limit` | int | Max bars to return (default: 1000) |

> **Note**: When `X-Session-Id` is provided, times are relative to the session's current simulation time.

### Response

```json
{
  "bars": [
    {
      "t": "2023-01-03T09:30:00Z",
      "o": 130.28,
      "h": 130.90,
      "l": 129.89,
      "c": 130.50,
      "v": 1234567,
      "vw": 130.45,
      "n": 5432
    }
  ],
  "symbol": "AAPL",
  "next_page_token": null
}
```

### Bar Fields

| Field | Description |
|-------|-------------|
| `t` | Timestamp |
| `o` | Open price |
| `h` | High price |
| `l` | Low price |
| `c` | Close price |
| `v` | Volume |
| `vw` | Volume-weighted average price |
| `n` | Number of trades |

---

## Get Latest Quote

Returns the latest quote for a symbol.

```
GET /v1/assets/{symbol}/quotes/latest
```

> **Note**: Quotes are synthesized from bar data. In simulation mode, the quote reflects the bar at the session's current time.

### Response

```json
{
  "symbol": "AAPL",
  "quote": {
    "t": "2023-01-03T09:30:00Z",
    "ax": "Q",
    "ap": 130.51,
    "as": 100,
    "bx": "Q",
    "bp": 130.49,
    "bs": 100,
    "c": ["R"],
    "z": "C"
  }
}
```

### Quote Fields

| Field | Description |
|-------|-------------|
| `t` | Timestamp |
| `ax` | Ask exchange |
| `ap` | Ask price |
| `as` | Ask size |
| `bx` | Bid exchange |
| `bp` | Bid price |
| `bs` | Bid size |
| `c` | Condition flags |
| `z` | Tape |

---

## Timeframes

| Timeframe | Description |
|-----------|-------------|
| `1Min` | 1-minute bars |
| `5Min` | 5-minute bars |
| `15Min` | 15-minute bars |
| `1Hour` | Hourly bars |
| `1Day` | Daily bars |

---

## Example: Get Price History

```bash
SESSION_ID="your-session-id"
AUTH="Authorization: Basic dGVzdC1hcGkta2V5OnRlc3QtYXBpLXNlY3JldA=="

# Get last hour of minute bars (relative to simulation time)
curl "http://localhost:5000/v1/assets/AAPL/bars?timeframe=1Min&limit=60" \
  -H "$AUTH" \
  -H "X-Session-Id: $SESSION_ID"

# Get daily bars for a date range
curl "http://localhost:5000/v1/assets/AAPL/bars?timeframe=1Day&start=2023-01-01&end=2023-03-31" \
  -H "$AUTH"

# Get current quote
curl "http://localhost:5000/v1/assets/AAPL/quotes/latest" \
  -H "$AUTH" \
  -H "X-Session-Id: $SESSION_ID"
```

---

## Data Availability

Market data is sourced from Polygon.io and must be loaded using the data ingestion tool:

```bash
# Load minute bars for a symbol
dotnet run --project src/AlpacaMock.DataIngestion -- load-bars \
  -c "connection-string" \
  -k "polygon-api-key" \
  -s AAPL \
  --from 2020-01-01 \
  --to 2024-01-01 \
  -r minute

# Check what data is available
dotnet run --project src/AlpacaMock.DataIngestion -- stats \
  -c "connection-string"
```

See [Data Ingestion](../guides/data-ingestion.md) for more details.
