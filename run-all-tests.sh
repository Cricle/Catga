#!/bin/bash
# Catga Flow DSL - Complete Test Suite Runner
# This script runs all tests and generates comprehensive reports

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
WHITE='\033[1;37m'
NC='\033[0m' # No Color

echo -e "${CYAN}══════════════════════════════════════════════════════════════${NC}"
echo -e "${YELLOW}           CATGA FLOW DSL - COMPLETE TEST SUITE${NC}"
echo -e "${CYAN}══════════════════════════════════════════════════════════════${NC}"
echo ""

# Create results directory
RESULTS_DIR="TestResults"
rm -rf $RESULTS_DIR
mkdir -p $RESULTS_DIR

TOTAL_START_TIME=$(date +%s)

# Function to run tests with timing
run_test_category() {
    local category=$1
    local description=$2
    local filter=$3

    echo -e "${GREEN}▶ Running $description...${NC}"
    local start_time=$(date +%s)

    if [ -n "$filter" ]; then
        dotnet test --filter "$filter" \
            --logger "console;verbosity=normal" \
            --logger "html;LogFileName=$category.html" \
            --results-directory "$RESULTS_DIR/$category" > /dev/null 2>&1
    else
        dotnet test \
            --logger "console;verbosity=normal" \
            --logger "html;LogFileName=$category.html" \
            --results-directory "$RESULTS_DIR/$category" > /dev/null 2>&1
    fi

    local exit_code=$?
    local elapsed=$(($(date +%s) - start_time))

    if [ $exit_code -eq 0 ]; then
        echo -e "  ${GREEN}✅ PASS${NC} - Completed in ${elapsed}s"
    else
        echo -e "  ${RED}❌ FAIL${NC} - Completed in ${elapsed}s"
    fi
    echo ""

    return $exit_code
}

# Track results
declare -a test_results
declare -a test_names
declare -a test_times
test_count=0
pass_count=0
fail_count=0

# Run test categories
echo -e "${CYAN}═══════════════════════════════════════════════════════════════${NC}"
echo -e "${YELLOW}1. UNIT TESTS${NC}"
echo -e "${CYAN}═══════════════════════════════════════════════════════════════${NC}"
start_time=$(date +%s)
run_test_category "Unit" "Core Unit Tests" "Category=Unit|FullyQualifiedName~Unit"
test_results[$test_count]=$?
test_names[$test_count]="Unit Tests"
test_times[$test_count]=$(($(date +%s) - start_time))
((test_count++))

echo -e "${CYAN}═══════════════════════════════════════════════════════════════${NC}"
echo -e "${YELLOW}2. INTEGRATION TESTS${NC}"
echo -e "${CYAN}═══════════════════════════════════════════════════════════════${NC}"
start_time=$(date +%s)
run_test_category "Integration" "Integration Tests" "Category=Integration|FullyQualifiedName~Integration"
test_results[$test_count]=$?
test_names[$test_count]="Integration Tests"
test_times[$test_count]=$(($(date +%s) - start_time))
((test_count++))

echo -e "${CYAN}═══════════════════════════════════════════════════════════════${NC}"
echo -e "${YELLOW}3. STORAGE PARITY TESTS${NC}"
echo -e "${CYAN}═══════════════════════════════════════════════════════════════${NC}"
start_time=$(date +%s)
run_test_category "StorageParity" "Storage Parity Tests" "FullyQualifiedName~StorageParity|FullyQualifiedName~StorageFeature|FullyQualifiedName~StorageDetailed"
test_results[$test_count]=$?
test_names[$test_count]="Storage Parity Tests"
test_times[$test_count]=$(($(date +%s) - start_time))
((test_count++))

echo -e "${CYAN}═══════════════════════════════════════════════════════════════${NC}"
echo -e "${YELLOW}4. END-TO-END TESTS${NC}"
echo -e "${CYAN}═══════════════════════════════════════════════════════════════${NC}"
start_time=$(date +%s)
run_test_category "E2E" "E2E Tests" "Category=E2E|FullyQualifiedName~E2E"
test_results[$test_count]=$?
test_names[$test_count]="E2E Tests"
test_times[$test_count]=$(($(date +%s) - start_time))
((test_count++))

echo -e "${CYAN}═══════════════════════════════════════════════════════════════${NC}"
echo -e "${YELLOW}5. PERFORMANCE TESTS${NC}"
echo -e "${CYAN}═══════════════════════════════════════════════════════════════${NC}"
start_time=$(date +%s)
run_test_category "Performance" "Performance Tests" "Category=Performance|FullyQualifiedName~Performance|FullyQualifiedName~MassTransit"
test_results[$test_count]=$?
test_names[$test_count]="Performance Tests"
test_times[$test_count]=$(($(date +%s) - start_time))
((test_count++))

echo -e "${CYAN}═══════════════════════════════════════════════════════════════${NC}"
echo -e "${YELLOW}6. SOURCE GENERATION TESTS${NC}"
echo -e "${CYAN}═══════════════════════════════════════════════════════════════${NC}"
start_time=$(date +%s)
run_test_category "SourceGeneration" "Source Generation Tests" "FullyQualifiedName~SourceGeneration|FullyQualifiedName~Generation"
test_results[$test_count]=$?
test_names[$test_count]="Source Generation"
test_times[$test_count]=$(($(date +%s) - start_time))
((test_count++))

