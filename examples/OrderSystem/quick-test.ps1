#!/usr/bin/env pwsh
# Quick test script for OrderSystem

Write-Host "╔══════════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║           Catga OrderSystem - Quick Test                    ║" -ForegroundColor Cyan
Write-Host "╚══════════════════════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

$baseUrl = "http://localhost:5000"

Write-Host "Testing OrderSystem at $baseUrl..." -ForegroundColor Yellow
Write-Host ""

# Test 1: System Info
Write-Host "[1/7] System Info..." -NoNewline
try {
    $info = Invoke-RestMethod -Uri "$baseUrl/" -Method GET
    Write-Host " ✓" -ForegroundColor Green
    Write-Host "      Node: $($info.node), Mode: $($info.mode), Transport: $($info.transport)" -ForegroundColor Gray
} catch {
    Write-Host " ✗ FAILED" -ForegroundColor Red
    Write-Host "      Error: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

# Test 2: Health Check
Write-Host "[2/7] Health Check..." -NoNewline
try {
    $health = Invoke-RestMethod -Uri "$baseUrl/health" -Method GET
    Write-Host " ✓" -ForegroundColor Green
} catch {
    Write-Host " ✗ FAILED" -ForegroundColor Red
    exit 1
}

# Test 3: Create Order
Write-Host "[3/7] Create Order..." -NoNewline
try {
    $createBody = @{
        customerId = "customer-$(Get-Random)"
        items = @(
            @{
                productId = "prod-1"
                name = "Test Product"
                quantity = 2
                price = 49.99
            }
        )
    } | ConvertTo-Json -Depth 10
    
    $order = Invoke-RestMethod -Uri "$baseUrl/orders" -Method POST -Body $createBody -ContentType "application/json"
    $orderId = $order.orderId
    Write-Host " ✓" -ForegroundColor Green
    Write-Host "      Order ID: $orderId, Total: `$$($order.total)" -ForegroundColor Gray
} catch {
    Write-Host " ✗ FAILED" -ForegroundColor Red
    Write-Host "      Error: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

# Test 4: Get Order
Write-Host "[4/7] Get Order..." -NoNewline
try {
    $retrieved = Invoke-RestMethod -Uri "$baseUrl/orders/$orderId" -Method GET
    Write-Host " ✓" -ForegroundColor Green
    Write-Host "      Status: $($retrieved.status), Items: $($retrieved.items.Count)" -ForegroundColor Gray
} catch {
    Write-Host " ✗ FAILED" -ForegroundColor Red
    exit 1
}

# Test 5: Pay Order
Write-Host "[5/7] Pay Order..." -NoNewline
try {
    $payBody = @{ paymentMethod = "credit_card" } | ConvertTo-Json
    Invoke-RestMethod -Uri "$baseUrl/orders/$orderId/pay" -Method POST -Body $payBody -ContentType "application/json" | Out-Null
    Write-Host " ✓" -ForegroundColor Green
} catch {
    Write-Host " ✗ FAILED" -ForegroundColor Red
    exit 1
}

# Test 6: Get Event History
Write-Host "[6/7] Get Event History..." -NoNewline
try {
    $history = Invoke-RestMethod -Uri "$baseUrl/orders/$orderId/history" -Method GET
    Write-Host " ✓" -ForegroundColor Green
    Write-Host "      Events: $($history.Count)" -ForegroundColor Gray
} catch {
    Write-Host " ✗ FAILED" -ForegroundColor Red
    exit 1
}

# Test 7: Get Statistics
Write-Host "[7/7] Get Statistics..." -NoNewline
try {
    $stats = Invoke-RestMethod -Uri "$baseUrl/stats" -Method GET
    Write-Host " ✓" -ForegroundColor Green
    Write-Host "      Total Orders: $($stats.totalOrders), Revenue: `$$($stats.totalRevenue)" -ForegroundColor Gray
} catch {
    Write-Host " ✗ FAILED" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "╔══════════════════════════════════════════════════════════════╗" -ForegroundColor Green
Write-Host "║                  All Tests Passed! ✓                         ║" -ForegroundColor Green
Write-Host "╚══════════════════════════════════════════════════════════════╝" -ForegroundColor Green
Write-Host ""
Write-Host "OrderSystem is working correctly!" -ForegroundColor Green
Write-Host ""
Write-Host "Try these commands:" -ForegroundColor Yellow
Write-Host "  curl http://localhost:5000/" -ForegroundColor Gray
Write-Host "  curl http://localhost:5000/orders" -ForegroundColor Gray
Write-Host "  curl http://localhost:5000/stats" -ForegroundColor Gray
