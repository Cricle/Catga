#!/usr/bin/env pwsh
# Catga OrderSystem - Comprehensive Test Script
# Tests all configurations: InMemory, Redis, NATS, Cluster

$ErrorActionPreference = "Stop"

Write-Host "╔══════════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║        Catga OrderSystem - Comprehensive Test Suite         ║" -ForegroundColor Cyan
Write-Host "╚══════════════════════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

$baseUrl = "http://localhost:5000"
$testsPassed = 0
$testsFailed = 0

function Test-Endpoint {
    param(
        [string]$Name,
        [string]$Method,
        [string]$Url,
        [string]$Body = $null,
        [int]$ExpectedStatus = 200
    )
    
    Write-Host "  Testing: $Name..." -NoNewline
    
    try {
        $headers = @{ "Content-Type" = "application/json" }
        
        if ($Body) {
            $response = Invoke-WebRequest -Uri $Url -Method $Method -Body $Body -Headers $headers -UseBasicParsing
        } else {
            $response = Invoke-WebRequest -Uri $Url -Method $Method -UseBasicParsing
        }
        
        if ($response.StatusCode -eq $ExpectedStatus) {
            Write-Host " ✓ PASS" -ForegroundColor Green
            $script:testsPassed++
            return $response.Content | ConvertFrom-Json
        } else {
            Write-Host " ✗ FAIL (Status: $($response.StatusCode))" -ForegroundColor Red
            $script:testsFailed++
            return $null
        }
    } catch {
        Write-Host " ✗ FAIL ($($_.Exception.Message))" -ForegroundColor Red
        $script:testsFailed++
        return $null
    }
}

function Start-TestServer {
    param(
        [string]$Config,
        [string]$Args = ""
    )
    
    Write-Host "`n[$Config] Starting server..." -ForegroundColor Yellow
    
    $process = Start-Process -FilePath "dotnet" -ArgumentList "run -- $Args" -PassThru -NoNewWindow
    Start-Sleep -Seconds 3
    
    return $process
}

function Stop-TestServer {
    param($Process)
    
    if ($Process -and !$Process.HasExited) {
        Stop-Process -Id $Process.Id -Force
        Start-Sleep -Seconds 1
    }
}

function Run-OrderWorkflow {
    param([string]$ConfigName)
    
    Write-Host "`n╔══════════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
    Write-Host "║  Testing: $ConfigName" -ForegroundColor Cyan
    Write-Host "╚══════════════════════════════════════════════════════════════╝" -ForegroundColor Cyan
    
    # Test system info
    $info = Test-Endpoint "System Info" "GET" "$baseUrl/"
    if ($info) {
        Write-Host "    Node: $($info.node), Mode: $($info.mode)" -ForegroundColor Gray
    }
    
    # Test health
    Test-Endpoint "Health Check" "GET" "$baseUrl/health"
    
    # Create order
    $createBody = @{
        customerId = "test-customer-$(Get-Random)"
        items = @(
            @{
                productId = "prod-1"
                name = "Test Product"
                quantity = 2
                price = 49.99
            }
        )
    } | ConvertTo-Json
    
    $order = Test-Endpoint "Create Order" "POST" "$baseUrl/orders" $createBody 201
    
    if ($order) {
        $orderId = $order.orderId
        Write-Host "    Order ID: $orderId" -ForegroundColor Gray
        
        # Get order
        Test-Endpoint "Get Order" "GET" "$baseUrl/orders/$orderId"
        
        # Pay order
        $payBody = @{ paymentMethod = "credit_card" } | ConvertTo-Json
        Test-Endpoint "Pay Order" "POST" "$baseUrl/orders/$orderId/pay" $payBody
        
        # Ship order
        $shipBody = @{ trackingNumber = "TRACK-$(Get-Random)" } | ConvertTo-Json
        Test-Endpoint "Ship Order" "POST" "$baseUrl/orders/$orderId/ship" $shipBody
        
        # Get history
        Test-Endpoint "Get History" "GET" "$baseUrl/orders/$orderId/history"
        
        # Get all orders
        Test-Endpoint "List Orders" "GET" "$baseUrl/orders"
        
        # Get stats
        Test-Endpoint "Get Stats" "GET" "$baseUrl/stats"
    }
}

# Test 1: InMemory
Write-Host "`n[1/4] Testing InMemory Configuration" -ForegroundColor Magenta
$process1 = Start-TestServer "InMemory" "--port 5000"
try {
    Run-OrderWorkflow "InMemory (Standalone)"
} finally {
    Stop-TestServer $process1
}

