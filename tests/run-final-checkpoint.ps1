# Final Checkpoint Test Execution Script
# Task 37: 确保所有测试通过

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Final Checkpoint - TDD Validation Tests" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Check for compilation issues
Write-Host "Step 1: Checking for compilation issues..." -ForegroundColor Yellow
$buildResult = dotnet build tests/Catga.Tests/Catga.Tests.csproj --verbosity quiet 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ Build failed. There are compilation errors that need to be fixed first." -ForegroundColor Red
    Write-Host ""
    Write-Host "Known Issues:" -ForegroundColor Yellow
    Write-Host "- Catga.Persistence.Redis has 29 compilation errors related to RedisValue implicit operators" -ForegroundColor Yellow
    Write-Host "- These errors are blocking the test suite from running" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Recommendation: Fix Redis persistence compilation errors before running final checkpoint" -ForegroundColor Yellow
    exit 1
}

Write-Host "✓ Build successful" -ForegroundColor Green
Write-Host ""

# Run test suite by category
Write-Host "Step 2: Running test suite by category..." -ForegroundColor Yellow
Write-Host ""

$categories = @(
    @{Name="Unit Tests"; Filter="Category=Unit"},
    @{Name="Boundary Tests"; Filter="Category=Boundary"},
    @{Name="Property Tests"; Filter="Category=Property"},
    @{Name="Integration Tests"; Filter="Category=Integration"},
    @{Name="E2E Tests"; Filter="Category=E2E"},
    @{Name="Stress Tests"; Filter="Category=Stress"}
)

$totalPassed = 0
$totalFailed = 0
$totalSkipped = 0

foreach ($category in $categories) {
    Write-Host "Running $($category.Name)..." -ForegroundColor Cyan
    
    $testOutput = dotnet test tests/Catga.Tests/Catga.Tests.csproj `
        --filter $category.Filter `
        --verbosity quiet `
        --logger "console;verbosity=minimal" `
        --no-build 2>&1
    
    # Parse results
    $passed = ($testOutput | Select-String "Passed!" | Measure-Object).Count
    $failed = ($testOutput | Select-String "Failed!" | Measure-Object).Count
    $skipped = ($testOutput | Select-String "Skipped!" | Measure-Object).Count
    
    $totalPassed += $passed
    $totalFailed += $failed
    $totalSkipped += $skipped
    
    if ($failed -eq 0) {
        Write-Host "  ✓ $passed passed" -ForegroundColor Green
    } else {
        Write-Host "  ✗ $passed passed, $failed failed" -ForegroundColor Red
    }
    
    if ($skipped -gt 0) {
        Write-Host "  ⊘ $skipped skipped" -ForegroundColor Yellow
    }
    
    Write-Host ""
}

# Summary
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Test Summary" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Total Passed:  $totalPassed" -ForegroundColor Green
Write-Host "Total Failed:  $totalFailed" -ForegroundColor $(if ($totalFailed -eq 0) { "Green" } else { "Red" })
Write-Host "Total Skipped: $totalSkipped" -ForegroundColor Yellow
Write-Host ""

# Generate coverage report
Write-Host "Step 3: Generating coverage report..." -ForegroundColor Yellow
$coverageResult = dotnet test tests/Catga.Tests/Catga.Tests.csproj `
    --collect:"XPlat Code Coverage" `
    --results-directory:"./TestResults" `
    --verbosity quiet `
    --no-build 2>&1

if ($LASTEXITCODE -eq 0) {
    Write-Host "✓ Coverage report generated in ./TestResults" -ForegroundColor Green
} else {
    Write-Host "⚠ Coverage report generation failed" -ForegroundColor Yellow
}
Write-Host ""

# Final status
if ($totalFailed -eq 0) {
    Write-Host "========================================" -ForegroundColor Green
    Write-Host "✓ All tests passed!" -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Green
    exit 0
} else {
    Write-Host "========================================" -ForegroundColor Red
    Write-Host "✗ Some tests failed. Please review." -ForegroundColor Red
    Write-Host "========================================" -ForegroundColor Red
    exit 1
}
