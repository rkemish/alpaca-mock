#!/bin/bash
set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Configuration
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(dirname "$SCRIPT_DIR")"
ENV_FILE="$PROJECT_ROOT/.env"

# Tech stocks to load (FAANG+)
SYMBOLS=("AAPL" "MSFT" "GOOGL" "AMZN" "META" "NVDA" "TSLA")

# Date range (full year 2024)
FROM_DATE="2024-01-01"
TO_DATE="2024-12-31"

# PostgreSQL connection string
POSTGRES_CONN="Host=localhost;Database=alpacamock;Username=postgres;Password=postgres"

echo -e "${BLUE}======================================${NC}"
echo -e "${BLUE}  AlpacaMock Local Environment Setup${NC}"
echo -e "${BLUE}======================================${NC}"
echo

# Check for .env file
if [ ! -f "$ENV_FILE" ]; then
    echo -e "${RED}Error: .env file not found${NC}"
    echo
    echo "Create a .env file with your Polygon API key:"
    echo "  cp .env.example .env"
    echo "  # Edit .env and add your POLYGON_API_KEY"
    echo
    exit 1
fi

# Load .env file
source "$ENV_FILE"

# Validate Polygon API key
if [ -z "$POLYGON_API_KEY" ] || [ "$POLYGON_API_KEY" = "your_polygon_api_key_here" ]; then
    echo -e "${RED}Error: POLYGON_API_KEY not set in .env file${NC}"
    echo
    echo "Edit your .env file and add your Polygon API key."
    echo "Get a key at: https://polygon.io/pricing"
    echo
    exit 1
fi

echo -e "${GREEN}[1/4]${NC} Starting Docker Compose services..."
cd "$PROJECT_ROOT"
docker compose -f deploy/docker-compose.yml up -d

echo
echo -e "${YELLOW}Waiting for services to be healthy...${NC}"
echo

# Wait for services with progress indicator
MAX_WAIT=60
WAITED=0
while [ $WAITED -lt $MAX_WAIT ]; do
    # Check health endpoints
    if curl -sf http://localhost:5050/health > /dev/null 2>&1; then
        break
    fi

    printf "."
    sleep 3
    WAITED=$((WAITED + 3))
done
echo

if [ $WAITED -ge $MAX_WAIT ]; then
    echo -e "${RED}Timeout waiting for services. Check logs:${NC}"
    echo "  docker compose -f deploy/docker-compose.yml logs"
    exit 1
fi

echo -e "${GREEN}All services are healthy!${NC}"
echo

echo -e "${GREEN}[2/4]${NC} Verifying API is accessible..."
curl -s http://localhost:5050/health | head -c 100
echo
echo

echo -e "${GREEN}[3/4]${NC} Loading market data for tech stocks..."
echo "  Symbols: ${SYMBOLS[*]}"
echo "  Date range: $FROM_DATE to $TO_DATE"
echo "  Resolution: minute bars"
echo
echo -e "${YELLOW}This may take 30-60 minutes for a full year of data.${NC}"
echo

LOADED=0
FAILED=0

for SYMBOL in "${SYMBOLS[@]}"; do
    echo -e "${BLUE}Loading $SYMBOL...${NC}"

    if dotnet run --project "$PROJECT_ROOT/src/AlpacaMock.DataIngestion" -- load-bars \
        -c "$POSTGRES_CONN" \
        -k "$POLYGON_API_KEY" \
        -s "$SYMBOL" \
        --from "$FROM_DATE" \
        --to "$TO_DATE" \
        -r minute 2>&1; then
        echo -e "${GREEN}  $SYMBOL loaded successfully${NC}"
        LOADED=$((LOADED + 1))
    else
        echo -e "${RED}  Failed to load $SYMBOL${NC}"
        FAILED=$((FAILED + 1))
    fi
    echo
done

echo -e "${GREEN}[4/4]${NC} Verifying data..."
dotnet run --project "$PROJECT_ROOT/src/AlpacaMock.DataIngestion" -- stats -c "$POSTGRES_CONN"

echo
echo -e "${BLUE}======================================${NC}"
echo -e "${BLUE}  Setup Complete!${NC}"
echo -e "${BLUE}======================================${NC}"
echo
echo -e "  Symbols loaded: ${GREEN}$LOADED${NC}"
if [ $FAILED -gt 0 ]; then
    echo -e "  Failed: ${RED}$FAILED${NC}"
fi
echo
echo "  API URL: http://localhost:5050"
echo "  Auth: Basic dGVzdC1hcGkta2V5OnRlc3QtYXBpLXNlY3JldA=="
echo
echo "Next steps:"
echo "  1. Import Postman collection: postman/AlpacaMock.postman_collection.json"
echo "  2. Select environment: postman/Local.postman_environment.json"
echo "  3. Run functional tests: newman run postman/AlpacaMock.postman_collection.json -e postman/Local.postman_environment.json --folder 'Workflows'"
echo
