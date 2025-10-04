#!/usr/bin/env pwsh
# CatCat.Transit 性能基准测试运行脚本

param(
    [string]$Filter = "*",
    [switch]$Quick,
    [switch]$Memory,
    [switch]$Export
)

Write-Host "===========================================`n" -ForegroundColor Cyan
Write-Host "  CatCat.Transit 性能基准测试`n" -ForegroundColor Cyan
Write-Host "===========================================" -ForegroundColor Cyan
Write-Host ""

# 检查是否在 Release 模式
if ($Quick) {
    Write-Host "⚡ 快速模式 (较少迭代)" -ForegroundColor Yellow
    $args = @("--filter", $Filter, "--job", "short")
} else {
    Write-Host "📊 完整模式 (完整迭代)" -ForegroundColor Green
    $args = @("--filter", $Filter)
}

if ($Memory) {
    Write-Host "💾 启用内存诊断" -ForegroundColor Magenta
    $args += "--memory"
}

if ($Export) {
    Write-Host "📄 导出 HTML 和 JSON 报告" -ForegroundColor Blue
    $args += @("--exporters", "html", "json")
}

Write-Host ""
Write-Host "🔨 编译 Release 版本..." -ForegroundColor Yellow
dotnet build Catga.Benchmarks -c Release --no-incremental

if ($LASTEXITCODE -ne 0) {
    Write-Host "`n❌ 编译失败!" -ForegroundColor Red
    exit 1
}

Write-Host "✅ 编译成功`n" -ForegroundColor Green
Write-Host "🚀 开始运行基准测试...`n" -ForegroundColor Cyan

dotnet run --project Catga.Benchmarks -c Release --no-build -- @args

if ($LASTEXITCODE -eq 0) {
    Write-Host "`n✅ 基准测试完成!" -ForegroundColor Green

    if ($Export) {
        Write-Host "`n📁 报告位置: Catga.Benchmarks/BenchmarkDotNet.Artifacts/results/" -ForegroundColor Blue
    }
} else {
    Write-Host "`n❌ 基准测试失败!" -ForegroundColor Red
}

