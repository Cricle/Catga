# Flow DSL Complete Test Script
# Tests all Flow DSL features: basic flow, complex flow, recovery, distributed execution

$ErrorActionPreference = "Stop"
$baseUrl = "http://localhost:5000"

Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "Flow DSL Complete Test Suite" -ForegroundColor Cyan
Write-Host "========================================`n" -ForegroundColor Cyan

# Helper function to make HTTP requests
function Invoke-ApiRequest {
    param(
        [string]$Method,
        [string]$Endpoint,
        [object]$Body = $null
    )
    
    $url = "$baseUrl$Endpoint"
    $params = @{
        Method = $Method
        Uri = $url
        ContentType = "application/json"
    }
    
    if ($Body) {
        $params.Body = ($Body | ConvertTo-Json -Depth 10)
    }
    
    try {
        $response = Invoke-RestMethod @params
        return $response
    }
    catch {
        Write-Host "Error: $_" -ForegroundColor Red
        return $null
    }
}

# Test counters
$totalTests = 0
$passedTests = 0
$failedTests = 0

function Test-Case {
    param(
        [string]$Name,
        [scriptblock]$Test
    )
    
    $script:totalTests++
    Write-Host "`n[$script:totalTests] Testing: $Name" -ForegroundColor Yellow
    
    try {
        & $Test
        $script:passedTests++
        Write-Host "✓ PASSED" -ForegroundColor Green
    }
    catch {
        $script:failedTests++
        Write-Host "✗ FAILED: $_" -ForegroundColor Red
    }
}

# Wait for server to be ready
Write-Host "Waiting for server to be ready..." -ForegroundColor Gray
Start-Sleep -Seconds 2

# ========================================
# Test 1: Basic Order Fulfillment Flow
# ========================================
Test-Case "Start basic order fulfillment flow" {
    $request = @{
        customerId = "CUST-001"
        items = @(
            @{ productId = "PROD-1"; name = "Widget"; quantity = 2; price = 10.0 },
            @{ productId = "PROD-2"; name = "Gadget"; quantity = 1; price = 25.0 }
        )
    }
    
    $result = Invoke-ApiRequest -Method POST -Endpoint "/api/flows/fulfillment/start" -Body $request
    
    if (-not $result) { throw "Failed to start flow" }
    if (-not $result.flowId) { throw "No flowId returned" }
    if (-not $result.orderId) { throw "No orderId returned" }
    if ($result.total -ne 45.0) { throw "Incorrect total: $($result.total)" }
    
    Write-Host "  Flow ID: $($result.flowId)" -ForegroundColor Gray
    Write-Host "  Order ID: $($result.orderId)" -ForegroundColor Gray
    Write-Host "  Total: $($result.total)" -ForegroundColor Gray
    Write-Host "  Status: $($result.status)" -ForegroundColor Gray
    
    $script:fulfillmentFlowId = $result.flowId
    $script:fulfillmentOrderId = $result.orderId
}

# ========================================
# Test 2: Get Flow Status
# ========================================
Test-Case "Get flow status" {
    if (-not $script:fulfillmentFlowId) { throw "No flow ID from previous test" }
    
    $result = Invoke-ApiRequest -Method GET -Endpoint "/api/flows/status/$($script:fulfillmentFlowId)"
    
    if (-not $result) { throw "Failed to get flow status" }
    if ($result.flowId -ne $script:fulfillmentFlowId) { throw "Flow ID mismatch" }
    if (-not $result.status) { throw "No status returned" }
    
    Write-Host "  Status: $($result.status)" -ForegroundColor Gray
    Write-Host "  Order ID: $($result.orderId)" -ForegroundColor Gray
    Write-Host "  Total: $($result.total)" -ForegroundColor Gray
    Write-Host "  Created: $($result.createdAt)" -ForegroundColor Gray
    Write-Host "  Version: $($result.version)" -ForegroundColor Gray
}

# ========================================
# Test 3: Complex Order Flow - Standard Type
# ========================================
Test-Case "Start complex flow - Standard type" {
    $request = @{
        customerId = "CUST-002"
        type = "Standard"
        items = @(
            @{ productId = "PROD-3"; name = "Book"; quantity = 3; price = 15.0 }
        )
    }
    
    $result = Invoke-ApiRequest -Method POST -Endpoint "/api/flows/complex/start" -Body $request
    
    if (-not $result) { throw "Failed to start complex flow" }
    if (-not $result.flowId) { throw "No flowId returned" }
    if ($result.total -ne 45.0) { throw "Incorrect total: $($result.total)" }
    
    Write-Host "  Flow ID: $($result.flowId)" -ForegroundColor Gray
    Write-Host "  Order ID: $($result.orderId)" -ForegroundColor Gray
    Write-Host "  Processed Items: $($result.processedItems)" -ForegroundColor Gray
    
    $script:complexFlowId = $result.flowId
}

# ========================================
# Test 4: Complex Order Flow - Express Type
# ========================================
Test-Case "Start complex flow - Express type" {
    $request = @{
        customerId = "CUST-003"
        type = "Express"
        items = @(
            @{ productId = "PROD-4"; name = "Laptop"; quantity = 1; price = 999.0 },
            @{ productId = "PROD-5"; name = "Mouse"; quantity = 2; price = 25.0 }
        )
    }
    
    $result = Invoke-ApiRequest -Method POST -Endpoint "/api/flows/complex/start" -Body $request
    
    if (-not $result) { throw "Failed to start express flow" }
    if (-not $result.flowId) { throw "No flowId returned" }
    if ($result.total -ne 1049.0) { throw "Incorrect total: $($result.total)" }
    
    Write-Host "  Flow ID: $($result.flowId)" -ForegroundColor Gray
    Write-Host "  Total: $($result.total)" -ForegroundColor Gray
}

