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

# Default tech stocks (FAANG+)
DEFAULT_SYMBOLS=("AAPL" "MSFT" "GOOGL" "AMZN" "META" "NVDA" "TSLA")

# Default date range (full year 2024)
FROM_DATE="${FROM_DATE:-2024-01-01}"
TO_DATE="${TO_DATE:-2024-12-31}"

# PostgreSQL connection string
POSTGRES_CONN="${POSTGRES_CONNECTION_STRING:-Host=localhost;Database=alpacamock;Username=postgres;Password=postgres}"

# Parse command line arguments
SYMBOLS=()
while [[ $# -gt 0 ]]; do
    case $1 in
        -s|--symbol)
            SYMBOLS+=("$2")
            shift 2
            ;;
        --from)
            FROM_DATE="$2"
            shift 2
            ;;
        --to)
            TO_DATE="$2"
            shift 2
            ;;
        -h|--help)
            echo "Usage: $0 [OPTIONS]"
            echo
            echo "Load historical market data for tech stocks from Polygon.io"
            echo
            echo "Options:"
            echo "  -s, --symbol SYMBOL   Add symbol to load (can be repeated)"
            echo "  --from DATE           Start date (default: 2024-01-01)"
            echo "  --to DATE             End date (default: 2024-12-31)"
            echo "  -h, --help            Show this help"
            echo
            echo "If no symbols specified, loads: ${DEFAULT_SYMBOLS[*]}"
            echo
            echo "Examples:"
            echo "  $0                           # Load all FAANG+ stocks for 2024"
            echo "  $0 -s AAPL -s MSFT           # Load only AAPL and MSFT"
            echo "  $0 --from 2024-01-01 --to 2024-03-31  # Load Q1 2024 only"
            exit 0
            ;;
        *)
            echo "Unknown option: $1"
            exit 1
            ;;
    esac
done

# Use default symbols if none specified
if [ ${#SYMBOLS[@]} -eq 0 ]; then
    SYMBOLS=("${DEFAULT_SYMBOLS[@]}")
fi

echo -e "${BLUE}======================================${NC}"
echo -e "${BLUE}  AlpacaMock Data Loader${NC}"
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
    exit 1
fi

echo "Symbols: ${SYMBOLS[*]}"
echo "Date range: $FROM_DATE to $TO_DATE"
echo "Resolution: minute bars"
echo
echo -e "${YELLOW}This may take a while depending on date range and number of symbols.${NC}"
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

echo -e "${BLUE}======================================${NC}"
echo -e "${BLUE}  Data Load Complete${NC}"
echo -e "${BLUE}======================================${NC}"
echo
echo -e "  Symbols loaded: ${GREEN}$LOADED${NC}"
if [ $FAILED -gt 0 ]; then
    echo -e "  Failed: ${RED}$FAILED${NC}"
fi
echo

# Show stats
echo "Database statistics:"
dotnet run --project "$PROJECT_ROOT/src/AlpacaMock.DataIngestion" -- stats -c "$POSTGRES_CONN"
