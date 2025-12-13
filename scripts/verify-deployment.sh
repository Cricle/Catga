#!/bin/bash

# Deployment Verification Script for OrderSystem.Api
# This script verifies the Docker deployment and health checks

set -e

echo "=== OrderSystem.Api Deployment Verification ==="
echo ""

# Colors for output
GREEN='\033[0;32m'
RED='\033[0;31m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Configuration
API_URL="${API_URL:-http://localhost:8080}"
HEALTH_CHECK_TIMEOUT=30
HEALTH_CHECK_INTERVAL=2

echo "Verifying deployment at: $API_URL"
echo ""

# Function to check health endpoint
check_health() {
    local endpoint=$1
    local name=$2

    echo -n "Checking $name... "

    for i in $(seq 1 $((HEALTH_CHECK_TIMEOUT / HEALTH_CHECK_INTERVAL))); do
        if curl -s "$API_URL$endpoint" > /dev/null 2>&1; then
            echo -e "${GREEN}✓ OK${NC}"
            return 0
        fi
        echo -n "."
        sleep $HEALTH_CHECK_INTERVAL
    done

    echo -e "${RED}✗ FAILED${NC}"
    return 1
}

# Function to test API endpoint
test_endpoint() {
    local method=$1
    local endpoint=$2
    local data=$3
    local expected_code=$4

    echo -n "Testing $method $endpoint... "

    if [ -z "$data" ]; then
        response=$(curl -s -w "\n%{http_code}" -X "$method" "$API_URL$endpoint")
    else
        response=$(curl -s -w "\n%{http_code}" -X "$method" "$API_URL$endpoint" \
            -H "Content-Type: application/json" \
            -d "$data")
    fi

    http_code=$(echo "$response" | tail -n1)
    body=$(echo "$response" | head -n-1)

    if [ "$http_code" = "$expected_code" ]; then
        echo -e "${GREEN}✓ OK (HTTP $http_code)${NC}"
        return 0
    else
        echo -e "${RED}✗ FAILED (Expected $expected_code, got $http_code)${NC}"
        return 1
    fi
}

# Step 1: Check health endpoints
echo "Step 1: Health Check Endpoints"
echo "==============================="
check_health "/health" "Liveness" || exit 1
check_health "/health/ready" "Readiness" || exit 1
check_health "/health/live" "Live" || exit 1
echo ""

# Step 2: Test API endpoints
echo "Step 2: API Endpoints"
echo "===================="

# Test authentication
test_endpoint "POST" "/api/auth/login" \
    '{"email":"admin@ordersystem.local","password":"admin123"}' \
    "200" || true

# Test order creation
test_endpoint "POST" "/api/orders" \
    '{"customerId":"test-customer","items":[{"productId":"prod-1","quantity":1,"price":99.99}],"totalAmount":99.99}' \
    "200" || true

# Test order retrieval
test_endpoint "GET" "/api/orders/stats" "" "200" || true

echo ""

# Step 3: Performance check
echo "Step 3: Performance Check"
echo "========================="

echo -n "Measuring API response time... "
start_time=$(date +%s%N)
curl -s "$API_URL/health" > /dev/null
end_time=$(date +%s%N)
response_time=$(( (end_time - start_time) / 1000000 ))

echo -e "${GREEN}${response_time}ms${NC}"

if [ $response_time -lt 1000 ]; then
    echo -e "${GREEN}✓ Response time acceptable${NC}"
else
    echo -e "${YELLOW}⚠ Response time higher than expected${NC}"
fi

echo ""

# Step 4: Container status
echo "Step 4: Container Status"
echo "========================"

if command -v docker &> /dev/null; then
    echo "Docker containers:"
    docker ps --filter "name=ordersystem" --format "table {{.Names}}\t{{.Status}}\t{{.Ports}}" || true
else
    echo "Docker not available for container status check"
fi

echo ""
echo -e "${GREEN}=== Deployment Verification Complete ===${NC}"