# ========================================
# Test 5: Complex Order Flow - Bulk Type
# ========================================
Test-Case "Start complex flow - Bulk type" {
    $request = @{
        customerId = "CUST-004"
        type = "Bulk"
        items = @(
            @{ productId = "PROD-6"; name = "Paper"; quantity = 100; price = 0.5 }
        )
    }
    
    $result = Invoke-ApiRequest -Method POST -Endpoint "/api/flows/complex/start" -Body $request
    
    if (-not $result) { throw "Failed to start bulk flow" }
    if ($result.total -ne 50.0) { throw "Incorrect total: $($result.total)" }
    
    Write-Host "  Flow ID: $($result.flowId)" -ForegroundColor Gray
}

# ========================================
# Test 6: Multiple Items ForEach Processing
# ========================================
Test-Case "Complex flow with multiple items (ForEach)" {
    $request = @{
        customerId = "CUST-005"
        type = "Standard"
        items = @(
            @{ productId = "PROD-7"; name = "Item1"; quantity = 1; price = 10.0 },
            @{ productId = "PROD-8"; name = "Item2"; quantity = 1; price = 20.0 },
            @{ productId = "PROD-9"; name = "Item3"; quantity = 1; price = 30.0 },
            @{ productId = "PROD-10"; name = "Item4"; quantity = 1; price = 40.0 }
        )
    }
    
    $result = Invoke-ApiRequest -Method POST -Endpoint "/api/flows/complex/start" -Body $request
    
    if (-not $result) { throw "Failed to start flow with multiple items" }
    if ($result.total -ne 100.0) { throw "Incorrect total: $($result.total)" }
    if ($result.processedItems -ne 4) { throw "Expected 4 processed items, got $($result.processedItems)" }
    
    Write-Host "  Processed Items: $($result.processedItems)" -ForegroundColor Gray
}

# ========================================
# Test 7: Concurrent Flow Execution
# ========================================
Test-Case "Concurrent flow execution" {
    $jobs = @()
    
    for ($i = 1; $i -le 5; $i++) {
        $request = @{
            customerId = "CUST-CONCURRENT-$i"
            items = @(
                @{ productId = "PROD-$i"; name = "Item$i"; quantity = 1; price = 10.0 * $i }
            )
        }
        
        $job = Start-Job -ScriptBlock {
            param($url, $body)
            Invoke-RestMethod -Method POST -Uri "$url/api/flows/fulfillment/start" `
                -ContentType "application/json" -Body ($body | ConvertTo-Json -Depth 10)
        } -ArgumentList $baseUrl, $request
        
        $jobs += $job
    }
    
    $results = $jobs | Wait-Job | Receive-Job
    $jobs | Remove-Job
    
    if ($results.Count -ne 5) { throw "Expected 5 results, got $($results.Count)" }
    
    $successCount = ($results | Where-Object { $_.flowId }).Count
    if ($successCount -ne 5) { throw "Expected 5 successful flows, got $successCount" }
    
    Write-Host "  Successfully executed 5 concurrent flows" -ForegroundColor Gray
}

# ========================================
# Test 8: Flow with Zero Total (Conditional Branch)
# ========================================
Test-Case "Flow with zero total (tests conditional logic)" {
    $request = @{
        customerId = "CUST-ZERO"
        items = @(
            @{ productId = "PROD-FREE"; name = "Free Item"; quantity = 1; price = 0.0 }
        )
    }
    
    $result = Invoke-ApiRequest -Method POST -Endpoint "/api/flows/fulfillment/start" -Body $request
    
    if (-not $result) { throw "Failed to start flow with zero total" }
    if ($result.total -ne 0.0) { throw "Expected zero total" }
    
    Write-Host "  Flow handled zero total correctly" -ForegroundColor Gray
}

# ========================================
# Test 9: Get Non-Existent Flow
# ========================================
Test-Case "Get non-existent flow status" {
    $fakeFlowId = "00000000000000000000000000000000"
    $result = Invoke-ApiRequest -Method GET -Endpoint "/api/flows/status/$fakeFlowId"
    
    if (-not $result) { throw "Expected error response" }
    if (-not $result.error) { throw "Expected error message" }
    
    Write-Host "  Correctly handled non-existent flow" -ForegroundColor Gray
}

# ========================================
# Test 10: Verify Order Creation
# ========================================
Test-Case "Verify order was created by flow" {
    if (-not $script:fulfillmentOrderId) { throw "No order ID from flow" }
    
    $result = Invoke-ApiRequest -Method GET -Endpoint "/api/orders/$($script:fulfillmentOrderId)"
    
    if (-not $result) { throw "Order not found" }
    if ($result.id -ne $script:fulfillmentOrderId) { throw "Order ID mismatch" }
    if ($result.status -ne "Shipped") { throw "Expected Shipped status, got $($result.status)" }
    
    Write-Host "  Order Status: $($result.status)" -ForegroundColor Gray
    Write-Host "  Order Total: $($result.total)" -ForegroundColor Gray
}

# ========================================
# Summary
# ========================================
Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "Test Summary" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Total Tests: $totalTests" -ForegroundColor White
Write-Host "Passed: $passedTests" -ForegroundColor Green
Write-Host "Failed: $failedTests" -ForegroundColor $(if ($failedTests -eq 0) { "Green" } else { "Red" })

if ($failedTests -eq 0) {
    Write-Host "`n✓ All Flow DSL tests passed!" -ForegroundColor Green
    exit 0
} else {
    Write-Host "`n✗ Some tests failed" -ForegroundColor Red
    exit 1
}
