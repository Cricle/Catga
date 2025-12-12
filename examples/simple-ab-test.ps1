#!/usr/bin/env pwsh
# Simple Cross-Environment Performance Test
# Usage: .\simple-ab-test.ps1 [-Config InMemory|Redis|NATS|Full]

param(
    [ValidateSet("InMemory", "Redis", "NATS", "Full")]
    [string]$Config = "InMemory",
    [int]$Requests = 1000,
    [int]$Concurrency = 20
)

$scriptDir = $PSScriptRoot

# Find an available port by trying to bind to port 0 (OS assigns one)
function Get-AvailablePort {
    try {
        $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
        $listener.Start()
        $port = $listener.LocalEndpoint.Port
        $listener.Stop()
        return $port
    } catch {
        return 5275
    }
}

$port = Get-AvailablePort
$baseUrl = "http://localhost:$port"

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
Write-Host "`nStarting API on $baseUrl..." -ForegroundColor Yellow
$apiJob = Start-Job -ScriptBlock {
    param($dir, $transport, $persistence, $redis, $nats, $urls)
    $env:CATGA_TRANSPORT = $transport
    $env:CATGA_PERSISTENCE = $persistence
    $env:REDIS_CONNECTION = $redis
    $env:NATS_URL = $nats
    $env:ASPNETCORE_URLS = $urls
    Set-Location $dir
    dotnet run --project OrderSystem.Api -c Release --no-build --no-launch-profile 2>&1
} -ArgumentList $scriptDir, $env:CATGA_TRANSPORT, $env:CATGA_PERSISTENCE, $env:REDIS_CONNECTION, $env:NATS_URL, $baseUrl

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

# Parse ab output
function Parse-AbOutput {
    param([string]$Output)
    $result = @{RPS=0; AvgLatency=0; P50=0; P95=0; P99=0}
    if ($Output -match "Requests per second:\s+([\d.]+)") { $result.RPS = [Math]::Round([double]$Matches[1], 1) }
    if ($Output -match "Time per request:\s+([\d.]+) \[ms\] \(mean\)") { $result.AvgLatency = [Math]::Round([double]$Matches[1], 2) }
    if ($Output -match "50%\s+(\d+)") { $result.P50 = [int]$Matches[1] }
    if ($Output -match "95%\s+(\d+)") { $result.P95 = [int]$Matches[1] }
    if ($Output -match "99%\s+(\d+)") { $result.P99 = [int]$Matches[1] }
    return $result
}

# Run tests
Write-Host "`nRunning benchmarks (n=$Requests, c=$Concurrency)..." -ForegroundColor Yellow

Write-Host "  GET /health..." -NoNewline
$output = & ab -n $Requests -c $Concurrency "$baseUrl/health" 2>&1 | Out-String
$health = Parse-AbOutput $output
Write-Host " $($health.RPS) req/s" -ForegroundColor Green

Write-Host "  POST /api/orders..." -NoNewline
$output = & ab -n $Requests -c $Concurrency -p $orderJson -T "application/json" "$baseUrl/api/orders" 2>&1 | Out-String
$orders = Parse-AbOutput $output
Write-Host " $($orders.RPS) req/s" -ForegroundColor Green

Write-Host "  POST /api/orders/flow..." -NoNewline
$output = & ab -n $Requests -c $Concurrency -p $orderJson -T "application/json" "$baseUrl/api/orders/flow" 2>&1 | Out-String
$flow = Parse-AbOutput $output
Write-Host " $($flow.RPS) req/s" -ForegroundColor Green

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
