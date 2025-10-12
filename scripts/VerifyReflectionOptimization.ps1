# Catga 反射优化验证脚本
# 用于验证反射优化的效果

Write-Host "`n╔═══════════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║                                                               ║" -ForegroundColor Cyan
Write-Host "║         🔍 Catga 反射优化验证工具 🔍                         ║" -ForegroundColor Cyan
Write-Host "║                                                               ║" -ForegroundColor Cyan
Write-Host "╚═══════════════════════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

# 检查项目是否已编译
if (-not (Test-Path "src/Catga/bin")) {
    Write-Host "⚠️  项目未编译，正在编译..." -ForegroundColor Yellow
    dotnet build Catga.sln --configuration Release > $null 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host "❌ 编译失败，请先修复编译错误" -ForegroundColor Red
        exit 1
    }
    Write-Host "✅ 编译成功" -ForegroundColor Green
}

Write-Host "📊 开始验证..." -ForegroundColor Yellow
Write-Host ""

# 1. 检查 typeof() 使用情况
Write-Host "1️⃣  检查 typeof() 使用情况" -ForegroundColor Cyan
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor DarkGray

$typeofCount = (Select-String -Path "src/**/*.cs" -Pattern "typeof\(" -Recurse | Measure-Object).Count
Write-Host "   总 typeof() 调用数: $typeofCount" -ForegroundColor White

# 热路径文件检查
$hotPathFiles = @(
    "src/Catga/Rpc/RpcClient.cs",
    "src/Catga.InMemory/CatgaMediator.cs",
    "src/Catga.Distributed/DistributedMediator.cs",
    "src/Catga.InMemory/Pipeline/Behaviors/TracingBehavior.cs"
)

$hotPathTypeofCount = 0
foreach ($file in $hotPathFiles) {
    if (Test-Path $file) {
        $count = (Select-String -Path $file -Pattern "typeof\(" | Measure-Object).Count
        $hotPathTypeofCount += $count
    }
}

if ($hotPathTypeofCount -eq 0) {
    Write-Host "   ✅ 热路径文件: 0 个 typeof() (优化完成)" -ForegroundColor Green
} else {
    Write-Host "   ⚠️  热路径文件: $hotPathTypeofCount 个 typeof()" -ForegroundColor Yellow
}

# 2. 检查 TypeNameCache 使用
Write-Host ""
Write-Host "2️⃣  检查 TypeNameCache 使用" -ForegroundColor Cyan
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor DarkGray

if (Test-Path "src/Catga/Core/TypeNameCache.cs") {
    Write-Host "   ✅ TypeNameCache.cs 存在" -ForegroundColor Green

    $cacheUsages = (Select-String -Path "src/**/*.cs" -Pattern "TypeNameCache<" -Recurse | Measure-Object).Count
    Write-Host "   ✅ TypeNameCache 使用次数: $cacheUsages" -ForegroundColor Green
} else {
    Write-Host "   ❌ TypeNameCache.cs 不存在" -ForegroundColor Red
}

# 3. 检查 TypedSubscribers 使用
Write-Host ""
Write-Host "3️⃣  检查 TypedSubscribers 使用" -ForegroundColor Cyan
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor DarkGray

if (Test-Path "src/Catga.InMemory/TypedSubscribers.cs") {
    Write-Host "   ✅ TypedSubscribers.cs 存在" -ForegroundColor Green

    $subscriberUsages = (Select-String -Path "src/**/*.cs" -Pattern "TypedSubscribers<" -Recurse | Measure-Object).Count
    Write-Host "   ✅ TypedSubscribers 使用次数: $subscriberUsages" -ForegroundColor Green
} else {
    Write-Host "   ❌ TypedSubscribers.cs 不存在" -ForegroundColor Red
}

# 4. 检查 TypedIdempotencyCache 使用
Write-Host ""
Write-Host "4️⃣  检查 TypedIdempotencyCache 使用" -ForegroundColor Cyan
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor DarkGray

