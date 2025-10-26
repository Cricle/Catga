#!/usr/bin/env pwsh
# Catga测试结果分析和报告生成工具
# 用途: 运行测试、分析结果、生成可视化报告

param(
    [switch]$Coverage,
    [switch]$Detailed,
    [switch]$OpenReport,
    [string]$Filter = "",
    [switch]$SkipIntegration = $true,
    [switch]$Help
)

function Show-Help {
    Write-Host @"
🧪 Catga测试分析工具

用法: .\analyze-test-results.ps1 [选项]

选项:
  -Coverage          收集代码覆盖率
  -Detailed          生成详细报告
  -OpenReport        自动打开HTML报告
  -Filter <pattern>  过滤测试（例如: "CircuitBreaker"）
  -SkipIntegration   跳过集成测试（默认）
  -Help              显示此帮助信息

示例:
  .\analyze-test-results.ps1
  .\analyze-test-results.ps1 -Coverage -OpenReport
  .\analyze-test-results.ps1 -Filter "CircuitBreaker" -Detailed
  .\analyze-test-results.ps1 -Coverage -Detailed -OpenReport

"@ -ForegroundColor Cyan
    exit 0
}

if ($Help) {
    Show-Help
}

# 配置
$TestProject = "tests/Catga.Tests/Catga.Tests.csproj"
$ReportDir = "test-reports"
$CoverageDir = "coverage_report"
$Timestamp = Get-Date -Format "yyyy-MM-dd_HH-mm-ss"

# 颜色函数
function Write-Success { param($msg) Write-Host "✅ $msg" -ForegroundColor Green }
function Write-Info { param($msg) Write-Host "ℹ️  $msg" -ForegroundColor Cyan }
function Write-Warning { param($msg) Write-Host "⚠️  $msg" -ForegroundColor Yellow }
function Write-Error { param($msg) Write-Host "❌ $msg" -ForegroundColor Red }
function Write-Header { param($msg) Write-Host "`n$('=' * 60)" -ForegroundColor Magenta; Write-Host "  $msg" -ForegroundColor Magenta; Write-Host "$('=' * 60)`n" -ForegroundColor Magenta }

# 创建输出目录
Write-Info "创建报告目录..."
New-Item -ItemType Directory -Force -Path $ReportDir | Out-Null
if ($Coverage) {
    New-Item -ItemType Directory -Force -Path $CoverageDir | Out-Null
}

Write-Header "🚀 Catga测试分析工具"

# 构建测试过滤器
$TestFilter = ""
if ($SkipIntegration) {
    $TestFilter = "FullyQualifiedName!~Integration"
}
if ($Filter) {
    if ($TestFilter) {
        $TestFilter += "&FullyQualifiedName~$Filter"
    } else {
        $TestFilter = "FullyQualifiedName~$Filter"
    }
}

# 步骤1: 编译项目
Write-Header "📦 步骤1: 编译项目"
Write-Info "正在编译测试项目..."
$buildResult = dotnet build $TestProject --configuration Release 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Error "编译失败！"
    Write-Host $buildResult
    exit 1
}
Write-Success "编译成功"

# 步骤2: 运行测试
Write-Header "🧪 步骤2: 运行测试"

$testArgs = @(
    "test",
    $TestProject,
    "--no-build",
    "--configuration", "Release",
    "--logger", "trx;LogFileName=test-results-$Timestamp.trx",
    "--logger", "console;verbosity=minimal"
)

if ($TestFilter) {
    Write-Info "应用过滤器: $TestFilter"
    $testArgs += "--filter"
    $testArgs += $TestFilter
}

if ($Coverage) {
    Write-Info "启用代码覆盖率收集..."
    $testArgs += "/p:CollectCoverage=true"
    $testArgs += "/p:CoverletOutputFormat=opencover"
    $testArgs += "/p:CoverletOutput=$CoverageDir/opencover.xml"
}

Write-Info "运行测试..."
$testOutput = & dotnet $testArgs 2>&1 | Tee-Object -Variable testOutputCapture
$testExitCode = $LASTEXITCODE

