# Catga Flow DSL - Benchmark Runner Script
# This script runs performance benchmarks comparing Catga with other frameworks

param(
    [string]$Filter = "*",
    [string]$Framework = "net8.0",
    [switch]$Quick,
    [switch]$Detailed
)

Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "           CATGA FLOW DSL - BENCHMARK RUNNER" -ForegroundColor Yellow
Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""

# Check if we're in the right directory
if (-not (Test-Path "tests\Catga.Tests\Catga.Tests.csproj")) {
    Write-Host "❌ Error: Please run this script from the Catga solution root directory" -ForegroundColor Red
    exit 1
}

# Build in Release mode first
Write-Host "▶ Building in Release mode..." -ForegroundColor Green
dotnet build -c Release --no-incremental
if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ Build failed" -ForegroundColor Red
    exit 1
}
Write-Host "✅ Build successful" -ForegroundColor Green
Write-Host ""

# Set benchmark parameters
$benchmarkArgs = @()

if ($Quick) {
    Write-Host "🚀 Running in Quick mode (fewer iterations)..." -ForegroundColor Yellow
    $benchmarkArgs += "--job", "Short"
    $benchmarkArgs += "--warmupCount", "3"
    $benchmarkArgs += "--iterationCount", "5"
} elseif ($Detailed) {
    Write-Host "📊 Running in Detailed mode (more iterations)..." -ForegroundColor Yellow
    $benchmarkArgs += "--job", "Long"
    $benchmarkArgs += "--warmupCount", "10"
    $benchmarkArgs += "--iterationCount", "30"
} else {
    Write-Host "📈 Running in Standard mode..." -ForegroundColor Cyan
}

# Add filter if specified
if ($Filter -ne "*") {
    Write-Host "🔍 Filter: $Filter" -ForegroundColor Cyan
    $benchmarkArgs += "--filter", "$Filter"
}

# Output formats
$benchmarkArgs += "--exporters", "html", "csv", "markdown", "json"

# Results directory
$resultsDir = "BenchmarkDotNet.Artifacts"
if (Test-Path $resultsDir) {
    Write-Host "📁 Cleaning previous results..." -ForegroundColor Gray
    Remove-Item -Path $resultsDir -Recurse -Force
}

Write-Host ""
Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "                    RUNNING BENCHMARKS" -ForegroundColor Yellow
Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""

# Run benchmarks
$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()

dotnet run -c Release --project tests\Catga.Tests --framework $Framework -- $benchmarkArgs

$stopwatch.Stop()
$elapsed = $stopwatch.Elapsed

Write-Host ""
Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "                    BENCHMARK RESULTS" -ForegroundColor Yellow
Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""

if ($LASTEXITCODE -eq 0) {
    Write-Host "✅ Benchmarks completed successfully!" -ForegroundColor Green
    Write-Host "⏱️  Total time: $($elapsed.ToString('mm\:ss'))" -ForegroundColor Cyan
    Write-Host ""

    # Check if results exist
    if (Test-Path $resultsDir) {
        $htmlFiles = Get-ChildItem -Path $resultsDir -Filter "*.html" -Recurse
        $csvFiles = Get-ChildItem -Path $resultsDir -Filter "*.csv" -Recurse

        Write-Host "📊 Results available:" -ForegroundColor Cyan
        Write-Host "  HTML Reports: $($htmlFiles.Count) files" -ForegroundColor White
        Write-Host "  CSV Data: $($csvFiles.Count) files" -ForegroundColor White
        Write-Host "  Location: $resultsDir" -ForegroundColor White
        Write-Host ""

        # Display summary from the most recent results
        $latestHtml = $htmlFiles | Sort-Object LastWriteTime -Descending | Select-Object -First 1
        if ($latestHtml) {
            Write-Host "📈 Opening latest results in browser..." -ForegroundColor Green
            Start-Process $latestHtml.FullName
        }
    }
} else {
    Write-Host "❌ Benchmarks failed with exit code $LASTEXITCODE" -ForegroundColor Red
}

Write-Host ""
Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor Cyan

# Prompt to keep window open
Read-Host "Press Enter to exit"
