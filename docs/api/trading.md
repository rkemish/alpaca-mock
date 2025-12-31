# Trading API

The trading API provides Alpaca-compatible endpoints for order management and position tracking.

> **Note**: All trading endpoints require the `X-Session-Id` header.

---

## Create Order

Places a new order.

```
POST /v1/trading/accounts/{accountId}/orders
```

### Request Body

```json
{
  "symbol": "AAPL",
  "side": "buy",
  "qty": 10,
  "type": "limit",
  "time_in_force": "day",
  "limit_price": 150.00
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `symbol` | string | Yes | Stock symbol (e.g., "AAPL") |
| `side` | string | Yes | "buy" or "sell" |
| `qty` | decimal | No* | Number of shares |
| `notional` | decimal | No* | Dollar amount to trade |
| `type` | string | No | "market", "limit", "stop", "stop_limit" (default: "market") |
| `time_in_force` | string | No | "day", "gtc", "ioc", "fok" (default: "day") |
| `limit_price` | decimal | No | Required for limit orders |
| `stop_price` | decimal | No | Required for stop orders |
| `extended_hours` | bool | No | Allow extended hours trading |
| `client_order_id` | string | No | Your unique order ID |

*Either `qty` or `notional` is required.

### Response

```json
{
  "id": "order-123",
  "client_order_id": "my-order-1",
  "symbol": "AAPL",
  "asset_class": "us_equity",
  "qty": "10",
  "filled_qty": "0",
  "filled_avg_price": null,
  "type": "limit",
  "side": "buy",
  "time_in_force": "day",
  "limit_price": "150.0000",
  "stop_price": null,
  "status": "accepted",
  "extended_hours": false,
  "submitted_at": "2023-01-03T09:30:00Z",
  "filled_at": null
}
```

---

## Order Types

### Market Order

Executes immediately at current market price.

```json
{
  "symbol": "AAPL",
  "side": "buy",
  "qty": 10,
  "type": "market"
}
```

**Fill Behavior**: Fills at the next bar's open price.

### Limit Order

Executes only at specified price or better.

```json
{
  "symbol": "AAPL",
  "side": "buy",
  "qty": 10,
  "type": "limit",
  "limit_price": 150.00
}
```

**Fill Behavior**:
- Buy: Fills when bar's low ≤ limit price
- Sell: Fills when bar's high ≥ limit price

### Stop Order

Triggers a market order when stop price is reached.

```json
{
  "symbol": "AAPL",
  "side": "sell",
  "qty": 10,
  "type": "stop",
  "stop_price": 145.00
}
```

**Trigger Behavior**:
- Buy stop: Triggers when bar's high ≥ stop price
- Sell stop: Triggers when bar's low ≤ stop price

### Stop-Limit Order

Triggers a limit order when stop price is reached.

```json
{
  "symbol": "AAPL",
  "side": "sell",
  "qty": 10,
  "type": "stop_limit",
  "stop_price": 145.00,
  "limit_price": 144.50
}
```

---

## Notional Orders

Trade by dollar amount instead of share quantity:

```json
{
  "symbol": "AAPL",
  "side": "buy",
  "notional": 1000,
  "type": "market"
}
```

The system calculates shares: `qty = notional / current_price`

---

## List Orders

Returns orders for an account.

```
GET /v1/trading/accounts/{accountId}/orders
```

### Query Parameters

| Parameter | Type | Description |
|-----------|------|-------------|
| `status` | string | Filter by status (e.g., "filled", "open") |
| `limit` | int | Max results (default: 100) |

---

## Get Order

Returns a specific order.

```
GET /v1/trading/accounts/{accountId}/orders/{orderId}
```

---

## Cancel Order

Cancels a pending order.

```
DELETE /v1/trading/accounts/{accountId}/orders/{orderId}
```

Only orders with status `new`, `accepted`, or `partially_filled` can be cancelled.

---

## Order Statuses

| Status | Description |
|--------|-------------|
| `new` | Order received, not yet processed |
| `accepted` | Order accepted by the system |
| `partially_filled` | Some shares filled |
| `filled` | All shares filled |
| `cancelled` | Order cancelled |
| `expired` | Day order expired at end of session |
| `rejected` | Order rejected |

---

## List Positions

Returns all open positions.

```
GET /v1/trading/accounts/{accountId}/positions
```

### Response

```json
[
  {
    "symbol": "AAPL",
    "exchange": "NASDAQ",
    "asset_class": "us_equity",
    "avg_entry_price": "150.2500",
    "qty": "10",
    "side": "long",
    "market_value": "1520.00",
    "cost_basis": "1502.50",
    "unrealized_pl": "17.50",
    "unrealized_plpc": "0.0117",
    "current_price": "152.0000",
    "lastday_price": "151.0000",
    "change_today": "0.0066"
  }
]
```

---

## Get Position

Returns position for a specific symbol.

```
GET /v1/trading/accounts/{accountId}/positions/{symbol}
```

---

## Close Position

Closes a position by creating a market sell/buy order.

```
DELETE /v1/trading/accounts/{accountId}/positions/{symbol}
```

---

## Example: Buy and Sell Flow

```bash
SESSION_ID="your-session-id"
ACCOUNT_ID="your-account-id"
AUTH="Authorization: Basic dGVzdC1hcGkta2V5OnRlc3QtYXBpLXNlY3JldA=="

# 1. Buy 10 shares of AAPL
curl -X POST "http://localhost:5000/v1/trading/accounts/$ACCOUNT_ID/orders" \
  -H "$AUTH" \
  -H "X-Session-Id: $SESSION_ID" \
  -H "Content-Type: application/json" \
  -d '{"symbol": "AAPL", "side": "buy", "qty": 10, "type": "market"}'

# 2. Advance time to fill the order
curl -X POST "http://localhost:5000/v1/sessions/$SESSION_ID/time/advance" \
  -H "$AUTH" \
  -d '{"duration": 1}'

# 3. Check position
curl "http://localhost:5000/v1/trading/accounts/$ACCOUNT_ID/positions/AAPL" \
  -H "$AUTH" \
  -H "X-Session-Id: $SESSION_ID"

# 4. Set a stop-loss at $145
curl -X POST "http://localhost:5000/v1/trading/accounts/$ACCOUNT_ID/orders" \
  -H "$AUTH" \
  -H "X-Session-Id: $SESSION_ID" \
  -H "Content-Type: application/json" \
  -d '{"symbol": "AAPL", "side": "sell", "qty": 10, "type": "stop", "stop_price": 145}'
```
