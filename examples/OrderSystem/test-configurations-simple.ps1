#!/usr/bin/env pwsh
<#
.SYNOPSIS
    OrderSystem 简化配置测试脚本
.DESCRIPTION
    逐个测试各种配置：InMemory、Redis、NATS
#>

param(
    [string]$Configuration = "all"
)

$ErrorActionPreference = "Stop"

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

function Write-Section {
    param([string]$Message)
    Write-Host "`n╔══════════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
    Write-Host "║ $($Message.PadRight(60)) ║" -ForegroundColor Cyan
    Write-Host "╚══════════════════════════════════════════════════════════════╝" -ForegroundColor Cyan
}

$script:Results = @()

function Test-SingleConfiguration {
    param(
        [string]$Name,
        [string]$Transport,
        [string]$Persistence,
        [int]$Port = 5000
    )
    
    Write-Section "测试: $Name"
    Write-Info "Transport: $Transport, Persistence: $Persistence, Port: $Port"
    
    # 启动服务
    Write-Info "启动服务..."
    $process = Start-Process -FilePath "dotnet" `
        -ArgumentList "run", "--", "--transport", $Transport, "--persistence", $Persistence, "--port", $Port `
        -WorkingDirectory $PSScriptRoot `
        -PassThru `
        -WindowStyle Hidden
    
    try {
        # 等待服务启动
        Start-Sleep -Seconds 5
        
        # 检查服务是否运行
        $maxRetries = 10
        $isRunning = $false
        
        for ($i = 0; $i -lt $maxRetries; $i++) {
            try {
                $response = Invoke-RestMethod -Uri "http://localhost:$Port/" -Method Get -TimeoutSec 2 -ErrorAction Stop
                $isRunning = $true
                Write-Success "服务启动成功"
                break
            } catch {
                Write-Info "等待服务响应... ($($i+1)/$maxRetries)"
                Start-Sleep -Seconds 2
            }
        }
        
        if (-not $isRunning) {
            throw "服务启动超时"
        }
        
        # 运行快速测试
        Write-Info "运行 API 测试..."
        
        # 1. 系统信息
        $sysInfo = Invoke-RestMethod -Uri "http://localhost:$Port/" -Method Get
        Write-Success "系统信息: $($sysInfo.service)"
        
        # 2. 健康检查
        $health = Invoke-RestMethod -Uri "http://localhost:$Port/health" -Method Get
        Write-Success "健康检查: $health"
        
        # 3. 创建订单
        $order = Invoke-RestMethod -Uri "http://localhost:$Port/orders" -Method Post -ContentType "application/json" -Body (@{
            customerId = "test-customer-$(Get-Date -Format 'yyyyMMddHHmmss')"
            items = @(
                @{
                    productId = "test-product-001"
                    name = "测试商品"
                    quantity = 1
                    price = 99.99
                }
            )
        } | ConvertTo-Json)
        Write-Success "创建订单: $($order.orderId)"
        
        # 4. 获取订单
        $getOrder = Invoke-RestMethod -Uri "http://localhost:$Port/orders/$($order.orderId)" -Method Get
        Write-Success "获取订单: Status=$($getOrder.status)"
        
        # 5. 支付订单
        $paidOrder = Invoke-RestMethod -Uri "http://localhost:$Port/orders/$($order.orderId)/pay" -Method Post
        Write-Success "支付订单: Status=$($paidOrder.status)"
        
        # 6. 获取统计
        $stats = Invoke-RestMethod -Uri "http://localhost:$Port/stats" -Method Get
        Write-Success "统计信息: TotalOrders=$($stats.totalOrders), Revenue=$($stats.totalRevenue)"
        
        Write-Success "配置测试通过: $Name"
        
        $script:Results += [PSCustomObject]@{
            Name = $Name
            Transport = $Transport
            Persistence = $Persistence
            Status = "PASS"
            Message = "所有测试通过"
        }
        
    } catch {
        Write-Error-Custom "配置测试失败: $($_.Exception.Message)"
        
        $script:Results += [PSCustomObject]@{
            Name = $Name
            Transport = $Transport
            Persistence = $Persistence
            Status = "FAIL"
            Message = $_.Exception.Message
        }
    } finally {
        # 停止服务
        if ($process -and -not $process.HasExited) {
            Write-Info "停止服务..."
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
            Start-Sleep -Seconds 2
        }
    }
}

# 主测试流程
Write-Host @"

╔══════════════════════════════════════════════════════════════╗
║       Catga OrderSystem - 配置测试                           ║
╠══════════════════════════════════════════════════════════════╣
║  时间: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")                        ║
╚══════════════════════════════════════════════════════════════╝

"@ -ForegroundColor Cyan

# 检查依赖
Write-Info "检查依赖服务..."
$redisOk = (Test-NetConnection -ComputerName localhost -Port 6379 -WarningAction SilentlyContinue).TcpTestSucceeded
$natsOk = (Test-NetConnection -ComputerName localhost -Port 4222 -WarningAction SilentlyContinue).TcpTestSucceeded

if ($redisOk) { Write-Success "Redis 可用" } else { Write-Error-Custom "Redis 不可用" }
if ($natsOk) { Write-Success "NATS 可用" } else { Write-Error-Custom "NATS 不可用" }

# 运行测试
if ($Configuration -eq "all" -or $Configuration -eq "inmemory") {
    Test-SingleConfiguration -Name "InMemory (Full Stack)" -Transport "inmemory" -Persistence "inmemory"
}

if (($Configuration -eq "all" -or $Configuration -eq "redis") -and $redisOk) {
    Test-SingleConfiguration -Name "Redis Transport + InMemory Persistence" -Transport "redis" -Persistence "inmemory"
    Test-SingleConfiguration -Name "InMemory Transport + Redis Persistence" -Transport "inmemory" -Persistence "redis"
    Test-SingleConfiguration -Name "Redis (Full Stack)" -Transport "redis" -Persistence "redis"
}

if (($Configuration -eq "all" -or $Configuration -eq "nats") -and $natsOk) {
    Test-SingleConfiguration -Name "NATS Transport + InMemory Persistence" -Transport "nats" -Persistence "inmemory"
    Test-SingleConfiguration -Name "InMemory Transport + NATS Persistence" -Transport "inmemory" -Persistence "nats"
    Test-SingleConfiguration -Name "NATS (Full Stack)" -Transport "nats" -Persistence "nats"
}

# 显示报告
Write-Host "`n`n╔══════════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║                      测试报告                                ║" -ForegroundColor Cyan
Write-Host "╚══════════════════════════════════════════════════════════════╝" -ForegroundColor Cyan

$script:Results | Format-Table -Property Name, Transport, Persistence, Status, Message -AutoSize

$passed = ($script:Results | Where-Object { $_.Status -eq "PASS" }).Count
$failed = ($script:Results | Where-Object { $_.Status -eq "FAIL" }).Count
$total = $script:Results.Count

Write-Host "`n总计: $total, 通过: $passed, 失败: $failed" -ForegroundColor Cyan

if ($failed -eq 0) {
    Write-Success "`n所有配置测试通过！"
    exit 0
} else {
    Write-Error-Custom "`n部分配置测试失败"
    exit 1
}
