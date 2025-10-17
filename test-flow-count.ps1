#!/usr/bin/env pwsh
# Test script to verify flow count accuracy

Write-Host "�� 测试 Debugger 消息流计数准确性" -ForegroundColor Cyan
Write-Host "=" * 60 -ForegroundColor Gray

# Wait for service to start
Start-Sleep -Seconds 2

# Get initial stats
Write-Host "`n📊 初始状态:" -ForegroundColor Yellow
try {
    $initialStats = Invoke-RestMethod -Uri "http://localhost:5000/debug-api/stats"
    Write-Host "   Events: $($initialStats.totalEvents), Flows: $($initialStats.totalFlows)" -ForegroundColor Gray
} catch {
    Write-Host "   ❌ 无法连接到服务" -ForegroundColor Red
    exit 1
}

# Call API once
Write-Host "`n1️⃣  调用 1 次 /demo/order-success" -ForegroundColor Cyan
$order1 = Invoke-RestMethod -Uri "http://localhost:5000/demo/order-success" -Method Post
Write-Host "   订单: $($order1.orderId)" -ForegroundColor Green
Start-Sleep -Seconds 2

# Check stats after 1 call
$stats1 = Invoke-RestMethod -Uri "http://localhost:5000/debug-api/stats"
$addedEvents1 = $stats1.totalEvents - $initialStats.totalEvents
$addedFlows1 = $stats1.totalFlows - $initialStats.totalFlows
Write-Host "   增加 Events: $addedEvents1, 增加 Flows: $addedFlows1" -ForegroundColor $(if ($addedFlows1 -eq 1) {'Green'} else {'Red'})

# Call API a second time
Write-Host "`n2️⃣  调用 2 次 /demo/order-success" -ForegroundColor Cyan
$order2 = Invoke-RestMethod -Uri "http://localhost:5000/demo/order-success" -Method Post
Write-Host "   订单: $($order2.orderId)" -ForegroundColor Green
Start-Sleep -Seconds 2

# Check stats after 2 calls
$stats2 = Invoke-RestMethod -Uri "http://localhost:5000/debug-api/stats"
$addedEvents2 = $stats2.totalEvents - $stats1.totalEvents
$addedFlows2 = $stats2.totalFlows - $stats1.totalFlows
Write-Host "   增加 Events: $addedEvents2, 增加 Flows: $addedFlows2" -ForegroundColor $(if ($addedFlows2 -eq 1) {'Green'} else {'Red'})

# Verify flows
Write-Host "`n📋 消息流列表:" -ForegroundColor Yellow
$flows = Invoke-RestMethod -Uri "http://localhost:5000/debug-api/flows"
Write-Host "   API 返回: $($flows.flows.Count) 个流" -ForegroundColor Gray

if ($flows.flows.Count -ge 2) {
    Write-Host "`n   最近 2 个流:" -ForegroundColor Cyan
    $flows.flows[0..1] | ForEach-Object {
        Write-Host "   - CorrelationId: $($_.correlationId.Substring(0,8))..." -ForegroundColor Gray
        Write-Host "     MessageType: $($_.messageType), Status: $($_.status), Duration: $($_.duration)ms" -ForegroundColor Gray
    }
}

# Summary
Write-Host "`n✅ 结果:" -ForegroundColor Green
Write-Host "   总事件数: $($stats2.totalEvents)" -ForegroundColor White
Write-Host "   总流数: $($stats2.totalFlows)" -ForegroundColor White
Write-Host "   成功率: $($stats2.successRate)%" -ForegroundColor White
Write-Host "   平均延迟: $($stats2.averageLatency)ms" -ForegroundColor White

if ($addedFlows1 -eq 1 -and $addedFlows2 -eq 1) {
    Write-Host "`n✅ 流计数正确！每次调用增加 1 个流" -ForegroundColor Green
} else {
    Write-Host "`n❌ 流计数错误！第1次增加 $addedFlows1 个流，第2次增加 $addedFlows2 个流" -ForegroundColor Red
}

