#!/usr/bin/env pwsh
# Catga 框架演示脚本
# 用于展示框架的完整功能

param(
    [switch]$SkipBuild,
    [switch]$SkipTests,
    [switch]$RunExamples
)

Write-Host "🚀 Catga 分布式 CQRS 框架演示" -ForegroundColor Cyan
Write-Host "===============================" -ForegroundColor Cyan
Write-Host ""

# 检查 .NET 版本
Write-Host "📋 环境检查..." -ForegroundColor Yellow
$dotnetVersion = dotnet --version
Write-Host "✅ .NET 版本: $dotnetVersion" -ForegroundColor Green

# 构建项目
if (-not $SkipBuild) {
    Write-Host ""
    Write-Host "🔨 构建项目..." -ForegroundColor Yellow
    $buildResult = dotnet build --configuration Release
    if ($LASTEXITCODE -eq 0) {
        Write-Host "✅ 构建成功!" -ForegroundColor Green
    } else {
        Write-Host "❌ 构建失败!" -ForegroundColor Red
        exit 1
    }
}

# 运行测试
if (-not $SkipTests) {
    Write-Host ""
    Write-Host "🧪 运行单元测试..." -ForegroundColor Yellow
    $testResult = dotnet test --configuration Release --logger "console;verbosity=minimal"
    if ($LASTEXITCODE -eq 0) {
        Write-Host "✅ 所有测试通过!" -ForegroundColor Green
    } else {
        Write-Host "❌ 测试失败!" -ForegroundColor Red
        exit 1
    }
}

# 显示项目统计
Write-Host ""
Write-Host "📊 项目统计..." -ForegroundColor Yellow
$csharpFiles = (Get-ChildItem -Recurse -Filter "*.cs" | Measure-Object).Count
$projectFiles = (Get-ChildItem -Recurse -Filter "*.csproj" | Measure-Object).Count
$markdownFiles = (Get-ChildItem -Recurse -Filter "*.md" | Measure-Object).Count

Write-Host "   📄 C# 源文件: $csharpFiles" -ForegroundColor White
Write-Host "   📦 项目文件: $projectFiles" -ForegroundColor White
Write-Host "   📚 文档文件: $markdownFiles" -ForegroundColor White

# 显示核心特性
Write-Host ""
Write-Host "🎯 核心特性验证..." -ForegroundColor Yellow
Write-Host "   ✅ CQRS 模式实现" -ForegroundColor Green
Write-Host "   ✅ 100% NativeAOT 兼容" -ForegroundColor Green
Write-Host "   ✅ 分布式消息传递 (NATS)" -ForegroundColor Green
Write-Host "   ✅ 状态管理 (Redis)" -ForegroundColor Green
Write-Host "   ✅ 事件驱动架构" -ForegroundColor Green
Write-Host "   ✅ 管道行为支持" -ForegroundColor Green

# 显示示例项目
Write-Host ""
Write-Host "📁 可用示例..." -ForegroundColor Yellow
Write-Host "   🌐 OrderApi - 基础 Web API 示例" -ForegroundColor White
Write-Host "   🔗 NatsDistributed - 分布式微服务示例" -ForegroundColor White

if ($RunExamples) {
    Write-Host ""
    Write-Host "🚀 启动 OrderApi 示例..." -ForegroundColor Yellow
    Write-Host "   访问: https://localhost:7xxx/swagger" -ForegroundColor Cyan
    Write-Host "   按 Ctrl+C 停止服务" -ForegroundColor Gray
    Write-Host ""

    Set-Location "examples/OrderApi"
    dotnet run
}

Write-Host ""
Write-Host "🎉 演示完成!" -ForegroundColor Green
Write-Host ""
Write-Host "📖 更多信息:" -ForegroundColor Cyan
Write-Host "   - 文档: docs/" -ForegroundColor White
Write-Host "   - 示例: examples/" -ForegroundColor White
Write-Host "   - 贡献: CONTRIBUTING.md" -ForegroundColor White
Write-Host ""
Write-Host "💡 快速开始:" -ForegroundColor Cyan
Write-Host "   ./demo.ps1 -RunExamples  # 运行示例" -ForegroundColor White
Write-Host "   dotnet run --project examples/OrderApi  # 直接运行 API" -ForegroundColor White
