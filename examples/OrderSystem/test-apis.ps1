#!/usr/bin/env pwsh
# OrderSystem API 综合测试脚本

param(
    [string]$BaseUrl = "http://localhost:5000"
)

$ErrorActionPreference = "Stop"

Write-Host "=====================================" -ForegroundColor Cyan
Write-Host "  OrderSystem API 综合测试" -ForegroundColor Cyan
Write-Host "  Base URL: $BaseUrl" -ForegroundColor Cyan
Write-Host "=====================================" -ForegroundColor Cyan

function Test-Endpoint {
    param(
        [string]$Name,
        [scriptblock]$Test
    )
    
    Write-Host "`n>>> $Name" -ForegroundColor Yellow
    try {
        & $Test
        Write-Host "✓ $Name 成功" -ForegroundColor Green
        return $true
    }
    catch {
        Write-Host "✗ $Name 失败: $_" -ForegroundColor Red
        return $false
    }
}

$passed = 0
$failed = 0

# 1. 测试系统信息
if (Test-Endpoint "系统信息" {
    $info = Invoke-RestMethod -Uri "$BaseUrl/" -Method Get
    if (-not $info.mode) { throw "Missing mode" }
    Write-Host "  Mode: $($info.mode)" -ForegroundColor Gray
    Write-Host "  Transport: $($info.transport)" -ForegroundColor Gray
    Write-Host "  Persistence: $($info.persistence)" -ForegroundColor Gray
}) { $passed++ } else { $failed++ }

# 2. 测试健康检查
if (Test-Endpoint "健康检查" {
    $health = Invoke-RestMethod -Uri "$BaseUrl/health" -Method Get
    if ($health -ne "Healthy") { throw "Not healthy: $health" }
    Write-Host "  Status: $health" -ForegroundColor Gray
}) { $passed++ } else { $failed++ }

# 3. 测试统计信息
if (Test-Endpoint "统计信息（初始）" {
    $stats = Invoke-RestMethod -Uri "$BaseUrl/stats" -Method Get
    Write-Host "  Total Orders: $($stats.totalOrders)" -ForegroundColor Gray
    Write-Host "  Total Revenue: $($stats.totalRevenue)" -ForegroundColor Gray
}) { $passed++ } else { $failed++ }

# 4. 创建订单
$orderId1 = $null
if (Test-Endpoint "创建订单 #1" {
    $body = @{
        customerId = "customer-test-1"
        items = @(
            @{
                productId = "product-1"
                quantity = 2
                price = 100.0
            }
        )
    } | ConvertTo-Json
    
    $order = Invoke-RestMethod -Uri "$BaseUrl/orders" -Method Post -Body $body -ContentType "application/json"
    $script:orderId1 = $order.orderId
    if (-not $orderId1) { throw "No orderId returned" }
    Write-Host "  Order ID: $orderId1" -ForegroundColor Gray
    Write-Host "  Status: $($order.status)" -ForegroundColor Gray
    Write-Host "  Total: $($order.totalAmount)" -ForegroundColor Gray
}) { $passed++ } else { $failed++ }

# 5. 获取订单列表
if (Test-Endpoint "获取订单列表" {
    $orders = Invoke-RestMethod -Uri "$BaseUrl/orders" -Method Get
    if ($orders.Count -eq 0) { throw "No orders found" }
    Write-Host "  Found $($orders.Count) order(s)" -ForegroundColor Gray
}) { $passed++ } else { $failed++ }

# 6. 获取单个订单
if (Test-Endpoint "获取订单详情" {
    if (-not $orderId1) { throw "No orderId1 available" }
    $order = Invoke-RestMethod -Uri "$BaseUrl/orders/$orderId1" -Method Get
    if ($order.id -ne $orderId1) { throw "Order ID mismatch: expected $orderId1, got $($order.id)" }
    Write-Host "  Order ID: $($order.id)" -ForegroundColor Gray
    Write-Host "  Status: $($order.status)" -ForegroundColor Gray
}) { $passed++ } else { $failed++ }