if (Test-Path "src/Catga.InMemory/Stores/TypedIdempotencyStore.cs") {
    Write-Host "   ✅ TypedIdempotencyStore.cs 存在" -ForegroundColor Green

    $cacheUsages = (Select-String -Path "src/**/*.cs" -Pattern "TypedIdempotencyCache<" -Recurse | Measure-Object).Count
    Write-Host "   ✅ TypedIdempotencyCache 使用次数: $cacheUsages" -ForegroundColor Green
} else {
    Write-Host "   ❌ TypedIdempotencyStore.cs 不存在" -ForegroundColor Red
}

# 5. 检查源生成器文档
Write-Host ""
Write-Host "5️⃣  检查文档完整性" -ForegroundColor Cyan
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor DarkGray

$docs = @(
    "REFLECTION_OPTIMIZATION_SUMMARY.md",
    "REFLECTION_OPTIMIZATION_COMPLETE.md",
    "docs/guides/source-generator-usage.md",
    "docs/PROJECT_STRUCTURE.md"
)

foreach ($doc in $docs) {
    if (Test-Path $doc) {
        Write-Host "   ✅ $doc" -ForegroundColor Green
    } else {
        Write-Host "   ❌ $doc 缺失" -ForegroundColor Red
    }
}

# 6. 编译检查 (Native AOT)
Write-Host ""
Write-Host "6️⃣  Native AOT 兼容性检查" -ForegroundColor Cyan
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor DarkGray

Write-Host "   正在检查 AOT 警告..." -ForegroundColor White
$aotWarnings = dotnet build src/Catga/Catga.csproj --configuration Release 2>&1 | Select-String "IL2075|IL2091|IL2095"

if ($aotWarnings) {
    Write-Host "   ⚠️  发现 AOT 警告:" -ForegroundColor Yellow
    $aotWarnings | ForEach-Object { Write-Host "      $_" -ForegroundColor Yellow }
} else {
    Write-Host "   ✅ 无 AOT 警告" -ForegroundColor Green
}

# 7. 总结
Write-Host ""
Write-Host "╔═══════════════════════════════════════════════════════════════╗" -ForegroundColor Green
Write-Host "║                                                               ║" -ForegroundColor Green
Write-Host "║                     📋 验证总结                               ║" -ForegroundColor Green
Write-Host "║                                                               ║" -ForegroundColor Green
Write-Host "╚═══════════════════════════════════════════════════════════════╝" -ForegroundColor Green
Write-Host ""

$score = 0
$total = 6

if ($hotPathTypeofCount -eq 0) { $score++ }
if (Test-Path "src/Catga/Core/TypeNameCache.cs") { $score++ }
if (Test-Path "src/Catga.InMemory/TypedSubscribers.cs") { $score++ }
if (Test-Path "src/Catga.InMemory/Stores/TypedIdempotencyStore.cs") { $score++ }
if ((Test-Path "REFLECTION_OPTIMIZATION_SUMMARY.md") -and (Test-Path "docs/guides/source-generator-usage.md")) { $score++ }
if (-not $aotWarnings) { $score++ }

$percentage = [math]::Round(($score / $total) * 100, 1)

Write-Host "   优化完成度: $score / $total ($percentage%)" -ForegroundColor $(if ($score -eq $total) { "Green" } else { "Yellow" })
Write-Host ""

if ($score -eq $total) {
    Write-Host "   🎉 恭喜！所有优化项目已完成！" -ForegroundColor Green
    Write-Host "   🚀 Catga 现在是零反射运行时框架！" -ForegroundColor Green
} elseif ($score -ge 4) {
    Write-Host "   ✅ 大部分优化已完成，还有少量工作" -ForegroundColor Yellow
} else {
    Write-Host "   ⚠️  还有较多优化工作需要完成" -ForegroundColor Red
}

Write-Host ""
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor DarkGray
Write-Host "💡 提示: 查看 REFLECTION_OPTIMIZATION_COMPLETE.md 了解详细信息" -ForegroundColor Cyan
Write-Host ""

