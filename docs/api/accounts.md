# Accounts API

The accounts API provides Alpaca-compatible endpoints for account management.

> **Note**: All account endpoints require the `X-Session-Id` header.

---

## Create Account

Creates a new trading account within a session.

```
POST /v1/accounts
```

### Request Body

```json
{
  "initialCash": 100000,
  "contact": {
    "emailAddress": "trader@example.com",
    "city": "New York",
    "state": "NY"
  },
  "identity": {
    "givenName": "John",
    "familyName": "Doe"
  }
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `initialCash` | decimal | No | Starting balance (default: 100,000) |
| `contact` | object | No | Contact information |
| `identity` | object | No | Personal identity info |

### Response

```json
{
  "id": "account-123",
  "account_number": "A12345678",
  "status": "ACTIVE",
  "crypto_status": "ACTIVE",
  "currency": "USD",
  "cash": "100000.00",
  "portfolio_value": "100000.00",
  "buying_power": "100000.00",
  "equity": "100000.00",
  "last_equity": "0.00",
  "long_market_value": "0.00",
  "short_market_value": "0.00",
  "initial_margin": "0.00",
  "maintenance_margin": "0.00",
  "daytrade_count": 0,
  "pattern_day_trader": false,
  "trading_blocked": false,
  "transfers_blocked": false,
  "created_at": "2024-01-15T10:30:00Z"
}
```

---

## List Accounts

Returns all accounts in the current session.

```
GET /v1/accounts
```

---

## Get Account

Returns a specific account.

```
GET /v1/accounts/{accountId}
```

---

## Update Account

Updates account information.

```
PATCH /v1/accounts/{accountId}
```

### Request Body

```json
{
  "contact": {
    "emailAddress": "newemail@example.com"
  }
}
```

---

## Close Account

Closes an account (sets status to ACCOUNT_CLOSED).

```
DELETE /v1/accounts/{accountId}
```

---

## Account Fields

### Balance Fields

| Field | Description |
|-------|-------------|
| `cash` | Cash available for trading |
| `portfolio_value` | Total account value |
| `buying_power` | Available purchasing power |
| `equity` | Cash + positions value |
| `last_equity` | Previous day's equity |

### Position Values

| Field | Description |
|-------|-------------|
| `long_market_value` | Value of long positions |
| `short_market_value` | Value of short positions |
| `initial_margin` | Initial margin requirement |
| `maintenance_margin` | Maintenance margin requirement |

### Trading Status

| Field | Description |
|-------|-------------|
| `daytrade_count` | Day trades in last 5 days |
| `pattern_day_trader` | PDT flag |
| `trading_blocked` | Whether trading is blocked |
| `transfers_blocked` | Whether transfers are blocked |

---

## Account Statuses

| Status | Description |
|--------|-------------|
| `ACTIVE` | Account is active and can trade |
| `ONBOARDING` | Account setup in progress |
| `SUBMITTED` | Application submitted |
| `APPROVED` | Application approved |
| `REJECTED` | Application rejected |
| `DISABLED` | Account temporarily disabled |
| `ACCOUNT_CLOSED` | Account permanently closed |

---

## Example: Multi-Account Strategy

```bash
SESSION_ID="your-session-id"
AUTH="Authorization: Basic dGVzdC1hcGkta2V5OnRlc3QtYXBpLXNlY3JldA=="

# Create aggressive account
AGGRESSIVE=$(curl -s -X POST "http://localhost:5000/v1/accounts" \
  -H "$AUTH" \
  -H "X-Session-Id: $SESSION_ID" \
  -H "Content-Type: application/json" \
  -d '{"initialCash": 50000}' | jq -r '.id')

# Create conservative account
CONSERVATIVE=$(curl -s -X POST "http://localhost:5000/v1/accounts" \
  -H "$AUTH" \
  -H "X-Session-Id: $SESSION_ID" \
  -H "Content-Type: application/json" \
  -d '{"initialCash": 50000}' | jq -r '.id')

# Trade differently in each account
curl -X POST "http://localhost:5000/v1/trading/accounts/$AGGRESSIVE/orders" \
  -H "$AUTH" -H "X-Session-Id: $SESSION_ID" \
  -H "Content-Type: application/json" \
  -d '{"symbol": "TSLA", "side": "buy", "qty": 50, "type": "market"}'

curl -X POST "http://localhost:5000/v1/trading/accounts/$CONSERVATIVE/orders" \
  -H "$AUTH" -H "X-Session-Id: $SESSION_ID" \
  -H "Content-Type: application/json" \
  -d '{"symbol": "VTI", "side": "buy", "qty": 100, "type": "market"}'
```
