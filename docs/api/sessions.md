# Sessions API

Sessions represent isolated backtest environments with their own simulation time, accounts, and positions.

## Create Session

Creates a new backtest session.

```
POST /v1/sessions
```

### Request Body

```json
{
  "startTime": "2023-01-03T09:30:00Z",
  "endTime": "2023-12-29T16:00:00Z",
  "name": "Q1 2023 Backtest",
  "initialCash": 100000
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `startTime` | datetime | Yes | Start of simulation period |
| `endTime` | datetime | Yes | End of simulation period |
| `name` | string | No | Human-readable name |
| `initialCash` | decimal | No | Starting cash (default: 100,000) |

### Response

```json
{
  "id": "abc123",
  "name": "Q1 2023 Backtest",
  "status": "active",
  "simulation_start": "2023-01-03T09:30:00Z",
  "simulation_end": "2023-12-29T16:00:00Z",
  "current_time": "2023-01-03T09:30:00Z",
  "playback_state": "paused",
  "playback_speed": 1.0,
  "initial_cash": 100000,
  "total_realized_pnl": 0,
  "total_unrealized_pnl": 0,
  "created_at": "2024-01-15T10:30:00Z"
}
```

---

## List Sessions

Returns all sessions for the authenticated API key.

```
GET /v1/sessions
```

### Response

```json
[
  {
    "id": "abc123",
    "name": "Q1 2023 Backtest",
    "status": "active",
    ...
  }
]
```

---

## Get Session

Returns a specific session.

```
GET /v1/sessions/{sessionId}
```

---

## Delete Session

Deletes a session and all associated data.

```
DELETE /v1/sessions/{sessionId}
```

### Response

```
204 No Content
```

---

## Advance Time

Advances the simulation clock forward.

```
POST /v1/sessions/{sessionId}/time/advance
```

### Request Body

```json
{
  "duration": 5
}
```

**OR**

```json
{
  "targetTime": "2023-01-03T10:00:00Z"
}
```

| Field | Type | Description |
|-------|------|-------------|
| `duration` | number | Minutes to advance |
| `targetTime` | datetime | Specific time to advance to |

If neither specified, advances by 1 minute.

### Response

```json
{
  "previousTime": "2023-01-03T09:30:00Z",
  "currentTime": "2023-01-03T09:35:00Z",
  "simulationEnd": "2023-12-29T16:00:00Z",
  "isCompleted": false
}
```

---

## Start Playback

Starts continuous time advancement.

```
POST /v1/sessions/{sessionId}/time/play
```

While playing, the simulation clock advances in real-time (or accelerated if speed > 1).

---

## Pause Playback

Pauses continuous time advancement.

```
POST /v1/sessions/{sessionId}/time/pause
```

---

## Set Playback Speed

Sets the playback speed multiplier.

```
PUT /v1/sessions/{sessionId}/time/speed
```

### Request Body

```json
{
  "speed": 100
}
```

| Speed | Meaning |
|-------|---------|
| 1 | Real-time (1 minute = 1 minute) |
| 10 | 10x speed (10 minutes = 1 minute) |
| 100 | 100x speed (1 hour 40 min = 1 minute) |

---

## Session Lifecycle

```
Created (paused)
    │
    ├─── advance ───> Time moves forward, orders fill
    │
    ├─── play ─────> Continuous advancement starts
    │                    │
    │                    └─── pause ───> Back to paused
    │
    └─── delete ───> Session removed
```

## Example: Complete Backtest Flow

```bash
# 1. Create session
SESSION=$(curl -s -X POST http://localhost:5000/v1/sessions \
  -H "Authorization: Basic dGVzdC1hcGkta2V5OnRlc3QtYXBpLXNlY3JldA==" \
  -H "Content-Type: application/json" \
  -d '{"startTime": "2023-01-03T09:30:00Z", "endTime": "2023-03-31T16:00:00Z"}' \
  | jq -r '.id')

# 2. Create account
ACCOUNT=$(curl -s -X POST http://localhost:5000/v1/accounts \
  -H "Authorization: Basic dGVzdC1hcGkta2V5OnRlc3QtYXBpLXNlY3JldA==" \
  -H "X-Session-Id: $SESSION" \
  -H "Content-Type: application/json" \
  -d '{"initialCash": 100000}' \
  | jq -r '.id')

# 3. Place an order
curl -X POST "http://localhost:5000/v1/trading/accounts/$ACCOUNT/orders" \
  -H "Authorization: Basic dGVzdC1hcGkta2V5OnRlc3QtYXBpLXNlY3JldA==" \
  -H "X-Session-Id: $SESSION" \
  -H "Content-Type: application/json" \
  -d '{"symbol": "AAPL", "side": "buy", "qty": 10, "type": "market"}'

# 4. Advance time to fill the order
curl -X POST "http://localhost:5000/v1/sessions/$SESSION/time/advance" \
  -H "Authorization: Basic dGVzdC1hcGkta2V5OnRlc3QtYXBpLXNlY3JldA==" \
  -H "Content-Type: application/json" \
  -d '{"duration": 1}'

# 5. Check positions
curl "http://localhost:5000/v1/trading/accounts/$ACCOUNT/positions" \
  -H "Authorization: Basic dGVzdC1hcGkta2V5OnRlc3QtYXBpLXNlY3JldA==" \
  -H "X-Session-Id: $SESSION"
```
