#!/usr/bin/env pwsh
# Catga OrderSystem 全面测试脚本
# 测试所有 API 端点和页面

$ErrorActionPreference = "Continue"
$baseUrl = "http://localhost:5000"
$debuggerUrl = "$baseUrl/debugger"
$apiUrl = "$baseUrl/api"

Write-Host "🧪 Catga OrderSystem 全面测试" -ForegroundColor Cyan
Write-Host "================================" -ForegroundColor Cyan
Write-Host ""

# 等待服务启动
function Wait-ForService {
    param([string]$Url, [int]$MaxRetries = 30)

    Write-Host "⏳ 等待服务启动: $Url" -ForegroundColor Yellow

    for ($i = 1; $i -le $MaxRetries; $i++) {
        try {
            $response = Invoke-WebRequest -Uri "$Url/health" -Method GET -TimeoutSec 2 -ErrorAction Stop
            if ($response.StatusCode -eq 200) {
                Write-Host "✅ 服务已启动!" -ForegroundColor Green
                return $true
            }
        }
        catch {
            Write-Host "   尝试 $i/$MaxRetries..." -ForegroundColor Gray
            Start-Sleep -Seconds 2
        }
    }

    Write-Host "❌ 服务启动超时" -ForegroundColor Red
    return $false
}

# 测试结果统计
$script:totalTests = 0
$script:passedTests = 0
$script:failedTests = 0
$script:failedTestDetails = @()

function Test-Endpoint {
    param(
        [string]$Name,
        [string]$Url,
        [string]$Method = "GET",
        [object]$Body = $null,
        [int]$ExpectedStatus = 200,
        [string]$ContentType = "application/json"
    )

    $script:totalTests++

    try {
        $params = @{
            Uri = $Url
            Method = $Method
            TimeoutSec = 10
            ErrorAction = "Stop"
        }

        if ($Body) {
            $params.Body = ($Body | ConvertTo-Json -Depth 10)
            $params.ContentType = $ContentType
        }

        $response = Invoke-WebRequest @params

        if ($response.StatusCode -eq $ExpectedStatus) {
            Write-Host "  ✅ $Name" -ForegroundColor Green
            $script:passedTests++
            return $true
        }
        else {
            Write-Host "  ❌ $Name (状态码: $($response.StatusCode), 期望: $ExpectedStatus)" -ForegroundColor Red
            $script:failedTests++
            $script:failedTestDetails += "❌ $Name - 状态码不匹配: $($response.StatusCode) != $ExpectedStatus"
            return $false
        }
    }
    catch {
        Write-Host "  ❌ $Name (错误: $($_.Exception.Message))" -ForegroundColor Red
        $script:failedTests++
        $script:failedTestDetails += "❌ $Name - $($_.Exception.Message)"
        return $false
    }
}

function Test-PageAccessible {
    param(
        [string]$Name,
        [string]$Url
    )

    $script:totalTests++

    try {
        $response = Invoke-WebRequest -Uri $Url -Method GET -TimeoutSec 10 -ErrorAction Stop

        if ($response.StatusCode -eq 200 -and $response.Content.Length -gt 0) {
            Write-Host "  ✅ $Name (大小: $($response.Content.Length) bytes)" -ForegroundColor Green
            $script:passedTests++
            return $true
        }
        else {
            Write-Host "  ❌ $Name (状态码: $($response.StatusCode))" -ForegroundColor Red
            $script:failedTests++
            $script:failedTestDetails += "❌ $Name - 页面无法访问"
            return $false
        }
    }
    catch {
        Write-Host "  ❌ $Name (错误: $($_.Exception.Message))" -ForegroundColor Red
        $script:failedTests++
        $script:failedTestDetails += "❌ $Name - $($_.Exception.Message)"
        return $false
    }
}

# 1. 测试健康检查
Write-Host ""
Write-Host "📊 1. 健康检查测试" -ForegroundColor Cyan
Write-Host "─────────────────────────────────────────" -ForegroundColor Gray

if (-not (Wait-ForService -Url $baseUrl)) {
    Write-Host ""
    Write-Host "❌ 服务未启动，请先运行: dotnet run --project examples/OrderSystem.AppHost" -ForegroundColor Red
    exit 1
}

Test-Endpoint -Name "Health Check (/health)" -Url "$baseUrl/health"
Test-Endpoint -Name "Liveness Check (/health/live)" -Url "$baseUrl/health/live"
Test-Endpoint -Name "Readiness Check (/health/ready)" -Url "$baseUrl/health/ready"

# 2. 测试 API 端点
Write-Host ""
Write-Host "🔌 2. API 端点测试" -ForegroundColor Cyan
Write-Host "─────────────────────────────────────────" -ForegroundColor Gray

