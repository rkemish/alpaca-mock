# API Reference

AlpacaMock provides a REST API compatible with the Alpaca Broker API, plus additional endpoints for backtest session management.

## Base URL

```
http://localhost:5000  (development)
https://your-app.azurecontainerapps.io  (production)
```

## Authentication

All endpoints (except `/health`) require Basic authentication:

```
Authorization: Basic base64(API_KEY:API_SECRET)
```

**Example**:
```bash
# Using curl
curl -H "Authorization: Basic dGVzdC1hcGkta2V5OnRlc3QtYXBpLXNlY3JldA==" \
  http://localhost:5000/v1/sessions

# Using httpie
http localhost:5000/v1/sessions -a test-api-key:test-api-secret
```

**Test Credentials**:
| API Key | API Secret | Base64 Encoded |
|---------|------------|----------------|
| `test-api-key` | `test-api-secret` | `dGVzdC1hcGkta2V5OnRlc3QtYXBpLXNlY3JldA==` |

## Session Context

Most endpoints require a session context via the `X-Session-Id` header:

```
X-Session-Id: your-session-id
```

---

## Endpoint Reference

- [Sessions](sessions.md) - Backtest session management
- [Accounts](accounts.md) - Account CRUD operations
- [Trading](trading.md) - Orders and positions
- [Market Data](market-data.md) - Assets and price data

---

## Quick Reference

### Session Endpoints

| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/v1/sessions` | Create backtest session |
| GET | `/v1/sessions` | List sessions |
| GET | `/v1/sessions/{id}` | Get session details |
| DELETE | `/v1/sessions/{id}` | Delete session |
| POST | `/v1/sessions/{id}/time/advance` | Advance simulation time |
| POST | `/v1/sessions/{id}/time/play` | Start playback |
| POST | `/v1/sessions/{id}/time/pause` | Pause playback |
| PUT | `/v1/sessions/{id}/time/speed` | Set playback speed |

### Account Endpoints

| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/v1/accounts` | Create account |
| GET | `/v1/accounts` | List accounts |
| GET | `/v1/accounts/{id}` | Get account |
| PATCH | `/v1/accounts/{id}` | Update account |
| DELETE | `/v1/accounts/{id}` | Close account |

### Trading Endpoints

| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/v1/trading/accounts/{id}/orders` | Create order |
| GET | `/v1/trading/accounts/{id}/orders` | List orders |
| GET | `/v1/trading/accounts/{id}/orders/{orderId}` | Get order |
| DELETE | `/v1/trading/accounts/{id}/orders/{orderId}` | Cancel order |
| GET | `/v1/trading/accounts/{id}/positions` | List positions |
| GET | `/v1/trading/accounts/{id}/positions/{symbol}` | Get position |

### Market Data Endpoints

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/v1/assets` | List assets |
| GET | `/v1/assets/{symbol}` | Get asset |
| GET | `/v1/assets/{symbol}/bars` | Get historical bars |
| GET | `/v1/assets/{symbol}/quotes/latest` | Get latest quote |

---

## Error Responses

All errors return JSON with a `code` and `message`:

```json
{
  "code": 40410000,
  "message": "Account not found"
}
```

### Error Codes

| Code | HTTP Status | Description |
|------|-------------|-------------|
| 40010000 | 400 | Bad request / validation error |
| 40110000 | 401 | Missing authorization |
| 40110002 | 401 | Invalid credentials |
| 40410000 | 404 | Resource not found |

---

## Rate Limiting

Development: No limits
Production: 200 requests/minute per API key

When rate limited, you'll receive:
```
HTTP/1.1 429 Too Many Requests
Retry-After: 60
```
