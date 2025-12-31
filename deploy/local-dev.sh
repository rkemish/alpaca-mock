#!/bin/bash
set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

echo "========================================"
echo "  AlpacaMock Local Development Setup   "
echo "========================================"
echo ""

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Check Docker is running
if ! docker info &> /dev/null; then
    echo -e "${RED}Error: Docker is not running. Please start Docker Desktop.${NC}"
    exit 1
fi

# Check memory allocation
DOCKER_MEM=$(docker info --format '{{.MemTotal}}' 2>/dev/null || echo "0")
DOCKER_MEM_GB=$((DOCKER_MEM / 1024 / 1024 / 1024))
if [ "$DOCKER_MEM_GB" -lt 4 ]; then
    echo -e "${YELLOW}Warning: Docker has less than 4GB memory allocated.${NC}"
    echo -e "${YELLOW}Cosmos DB emulator requires at least 3GB. Consider increasing Docker memory.${NC}"
    echo ""
fi

case "$1" in
    start)
        echo -e "${GREEN}Starting services...${NC}"
        echo ""

        # Build and start
        docker compose up -d --build

        echo ""
        echo -e "${GREEN}Services starting...${NC}"
        echo ""
        echo "Waiting for services to be ready..."
        echo "(Cosmos DB emulator takes 1-2 minutes to start)"
        echo ""

        # Wait for PostgreSQL
        echo -n "PostgreSQL: "
        for i in {1..30}; do
            if docker compose exec -T postgres pg_isready -U postgres &>/dev/null; then
                echo -e "${GREEN}Ready${NC}"
                break
            fi
            echo -n "."
            sleep 2
        done

        # Wait for Cosmos DB emulator
        echo -n "Cosmos DB:  "
        for i in {1..60}; do
            if curl -sk https://localhost:8081/_explorer/emulator.pem &>/dev/null; then
                echo -e "${GREEN}Ready${NC}"
                break
            fi
            echo -n "."
            sleep 3
        done

        echo ""
        echo -e "${GREEN}========================================"
        echo "  Services are ready!"
        echo "========================================"
        echo ""
        echo "  API:        http://localhost:5000"
        echo "  Health:     http://localhost:5000/health"
        echo "  PostgreSQL: localhost:5432"
        echo "  Cosmos:     https://localhost:8081/_explorer/index.html"
        echo ""
        echo "  API Credentials:"
        echo "    Key:    test-api-key"
        echo "    Secret: test-api-secret"
        echo ""
        echo "  Example:"
        echo "    curl -u 'test-api-key:test-api-secret' http://localhost:5000/v1/sessions"
        echo ""
        echo -e "${NC}"
        ;;

    stop)
        echo -e "${YELLOW}Stopping services...${NC}"
        docker compose down
        echo -e "${GREEN}Services stopped.${NC}"
        ;;

    logs)
        docker compose logs -f "${2:-api}"
        ;;

    clean)
        echo -e "${YELLOW}Stopping and removing all data...${NC}"
        docker compose down -v
        echo -e "${GREEN}All services stopped and data removed.${NC}"
        ;;

    status)
        docker compose ps
        ;;

    shell)
        case "$2" in
            postgres)
                docker compose exec postgres psql -U postgres -d alpacamock
                ;;
            api)
                docker compose exec api /bin/sh
                ;;
            *)
                echo "Usage: $0 shell [postgres|api]"
                ;;
        esac
        ;;

    rebuild)
        echo -e "${YELLOW}Rebuilding API image...${NC}"
        docker compose build --no-cache api
        docker compose up -d api
        echo -e "${GREEN}API rebuilt and restarted.${NC}"
        ;;

    *)
        echo "AlpacaMock Local Development Helper"
        echo ""
        echo "Usage: $0 {start|stop|logs|clean|status|shell|rebuild}"
        echo ""
        echo "Commands:"
        echo "  start          Start all services (PostgreSQL, Cosmos DB, API)"
        echo "  stop           Stop all services"
        echo "  logs [svc]     Follow logs (default: api)"
        echo "  clean          Stop services and remove all data volumes"
        echo "  status         Show service status"
        echo "  shell [svc]    Open shell (postgres or api)"
        echo "  rebuild        Rebuild and restart API container"
        echo ""
        echo "Examples:"
        echo "  $0 start           # Start everything"
        echo "  $0 logs api        # Follow API logs"
        echo "  $0 shell postgres  # Open psql shell"
        echo ""
        ;;
esac
