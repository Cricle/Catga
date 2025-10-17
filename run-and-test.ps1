#!/usr/bin/env pwsh
# Catga OrderSystem 一键启动和测试脚本

$ErrorActionPreference = "Stop"

Write-Host "🚀 Catga OrderSystem - 一键启动和测试" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""

# 检查是否已有进程在运行
$existingProcess = Get-Process -Name "OrderSystem.AppHost" -ErrorAction SilentlyContinue
if ($existingProcess) {
    Write-Host "⚠️  检测到 OrderSystem 正在运行 (PID: $($existingProcess.Id))" -ForegroundColor Yellow
    $continue = Read-Host "是否停止现有进程并重新启动? (y/n)"
    if ($continue -eq 'y' -or $continue -eq 'Y') {
        Write-Host "🛑 停止现有进程..." -ForegroundColor Yellow
        Stop-Process -Id $existingProcess.Id -Force
        Start-Sleep -Seconds 2
    }
    else {
        Write-Host "ℹ️  使用现有进程进行测试" -ForegroundColor Cyan
        Start-Sleep -Seconds 2
        & .\test-ordersystem-full.ps1
        exit $LASTEXITCODE
    }
}

Write-Host "📦 步骤 1: 构建项目" -ForegroundColor Cyan
Write-Host "─────────────────────────────────────────" -ForegroundColor Gray

try {
    dotnet build examples/OrderSystem.AppHost/OrderSystem.AppHost.csproj --nologo
    if ($LASTEXITCODE -ne 0) {
        Write-Host "❌ 构建失败" -ForegroundColor Red
        exit 1
    }
    Write-Host "✅ 构建成功" -ForegroundColor Green
    Write-Host ""
}
catch {
    Write-Host "❌ 构建失败: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

Write-Host "🚀 步骤 2: 启动服务 (后台)" -ForegroundColor Cyan
Write-Host "─────────────────────────────────────────" -ForegroundColor Gray

$appHostPath = "examples/OrderSystem.AppHost"
$logFile = "ordersystem-output.log"

# 后台启动服务
$job = Start-Job -ScriptBlock {
    param($path)
    Set-Location $path
    dotnet run --no-build
} -ArgumentList (Resolve-Path $appHostPath)

Write-Host "✅ 服务已在后台启动 (Job ID: $($job.Id))" -ForegroundColor Green
Write-Host "   日志文件: $logFile" -ForegroundColor Gray
Write-Host ""

# 等待服务启动
Write-Host "⏳ 步骤 3: 等待服务就绪" -ForegroundColor Cyan
Write-Host "─────────────────────────────────────────" -ForegroundColor Gray

$maxWait = 60
$waited = 0
$serviceReady = $false

while ($waited -lt $maxWait) {
    try {
        $response = Invoke-WebRequest -Uri "http://localhost:5000/health" -Method GET -TimeoutSec 2 -ErrorAction Stop
        if ($response.StatusCode -eq 200) {
            $serviceReady = $true
            break
        }
    }
    catch {
        # 服务未就绪，继续等待
    }

    Write-Host "   等待中... ($waited/$maxWait 秒)" -ForegroundColor Gray
    Start-Sleep -Seconds 2
    $waited += 2
}

if (-not $serviceReady) {
    Write-Host "❌ 服务启动超时" -ForegroundColor Red
    Write-Host ""
    Write-Host "查看日志:" -ForegroundColor Yellow
    Receive-Job -Job $job
    Stop-Job -Job $job
    Remove-Job -Job $job
    exit 1
}

Write-Host "✅ 服务已就绪 (耗时 $waited 秒)" -ForegroundColor Green
Write-Host ""

# 运行测试
Write-Host "🧪 步骤 4: 运行全面测试" -ForegroundColor Cyan
Write-Host "─────────────────────────────────────────" -ForegroundColor Gray
Write-Host ""

try {
    & .\test-ordersystem-full.ps1
    $testResult = $LASTEXITCODE
}
catch {
    Write-Host "❌ 测试执行失败: $($_.Exception.Message)" -ForegroundColor Red
    $testResult = 1
}

# 清理
Write-Host ""
Write-Host "🧹 步骤 5: 清理" -ForegroundColor Cyan
Write-Host "─────────────────────────────────────────" -ForegroundColor Gray

$keepRunning = Read-Host "是否保持服务运行? (y/n)"
if ($keepRunning -ne 'y' -and $keepRunning -ne 'Y') {
    Write-Host "🛑 停止服务..." -ForegroundColor Yellow
    Stop-Job -Job $job
    Remove-Job -Job $job

    # 确保进程完全停止
    Start-Sleep -Seconds 2
    $processes = Get-Process -Name "dotnet","OrderSystem.AppHost","OrderSystem.Api" -ErrorAction SilentlyContinue
    if ($processes) {
        $processes | Stop-Process -Force -ErrorAction SilentlyContinue
    }

    Write-Host "✅ 服务已停止" -ForegroundColor Green
}
else {
    Write-Host "ℹ️  服务继续运行在后台 (Job ID: $($job.Id))" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "可访问的 URL:" -ForegroundColor Cyan
    Write-Host "  • OrderSystem UI: http://localhost:5000" -ForegroundColor White
    Write-Host "  • Catga Debugger: http://localhost:5000/debugger/index.html" -ForegroundColor White
    Write-Host "  • Swagger API: http://localhost:5000/swagger" -ForegroundColor White
    Write-Host "  • Aspire Dashboard: http://localhost:15888" -ForegroundColor White
    Write-Host ""
    Write-Host "停止服务:" -ForegroundColor Yellow
    Write-Host "  Stop-Job -Id $($job.Id)" -ForegroundColor Gray
    Write-Host "  Remove-Job -Id $($job.Id)" -ForegroundColor Gray
}

Write-Host ""
exit $testResult

