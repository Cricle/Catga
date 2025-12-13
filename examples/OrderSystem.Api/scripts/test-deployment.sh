#!/bin/bash
#
# OrderSystem Deployment Test Script
# Usage:
#   ./test-deployment.sh memory          # Test InMemory mode
#   ./test-deployment.sh redis           # Test Redis mode
#   ./test-deployment.sh nats            # Test NATS mode
#   ./test-deployment.sh all             # Test all modes
#   ./test-deployment.sh redis --stress  # With stress testing
#

set -e

# Configuration
MODE="${1:-memory}"
STRESS_TEST=false
CONCURRENCY=20
REQUEST_COUNT=200
WAIT_SECONDS=30
BASE_URL="http://localhost:5275"
COMPOSE_FILE="docker-compose.prod.yml"

# Parse arguments
shift || true
while [[ $# -gt 0 ]]; do
    case $1 in
        --stress) STRESS_TEST=true; shift ;;
        --concurrency) CONCURRENCY="$2"; shift 2 ;;
        --requests) REQUEST_COUNT="$2"; shift 2 ;;
        *) shift ;;
    esac
done

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
NC='\033[0m'

# Test results
TESTS_PASSED=0
TESTS_FAILED=0

log_info() { echo -e "${CYAN}$1${NC}"; }
log_success() { echo -e "${GREEN}$1${NC}"; }
log_warn() { echo -e "${YELLOW}$1${NC}"; }
log_error() { echo -e "${RED}$1${NC}"; }

test_endpoint() {
    local name="$1"
    local url="$2"
    local method="${3:-GET}"
    local body="$4"
    local expected="${5:-200}"

    local start_time=$(date +%s%N)
    local response
    local status

    if [[ "$method" == "POST" && -n "$body" ]]; then
        response=$(curl -s -w "\n%{http_code}" -X POST "$url" -H "Content-Type: application/json" -d "$body" 2>/dev/null)
    else
        response=$(curl -s -w "\n%{http_code}" "$url" 2>/dev/null)
    fi

    status=$(echo "$response" | tail -n1)
    local body_response=$(echo "$response" | head -n -1)
    local end_time=$(date +%s%N)
    local duration=$(( (end_time - start_time) / 1000000 ))

    if [[ "$status" == "$expected" || "$status" == "200" || "$status" == "204" ]]; then
        log_success "  [PASS] $name (${status}, ${duration}ms)"
        ((TESTS_PASSED++))
        echo "$body_response"
    else
        log_error "  [FAIL] $name (${status}, expected ${expected})"
        ((TESTS_FAILED++))
        echo ""
    fi
}

wait_for_service() {
    local timeout=$WAIT_SECONDS
    log_info "Waiting for service to be ready..."

    for ((i=0; i<timeout; i++)); do
        if curl -s "$BASE_URL/health" >/dev/null 2>&1; then
            log_success "Service is ready!"
            return 0
        fi
        echo -n "."
        sleep 2
    done

    log_error "Service failed to start within ${timeout} seconds"
    return 1
}

start_deployment() {
    local profile="$1"

    echo ""
    log_info "=========================================="
    log_info "Starting deployment: $profile"
    log_info "=========================================="
    echo ""

    # Stop existing
    log_info "Stopping existing containers..."
    docker-compose -f "$COMPOSE_FILE" down --remove-orphans 2>/dev/null || true

    # Start services
    log_info "Starting $profile profile..."
    docker-compose -f "$COMPOSE_FILE" --profile "$profile" up -d

    wait_for_service
}

stop_deployment() {
    log_info "Stopping deployment..."
    docker-compose -f "$COMPOSE_FILE" down --remove-orphans 2>/dev/null || true
}

run_functional_tests() {
    echo ""
    log_info "--- Functional Tests ---"
    echo ""

    # Health check
    test_endpoint "Health Check" "$BASE_URL/health"

    # System info
    test_endpoint "System Info" "$BASE_URL/api/system/info"

    # Get initial stats
    test_endpoint "Get Stats (Initial)" "$BASE_URL/api/orders/stats"

    # Create order
    local order_body='{"customerId":"TEST-001","items":[{"productId":"PROD-001","productName":"Test Product","quantity":2,"unitPrice":99.99}]}'
    local order_response=$(test_endpoint "Create Order" "$BASE_URL/api/orders" "POST" "$order_body")

    if [[ -n "$order_response" ]]; then
        local order_id=$(echo "$order_response" | grep -o '"orderId":"[^"]*"' | cut -d'"' -f4)
        log_info "  Created Order: $order_id"

        if [[ -n "$order_id" ]]; then
            # Get order
            test_endpoint "Get Order" "$BASE_URL/api/orders/$order_id"

            # Pay order
            test_endpoint "Pay Order" "$BASE_URL/api/orders/$order_id/pay" "POST" '{"paymentMethod":"Credit Card","transactionId":"TXN-12345"}'

            # Process order
            test_endpoint "Process Order" "$BASE_URL/api/orders/$order_id/process" "POST" '{}'

            # Ship order
            test_endpoint "Ship Order" "$BASE_URL/api/orders/$order_id/ship" "POST" '{"trackingNumber":"TRK-12345"}'

            # Deliver order
            test_endpoint "Deliver Order" "$BASE_URL/api/orders/$order_id/deliver" "POST" '{}'
        fi
    fi

    # Create order with Flow
    test_endpoint "Create Order (Flow)" "$BASE_URL/api/orders/flow" "POST" "$order_body"

    # Get all orders
    test_endpoint "Get All Orders" "$BASE_URL/api/orders?limit=50"

    # Get final stats
    test_endpoint "Get Stats (Final)" "$BASE_URL/api/orders/stats"
}