# 2.1 创建订单 (成功场景)
$createOrderSuccess = @{
    customerId = "CUST-001"
    items = @(
        @{ productId = "PROD-001"; quantity = 2; price = 99.99 }
        @{ productId = "PROD-002"; quantity = 1; price = 49.99 }
    )
    shippingAddress = "123 Main St, City, Country"
    paymentMethod = "CreditCard"
}

$orderResult = $null
try {
    $response = Invoke-WebRequest -Uri "$apiUrl/orders" -Method POST -Body ($createOrderSuccess | ConvertTo-Json -Depth 10) -ContentType "application/json" -TimeoutSec 10
    if ($response.StatusCode -eq 200) {
        $orderResult = $response.Content | ConvertFrom-Json
        Write-Host "  ✅ 创建订单 - 成功场景 (订单ID: $($orderResult.orderId))" -ForegroundColor Green
        $script:passedTests++
        $script:totalTests++
    }
}
catch {
    Write-Host "  ❌ 创建订单 - 成功场景 (错误: $($_.Exception.Message))" -ForegroundColor Red
    $script:failedTests++
    $script:totalTests++
    $script:failedTestDetails += "❌ 创建订单(成功) - $($_.Exception.Message)"
}

# 2.2 创建订单 (失败场景 - 库存不足)
$createOrderFail = @{
    customerId = "CUST-002"
    items = @(
        @{ productId = "OUT-OF-STOCK"; quantity = 999; price = 99.99 }
    )
    shippingAddress = "456 Second St, City, Country"
    paymentMethod = "Alipay"
}

Test-Endpoint -Name "创建订单 - 失败场景 (库存不足)" -Url "$apiUrl/orders" -Method POST -Body $createOrderFail -ExpectedStatus 200

# 2.3 查询订单
if ($orderResult -and $orderResult.orderId) {
    Test-Endpoint -Name "查询订单 (ID: $($orderResult.orderId))" -Url "$apiUrl/orders/$($orderResult.orderId)"
}
else {
    Write-Host "  ⚠️  跳过查询订单测试 (无有效订单ID)" -ForegroundColor Yellow
}

# 2.4 取消订单
if ($orderResult -and $orderResult.orderId) {
    $cancelOrderBody = @{ orderId = $orderResult.orderId }
    Test-Endpoint -Name "取消订单 (ID: $($orderResult.orderId))" -Url "$apiUrl/orders/$($orderResult.orderId)/cancel" -Method POST -Body $cancelOrderBody
}
else {
    Write-Host "  ⚠️  跳过取消订单测试 (无有效订单ID)" -ForegroundColor Yellow
}

# 3. 测试 Debugger API
Write-Host ""
Write-Host "🔍 3. Debugger API 测试" -ForegroundColor Cyan
Write-Host "─────────────────────────────────────────" -ForegroundColor Gray

Test-Endpoint -Name "获取所有消息流 (/debug-api/flows)" -Url "$baseUrl/debug-api/flows"
Test-Endpoint -Name "获取统计信息 (/debug-api/stats)" -Url "$baseUrl/debug-api/stats"

# 获取第一个消息流的详细信息
try {
    $flowsResponse = Invoke-WebRequest -Uri "$baseUrl/debug-api/flows" -Method GET -TimeoutSec 10
    $flows = ($flowsResponse.Content | ConvertFrom-Json).flows

    if ($flows -and $flows.Count -gt 0) {
        $firstFlow = $flows[0]
        Test-Endpoint -Name "获取消息流详情 (ID: $($firstFlow.correlationId.Substring(0,8))...)" -Url "$baseUrl/debug-api/flows/$($firstFlow.correlationId)"
        Test-Endpoint -Name "获取流事件 (ID: $($firstFlow.correlationId.Substring(0,8))...)" -Url "$baseUrl/debug-api/flows/$($firstFlow.correlationId)/events"
    }
    else {
        Write-Host "  ⚠️  跳过流详情测试 (无可用消息流)" -ForegroundColor Yellow
    }
}
catch {
    Write-Host "  ⚠️  跳过流详情测试 (无法获取消息流列表)" -ForegroundColor Yellow
}

# 4. 测试页面访问
Write-Host ""
Write-Host "🌐 4. 页面访问测试" -ForegroundColor Cyan
Write-Host "─────────────────────────────────────────" -ForegroundColor Gray

Test-PageAccessible -Name "主页 (/)" -Url "$baseUrl/"
Test-PageAccessible -Name "OrderSystem UI (/index.html)" -Url "$baseUrl/index.html"
Test-PageAccessible -Name "Debugger 主页 (/debugger/index.html)" -Url "$debuggerUrl/index.html"
Test-PageAccessible -Name "时间旅行调试器 (/debugger/replay-player.html)" -Url "$debuggerUrl/replay-player.html"
Test-PageAccessible -Name "断点调试器 (/debugger/breakpoints.html)" -Url "$debuggerUrl/breakpoints.html"
Test-PageAccessible -Name "性能分析器 (/debugger/profiling.html)" -Url "$debuggerUrl/profiling.html"