# Test 2: Redis (if available)
Write-Host "`n[2/4] Testing Redis Configuration" -ForegroundColor Magenta
Write-Host "Checking Redis availability..." -NoNewline
try {
    $redisTest = Test-Connection -ComputerName localhost -Port 6379 -Count 1 -ErrorAction SilentlyContinue
    if ($redisTest) {
        Write-Host " ✓ Available" -ForegroundColor Green
        $process2 = Start-TestServer "Redis" "--transport redis --persistence redis --port 5000"
        try {
            Run-OrderWorkflow "Redis (Transport + Persistence)"
        } finally {
            Stop-TestServer $process2
        }
    } else {
        Write-Host " ✗ Not available (skipping)" -ForegroundColor Yellow
    }
} catch {
    Write-Host " ✗ Not available (skipping)" -ForegroundColor Yellow
}

# Test 3: NATS (if available)
Write-Host "`n[3/4] Testing NATS Configuration" -ForegroundColor Magenta
Write-Host "Checking NATS availability..." -NoNewline
try {
    $natsTest = Test-Connection -ComputerName localhost -Port 4222 -Count 1 -ErrorAction SilentlyContinue
    if ($natsTest) {
        Write-Host " ✓ Available" -ForegroundColor Green
        $process3 = Start-TestServer "NATS" "--transport nats --persistence nats --port 5000"
        try {
            Run-OrderWorkflow "NATS (Transport + Persistence)"
        } finally {
            Stop-TestServer $process3
        }
    } else {
        Write-Host " ✗ Not available (skipping)" -ForegroundColor Yellow
    }
} catch {
    Write-Host " ✗ Not available (skipping)" -ForegroundColor Yellow
}

# Test 4: Cluster Mode (if Redis available)
Write-Host "`n[4/4] Testing Cluster Mode" -ForegroundColor Magenta
try {
    $redisTest = Test-Connection -ComputerName localhost -Port 6379 -Count 1 -ErrorAction SilentlyContinue
    if ($redisTest) {
        Write-Host "Starting 3-node cluster..." -ForegroundColor Yellow
        
        $node1 = Start-Process -FilePath "dotnet" -ArgumentList "run -- --cluster --node-id node1 --port 5001 --transport redis --persistence redis" -PassThru -NoNewWindow
        Start-Sleep -Seconds 3
        
        $node2 = Start-Process -FilePath "dotnet" -ArgumentList "run -- --cluster --node-id node2 --port 5002 --transport redis --persistence redis" -PassThru -NoNewWindow
        Start-Sleep -Seconds 2
        
        $node3 = Start-Process -FilePath "dotnet" -ArgumentList "run -- --cluster --node-id node3 --port 5003 --transport redis --persistence redis" -PassThru -NoNewWindow
        Start-Sleep -Seconds 2
        
        try {
            # Test each node
            foreach ($port in @(5001, 5002, 5003)) {
                Write-Host "`n  Testing Node on port $port" -ForegroundColor Cyan
                $nodeUrl = "http://localhost:$port"
                
                Test-Endpoint "Node Health" "GET" "$nodeUrl/health"
                
                $createBody = @{
                    customerId = "cluster-test-$(Get-Random)"
                    items = @(@{ productId = "p1"; name = "Item"; quantity = 1; price = 10.0 })
                } | ConvertTo-Json
                
                Test-Endpoint "Create Order (Node $port)" "POST" "$nodeUrl/orders" $createBody 201
            }
            
            # Verify data consistency across nodes
            Write-Host "`n  Verifying cluster consistency..." -ForegroundColor Cyan
            $orders1 = (Invoke-WebRequest -Uri "http://localhost:5001/orders" -UseBasicParsing).Content | ConvertFrom-Json
            $orders2 = (Invoke-WebRequest -Uri "http://localhost:5002/orders" -UseBasicParsing).Content | ConvertFrom-Json
            $orders3 = (Invoke-WebRequest -Uri "http://localhost:5003/orders" -UseBasicParsing).Content | ConvertFrom-Json
            
            if ($orders1.Count -eq $orders2.Count -and $orders2.Count -eq $orders3.Count) {
                Write-Host "    ✓ All nodes have consistent data ($($orders1.Count) orders)" -ForegroundColor Green
                $script:testsPassed++
            } else {
                Write-Host "    ✗ Data inconsistency detected" -ForegroundColor Red
                $script:testsFailed++
            }
            
        } finally {
            Stop-TestServer $node1
            Stop-TestServer $node2
            Stop-TestServer $node3
        }
    } else {
        Write-Host "Redis not available (skipping cluster test)" -ForegroundColor Yellow
    }
} catch {
    Write-Host "Cluster test failed: $($_.Exception.Message)" -ForegroundColor Red
}

# Summary
Write-Host "`n╔══════════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║                      Test Summary                            ║" -ForegroundColor Cyan
Write-Host "╚══════════════════════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host "  Passed: $testsPassed" -ForegroundColor Green
Write-Host "  Failed: $testsFailed" -ForegroundColor $(if ($testsFailed -eq 0) { "Green" } else { "Red" })
Write-Host ""

if ($testsFailed -eq 0) {
    Write-Host "✓ All tests passed!" -ForegroundColor Green
    exit 0
} else {
    Write-Host "✗ Some tests failed" -ForegroundColor Red
    exit 1
}