# 解析测试结果
Write-Header "📊 步骤3: 分析结果"

$totalTests = 0
$passed = 0
$failed = 0
$skipped = 0

# 从输出中提取统计
$testOutput | ForEach-Object {
    if ($_ -match "总计:\s*(\d+).*失败:\s*(\d+).*成功:\s*(\d+).*已跳过:\s*(\d+)") {
        $totalTests = [int]$matches[1]
        $failed = [int]$matches[2]
        $passed = [int]$matches[3]
        $skipped = [int]$matches[4]
    }
}

# 计算通过率
$passRate = if ($totalTests -gt 0) { [math]::Round(($passed / $totalTests) * 100, 1) } else { 0 }

# 显示结果摘要
Write-Host "`n" + "═" * 60 -ForegroundColor Cyan
Write-Host "                   测试结果摘要                     " -ForegroundColor Cyan
Write-Host "═" * 60 -ForegroundColor Cyan
Write-Host "  总测试数:  " -NoNewline; Write-Host "$totalTests" -ForegroundColor White
Write-Host "  ✅ 通过:   " -NoNewline; Write-Host "$passed" -ForegroundColor Green
Write-Host "  ❌ 失败:   " -NoNewline; Write-Host "$failed" -ForegroundColor $(if ($failed -gt 0) { "Red" } else { "Gray" })
Write-Host "  ⏭️  跳过:   " -NoNewline; Write-Host "$skipped" -ForegroundColor Yellow
Write-Host "  📊 通过率: " -NoNewline; Write-Host "$passRate%" -ForegroundColor $(if ($passRate -ge 95) { "Green" } elseif ($passRate -ge 80) { "Yellow" } else { "Red" })
Write-Host "═" * 60 -ForegroundColor Cyan

# 生成进度条
$barLength = 50
$passedBars = [math]::Round(($passed / $totalTests) * $barLength)
$failedBars = [math]::Round(($failed / $totalTests) * $barLength)
$skippedBars = $barLength - $passedBars - $failedBars

Write-Host "`n  " -NoNewline
Write-Host ("█" * $passedBars) -NoNewline -ForegroundColor Green
Write-Host ("█" * $failedBars) -NoNewline -ForegroundColor Red
Write-Host ("░" * $skippedBars) -ForegroundColor Gray
Write-Host ""

