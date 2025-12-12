#!/usr/bin/env pwsh
# Simple Cross-Environment Performance Test
# Usage: .\simple-ab-test.ps1 [-Config InMemory|Redis|NATS|Full] [-Requests 1000] [-Concurrency 20]

param(
    [ValidateSet("InMemory", "Redis", "NATS", "Full")]
    [string]$Config = "InMemory",
    [int]$Requests = 100,
    [int]$Concurrency = 10
)

$scriptDir = $PSScriptRoot
$baseUrl = "http://localhost:5275"

Write-Host "╔══════════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║     Catga Performance Test - $Config                         ║" -ForegroundColor Cyan
Write-Host "╚══════════════════════════════════════════════════════════════╝" -ForegroundColor Cyan

# Set environment variables
$env:ASPNETCORE_URLS = $baseUrl
switch ($Config) {
    "InMemory" {
        $env:CATGA_TRANSPORT = "InMemory"
        $env:CATGA_PERSISTENCE = "InMemory"
    }
    "Redis" {
        $env:CATGA_TRANSPORT = "InMemory"
        $env:CATGA_PERSISTENCE = "Redis"
        $env:REDIS_CONNECTION = "localhost:6379"
    }
    "NATS" {
        $env:CATGA_TRANSPORT = "NATS"
        $env:CATGA_PERSISTENCE = "InMemory"
        $env:NATS_URL = "nats://localhost:4222"
    }
    "Full" {
        $env:CATGA_TRANSPORT = "NATS"
        $env:CATGA_PERSISTENCE = "Redis"
        $env:REDIS_CONNECTION = "localhost:6379"
        $env:NATS_URL = "nats://localhost:4222"
    }
}

Write-Host "Transport: $($env:CATGA_TRANSPORT) | Persistence: $($env:CATGA_PERSISTENCE)" -ForegroundColor Gray

# Create order.json
$orderJson = Join-Path $scriptDir "order.json"
'{"customerId":"C001","items":[{"productId":"P1","productName":"Test","quantity":1,"unitPrice":100}]}' | Out-File -FilePath $orderJson -Encoding UTF8 -NoNewline

# Start API
Write-Host "`nStarting API..." -ForegroundColor Yellow
$apiJob = Start-Job -ScriptBlock {
    param($dir, $transport, $persistence, $redis, $nats, $urls)
    $env:CATGA_TRANSPORT = $transport
    $env:CATGA_PERSISTENCE = $persistence
    $env:REDIS_CONNECTION = $redis
    $env:NATS_URL = $nats
    $env:ASPNETCORE_URLS = $urls
    Set-Location $dir
    dotnet run --project examples/OrderSystem.Api -c Release --no-build 2>&1
} -ArgumentList $scriptDir, $env:CATGA_TRANSPORT, $env:CATGA_PERSISTENCE, $env:REDIS_CONNECTION, $env:NATS_URL, $env:ASPNETCORE_URLS

# Wait for API
$timeout = 30
$start = Get-Date
while (((Get-Date) - $start).TotalSeconds -lt $timeout) {
    try {
        $response = Invoke-WebRequest -Uri "$baseUrl/health" -TimeoutSec 2 -ErrorAction SilentlyContinue
        if ($response.StatusCode -eq 200) {
            Write-Host "API Ready!" -ForegroundColor Green
            break
        }
    } catch {}
    Start-Sleep -Milliseconds 500
}

if (((Get-Date) - $start).TotalSeconds -ge $timeout) {
    Write-Host "API failed to start!" -ForegroundColor Red
    $apiJob | Stop-Job | Remove-Job
    exit 1
}

