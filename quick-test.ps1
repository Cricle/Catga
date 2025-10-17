#!/usr/bin/env pwsh
# 快速测试脚本 - 用于已运行的服务

$baseUrl = "http://localhost:5000"

Write-Host "⚡ Catga OrderSystem 快速测试" -ForegroundColor Cyan
Write-Host ""

# 检查服务是否运行
try {
    $health = Invoke-WebRequest -Uri "$baseUrl/health" -Method GET -TimeoutSec 5
    Write-Host "✅ 服务运行中" -ForegroundColor Green
}
catch {
    Write-Host "❌ 服务未运行，请先启动服务:" -ForegroundColor Red
    Write-Host "   cd examples/OrderSystem.AppHost && dotnet run" -ForegroundColor Yellow
    exit 1
}

Write-Host ""
Write-Host "🧪 测试核心功能..." -ForegroundColor Cyan
Write-Host ""

$passed = 0
$failed = 0

# 1. 创建订单
Write-Host "1️⃣  创建订单..." -NoNewline
try {
    $order = @{
        customerId = "CUST-$(Get-Random -Maximum 9999)"
        items = @(
            @{ productId = "PROD-001"; quantity = 1; price = 99.99 }
        )
        shippingAddress = "Test Address"
        paymentMethod = "CreditCard"
    }
    
    $response = Invoke-RestMethod -Uri "$baseUrl/api/orders" -Method POST -Body ($order | ConvertTo-Json) -ContentType "application/json" -TimeoutSec 10
    
    if ($response.orderId) {
        Write-Host " ✅" -ForegroundColor Green
        $orderId = $response.orderId
        $passed++
    }
    else {
        Write-Host " ❌ (无订单ID)" -ForegroundColor Red
        $failed++
    }
}
catch {
    Write-Host " ❌ $($_.Exception.Message)" -ForegroundColor Red
    $failed++
}

# 2. 查询订单
if ($orderId) {
    Write-Host "2️⃣  查询订单..." -NoNewline
    try {
        $response = Invoke-RestMethod -Uri "$baseUrl/api/orders/$orderId" -Method GET -TimeoutSec 10
        if ($response.orderId -eq $orderId) {
            Write-Host " ✅" -ForegroundColor Green
            $passed++
        }
        else {
            Write-Host " ❌ (订单ID不匹配)" -ForegroundColor Red
            $failed++
        }
    }
    catch {
        Write-Host " ❌ $($_.Exception.Message)" -ForegroundColor Red
        $failed++
    }
}

# 3. Debugger API
Write-Host "3️⃣  Debugger API..." -NoNewline
try {
    $flows = Invoke-RestMethod -Uri "$baseUrl/debug-api/flows" -Method GET -TimeoutSec 10
    if ($flows.flows) {
        Write-Host " ✅ ($($flows.flows.Count) 个流)" -ForegroundColor Green
        $passed++
    }
    else {
        Write-Host " ❌ (无流数据)" -ForegroundColor Red
        $failed++
    }
}
catch {
    Write-Host " ❌ $($_.Exception.Message)" -ForegroundColor Red
    $failed++
}

# 4. 页面访问
Write-Host "4️⃣  页面访问..." -NoNewline
try {
    $page = Invoke-WebRequest -Uri "$baseUrl/index.html" -Method GET -TimeoutSec 10
    if ($page.StatusCode -eq 200) {
        Write-Host " ✅" -ForegroundColor Green
        $passed++
    }
    else {
        Write-Host " ❌" -ForegroundColor Red
        $failed++
    }
}
catch {
    Write-Host " ❌ $($_.Exception.Message)" -ForegroundColor Red
    $failed++
}

# 5. Debugger UI
Write-Host "5️⃣  Debugger UI..." -NoNewline
try {
    $debugger = Invoke-WebRequest -Uri "$baseUrl/debugger/index.html" -Method GET -TimeoutSec 10
    if ($debugger.StatusCode -eq 200) {
        Write-Host " ✅" -ForegroundColor Green
        $passed++
    }
    else {
        Write-Host " ❌" -ForegroundColor Red
        $failed++
    }
}
catch {
    Write-Host " ❌ $($_.Exception.Message)" -ForegroundColor Red
    $failed++
}

# 结果
Write-Host ""
Write-Host "─────────────────────────────────" -ForegroundColor Gray
$total = $passed + $failed
$rate = if ($total -gt 0) { [math]::Round(($passed / $total) * 100, 1) } else { 0 }

if ($failed -eq 0) {
    Write-Host "🎉 全部通过! ($passed/$total, $rate%)" -ForegroundColor Green
    exit 0
}
else {
    Write-Host "⚠️  通过: $passed/$total ($rate%)" -ForegroundColor Yellow
    Write-Host "   失败: $failed" -ForegroundColor Red
    Write-Host ""
    Write-Host "💡 运行完整测试: .\test-ordersystem-full.ps1" -ForegroundColor Cyan
    exit 1
}

