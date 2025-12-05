<#
.SYNOPSIS
    Catga OrderSystem Demo Tester

.DESCRIPTION
    Automated test script to verify OrderSystem functionality.
    Tests order creation, cancellation, and cluster features.

.PARAMETER BaseUrl
    The base URL of the OrderSystem API (default: http://localhost:5275)

.PARAMETER TestCluster
    If specified, tests multiple nodes for cluster verification

.EXAMPLE
    .\test-demo.ps1
    .\test-demo.ps1 -BaseUrl http://localhost:5276
    .\test-demo.ps1 -TestCluster
#>

param(
    [string]$BaseUrl = "http://localhost:5275",
    [switch]$TestCluster
)

$ErrorActionPreference = "Stop"

function Write-TestHeader {
    param([string]$Title)
    Write-Host ""
    Write-Host "=== $Title ===" -ForegroundColor Cyan
}

function Write-TestResult {
    param([string]$Test, [bool]$Passed, [string]$Details = "")
    if ($Passed) {
        Write-Host "  [PASS] $Test" -ForegroundColor Green
    } else {
        Write-Host "  [FAIL] $Test" -ForegroundColor Red
    }
    if ($Details) {
        Write-Host "         $Details" -ForegroundColor Gray
    }
}

function Test-Endpoint {
    param([string]$Url, [string]$Method = "GET", [object]$Body = $null)
    try {
        $params = @{
            Uri = $Url
            Method = $Method
            ContentType = "application/json"
            ErrorAction = "Stop"
        }
        if ($Body) {
            $params.Body = ($Body | ConvertTo-Json -Depth 10)
        }
        $response = Invoke-RestMethod @params
        return @{ Success = $true; Data = $response }
    } catch {
        return @{ Success = $false; Error = $_.Exception.Message }
    }
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "   Catga OrderSystem Demo Tester" -ForegroundColor Cyan
Write-Host "   Target: $BaseUrl" -ForegroundColor Yellow
Write-Host "========================================" -ForegroundColor Cyan

$totalTests = 0
$passedTests = 0

# Test 1: Health Check
Write-TestHeader "Health Checks"

$result = Test-Endpoint "$BaseUrl/health"
$totalTests++
if ($result.Success) {
    $passedTests++
    Write-TestResult "Health endpoint" $true
} else {
    Write-TestResult "Health endpoint" $false $result.Error
    Write-Host ""
    Write-Host "ERROR: Service not reachable. Make sure OrderSystem is running." -ForegroundColor Red
    Write-Host "Run: .\run-demo.ps1 -Mode Single" -ForegroundColor Yellow
    exit 1
}

# Test 2: Cluster Status
Write-TestHeader "Cluster Status"

$result = Test-Endpoint "$BaseUrl/api/cluster/status"
$totalTests++
if ($result.Success) {
    $passedTests++
    $clusterEnabled = $result.Data.clusterEnabled
    $nodeCount = $result.Data.nodeCount
    Write-TestResult "Cluster status endpoint" $true "ClusterEnabled=$clusterEnabled, Nodes=$nodeCount"
} else {
    Write-TestResult "Cluster status endpoint" $false $result.Error
}

$result = Test-Endpoint "$BaseUrl/api/cluster/node"
$totalTests++
if ($result.Success) {
    $passedTests++
    $nodeId = $result.Data.nodeId
    $processId = $result.Data.processId
    Write-TestResult "Node info endpoint" $true "NodeId=$nodeId, PID=$processId"
} else {
    Write-TestResult "Node info endpoint" $false $result.Error
}

# Test 3: Create Order
Write-TestHeader "Order Operations"

$orderPayload = @{
    customerId = "TEST-CUST-$(Get-Random -Maximum 9999)"
    items = @(
        @{
            productId = "PROD-001"
            productName = "Test Laptop"
            quantity = 1
            unitPrice = 999.99
        },
        @{
            productId = "PROD-002"
            productName = "Test Mouse"
            quantity = 2
            unitPrice = 29.99
        }
    )
    shippingAddress = "123 Test Street, Test City"
    paymentMethod = "credit_card"
}

$result = Test-Endpoint "$BaseUrl/api/orders" "POST" $orderPayload
$totalTests++
if ($result.Success -and $result.Data.orderId) {
    $passedTests++
    $orderId = $result.Data.orderId
    $totalAmount = $result.Data.totalAmount
    Write-TestResult "Create order" $true "OrderId=$orderId, Total=$totalAmount"

    # Test 4: Get Order
    $result = Test-Endpoint "$BaseUrl/api/orders/$orderId"
    $totalTests++
    if ($result.Success) {
        $passedTests++
        Write-TestResult "Get order by ID" $true
    } else {
        Write-TestResult "Get order by ID" $false $result.Error
    }

    # Test 5: Cancel Order
    $cancelPayload = @{
        orderId = $orderId
        reason = "Test cancellation"
    }
    $result = Test-Endpoint "$BaseUrl/api/orders/$orderId/cancel" "POST" $cancelPayload
    $totalTests++
    if ($result.Success) {
        $passedTests++
        Write-TestResult "Cancel order" $true
    } else {
        # May fail if endpoint doesn't exist, that's ok
        Write-TestResult "Cancel order" $false $result.Error
    }
} else {
    Write-TestResult "Create order" $false ($result.Error ?? "No orderId returned")
}

# Test 6: Get Orders by Customer
$result = Test-Endpoint "$BaseUrl/api/users/$($orderPayload.customerId)/orders"
$totalTests++
if ($result.Success) {
    $passedTests++
    $orderCount = if ($result.Data -is [array]) { $result.Data.Count } else { 1 }
    Write-TestResult "Get orders by customer" $true "Found $orderCount order(s)"
} else {
    Write-TestResult "Get orders by customer" $false $result.Error
}

# Test 7: Swagger
Write-TestHeader "API Documentation"

$result = Test-Endpoint "$BaseUrl/swagger/v1/swagger.json"
$totalTests++
if ($result.Success) {
    $passedTests++
    $pathCount = ($result.Data.paths | Get-Member -MemberType NoteProperty).Count
    Write-TestResult "Swagger spec" $true "$pathCount endpoints documented"
} else {
    Write-TestResult "Swagger spec" $false $result.Error
}

# Cluster Tests
if ($TestCluster) {
    Write-TestHeader "Cluster Node Tests"

    $ports = @(5275, 5276, 5277)
    foreach ($port in $ports) {
        $nodeUrl = "http://localhost:$port"
        $result = Test-Endpoint "$nodeUrl/api/cluster/node"
        $totalTests++
        if ($result.Success) {
            $passedTests++
            Write-TestResult "Node on port $port" $true "NodeId=$($result.Data.nodeId)"
        } else {
            Write-TestResult "Node on port $port" $false "Not reachable"
        }
    }

    # Test cross-node order visibility
    Write-Host ""
    Write-Host "  Testing cross-node order visibility..." -ForegroundColor Gray

    # Create order on node 1
    $result = Test-Endpoint "http://localhost:5275/api/orders" "POST" $orderPayload
    if ($result.Success -and $result.Data.orderId) {
        $crossOrderId = $result.Data.orderId
        Write-Host "  Created order $crossOrderId on node 1" -ForegroundColor Gray

        # Try to read from node 2
        Start-Sleep -Milliseconds 500
        $result = Test-Endpoint "http://localhost:5276/api/orders/$crossOrderId"
        $totalTests++
        if ($result.Success) {
            $passedTests++
            Write-TestResult "Cross-node order read" $true "Order visible on node 2"
        } else {
            Write-TestResult "Cross-node order read" $false "Order not visible on node 2"
        }
    }
}

# Summary
Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "   Test Summary" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

$percentage = [math]::Round(($passedTests / $totalTests) * 100)
$color = if ($percentage -eq 100) { "Green" } elseif ($percentage -ge 80) { "Yellow" } else { "Red" }

Write-Host "  Total:  $totalTests tests" -ForegroundColor White
Write-Host "  Passed: $passedTests tests" -ForegroundColor Green
Write-Host "  Failed: $($totalTests - $passedTests) tests" -ForegroundColor Red
Write-Host "  Rate:   $percentage%" -ForegroundColor $color
Write-Host ""

if ($passedTests -eq $totalTests) {
    Write-Host "  All tests passed!" -ForegroundColor Green
} else {
    Write-Host "  Some tests failed. Check the output above." -ForegroundColor Yellow
}

Write-Host ""
