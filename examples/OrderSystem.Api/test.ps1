# OrderSystem API Test Script
# Usage: .\test.ps1 [-BaseUrl "http://localhost:5275"]

param(
    [string]$BaseUrl = "http://localhost:5275"
)

$ErrorActionPreference = "Stop"
$passed = 0
$failed = 0
$results = @()

function Test-Api {
    param(
        [string]$Name,
        [string]$Method,
        [string]$Endpoint,
        [object]$Body = $null,
        [int]$ExpectedStatus = 200
    )

    $url = "$BaseUrl$Endpoint"
    $result = @{ Name = $Name; Status = "FAIL"; Message = "" }

    try {
        $params = @{
            Uri = $url
            Method = $Method
            ContentType = "application/json"
            ErrorAction = "Stop"
        }

        if ($Body) {
            $params.Body = ($Body | ConvertTo-Json -Depth 10)
        }

        $response = Invoke-RestMethod @params
        $result.Status = "PASS"
        $result.Message = "OK"
        $script:passed++
    }
    catch {
        $result.Message = $_.Exception.Message
        $script:failed++
    }

    $color = if ($result.Status -eq "PASS") { "Green" } else { "Red" }
    Write-Host "[$($result.Status)] $Name" -ForegroundColor $color
    if ($result.Status -eq "FAIL") {
        Write-Host "       $($result.Message)" -ForegroundColor DarkGray
    }

    $script:results += $result
    return $result.Status -eq "PASS"
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host " OrderSystem API Test Suite" -ForegroundColor Cyan
Write-Host " Base URL: $BaseUrl" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# ========== System ==========
Write-Host "--- System ---" -ForegroundColor Yellow
Test-Api "System Info" "GET" "/api/system/info"
Test-Api "Health Check" "GET" "/health"

# ========== Orders ==========
Write-Host ""
Write-Host "--- Orders ---" -ForegroundColor Yellow
Test-Api "Get All Orders" "GET" "/api/orders"
Test-Api "Get Order Stats" "GET" "/api/orders/stats"

$orderBody = @{
    customerId = "TEST-" + (Get-Date -Format "HHmmss")
    items = @(
        @{ productId = "P1"; productName = "Test Product"; quantity = 1; unitPrice = 99.99 }
    )
}
Test-Api "Create Order" "POST" "/api/orders" $orderBody

Test-Api "Get Orders After Create" "GET" "/api/orders"

# ========== Observability ==========
Write-Host ""
Write-Host "--- Observability ---" -ForegroundColor Yellow
Test-Api "Get Metrics Info" "GET" "/api/observability/metrics"
Test-Api "Get Grafana Info" "GET" "/api/observability/grafana"
Test-Api "Record Flow Demo" "POST" "/api/observability/demo/record-flow?flowName=TestFlow&durationMs=100"
Test-Api "Record Failure Demo" "POST" "/api/observability/demo/record-failure?flowName=TestFlow&error=TestError"

# ========== Hot Reload ==========
Write-Host ""
Write-Host "--- Hot Reload ---" -ForegroundColor Yellow
Test-Api "Get Registered Flows" "GET" "/api/hotreload/flows"
Test-Api "Register Flow" "POST" "/api/hotreload/flows/TestFlow"
Test-Api "Get Flow Details" "GET" "/api/hotreload/flows/TestFlow"
Test-Api "Get Flow Version" "GET" "/api/hotreload/versions/TestFlow"
Test-Api "Reload Flow" "PUT" "/api/hotreload/flows/TestFlow/reload"
Test-Api "Get Reload Event Info" "GET" "/api/hotreload/events/info"
Test-Api "Unregister Flow" "DELETE" "/api/hotreload/flows/TestFlow"

# ========== Read Model Sync ==========
Write-Host ""
Write-Host "--- Read Model Sync ---" -ForegroundColor Yellow
Test-Api "Get Sync Status" "GET" "/api/readmodelsync/status"
Test-Api "Get Pending Changes" "GET" "/api/readmodelsync/pending"
Test-Api "Get Sync Strategies" "GET" "/api/readmodelsync/strategies"
Test-Api "Track Change" "POST" "/api/readmodelsync/demo/track?entityType=Order&entityId=TEST-001&changeType=0"
Test-Api "Trigger Sync" "POST" "/api/readmodelsync/sync"
Test-Api "Get Pending After Sync" "GET" "/api/readmodelsync/pending"

# ========== Summary ==========
Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host " Test Summary" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host " Total:  $($passed + $failed)" -ForegroundColor White
Write-Host " Passed: $passed" -ForegroundColor Green
Write-Host " Failed: $failed" -ForegroundColor $(if ($failed -gt 0) { "Red" } else { "Green" })
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

if ($failed -gt 0) {
    Write-Host "Some tests failed!" -ForegroundColor Red
    exit 1
} else {
    Write-Host "All tests passed!" -ForegroundColor Green
    exit 0
}