# 步骤4: 生成详细报告
if ($Detailed) {
    Write-Header "📝 步骤4: 生成详细报告"

    $reportPath = "$ReportDir/test-report-$Timestamp.html"

    $html = @"
<!DOCTYPE html>
<html lang="zh-CN">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>Catga测试报告 - $Timestamp</title>
    <style>
        * { margin: 0; padding: 0; box-sizing: border-box; }
        body {
            font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
            padding: 20px;
            min-height: 100vh;
        }
        .container {
            max-width: 1200px;
            margin: 0 auto;
            background: white;
            border-radius: 15px;
            box-shadow: 0 20px 60px rgba(0,0,0,0.3);
            overflow: hidden;
        }
        .header {
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
            color: white;
            padding: 40px;
            text-align: center;
        }
        .header h1 {
            font-size: 2.5em;
            margin-bottom: 10px;
        }
        .header .subtitle {
            font-size: 1.2em;
            opacity: 0.9;
        }
        .summary {
            display: grid;
            grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
            gap: 20px;
            padding: 40px;
            background: #f8f9fa;
        }
        .stat-card {
            background: white;
            padding: 25px;
            border-radius: 10px;
            box-shadow: 0 4px 6px rgba(0,0,0,0.1);
            text-align: center;
            transition: transform 0.3s;
        }
        .stat-card:hover {
            transform: translateY(-5px);
        }
        .stat-card .number {
            font-size: 3em;
            font-weight: bold;
            margin: 10px 0;
        }
        .stat-card .label {
            color: #666;
            font-size: 1.1em;
        }
        .stat-card.total .number { color: #667eea; }
        .stat-card.passed .number { color: #28a745; }
        .stat-card.failed .number { color: #dc3545; }
        .stat-card.skipped .number { color: #ffc107; }
        .progress-bar {
            margin: 40px;
            background: #e9ecef;
            height: 40px;
            border-radius: 20px;
            overflow: hidden;
            box-shadow: inset 0 2px 4px rgba(0,0,0,0.1);
        }
        .progress-fill {
            height: 100%;
            display: flex;
        }
        .progress-passed { background: #28a745; }
        .progress-failed { background: #dc3545; }
        .progress-skipped { background: #ffc107; }
        .details {
            padding: 40px;
        }
        .section-title {
            font-size: 1.8em;
            color: #333;
            margin-bottom: 20px;
            padding-bottom: 10px;
            border-bottom: 3px solid #667eea;
        }
        .info-grid {
            display: grid;
            grid-template-columns: repeat(auto-fit, minmax(300px, 1fr));
            gap: 20px;
            margin-bottom: 30px;
        }
        .info-item {
            background: #f8f9fa;
            padding: 15px;
            border-radius: 8px;
            border-left: 4px solid #667eea;
        }
        .info-item strong {
            color: #667eea;
            display: block;
            margin-bottom: 5px;
        }
        .footer {
            background: #2c3e50;
            color: white;
            padding: 30px;
            text-align: center;
        }
        .badge {
            display: inline-block;
            padding: 5px 15px;
            border-radius: 20px;
            font-size: 0.9em;
            font-weight: bold;
            margin: 5px;
        }
        .badge-success { background: #28a745; color: white; }
        .badge-danger { background: #dc3545; color: white; }
        .badge-warning { background: #ffc107; color: #000; }
        .badge-info { background: #17a2b8; color: white; }
    </style>
</head>
<body>
    <div class="container">
        <div class="header">
            <h1>🧪 Catga测试报告</h1>
            <div class="subtitle">$Timestamp</div>
        </div>

        <div class="summary">
            <div class="stat-card total">
                <div class="label">总测试数</div>
                <div class="number">$totalTests</div>
            </div>
            <div class="stat-card passed">
                <div class="label">✅ 通过</div>
                <div class="number">$passed</div>
            </div>
            <div class="stat-card failed">
                <div class="label">❌ 失败</div>
                <div class="number">$failed</div>
            </div>
            <div class="stat-card skipped">
                <div class="label">⏭️ 跳过</div>
                <div class="number">$skipped</div>
            </div>
        </div>

        <div class="progress-bar">
            <div class="progress-fill">
                <div class="progress-passed" style="width: $($passed/$totalTests*100)%"></div>
                <div class="progress-failed" style="width: $($failed/$totalTests*100)%"></div>
                <div class="progress-skipped" style="width: $($skipped/$totalTests*100)%"></div>
            </div>
        </div>

        <div class="details">
            <h2 class="section-title">📊 测试详情</h2>

            <div class="info-grid">
                <div class="info-item">
                    <strong>通过率</strong>
                    <span class="badge $(if ($passRate -ge 95) { 'badge-success' } elseif ($passRate -ge 80) { 'badge-warning' } else { 'badge-danger' })">
                        $passRate%
                    </span>
                </div>
                <div class="info-item">
                    <strong>测试项目</strong>
                    Catga.Tests
                </div>
                <div class="info-item">
                    <strong>配置</strong>
                    Release
                </div>
                <div class="info-item">
                    <strong>过滤器</strong>
                    $(if ($TestFilter) { $TestFilter } else { "无" })
                </div>
            </div>

            <h2 class="section-title">🎯 质量评估</h2>

            <div class="info-grid">
                <div class="info-item">
                    <strong>整体状态</strong>
                    $(if ($failed -eq 0) { '<span class="badge badge-success">优秀 ⭐⭐⭐⭐⭐</span>' }
                      elseif ($passRate -ge 95) { '<span class="badge badge-success">良好 ⭐⭐⭐⭐</span>' }
                      elseif ($passRate -ge 90) { '<span class="badge badge-warning">合格 ⭐⭐⭐</span>' }
                      else { '<span class="badge badge-danger">需改进 ⭐⭐</span>' })
                </div>
                <div class="info-item">
                    <strong>推荐行动</strong>
                    $(if ($failed -eq 0) { "保持当前质量，继续维护" }
                      elseif ($failed -le 5) { "修复少量失败测试" }
                      elseif ($failed -le 20) { "优先修复关键测试" }
                      else { "全面审查测试和代码" })
                </div>
            </div>

            $(if ($Coverage) { @"
            <h2 class="section-title">📈 代码覆盖率</h2>
            <div class="info-item">
                <strong>覆盖率报告</strong>
                已生成在: $CoverageDir/opencover.xml<br>
                运行 <code>reportgenerator</code> 查看详细HTML报告
            </div>
"@ })
        </div>

        <div class="footer">
            <p>Catga TDD测试套件 | 生成时间: $Timestamp</p>
            <p>查看详细日志: test-reports/test-results-$Timestamp.trx</p>
        </div>
    </div>
</body>
</html>
"@

    $html | Out-File -FilePath $reportPath -Encoding UTF8
    Write-Success "HTML报告已生成: $reportPath"

    if ($OpenReport) {
        Write-Info "打开报告..."
        Start-Process $reportPath
    }
}

# 步骤5: 覆盖率报告
if ($Coverage) {
    Write-Header "📈 步骤5: 生成覆盖率报告"

    $coverageFile = "$CoverageDir/opencover.xml"
    if (Test-Path $coverageFile) {
        Write-Success "覆盖率数据已生成: $coverageFile"

        # 检查是否安装了reportgenerator
        $hasReportGen = Get-Command reportgenerator -ErrorAction SilentlyContinue

        if ($hasReportGen) {
            Write-Info "使用ReportGenerator生成HTML报告..."
            reportgenerator `
                -reports:"$coverageFile" `
                -targetdir:"$CoverageDir/html" `
                -reporttypes:Html

            Write-Success "覆盖率HTML报告: $CoverageDir/html/index.htm"

            if ($OpenReport) {
                Start-Process "$CoverageDir/html/index.htm"
            }
        } else {
            Write-Warning "未找到ReportGenerator工具"
            Write-Info "安装命令: dotnet tool install -g dotnet-reportgenerator-globaltool"
        }
    } else {
        Write-Warning "未找到覆盖率文件"
    }
}

# 最终总结
Write-Header "🎉 完成"

if ($testExitCode -eq 0) {
    Write-Success "所有测试通过！"
} else {
    Write-Warning "存在失败的测试"
    Write-Info "查看详细信息: $ReportDir/"
    if ($failed -gt 0) {
        Write-Info "修复指南: tests/FIX_FAILING_TESTS_GUIDE.md"
    }
}

Write-Host "`n生成的文件:" -ForegroundColor Cyan
Write-Host "  📄 测试结果:    $ReportDir/test-results-$Timestamp.trx" -ForegroundColor Gray
if ($Detailed) {
    Write-Host "  📊 HTML报告:    $ReportDir/test-report-$Timestamp.html" -ForegroundColor Gray
}
if ($Coverage) {
    Write-Host "  📈 覆盖率数据:  $CoverageDir/opencover.xml" -ForegroundColor Gray
    if (Test-Path "$CoverageDir/html/index.htm") {
        Write-Host "  📈 覆盖率报告:  $CoverageDir/html/index.htm" -ForegroundColor Gray
    }
}

Write-Host "`n快速命令:" -ForegroundColor Cyan
Write-Host "  打开HTML报告:   start $ReportDir/test-report-$Timestamp.html" -ForegroundColor Gray
if ($Coverage -and (Test-Path "$CoverageDir/html/index.htm")) {
    Write-Host "  打开覆盖率:     start $CoverageDir/html/index.htm" -ForegroundColor Gray
}

Write-Host ""
exit $testExitCode

