# PowerShell脚本 - 运行新增TDD测试
# 用法: .\run-new-tests.ps1 [选项]

param(
    [switch]$CircuitBreaker,
    [switch]$Concurrency,
    [switch]$Stream,
    [switch]$Correlation,
    [switch]$Batch,
    [switch]$EventFailure,
    [switch]$HandlerCache,
    [switch]$ECommerce,
    [switch]$Coverage,
    [switch]$Verbose,
    [switch]$Help
)

# 颜色函数
function Write-ColorText {
    param([string]$Text, [string]$Color = "White")
    Write-Host $Text -ForegroundColor $Color
}

# 显示帮助
if ($Help) {
    Write-ColorText "`n用法: .\run-new-tests.ps1 [选项]`n" "Cyan"
    Write-ColorText "选项:" "Yellow"
    Write-Host "  -CircuitBreaker   只运行熔断器测试"
    Write-Host "  -Concurrency      只运行并发限制器测试"
    Write-Host "  -Stream           只运行流式处理测试"
    Write-Host "  -Correlation      只运行消息追踪测试"
    Write-Host "  -Batch            只运行批处理测试"
    Write-Host "  -EventFailure     只运行事件失败测试"
    Write-Host "  -HandlerCache     只运行Handler缓存测试"
    Write-Host "  -ECommerce        只运行电商订单测试"
    Write-Host "  -Coverage         收集测试覆盖率"
    Write-Host "  -Verbose          详细输出"
    Write-Host "  -Help             显示此帮助信息"
    Write-ColorText "`n示例:" "Yellow"
    Write-Host "  .\run-new-tests.ps1                    # 运行所有新增测试"
    Write-Host "  .\run-new-tests.ps1 -CircuitBreaker    # 只运行熔断器测试"
    Write-Host "  .\run-new-tests.ps1 -Coverage          # 运行测试并收集覆盖率"
    Write-Host "  .\run-new-tests.ps1 -Verbose -Coverage # 详细输出并收集覆盖率"
    exit 0
}

Write-ColorText "`n=====================================" "Blue"
Write-ColorText "   Catga TDD 测试运行脚本" "Blue"
Write-ColorText "=====================================" "Blue"
Write-Host ""

# 确定测试过滤器
$testFilter = ""
$runAll = $true

if ($CircuitBreaker) {
    $testFilter = "CircuitBreakerTests"
    $runAll = $false
} elseif ($Concurrency) {
    $testFilter = "ConcurrencyLimiterTests"
    $runAll = $false
} elseif ($Stream) {
    $testFilter = "StreamProcessingTests"
    $runAll = $false
} elseif ($Correlation) {
    $testFilter = "CorrelationTrackingTests"
    $runAll = $false
} elseif ($Batch) {
    $testFilter = "BatchProcessingEdgeCasesTests"
    $runAll = $false
} elseif ($EventFailure) {
    $testFilter = "EventHandlerFailureTests"
    $runAll = $false
} elseif ($HandlerCache) {
    $testFilter = "HandlerCachePerformanceTests"
    $runAll = $false
} elseif ($ECommerce) {
    $testFilter = "ECommerceOrderFlowTests"
    $runAll = $false
}

# 切换到项目根目录
$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location (Join-Path $scriptPath "..")

# 构建测试命令
$testCmd = "dotnet test tests/Catga.Tests/Catga.Tests.csproj"

$verbosityLevel = if ($Verbose) { "detailed" } else { "normal" }
$testCmd += " --logger `"console;verbosity=$verbosityLevel`""

if (-not $runAll) {
    Write-ColorText "运行测试: $testFilter" "Yellow"
    $testCmd += " --filter `"FullyQualifiedName~$testFilter`""
} else {
    Write-ColorText "运行所有新增测试" "Yellow"
}

if ($Coverage) {
    Write-ColorText "收集测试覆盖率..." "Yellow"
    $testCmd += " /p:CollectCoverage=true /p:CoverletOutputFormat=cobertura"
}

Write-ColorText "执行命令: $testCmd`n" "Blue"

# 运行测试
try {
    Invoke-Expression $testCmd

    if ($LASTEXITCODE -eq 0) {
        Write-Host ""
        Write-ColorText "=====================================" "Green"
        Write-ColorText "   ✅ 所有测试通过！" "Green"
        Write-ColorText "=====================================" "Green"

        if ($Coverage) {
            Write-Host ""
            Write-ColorText "覆盖率报告已生成: coverage.cobertura.xml" "Yellow"
            Write-ColorText "使用 reportgenerator 生成HTML报告:" "Yellow"
            Write-ColorText "reportgenerator -reports:coverage.cobertura.xml -targetdir:coveragereport" "Blue"
        }
    } else {
        Write-Host ""
        Write-ColorText "=====================================" "Red"
        Write-ColorText "   ❌ 测试失败！" "Red"
        Write-ColorText "=====================================" "Red"
        exit 1
    }
} catch {
    Write-Host ""
    Write-ColorText "=====================================" "Red"
    Write-ColorText "   ❌ 执行错误: $_" "Red"
    Write-ColorText "=====================================" "Red"
    exit 1
}

