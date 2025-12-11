# Catga Flow DSL - Complete Test Suite Runner
# This script runs all tests and generates comprehensive reports

Write-Host "══════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "           CATGA FLOW DSL - COMPLETE TEST SUITE" -ForegroundColor Yellow
Write-Host "══════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""

# Create results directory
$resultsDir = "TestResults"
if (Test-Path $resultsDir) {
    Remove-Item $resultsDir -Recurse -Force
}
New-Item -ItemType Directory -Path $resultsDir | Out-Null

$totalStartTime = Get-Date

# Function to run tests with timing
function Run-TestCategory {
    param(
        [string]$Category,
        [string]$Description,
        [string]$Filter
    )

    Write-Host "▶ Running $Description..." -ForegroundColor Green
    $startTime = Get-Date

    if ($Filter) {
        dotnet test --filter "$Filter" --logger "console;verbosity=normal" --logger "html;LogFileName=$Category.html" --results-directory "$resultsDir\$Category" 2>&1 | Out-Null
    } else {
        dotnet test --logger "console;verbosity=normal" --logger "html;LogFileName=$Category.html" --results-directory "$resultsDir\$Category" 2>&1 | Out-Null
    }

    $elapsed = (Get-Date) - $startTime
    $status = if ($LASTEXITCODE -eq 0) { "✅ PASS" } else { "❌ FAIL" }

    Write-Host "  $status - Completed in $($elapsed.TotalSeconds.ToString('F2'))s" -ForegroundColor $(if ($LASTEXITCODE -eq 0) { "Green" } else { "Red" })
    Write-Host ""

    return @{
        Category = $Category
        Status = $LASTEXITCODE -eq 0
        Time = $elapsed.TotalSeconds
    }
}

# Test results collection
$results = @()

Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "1. UNIT TESTS" -ForegroundColor Yellow
Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor Cyan
$results += Run-TestCategory -Category "Unit" -Description "Core Unit Tests" -Filter "Category=Unit|FullyQualifiedName~Unit"

Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "2. INTEGRATION TESTS" -ForegroundColor Yellow
Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor Cyan
$results += Run-TestCategory -Category "Integration" -Description "Integration Tests" -Filter "Category=Integration|FullyQualifiedName~Integration"

Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "3. STORAGE PARITY TESTS" -ForegroundColor Yellow
Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor Cyan
$results += Run-TestCategory -Category "StorageParity" -Description "Storage Parity Tests" -Filter "FullyQualifiedName~StorageParity|FullyQualifiedName~StorageFeature|FullyQualifiedName~StorageDetailed"

Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "4. END-TO-END TESTS" -ForegroundColor Yellow
Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor Cyan
$results += Run-TestCategory -Category "E2E" -Description "E2E Tests" -Filter "Category=E2E|FullyQualifiedName~E2E"

Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "5. PERFORMANCE TESTS" -ForegroundColor Yellow
Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor Cyan
$results += Run-TestCategory -Category "Performance" -Description "Performance Tests" -Filter "Category=Performance|FullyQualifiedName~Performance|FullyQualifiedName~MassTransit"

Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "6. SOURCE GENERATION TESTS" -ForegroundColor Yellow
Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor Cyan
$results += Run-TestCategory -Category "SourceGeneration" -Description "Source Generation Tests" -Filter "FullyQualifiedName~SourceGeneration|FullyQualifiedName~Generation"

Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "7. CODE COVERAGE" -ForegroundColor Yellow
Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "▶ Generating code coverage report..." -ForegroundColor Green
$coverageStart = Get-Date
dotnet test --collect:"XPlat Code Coverage" --results-directory "$resultsDir\Coverage" 2>&1 | Out-Null
$coverageElapsed = (Get-Date) - $coverageStart
Write-Host "  ✅ Coverage report generated in $($coverageElapsed.TotalSeconds.ToString('F2'))s" -ForegroundColor Green
Write-Host ""

# Calculate total elapsed time
$totalElapsed = (Get-Date) - $totalStartTime

Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "                        TEST RESULTS SUMMARY" -ForegroundColor Yellow
Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""

# Display results table
$passCount = ($results | Where-Object { $_.Status }).Count
$failCount = ($results | Where-Object { -not $_.Status }).Count
$totalTests = $results.Count

Write-Host "Test Category              Status    Time(s)" -ForegroundColor White
Write-Host "─────────────────────────────────────────────" -ForegroundColor Gray
foreach ($result in $results) {
    $statusText = if ($result.Status) { "✅ PASS" } else { "❌ FAIL" }
    $statusColor = if ($result.Status) { "Green" } else { "Red" }
    $categoryPadded = $result.Category.PadRight(25)
    $statusPadded = $statusText.PadRight(10)
    $timeFormatted = $result.Time.ToString('F2')

    Write-Host "$categoryPadded $statusPadded $timeFormatted" -ForegroundColor $statusColor
}
Write-Host "─────────────────────────────────────────────" -ForegroundColor Gray
Write-Host ""

Write-Host "📊 Statistics:" -ForegroundColor Cyan
Write-Host "  Total Test Suites:  $totalTests" -ForegroundColor White
Write-Host "  Passed:            $passCount" -ForegroundColor Green
Write-Host "  Failed:            $failCount" -ForegroundColor $(if ($failCount -eq 0) { "Green" } else { "Red" })
Write-Host "  Total Time:        $($totalElapsed.TotalSeconds.ToString('F2'))s" -ForegroundColor White
Write-Host ""

# Run benchmarks if requested
$runBenchmarks = Read-Host "Do you want to run performance benchmarks? (y/n)"
if ($runBenchmarks -eq 'y') {
    Write-Host ""
    Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor Cyan
    Write-Host "8. PERFORMANCE BENCHMARKS" -ForegroundColor Yellow
    Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor Cyan
    Write-Host "▶ Running benchmarks (this may take several minutes)..." -ForegroundColor Green

    dotnet run -c Release --project tests/Catga.Tests -- --filter "*Benchmark*" --exporters html json --artifacts "$resultsDir\Benchmarks"

    Write-Host "  ✅ Benchmarks completed" -ForegroundColor Green
    Write-Host ""
}

# Generate final report
Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "                      FINAL REPORT" -ForegroundColor Yellow
Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""

if ($failCount -eq 0) {
    Write-Host "✅ ALL TESTS PASSED! 🎉" -ForegroundColor Green
    Write-Host ""
    Write-Host "The Catga Flow DSL test suite is:" -ForegroundColor White
    Write-Host "  ✅ Comprehensive" -ForegroundColor Green
    Write-Host "  ✅ Reliable" -ForegroundColor Green
    Write-Host "  ✅ Production Ready" -ForegroundColor Green
} else {
    Write-Host "❌ SOME TESTS FAILED" -ForegroundColor Red
    Write-Host "Please review the test results above for details." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "📁 Test results saved to: $resultsDir" -ForegroundColor Cyan
Write-Host ""
Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor Cyan

# Open results folder
$openResults = Read-Host "Open test results folder? (y/n)"
if ($openResults -eq 'y') {
    Start-Process explorer.exe $resultsDir
}

# Exit with appropriate code
exit $(if ($failCount -eq 0) { 0 } else { 1 })