echo -e "${CYAN}═══════════════════════════════════════════════════════════════${NC}"
echo -e "${YELLOW}7. CODE COVERAGE${NC}"
echo -e "${CYAN}═══════════════════════════════════════════════════════════════${NC}"
echo -e "${GREEN}▶ Generating code coverage report...${NC}"
coverage_start=$(date +%s)
dotnet test --collect:"XPlat Code Coverage" --results-directory "$RESULTS_DIR/Coverage" > /dev/null 2>&1
coverage_elapsed=$(($(date +%s) - coverage_start))
echo -e "  ${GREEN}✅ Coverage report generated in ${coverage_elapsed}s${NC}"
echo ""

# Calculate totals
TOTAL_ELAPSED=$(($(date +%s) - TOTAL_START_TIME))

# Count pass/fail
for i in "${!test_results[@]}"; do
    if [ ${test_results[$i]} -eq 0 ]; then
        ((pass_count++))
    else
        ((fail_count++))
    fi
done

echo -e "${CYAN}═══════════════════════════════════════════════════════════════${NC}"
echo -e "${YELLOW}                        TEST RESULTS SUMMARY${NC}"
echo -e "${CYAN}═══════════════════════════════════════════════════════════════${NC}"
echo ""

# Display results table
echo -e "${WHITE}Test Category                Status      Time(s)${NC}"
echo -e "─────────────────────────────────────────────────"
for i in "${!test_results[@]}"; do
    if [ ${test_results[$i]} -eq 0 ]; then
        status="${GREEN}✅ PASS${NC}"
    else
        status="${RED}❌ FAIL${NC}"
    fi
    printf "%-25s %-20b %ds\n" "${test_names[$i]}" "$status" "${test_times[$i]}"
done
echo -e "─────────────────────────────────────────────────"
echo ""

echo -e "${CYAN}📊 Statistics:${NC}"
echo -e "  Total Test Suites:  $test_count"
echo -e "  ${GREEN}Passed:            $pass_count${NC}"
if [ $fail_count -eq 0 ]; then
    echo -e "  ${GREEN}Failed:            $fail_count${NC}"
else
    echo -e "  ${RED}Failed:            $fail_count${NC}"
fi
echo -e "  Total Time:        ${TOTAL_ELAPSED}s"
echo ""

# Ask about benchmarks
echo -n "Do you want to run performance benchmarks? (y/n): "
read -r run_benchmarks
if [ "$run_benchmarks" = "y" ]; then
    echo ""
    echo -e "${CYAN}═══════════════════════════════════════════════════════════════${NC}"
    echo -e "${YELLOW}8. PERFORMANCE BENCHMARKS${NC}"
    echo -e "${CYAN}═══════════════════════════════════════════════════════════════${NC}"
    echo -e "${GREEN}▶ Running benchmarks (this may take several minutes)...${NC}"

    dotnet run -c Release --project tests/Catga.Tests -- \
        --filter "*Benchmark*" \
        --exporters html json \
        --artifacts "$RESULTS_DIR/Benchmarks"

    echo -e "  ${GREEN}✅ Benchmarks completed${NC}"
    echo ""
fi

# Generate final report
echo -e "${CYAN}═══════════════════════════════════════════════════════════════${NC}"
echo -e "${YELLOW}                      FINAL REPORT${NC}"
echo -e "${CYAN}═══════════════════════════════════════════════════════════════${NC}"
echo ""

if [ $fail_count -eq 0 ]; then
    echo -e "${GREEN}✅ ALL TESTS PASSED! 🎉${NC}"
    echo ""
    echo "The Catga Flow DSL test suite is:"
    echo -e "  ${GREEN}✅ Comprehensive${NC}"
    echo -e "  ${GREEN}✅ Reliable${NC}"
    echo -e "  ${GREEN}✅ Production Ready${NC}"
else
    echo -e "${RED}❌ SOME TESTS FAILED${NC}"
    echo -e "${YELLOW}Please review the test results above for details.${NC}"
fi

echo ""
echo -e "${CYAN}📁 Test results saved to: $RESULTS_DIR${NC}"
echo ""
echo -e "${CYAN}═══════════════════════════════════════════════════════════════${NC}"

# Ask to open results
echo -n "Open test results folder? (y/n): "
read -r open_results
if [ "$open_results" = "y" ]; then
    if [ "$(uname)" == "Darwin" ]; then
        # macOS
        open "$RESULTS_DIR"
    elif [ "$(expr substr $(uname -s) 1 5)" == "Linux" ]; then
        # Linux
        xdg-open "$RESULTS_DIR" 2>/dev/null || echo "Please open $RESULTS_DIR manually"
    fi
fi

# Exit with appropriate code
if [ $fail_count -eq 0 ]; then
    exit 0
else
    exit 1
fi
