# Catga 集群测试脚本（Windows PowerShell）

param(
    [int]$OrderCount = 10,
    [int]$ConcurrentRequests = 5
)

Write-Host "🧪 Catga 集群测试脚本" -ForegroundColor Green
Write-Host ""
Write-Host "配置:" -ForegroundColor Cyan
Write-Host "  订单数量: $OrderCount" -ForegroundColor White
Write-Host "  并发请求: $ConcurrentRequests" -ForegroundColor White
Write-Host ""

# 测试负载均衡器健康
Write-Host "📡 测试负载均衡器..." -ForegroundColor Cyan
try {
    $health = Invoke-RestMethod -Uri "http://localhost:8080/health" -Method GET -TimeoutSec 5
    Write-Host "✅ 负载均衡器健康: $health" -ForegroundColor Green
} catch {
    Write-Host "❌ 负载均衡器不可用" -ForegroundColor Red
    exit 1
}
Write-Host ""

# 测试 OrderApi 实例
Write-Host "📡 测试 OrderApi 实例..." -ForegroundColor Cyan
$ports = @(5001, 5002, 5003)
foreach ($port in $ports) {
    try {
        $health = Invoke-RestMethod -Uri "http://localhost:$port/health" -Method GET -TimeoutSec 5
        Write-Host "  ✅ OrderApi-$($ports.IndexOf($port) + 1) (port $port) 健康" -ForegroundColor Green
    } catch {
        Write-Host "  ⚠️  OrderApi-$($ports.IndexOf($port) + 1) (port $port) 不可用" -ForegroundColor Yellow
    }
}
Write-Host ""

# 创建订单测试
Write-Host "🛒 创建订单测试（负载均衡）..." -ForegroundColor Cyan
$successCount = 0
$failCount = 0
$totalDuration = 0

$jobs = @()
for ($i = 1; $i -le $OrderCount; $i++) {
    # 创建后台任务以模拟并发
    $job = Start-Job -ScriptBlock {
        param($orderNum, $baseUrl)
        
        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        
        $body = @{
            customerId = "customer-$orderNum"
            items = @(
                @{
                    productId = "prod-$(Get-Random -Minimum 1 -Maximum 10)"
                    quantity = (Get-Random -Minimum 1 -Maximum 5)
                    price = (Get-Random -Minimum 50 -Maximum 500)
                }
            )
        } | ConvertTo-Json
        
        try {
            $response = Invoke-RestMethod -Method POST `
                -Uri "$baseUrl/api/orders" `
                -ContentType "application/json" `
                -Body $body `
                -TimeoutSec 10
            
            $sw.Stop()
            
            return @{
                Success = $true
                OrderId = $response.orderId
                Duration = $sw.ElapsedMilliseconds
                OrderNum = $orderNum
            }
        } catch {
            $sw.Stop()
            return @{
                Success = $false
                Error = $_.Exception.Message
                Duration = $sw.ElapsedMilliseconds
                OrderNum = $orderNum
            }
        }
    } -ArgumentList $i, "http://localhost:8080"
    
    $jobs += $job
    
    # 控制并发数
    if ($jobs.Count -ge $ConcurrentRequests) {
        $completed = Wait-Job -Job $jobs -Any
        $result = Receive-Job -Job $completed
        
        if ($result.Success) {
            $successCount++
            Write-Host "  ✅ 订单 $($result.OrderNum): $($result.OrderId) (耗时: $($result.Duration)ms)" -ForegroundColor Green
        } else {
            $failCount++
            Write-Host "  ❌ 订单 $($result.OrderNum): $($result.Error)" -ForegroundColor Red
        }
        
        $totalDuration += $result.Duration
        $jobs = $jobs | Where-Object { $_.Id -ne $completed.Id }
        Remove-Job -Job $completed
    }
    
    Start-Sleep -Milliseconds 100
}

# 等待剩余任务完成
if ($jobs.Count -gt 0) {
    Wait-Job -Job $jobs | Out-Null
    
    foreach ($job in $jobs) {
        $result = Receive-Job -Job $job
        
        if ($result.Success) {
            $successCount++
            Write-Host "  ✅ 订单 $($result.OrderNum): $($result.OrderId) (耗时: $($result.Duration)ms)" -ForegroundColor Green
        } else {
            $failCount++
            Write-Host "  ❌ 订单 $($result.OrderNum): $($result.Error)" -ForegroundColor Red
        }
        
        $totalDuration += $result.Duration
        Remove-Job -Job $job
    }
}

Write-Host ""

# 统计结果
$avgDuration = [math]::Round($totalDuration / $OrderCount, 2)
$successRate = [math]::Round(($successCount / $OrderCount) * 100, 2)

Write-Host "📊 测试结果统计：" -ForegroundColor Cyan
Write-Host "  总请求数: $OrderCount" -ForegroundColor White
Write-Host "  成功: $successCount ($successRate%)" -ForegroundColor Green
Write-Host "  失败: $failCount" -ForegroundColor $(if ($failCount -gt 0) { "Red" } else { "White" })
Write-Host "  平均延迟: ${avgDuration}ms" -ForegroundColor White
Write-Host ""

# 查看服务日志（最后 10 行）
Write-Host "📋 OrderService 日志（最后 10 行）:" -ForegroundColor Cyan
docker-compose -f docker-compose.apps.yml logs --tail=10 order-service
Write-Host ""

Write-Host "📋 NotificationService 日志（最后 10 行）:" -ForegroundColor Cyan
docker-compose -f docker-compose.apps.yml logs --tail=10 notification-service
Write-Host ""

# 指标查询建议
Write-Host "📊 查看详细指标：" -ForegroundColor Cyan
Write-Host "  Prometheus: http://localhost:9090" -ForegroundColor White
Write-Host "  Grafana:    http://localhost:3000" -ForegroundColor White
Write-Host "  Jaeger:     http://localhost:16686" -ForegroundColor White
Write-Host ""

if ($successRate -ge 95) {
    Write-Host "✅ 集群测试通过！成功率: $successRate%" -ForegroundColor Green
} elseif ($successRate -ge 80) {
    Write-Host "⚠️  集群测试部分通过。成功率: $successRate%" -ForegroundColor Yellow
} else {
    Write-Host "❌ 集群测试失败！成功率: $successRate%" -ForegroundColor Red
}

Write-Host ""

