#Requires -Version 7.0
<#
.SYNOPSIS
    Quick test script for local OrderSystem instance
.EXAMPLE
    .\test-local.ps1
    .\test-local.ps1 -BaseUrl "http://localhost:5275" -StressTest
#>

param(
    [string]$BaseUrl = "http://localhost:15275",
    [switch]$StressTest,
    [int]$Concurrency = 10,
    [int]$RequestCount = 100
)

$ErrorActionPreference = "Stop"

# Colors
function Write-Success { Write-Host $args -ForegroundColor Green }
function Write-Info { Write-Host $args -ForegroundColor Cyan }
function Write-Err { Write-Host $args -ForegroundColor Red }

$TestsPassed = 0
$TestsFailed = 0

function Test-Endpoint {
    param([string]$Name, [string]$Url, [string]$Method = "GET", [string]$Body = $null)

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        $params = @{ Uri = $Url; Method = $Method; ContentType = "application/json"; TimeoutSec = 30 }
        if ($Body) { $params.Body = $Body }

        $response = Invoke-RestMethod @params -StatusCodeVariable statusCode
        $sw.Stop()

        $script:TestsPassed++
        Write-Success "  [PASS] $Name ($($sw.ElapsedMilliseconds)ms)"
        return $response
    }
    catch {
        $sw.Stop()
        $script:TestsFailed++
        Write-Err "  [FAIL] $Name - $($_.Exception.Message)"
        return $null
    }
}

Write-Info "`n=== OrderSystem Local Test ===`n"
Write-Info "Base URL: $BaseUrl`n"

# Functional Tests
Write-Info "--- Functional Tests ---`n"

Test-Endpoint -Name "Health Check" -Url "$BaseUrl/health"
$sysInfo = Test-Endpoint -Name "System Info" -Url "$BaseUrl/api/system/info"
if ($sysInfo) { Write-Info "  Mode: $($sysInfo.transport)/$($sysInfo.persistence)" }

Test-Endpoint -Name "Get Stats (Initial)" -Url "$BaseUrl/api/orders/stats"

# Create Order
$orderBody = @{
    customerId = "TEST-$(Get-Random)"
    items = @(@{ productId = "P001"; productName = "Test Product"; quantity = 2; unitPrice = 99.99 })
} | ConvertTo-Json

$order = Test-Endpoint -Name "Create Order" -Url "$BaseUrl/api/orders" -Method "POST" -Body $orderBody

if ($order) {
    $orderId = $order.orderId
    Write-Info "  Order ID: $orderId"

    Test-Endpoint -Name "Get Order" -Url "$BaseUrl/api/orders/$orderId"

    # Lifecycle
    Test-Endpoint -Name "Pay Order" -Url "$BaseUrl/api/orders/$orderId/pay" -Method "POST" -Body (@{ paymentMethod = "Card"; transactionId = "TXN-$(Get-Random)" } | ConvertTo-Json)
    Test-Endpoint -Name "Process Order" -Url "$BaseUrl/api/orders/$orderId/process" -Method "POST" -Body "{}"
    Test-Endpoint -Name "Ship Order" -Url "$BaseUrl/api/orders/$orderId/ship" -Method "POST" -Body (@{ trackingNumber = "TRK-$(Get-Random)" } | ConvertTo-Json)
    Test-Endpoint -Name "Deliver Order" -Url "$BaseUrl/api/orders/$orderId/deliver" -Method "POST" -Body "{}"

    $final = Test-Endpoint -Name "Verify Delivered" -Url "$BaseUrl/api/orders/$orderId"
    if ($final -and $final.status -eq 4) { Write-Success "  Lifecycle completed!" }
}

# Flow Order
Test-Endpoint -Name "Create Order (Flow)" -Url "$BaseUrl/api/orders/flow" -Method "POST" -Body $orderBody

# Cancel Test
$cancelOrder = Test-Endpoint -Name "Create (Cancel Test)" -Url "$BaseUrl/api/orders" -Method "POST" -Body $orderBody
if ($cancelOrder) {
    Test-Endpoint -Name "Cancel Order" -Url "$BaseUrl/api/orders/$($cancelOrder.orderId)/cancel" -Method "POST" -Body (@{ reason = "Test" } | ConvertTo-Json)
}

