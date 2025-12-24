#!/usr/bin/env pwsh
<#
.SYNOPSIS
    OrderSystem API 自动化测试脚本
.DESCRIPTION
    自动验证所有 API 端点，确保系统正常运行
.PARAMETER BaseUrl
    API 基础 URL，默认为 http://localhost:5000
.PARAMETER Verbose
    显示详细输出
.EXAMPLE
    .\test-api.ps1
.EXAMPLE
    .\test-api.ps1 -BaseUrl "http://localhost:8080"
#>

param(
    [string]$BaseUrl = "http://localhost:5000",
    [switch]$Verbose
)

# 颜色输出函数
function Write-Success {
    param([string]$Message)
    Write-Host "✓ $Message" -ForegroundColor Green
}

function Write-Error-Custom {
    param([string]$Message)
    Write-Host "✗ $Message" -ForegroundColor Red
}

function Write-Info {
    param([string]$Message)
    Write-Host "ℹ $Message" -ForegroundColor Cyan
}

function Write-Warning-Custom {
    param([string]$Message)
    Write-Host "⚠ $Message" -ForegroundColor Yellow
}

# 测试结果统计
$script:TotalTests = 0
$script:PassedTests = 0
$script:FailedTests = 0
$script:TestResults = @()

# 测试函数
function Test-Endpoint {
    param(
        [string]$Name,
        [string]$Method,
        [string]$Endpoint,
        [hashtable]$Body = $null,
        [scriptblock]$Validator = $null,
        [string]$Description = ""
    )
    
    $script:TotalTests++
    $url = "$BaseUrl$Endpoint"
    
    Write-Host "`n[$script:TotalTests] 测试: $Name" -ForegroundColor Yellow
    if ($Description) {
        Write-Host "   描述: $Description" -ForegroundColor Gray
    }
    Write-Host "   方法: $Method $url" -ForegroundColor Gray
    
    try {
        $params = @{
            Uri = $url
            Method = $Method
            ContentType = "application/json"
            TimeoutSec = 10
        }
        
        if ($Body) {
            $params.Body = ($Body | ConvertTo-Json -Depth 10)
            if ($Verbose) {
                Write-Host "   请求体: $($params.Body)" -ForegroundColor Gray
            }
        }
        
        $response = Invoke-RestMethod @params -ErrorAction Stop
        
        if ($Verbose) {
            Write-Host "   响应: $($response | ConvertTo-Json -Depth 5)" -ForegroundColor Gray
        }
        
        # 执行自定义验证
        if ($Validator) {
            $validationResult = & $Validator $response
            if ($validationResult -eq $false) {
                throw "验证失败"
            }
        }
        
        $script:PassedTests++
        Write-Success "通过"
        $script:TestResults += [PSCustomObject]@{
            Name = $Name
            Status = "PASS"
            Method = $Method
            Endpoint = $Endpoint
            Message = "成功"
        }
        
        return $response
        
    } catch {
        $script:FailedTests++
        $errorMsg = $_.Exception.Message
        Write-Error-Custom "失败: $errorMsg"
        $script:TestResults += [PSCustomObject]@{
            Name = $Name
            Status = "FAIL"
            Method = $Method
            Endpoint = $Endpoint
            Message = $errorMsg
        }
        
        return $null
    }
}

# 打印横幅
function Show-Banner {
    Write-Host @"

╔══════════════════════════════════════════════════════════════╗
║          Catga OrderSystem - API 自动化测试                  ║
╠══════════════════════════════════════════════════════════════╣
║  基础 URL: $($BaseUrl.PadRight(48)) ║
║  时间: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")                        ║
╚══════════════════════════════════════════════════════════════╝

"@ -ForegroundColor Cyan
}

# 打印测试报告
function Show-Report {
    $passRate = if ($script:TotalTests -gt 0) { 
        [math]::Round(($script:PassedTests / $script:TotalTests) * 100, 2) 
    } else { 
        0 
    }
    
    Write-Host "`n`n╔══════════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
    Write-Host "║                      测试报告                                ║" -ForegroundColor Cyan
    Write-Host "╠══════════════════════════════════════════════════════════════╣" -ForegroundColor Cyan
    Write-Host "║  总测试数: $($script:TotalTests.ToString().PadRight(48)) ║" -ForegroundColor Cyan
    Write-Host "║  通过: $($script:PassedTests.ToString().PadRight(52)) ║" -ForegroundColor Green
    Write-Host "║  失败: $($script:FailedTests.ToString().PadRight(52)) ║" -ForegroundColor $(if ($script:FailedTests -gt 0) { "Red" } else { "Cyan" })
    Write-Host "║  通过率: $($passRate.ToString() + "%")".PadRight(50) "║" -ForegroundColor $(if ($passRate -eq 100) { "Green" } elseif ($passRate -ge 80) { "Yellow" } else { "Red" })
    Write-Host "╚══════════════════════════════════════════════════════════════╝" -ForegroundColor Cyan
    
    if ($script:FailedTests -gt 0) {
        Write-Host "`n失败的测试:" -ForegroundColor Red
        $script:TestResults | Where-Object { $_.Status -eq "FAIL" } | ForEach-Object {
            Write-Host "  • $($_.Name): $($_.Message)" -ForegroundColor Red
        }
    }
    
    Write-Host ""
}