# 7. 支付订单
if (Test-Endpoint "支付订单" {
    if (-not $orderId1) { throw "No orderId1 available" }
    $result = Invoke-RestMethod -Uri "$BaseUrl/orders/$orderId1/pay" -Method Post
    if ($result.status -ne "Paid") { throw "Status not Paid: $($result.status)" }
    Write-Host "  New Status: $($result.status)" -ForegroundColor Gray
}) { $passed++ } else { $failed++ }

# 8. 发货订单
if (Test-Endpoint "发货订单" {
    if (-not $orderId1) { throw "No orderId1 available" }
    $result = Invoke-RestMethod -Uri "$BaseUrl/orders/$orderId1/ship" -Method Post
    if ($result.status -ne "Shipped") { throw "Status not Shipped: $($result.status)" }
    Write-Host "  New Status: $($result.status)" -ForegroundColor Gray
}) { $passed++ } else { $failed++ }

# 9. 获取订单历史
if (Test-Endpoint "获取订单历史" {
    if (-not $orderId1) { throw "No orderId1 available" }
    $history = Invoke-RestMethod -Uri "$BaseUrl/orders/$orderId1/history" -Method Get
    if ($history.Count -lt 3) { throw "Expected at least 3 events, got $($history.Count)" }
    Write-Host "  Events: $($history.Count)" -ForegroundColor Gray
    foreach ($event in $history) {
        Write-Host "    - $($event.eventType)" -ForegroundColor DarkGray
    }
}) { $passed++ } else { $failed++ }

# 10. 创建第二个订单
$orderId2 = $null
if (Test-Endpoint "创建订单 #2" {
    $body = @{
        customerId = "customer-test-2"
        items = @(
            @{
                productId = "product-2"
                quantity = 1
                price = 50.0
            }
        )
    } | ConvertTo-Json
    
    $order = Invoke-RestMethod -Uri "$BaseUrl/orders" -Method Post -Body $body -ContentType "application/json"
    $script:orderId2 = $order.orderId
    if (-not $orderId2) { throw "No orderId returned" }
    Write-Host "  Order ID: $orderId2" -ForegroundColor Gray
}) { $passed++ } else { $failed++ }

# 11. 取消订单
if (Test-Endpoint "取消订单" {
    if (-not $orderId2) { throw "No orderId2 available" }
    $result = Invoke-RestMethod -Uri "$BaseUrl/orders/$orderId2/cancel" -Method Post
    if ($result.status -ne "Cancelled") { throw "Status not Cancelled: $($result.status)" }
    Write-Host "  New Status: $($result.status)" -ForegroundColor Gray
}) { $passed++ } else { $failed++ }

# 12. 最终统计
if (Test-Endpoint "统计信息（最终）" {
    $stats = Invoke-RestMethod -Uri "$BaseUrl/stats" -Method Get
    if ($stats.totalOrders -lt 2) { throw "Expected at least 2 orders" }
    Write-Host "  Total Orders: $($stats.totalOrders)" -ForegroundColor Gray
    Write-Host "  Total Revenue: $($stats.totalRevenue)" -ForegroundColor Gray
    Write-Host "  By Status:" -ForegroundColor Gray
    foreach ($status in $stats.byStatus.PSObject.Properties) {
        Write-Host "    $($status.Name): $($status.Value)" -ForegroundColor DarkGray
    }
}) { $passed++ } else { $failed++ }

# 总结
Write-Host "`n=====================================" -ForegroundColor Cyan
Write-Host "  测试完成" -ForegroundColor Cyan
Write-Host "  通过: $passed" -ForegroundColor Green
Write-Host "  失败: $failed" -ForegroundColor $(if ($failed -gt 0) { "Red" } else { "Green" })
Write-Host "=====================================" -ForegroundColor Cyan

if ($failed -gt 0) {
    exit 1
}