run_stress_test() {
    echo ""
    log_info "--- Stress Test ($CONCURRENCY concurrent, $REQUEST_COUNT requests) ---"
    echo ""

    local order_body='{"customerId":"STRESS","items":[{"productId":"STRESS-001","productName":"Stress","quantity":1,"unitPrice":10}]}'
    local start_time=$(date +%s%N)
    local success=0
    local failed=0
    local latencies=()

    # Create temp files for results
    local results_dir=$(mktemp -d)

    log_info "Running stress test..."

    # Run concurrent requests
    for ((i=0; i<CONCURRENCY; i++)); do
        (
            local count=$((REQUEST_COUNT / CONCURRENCY))
            local local_success=0
            local local_failed=0
            local local_latencies=""

            for ((j=0; j<count; j++)); do
                local req_start=$(date +%s%N)
                if curl -s -X POST "$BASE_URL/api/orders" -H "Content-Type: application/json" -d "$order_body" >/dev/null 2>&1; then
                    local req_end=$(date +%s%N)
                    local latency=$(( (req_end - req_start) / 1000000 ))
                    ((local_success++))
                    local_latencies="$local_latencies $latency"
                else
                    ((local_failed++))
                fi
            done

            echo "$local_success $local_failed $local_latencies" > "$results_dir/result_$i"
        ) &
    done

    wait

    local end_time=$(date +%s%N)
    local duration=$(( (end_time - start_time) / 1000000 ))

    # Aggregate results
    for f in "$results_dir"/result_*; do
        local result=$(cat "$f")
        local s=$(echo "$result" | awk '{print $1}')
        local f_count=$(echo "$result" | awk '{print $2}')
        success=$((success + s))
        failed=$((failed + f_count))
    done
    rm -rf "$results_dir"

    local total=$((success + failed))
    local success_rate=0
    local rps=0

    if [[ $total -gt 0 ]]; then
        success_rate=$(echo "scale=2; $success * 100 / $total" | bc)
    fi
    if [[ $duration -gt 0 ]]; then
        rps=$(echo "scale=2; $total * 1000 / $duration" | bc)
    fi

    echo ""
    log_info "Stress Test Results:"
    log_info "  Total Requests: $total"
    log_success "  Successful: $success"
    [[ $failed -gt 0 ]] && log_error "  Failed: $failed"
    log_info "  Success Rate: ${success_rate}%"
    log_info "  Requests/sec: $rps"
    log_info "  Duration: $(echo "scale=2; $duration / 1000" | bc)s"

    if (( $(echo "$success_rate >= 95" | bc -l) )); then
        ((TESTS_PASSED++))
    else
        ((TESTS_FAILED++))
    fi
}

show_summary() {
    echo ""
    log_info "=========================================="
    log_info "TEST SUMMARY"
    log_info "=========================================="
    echo ""

    log_info "Total Tests: $((TESTS_PASSED + TESTS_FAILED))"
    log_success "Passed: $TESTS_PASSED"
    [[ $TESTS_FAILED -gt 0 ]] && log_error "Failed: $TESTS_FAILED"
    echo ""
}

test_mode() {
    local profile="$1"

    start_deployment "$profile" || return 1
    run_functional_tests

    if [[ "$STRESS_TEST" == "true" ]]; then
        run_stress_test
    fi

    stop_deployment
}

# Main
echo ""
echo "  ██████╗ ██████╗ ██████╗ ███████╗██████╗ ███████╗██╗   ██╗███████╗"
echo " ██╔═══██╗██╔══██╗██╔══██╗██╔════╝██╔══██╗██╔════╝╚██╗ ██╔╝██╔════╝"
echo " ██║   ██║██████╔╝██║  ██║█████╗  ██████╔╝███████╗ ╚████╔╝ ███████╗"
echo " ██║   ██║██╔══██╗██║  ██║██╔══╝  ██╔══██╗╚════██║  ╚██╔╝  ╚════██║"
echo " ╚██████╔╝██║  ██║██████╔╝███████╗██║  ██║███████║   ██║   ███████║"
echo "  ╚═════╝ ╚═╝  ╚═╝╚═════╝ ╚══════╝╚═╝  ╚═╝╚══════╝   ╚═╝   ╚══════╝"
echo "                      Deployment Test Suite"
echo ""

cd "$(dirname "$0")/.."

if [[ "$MODE" == "all" ]]; then
    for m in memory redis nats; do
        TESTS_PASSED=0
        TESTS_FAILED=0
        test_mode "$m"
        show_summary
    done
else
    test_mode "$MODE"
    show_summary
fi

[[ $TESTS_FAILED -gt 0 ]] && exit 1
exit 0