# 检查服务是否运行
function Test-ServiceAvailability {
    Write-Info "检查服务可用性..."
    try {
        $response = Invoke-RestMethod -Uri $BaseUrl -Method Get -TimeoutSec 5 -ErrorAction Stop
        Write-Success "服务正在运行"
        return $true
    } catch {
        Write-Error-Custom "服务不可用: $($_.Exception.Message)"
        Write-Warning-Custom "请确保 OrderSystem 正在运行在 $BaseUrl"
        return $false
    }
}

# 主测试流程
function Start-Tests {
    Show-Banner
    
    # 检查服务可用性
    if (-not (Test-ServiceAvailability)) {
        Write-Host "`n提示: 启动服务命令:" -ForegroundColor Yellow
        Write-Host "  cd examples/OrderSystem" -ForegroundColor Gray
        Write-Host "  dotnet run" -ForegroundColor Gray
        exit 1
    }
    
    Write-Host "`n开始执行 API 测试..." -ForegroundColor Cyan
    Write-Host "=" * 64
    
    # 1. 测试系统信息端点
    $systemInfo = Test-Endpoint `
        -Name "获取系统信息" `
        -Method "GET" `
        -Endpoint "/" `
        -Description "验证系统基本信息" `
        -Validator {
            param($response)
            return ($response.service -and $response.node -and $response.status -eq "running")
        }
    
    # 2. 测试健康检查端点
    Test-Endpoint `
        -Name "健康检查 (全部)" `
        -Method "GET" `
        -Endpoint "/health" `
        -Description "验证系统整体健康状态"
    
    Test-Endpoint `
        -Name "健康检查 (就绪)" `
        -Method "GET" `
        -Endpoint "/health/ready" `
        -Description "验证系统就绪状态"
    
    Test-Endpoint `
        -Name "健康检查 (存活)" `
        -Method "GET" `
        -Endpoint "/health/live" `
        -Description "验证系统存活状态"
    
    # 3. 测试统计信息端点
    $stats = Test-Endpoint `
        -Name "获取统计信息" `
        -Method "GET" `
        -Endpoint "/stats" `
        -Description "验证订单统计数据" `
        -Validator {
            param($response)
            return ($null -ne $response.totalOrders -and $null -ne $response.totalRevenue)
        }
    
    # 4. 测试订单列表端点
    $ordersBefore = Test-Endpoint `
        -Name "获取订单列表 (创建前)" `
        -Method "GET" `
        -Endpoint "/orders" `
        -Description "验证订单列表查询"
    
    $initialOrderCount = if ($ordersBefore) { $ordersBefore.Count } else { 0 }
    Write-Info "当前订单数: $initialOrderCount"
    
    # 5. 测试创建订单
    $newOrder = Test-Endpoint `
        -Name "创建订单" `
        -Method "POST" `
        -Endpoint "/orders" `
        -Body @{
            customerId = "test-customer-$(Get-Date -Format 'yyyyMMddHHmmss')"
            items = @(
                @{
                    productId = "test-product-001"
                    name = "测试商品"
                    quantity = 2
                    price = 99.99
                }
            )
        } `
        -Description "创建新订单" `
        -Validator {
            param($response)
            return ($response.orderId -and $response.orderId.Length -gt 0)
        }
    
    if ($newOrder -and $newOrder.orderId) {
        $orderId = $newOrder.orderId
        Write-Info "创建的订单 ID: $orderId"
        
        # 6. 测试获取单个订单
        $order = Test-Endpoint `
            -Name "获取订单详情" `
            -Method "GET" `
            -Endpoint "/orders/$orderId" `
            -Description "通过 ID 查询订单" `
            -Validator {
                param($response)
                return ($response.id -eq $orderId -and $response.status -eq "Pending")
            }
        
        # 7. 测试支付订单
        $paidOrder = Test-Endpoint `
            -Name "支付订单" `
            -Method "POST" `
            -Endpoint "/orders/$orderId/pay" `
            -Description "标记订单为已支付" `
            -Validator {
                param($response)
                return ($response.status -eq "Paid" -and $response.paidAt)
            }
        
        # 8. 测试发货订单
        $shippedOrder = Test-Endpoint `
            -Name "发货订单" `
            -Method "POST" `
            -Endpoint "/orders/$orderId/ship" `
            -Description "标记订单为已发货" `
            -Validator {
                param($response)
                return ($response.status -eq "Shipped" -and $response.trackingNumber)
            }
        
        # 9. 测试获取订单历史
        $history = Test-Endpoint `
            -Name "获取订单历史" `
            -Method "GET" `
            -Endpoint "/orders/$orderId/history" `
            -Description "查询订单事件历史" `
            -Validator {
                param($response)
                return ($response.Count -ge 3)  # 至少有创建、支付、发货三个事件
            }
        
        if ($history) {
            Write-Info "订单事件数: $($history.Count)"
        }
        
        # 10. 创建另一个订单用于取消测试
        $cancelOrder = Test-Endpoint `
            -Name "创建订单 (用于取消)" `
            -Method "POST" `
            -Endpoint "/orders" `
            -Body @{
                customerId = "test-customer-cancel-$(Get-Date -Format 'yyyyMMddHHmmss')"
                items = @(
                    @{
                        productId = "test-product-002"
                        name = "待取消商品"
                        quantity = 1
                        price = 49.99
                    }
                )
            } `
            -Description "创建用于取消测试的订单"
        
        if ($cancelOrder -and $cancelOrder.orderId) {
            $cancelOrderId = $cancelOrder.orderId
            
            # 11. 测试取消订单
            Test-Endpoint `
                -Name "取消订单" `
                -Method "POST" `
                -Endpoint "/orders/$cancelOrderId/cancel" `
                -Description "取消待处理订单" `
                -Validator {
                    param($response)
                    return ($response.status -eq "Cancelled")
                }
        }
    }
    
    # 12. 验证订单列表已更新
    $ordersAfter = Test-Endpoint `
        -Name "获取订单列表 (创建后)" `
        -Method "GET" `
        -Endpoint "/orders" `
        -Description "验证新订单已添加到列表"
    
    if ($ordersAfter) {
        $finalOrderCount = $ordersAfter.Count
        Write-Info "最终订单数: $finalOrderCount"
        
        if ($finalOrderCount -gt $initialOrderCount) {
            Write-Success "订单数量增加了 $($finalOrderCount - $initialOrderCount) 个"
        }
    }
    
    # 13. 验证统计信息已更新
    $statsAfter = Test-Endpoint `
        -Name "获取统计信息 (更新后)" `
        -Method "GET" `
        -Endpoint "/stats" `
        -Description "验证统计数据已更新" `
        -Validator {
            param($response)
            if ($stats) {
                return ($response.totalOrders -ge $stats.totalOrders)
            }
            return $true
        }
    
    if ($statsAfter -and $stats) {
        Write-Info "订单总数: $($stats.totalOrders) → $($statsAfter.totalOrders)"
        Write-Info "总收入: ¥$($stats.totalRevenue) → ¥$($statsAfter.totalRevenue)"
    }
    
    # 14. 测试错误处理 - 获取不存在的订单
    Write-Host "`n[$script:TotalTests] 测试: 获取不存在的订单 (错误处理)" -ForegroundColor Yellow
    Write-Host "   描述: 验证 404 错误处理" -ForegroundColor Gray
    Write-Host "   方法: GET $BaseUrl/orders/non-existent-order-id" -ForegroundColor Gray
    
    $script:TotalTests++
    try {
        $response = Invoke-RestMethod -Uri "$BaseUrl/orders/non-existent-order-id" -Method Get -ErrorAction Stop
        # 如果没有抛出异常，说明返回了 200，这是不对的
        $script:FailedTests++
        Write-Error-Custom "失败: 应该返回 404，但返回了成功状态"
        $script:TestResults += [PSCustomObject]@{
            Name = "获取不存在的订单 (错误处理)"
            Status = "FAIL"
            Method = "GET"
            Endpoint = "/orders/non-existent-order-id"
            Message = "应该返回 404"
        }
    } catch {
        # 检查是否是 404 错误
        if ($_.Exception.Message -match "404") {
            $script:PassedTests++
            Write-Success "通过 (正确返回 404)"
            $script:TestResults += [PSCustomObject]@{
                Name = "获取不存在的订单 (错误处理)"
                Status = "PASS"
                Method = "GET"
                Endpoint = "/orders/non-existent-order-id"
                Message = "正确返回 404"
            }
        } else {
            $script:FailedTests++
            Write-Error-Custom "失败: 预期 404，但得到其他错误: $($_.Exception.Message)"
            $script:TestResults += [PSCustomObject]@{
                Name = "获取不存在的订单 (错误处理)"
                Status = "FAIL"
                Method = "GET"
                Endpoint = "/orders/non-existent-order-id"
                Message = $_.Exception.Message
            }
        }
    }
    
    # 显示测试报告
    Show-Report
    
    # 返回退出码
    if ($script:FailedTests -gt 0) {
        exit 1
    } else {
        Write-Success "`n所有测试通过！系统运行正常。"
        exit 0
    }
}

# 执行测试
Start-Tests
