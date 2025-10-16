# Run all Catga benchmarks and generate report
# Usage: .\run-all-benchmarks.ps1

$ErrorActionPreference = "Stop"
$timestamp = Get-Date -Format "yyyy-MM-dd-HHmmss"
$outputDir = "BenchmarkDotNet.Artifacts\results"
$reportFile = "benchmark-report-$timestamp.md"

Write-Host "🚀 Running Catga Performance Benchmarks..." -ForegroundColor Cyan
Write-Host "Timestamp: $timestamp" -ForegroundColor Gray
Write-Host ""

# Benchmark categories
$benchmarks = @(
    @{Name="CQRS Performance"; Filter="*CqrsPerformanceBenchmarks*"},
    @{Name="Serialization"; Filter="*SerializationBenchmarks*"},
    @{Name="Distributed ID"; Filter="*DistributedIdBenchmark*"},
    @{Name="Concurrency"; Filter="*ConcurrencyPerformanceBenchmarks*"},
    @{Name="Allocation"; Filter="*AllocationBenchmarks*"},
    @{Name="Source Generator"; Filter="*SourceGeneratorBenchmarks*"},
    @{Name="Debug"; Filter="*DebugBenchmarks*"},
    @{Name="Graceful Lifecycle"; Filter="*GracefulLifecycleBenchmarks*"},
    @{Name="SafeRequestHandler"; Filter="*SafeRequestHandlerBenchmarks*"}
)

$results = @()

foreach ($benchmark in $benchmarks) {
    Write-Host "📊 Running: $($benchmark.Name)" -ForegroundColor Yellow
    
    try {
        $output = & dotnet run -c Release --filter $benchmark.Filter 2>&1
        
        if ($LASTEXITCODE -eq 0) {
            Write-Host "   ✅ Completed" -ForegroundColor Green
            $results += @{
                Name = $benchmark.Name
                Status = "Success"
                Output = $output
            }
        } else {
            Write-Host "   ❌ Failed" -ForegroundColor Red
            $results += @{
                Name = $benchmark.Name
                Status = "Failed"
                Output = $output
            }
        }
    } catch {
        Write-Host "   ⚠️ Error: $_" -ForegroundColor Red
        $results += @{
            Name = $benchmark.Name
            Status = "Error"
            Output = $_.Exception.Message
        }
    }
    
    Write-Host ""
}

Write-Host "📄 Generating consolidated report..." -ForegroundColor Cyan

# Generate markdown report
$report = @"
# Catga Benchmark Results

**Date**: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")
**Machine**: $env:COMPUTERNAME
**OS**: $([System.Environment]::OSVersion.VersionString)
**.NET**: $((dotnet --version))

---

## Summary

| Benchmark Category | Status |
|-------------------|--------|
"@

foreach ($result in $results) {
    $status = if ($result.Status -eq "Success") { "✅" } else { "❌" }
    $report += "`n| $($result.Name) | $status $($result.Status) |"
}

$report += @"

---

## Detailed Results

"@

foreach ($result in $results) {
    $report += @"

### $($result.Name)

``````
$($result.Output -join "`n")
``````

"@
}

# Save report
$report | Out-File -FilePath $reportFile -Encoding UTF8

Write-Host "✅ Report saved: $reportFile" -ForegroundColor Green
Write-Host ""
Write-Host "📊 Results also available in: $outputDir" -ForegroundColor Gray

