#!/usr/bin/env pwsh
# OrderSystem 多节点集群启动脚本
# 用于演示分布式部署和 WorkerId 配置

param(
    [int]$NodeCount = 3,
    [switch]$Help
)

if ($Help) {
    Write-Host @"
OrderSystem 多节点集群启动脚本

用法:
    .\start-cluster.ps1 [-NodeCount <数量>] [-Help]

参数:
    -NodeCount  启动的节点数量 (默认: 3, 范围: 1-10)
    -Help       显示此帮助信息

示例:
    .\start-cluster.ps1              # 启动 3 个节点
    .\start-cluster.ps1 -NodeCount 5 # 启动 5 个节点

每个节点将:
    - 使用唯一的 WorkerId (1, 2, 3, ...)
    - 监听不同的端口 (5001, 5002, 5003, ...)
    - 在独立的 PowerShell 窗口中运行

停止集群:
    关闭所有 PowerShell 窗口即可

"@ -ForegroundColor Cyan
    exit 0
}

if ($NodeCount -lt 1 -or $NodeCount -gt 10) {
    Write-Host "❌ 错误: NodeCount 必须在 1-10 之间" -ForegroundColor Red
    exit 1
}

Write-Host "`n╔════════════════════════════════════════════════════════════════╗" -ForegroundColor Green
Write-Host "║     🚀 启动 OrderSystem 多节点集群 ($NodeCount 个节点)     ║" -ForegroundColor Green
Write-Host "╚════════════════════════════════════════════════════════════════╝`n" -ForegroundColor Green

$projectPath = $PSScriptRoot
$jobs = @()

for ($i = 1; $i -le $NodeCount; $i++) {
    $port = 5000 + $i
    $workerId = $i

    Write-Host "🌐 启动节点 $i (WorkerId=$workerId, Port=$port)..." -ForegroundColor Yellow

    # 在新的 PowerShell 窗口中启动节点
    $job = Start-Process pwsh -ArgumentList @(
        "-NoExit",
        "-Command",
        "cd '$projectPath'; dotnet run --project . -- $workerId"
    ) -PassThru

    $jobs += $job
    Start-Sleep -Milliseconds 500
}

Write-Host "`n✅ 已启动 $NodeCount 个节点！" -ForegroundColor Green
Write-Host "`n📊 节点信息:" -ForegroundColor Cyan

for ($i = 1; $i -le $NodeCount; $i++) {
    $port = 5000 + $i
    Write-Host "   节点 $i`: http://localhost:$port (WorkerId=$i)" -ForegroundColor White
}

Write-Host "`n🔗 测试端点:" -ForegroundColor Cyan
Write-Host "   创建订单:  POST http://localhost:5001/demo/order-success" -ForegroundColor White
Write-Host "   创建订单:  POST http://localhost:5002/demo/order-success" -ForegroundColor White
Write-Host "   创建订单:  POST http://localhost:5003/demo/order-success" -ForegroundColor White
Write-Host "   Swagger:   http://localhost:5001/swagger" -ForegroundColor White

Write-Host "`n💡 提示:" -ForegroundColor Cyan
Write-Host "   • 每个节点使用唯一的 WorkerId 生成不冲突的分布式 ID" -ForegroundColor Gray
Write-Host "   • 可以向任意节点发送请求，观察负载均衡效果" -ForegroundColor Gray
Write-Host "   • 关闭所有 PowerShell 窗口以停止集群" -ForegroundColor Gray

Write-Host "`n⏳ 按 Ctrl+C 退出监控（节点将继续运行）..." -ForegroundColor Yellow

# 保持脚本运行，以便用户可以看到日志
try {
    while ($true) {
        Start-Sleep -Seconds 1
    }
}
catch {
    Write-Host "`n👋 监控已停止，节点仍在运行" -ForegroundColor Yellow
}

