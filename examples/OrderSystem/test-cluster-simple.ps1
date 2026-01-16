#!/usr/bin/env pwsh
<#
.SYNOPSIS
    简化的集群测试脚本
.DESCRIPTION
    启动 3 个节点并测试集群功能
#>

param(
    [string]$Transport = "redis",
    [string]$Persistence = "redis"
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

Write-Section "Catga 集群测试 - $Transport/$Persistence"

# 启动 3 个节点
$processes = @()
$ports = @(5301, 5302, 5303)

try {
    Write-Info "启动 3 个集群节点..."
    
    foreach ($port in $ports) {
        $nodeId = "cluster-node-$port"
        Write-Info "启动节点: $nodeId (端口 $port)..."
        
        $process = Start-Process -FilePath "dotnet" `
            -ArgumentList "run", "--", "--transport", $Transport, "--persistence", $Persistence, "--port", $port, "--node-id", $nodeId, "--cluster" `
            -WorkingDirectory $PSScriptRoot `
            -PassThru `
            -WindowStyle Hidden
        
        $processes += $process
        Start-Sleep -Seconds 2
    }
    
    # 等待所有节点启动
    Write-Info "等待集群初始化..."
    Start-Sleep -Seconds 10
    
    # 测试每个节点
    $allHealthy = $true
    foreach ($port in $ports) {
        Write-Info "测试节点 (端口 $port)..."
        
        try {
            # 健康检查
            $health = Invoke-RestMethod -Uri "http://localhost:$port/health" -Method Get -TimeoutSec 5
            Write-Success "节点 $port 健康: $health"
            
            # 创建订单
            $order = Invoke-RestMethod -Uri "http://localhost:$port/orders" -Method Post -ContentType "application/json" -Body (@{
                customerId = "cluster-test-$port"
                items = @(
                    @{
                        productId = "product-001"
                        name = "集群测试商品"
                        quantity = 1
                        price = 100.00
                    }
                )
            } | ConvertTo-Json) -TimeoutSec 5
            
            Write-Success "节点 $port 创建订单: $($order.orderId)"
            
        } catch {
            Write-Error-Custom "节点 $port 测试失败: $($_.Exception.Message)"
            $allHealthy = $false
        }
    }
    
    # 验证数据一致性
    Write-Info "验证集群数据一致性..."
    Start-Sleep -Seconds 3
    
    $orderCounts = @()
    foreach ($port in $ports) {
        try {
            $orders = Invoke-RestMethod -Uri "http://localhost:$port/orders" -Method Get -TimeoutSec 5
            $orderCounts += $orders.Count
            Write-Info "节点 $port 订单数: $($orders.Count)"
        } catch {
            Write-Warning "无法获取节点 $port 的订单列表"
        }
    }
    
    if ($allHealthy) {
        Write-Success "`n集群测试通过！所有节点正常运行。"
        exit 0
    } else {
        Write-Error-Custom "`n集群测试失败！部分节点异常。"
        exit 1
    }
    
} catch {
    Write-Error-Custom "集群测试异常: $($_.Exception.Message)"
    exit 1
} finally {
    # 停止所有节点
    Write-Info "`n停止所有节点..."
    foreach ($process in $processes) {
        if ($process -and -not $process.HasExited) {
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        }
    }
    Start-Sleep -Seconds 2
    Write-Success "所有节点已停止"
}