# 5. 测试静态资源
Write-Host ""
Write-Host "📦 5. 静态资源测试" -ForegroundColor Cyan
Write-Host "─────────────────────────────────────────" -ForegroundColor Gray

Test-PageAccessible -Name "Alpine.js (OrderSystem)" -Url "$baseUrl/lib/alpine.min.js"
Test-PageAccessible -Name "Tailwind CSS (OrderSystem)" -Url "$baseUrl/lib/tailwind.js"
Test-PageAccessible -Name "SignalR (Debugger)" -Url "$debuggerUrl/../lib/signalr.min.js"
Test-PageAccessible -Name "Alpine.js (Debugger)" -Url "$debuggerUrl/../lib/alpine.min.js"
Test-PageAccessible -Name "Tailwind CSS (Debugger)" -Url "$debuggerUrl/../lib/tailwind.js"

# 6. 测试 Swagger
Write-Host ""
Write-Host "📚 6. Swagger 文档测试" -ForegroundColor Cyan
Write-Host "─────────────────────────────────────────" -ForegroundColor Gray

Test-PageAccessible -Name "Swagger UI (/swagger)" -Url "$baseUrl/swagger/index.html"
Test-Endpoint -Name "Swagger JSON (/swagger/v1/swagger.json)" -Url "$baseUrl/swagger/v1/swagger.json"

# 7. 测试 SignalR Hub
Write-Host ""
Write-Host "🔌 7. SignalR Hub 测试" -ForegroundColor Cyan
Write-Host "─────────────────────────────────────────" -ForegroundColor Gray

Test-Endpoint -Name "Debugger Hub 协商 (/debugger-hub/negotiate)" -Url "$baseUrl/debugger-hub/negotiate" -Method POST

# 最终统计
Write-Host ""
Write-Host "═══════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "📊 测试结果统计" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""
Write-Host "总测试数: $script:totalTests" -ForegroundColor White
Write-Host "通过: $script:passedTests ✅" -ForegroundColor Green
Write-Host "失败: $script:failedTests ❌" -ForegroundColor Red
$passRate = if ($script:totalTests -gt 0) { [math]::Round(($script:passedTests / $script:totalTests) * 100, 2) } else { 0 }
Write-Host "通过率: $passRate%" -ForegroundColor $(if ($passRate -ge 90) { "Green" } elseif ($passRate -ge 70) { "Yellow" } else { "Red" })
Write-Host ""

if ($script:failedTests -gt 0) {
    Write-Host "失败的测试详情:" -ForegroundColor Red
    Write-Host "─────────────────────────────────────────" -ForegroundColor Gray
    foreach ($detail in $script:failedTestDetails) {
        Write-Host "  $detail" -ForegroundColor Red
    }
    Write-Host ""
}

# 测试摘要
Write-Host "测试摘要:" -ForegroundColor Cyan
Write-Host "  • 健康检查: 3 个端点" -ForegroundColor Gray
Write-Host "  • API 端点: 订单创建/查询/取消" -ForegroundColor Gray
Write-Host "  • Debugger API: 消息流/统计/事件" -ForegroundColor Gray
Write-Host "  • 页面访问: 主页/UI/Debugger" -ForegroundColor Gray
Write-Host "  • 静态资源: JS/CSS 库文件" -ForegroundColor Gray
Write-Host "  • Swagger: API 文档" -ForegroundColor Gray
Write-Host "  • SignalR: 实时通信" -ForegroundColor Gray
Write-Host ""

# 建议
if ($script:failedTests -gt 0) {
    Write-Host "💡 建议:" -ForegroundColor Yellow
    Write-Host "  1. 检查服务是否完全启动" -ForegroundColor Gray
    Write-Host "  2. 查看应用日志获取详细错误信息" -ForegroundColor Gray
    Write-Host "  3. 确认所有依赖服务正常运行" -ForegroundColor Gray
    Write-Host "  4. 检查端口 5000 是否被其他程序占用" -ForegroundColor Gray
    Write-Host ""
    exit 1
}
else {
    Write-Host "🎉 所有测试通过！系统运行正常！" -ForegroundColor Green
    Write-Host ""
    Write-Host "可访问的 URL:" -ForegroundColor Cyan
    Write-Host "  • OrderSystem UI: $baseUrl" -ForegroundColor White
    Write-Host "  • Catga Debugger: $debuggerUrl/index.html" -ForegroundColor White
    Write-Host "  • Swagger API: $baseUrl/swagger" -ForegroundColor White
    Write-Host "  • Aspire Dashboard: http://localhost:15888" -ForegroundColor White
    Write-Host ""
    exit 0
}