Test-Endpoint -Name "Get All Orders" -Url "$BaseUrl/api/orders?limit=20"
Test-Endpoint -Name "Get Final Stats" -Url "$BaseUrl/api/orders/stats"

# Stress Test
if ($StressTest) {
    Write-Info "`n--- Stress Test ($Concurrency x $RequestCount) ---`n"

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $results = @{ Success = 0; Failed = 0; Latencies = [System.Collections.Concurrent.ConcurrentBag[int]]::new() }

    $jobs = 1..$Concurrency | ForEach-Object {
        Start-Job -ScriptBlock {
            param($BaseUrl, $Count)
            $r = @{ Success = 0; Failed = 0; Latencies = @() }
            $body = '{"customerId":"STRESS","items":[{"productId":"S001","productName":"Stress","quantity":1,"unitPrice":10}]}'

            1..$Count | ForEach-Object {
                $reqSw = [System.Diagnostics.Stopwatch]::StartNew()
                try {
                    $null = Invoke-RestMethod -Uri "$BaseUrl/api/orders" -Method POST -Body $body -ContentType "application/json" -TimeoutSec 30
                    $reqSw.Stop()
                    $r.Success++
                    $r.Latencies += $reqSw.ElapsedMilliseconds
                } catch {
                    $reqSw.Stop()
                    $r.Failed++
                }
            }
            return $r
        } -ArgumentList $BaseUrl, ([math]::Ceiling($RequestCount / $Concurrency))
    }

    $jobs | Wait-Job | ForEach-Object {
        $jr = Receive-Job $_
        $results.Success += $jr.Success
        $results.Failed += $jr.Failed
        $jr.Latencies | ForEach-Object { $results.Latencies.Add($_) }
    }
    $jobs | Remove-Job
    $sw.Stop()

    $total = $results.Success + $results.Failed
    $successRate = if ($total -gt 0) { [math]::Round($results.Success / $total * 100, 2) } else { 0 }
    $rps = if ($sw.Elapsed.TotalSeconds -gt 0) { [math]::Round($total / $sw.Elapsed.TotalSeconds, 2) } else { 0 }
    $latArr = $results.Latencies.ToArray()
    $avgLatency = if ($latArr.Count -gt 0) { [math]::Round(($latArr | Measure-Object -Average).Average, 2) } else { 0 }
    $p95 = if ($latArr.Count -gt 0) { ($latArr | Sort-Object)[[math]::Floor($latArr.Count * 0.95)] } else { 0 }

    Write-Info "Results:"
    Write-Info "  Total: $total | Success: $($results.Success) | Failed: $($results.Failed)"
    Write-Info "  Success Rate: $successRate%"
    Write-Info "  Requests/sec: $rps"
    Write-Info "  Avg Latency: ${avgLatency}ms | P95: ${p95}ms"
    Write-Info "  Duration: $([math]::Round($sw.Elapsed.TotalSeconds, 2))s"

    if ($successRate -ge 95) { $script:TestsPassed++ } else { $script:TestsFailed++ }

    # Read stress
    Write-Info "`n--- Read Stress Test ---`n"
    $sw2 = [System.Diagnostics.Stopwatch]::StartNew()
    $readJobs = 1..$Concurrency | ForEach-Object {
        Start-Job -ScriptBlock {
            param($BaseUrl, $Count)
            $success = 0
            1..$Count | ForEach-Object {
                try { $null = Invoke-RestMethod -Uri "$BaseUrl/api/orders/stats" -TimeoutSec 30; $success++ } catch {}
            }
            return $success
        } -ArgumentList $BaseUrl, ([math]::Ceiling($RequestCount / $Concurrency))
    }
    $readSuccess = ($readJobs | Wait-Job | ForEach-Object { Receive-Job $_ } | Measure-Object -Sum).Sum
    $readJobs | Remove-Job
    $sw2.Stop()

    $readRps = [math]::Round($readSuccess / $sw2.Elapsed.TotalSeconds, 2)
    Write-Info "  Read Requests/sec: $readRps"
}

# Summary
Write-Info "`n=== Summary ===`n"
Write-Info "Passed: $TestsPassed | Failed: $TestsFailed"
if ($TestsFailed -gt 0) { exit 1 }