# Performance test function using PowerShell
function Run-PerformanceTest {
    param(
        [string]$Endpoint,
        [int]$NumRequests,
        [int]$Concurrency,
        [string]$Method = "GET",
        [string]$Body = $null,
        [string]$ContentType = "application/json"
    )

    $results = @()
    $totalTime = 0
    $successCount = 0
    $failureCount = 0

    # Run requests with concurrency control
    $jobs = @()
    $batchSize = [Math]::Min($Concurrency, $NumRequests)

    Write-Host "  $Method $Endpoint..." -NoNewline

    $sw = [System.Diagnostics.Stopwatch]::StartNew()

    for ($i = 0; $i -lt $NumRequests; $i += $batchSize) {
        $batch = @()
        for ($j = 0; $j -lt $batchSize -and ($i + $j) -lt $NumRequests; $j++) {
            $batch += @{Index = $i + $j}
        }

        foreach ($item in $batch) {
            $job = Start-Job -ScriptBlock {
                param($url, $method, $body, $contentType)
                $itemSw = [System.Diagnostics.Stopwatch]::StartNew()
                try {
                    if ($method -eq "GET") {
                        $response = Invoke-WebRequest -Uri $url -TimeoutSec 10 -ErrorAction Stop
                    } else {
                        $response = Invoke-WebRequest -Uri $url -Method $method -Body $body -ContentType $contentType -TimeoutSec 10 -ErrorAction Stop
                    }
                    $itemSw.Stop()
                    return @{Success = $true; Latency = $itemSw.ElapsedMilliseconds; StatusCode = $response.StatusCode}
                } catch {
                    $itemSw.Stop()
                    return @{Success = $false; Latency = $itemSw.ElapsedMilliseconds; Error = $_.Exception.Message}
                }
            } -ArgumentList "$baseUrl$Endpoint", $Method, $Body, $ContentType

            $jobs += $job
        }

        # Wait for batch to complete
        $jobs | Wait-Job | Out-Null
    }

    # Collect results
    $latencies = @()
    foreach ($job in $jobs) {
        $result = Receive-Job -Job $job
        if ($result.Success) {
            $successCount++
            $latencies += $result.Latency
        } else {
            $failureCount++
        }
        Remove-Job -Job $job
    }

    $sw.Stop()

    # Calculate statistics
    $avgLatency = if ($latencies.Count -gt 0) { [Math]::Round(($latencies | Measure-Object -Average).Average, 2) } else { 0 }
    $p95Latency = if ($latencies.Count -gt 0) { $latencies | Sort-Object | Select-Object -Index ([Math]::Floor($latencies.Count * 0.95)) } else { 0 }
    $rps = [Math]::Round($NumRequests / ($sw.ElapsedMilliseconds / 1000), 1)

    Write-Host " $rps req/s (avg: ${avgLatency}ms, p95: ${p95Latency}ms)" -ForegroundColor Green

    return @{
        RPS = $rps
        AvgLatency = $avgLatency
        P95 = $p95Latency
        Success = $successCount
        Failed = $failureCount
    }
}

# Run tests
Write-Host "`nRunning benchmarks (n=$Requests, c=$Concurrency)..." -ForegroundColor Yellow

$health = Run-PerformanceTest -Endpoint "/health" -NumRequests $Requests -Concurrency $Concurrency

$orderBody = Get-Content $orderJson -Raw
$orders = Run-PerformanceTest -Endpoint "/api/orders" -NumRequests $Requests -Concurrency $Concurrency -Method "POST" -Body $orderBody

$flow = Run-PerformanceTest -Endpoint "/api/orders/flow" -NumRequests $Requests -Concurrency $Concurrency -Method "POST" -Body $orderBody

# Stop API
$apiJob | Stop-Job | Remove-Job -Force

# Print results
Write-Host "`n═══════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "                    RESULTS: $Config                            " -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""
Write-Host "┌────────────────────────┬──────────┬──────────┬──────────┐" -ForegroundColor Gray
Write-Host "│ Endpoint               │ RPS      │ Avg(ms)  │ P95(ms)  │" -ForegroundColor Gray
Write-Host "├────────────────────────┼──────────┼──────────┼──────────┤" -ForegroundColor Gray
Write-Host ("│ GET /health            │ {0,-8} │ {1,-8} │ {2,-8} │" -f $health.RPS, $health.AvgLatency, $health.P95) -ForegroundColor White
Write-Host ("│ POST /api/orders       │ {0,-8} │ {1,-8} │ {2,-8} │" -f $orders.RPS, $orders.AvgLatency, $orders.P95) -ForegroundColor White
Write-Host ("│ POST /api/orders/flow  │ {0,-8} │ {1,-8} │ {2,-8} │" -f $flow.RPS, $flow.AvgLatency, $flow.P95) -ForegroundColor White
Write-Host "└────────────────────────┴──────────┴──────────┴──────────┘" -ForegroundColor Gray
Write-Host ""

# Return results for aggregation
return @{
    Config = $Config
    Health = $health
    Orders = $orders
    Flow = $flow
}
