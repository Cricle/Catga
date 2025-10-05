# Catga 集群停止脚本（Windows PowerShell）

Write-Host "🛑 停止 Catga 集群..." -ForegroundColor Yellow
Write-Host ""

# 停止应用集群
Write-Host "📦 停止应用集群..." -ForegroundColor Cyan
docker-compose -f docker-compose.apps.yml down

if ($LASTEXITCODE -eq 0) {
    Write-Host "✅ 应用集群已停止" -ForegroundColor Green
} else {
    Write-Host "⚠️  应用集群停止失败" -ForegroundColor Yellow
}

Write-Host ""

# 停止基础设施
Write-Host "📦 停止基础设施..." -ForegroundColor Cyan
docker-compose -f docker-compose.infra.yml down

if ($LASTEXITCODE -eq 0) {
    Write-Host "✅ 基础设施已停止" -ForegroundColor Green
} else {
    Write-Host "⚠️  基础设施停止失败" -ForegroundColor Yellow
}

Write-Host ""

# 询问是否删除数据卷
$deleteVolumes = Read-Host "是否删除所有数据卷？(y/N)"
if ($deleteVolumes -eq 'y' -or $deleteVolumes -eq 'Y') {
    Write-Host "🗑️  删除数据卷..." -ForegroundColor Cyan
    docker-compose -f docker-compose.infra.yml down -v
    docker-compose -f docker-compose.apps.yml down -v
    Write-Host "✅ 数据卷已删除" -ForegroundColor Green
} else {
    Write-Host "ℹ️  保留数据卷" -ForegroundColor Blue
}

Write-Host ""

# 询问是否删除网络
$deleteNetwork = Read-Host "是否删除 Docker 网络？(y/N)"
if ($deleteNetwork -eq 'y' -or $deleteNetwork -eq 'Y') {
    Write-Host "🌐 删除 Docker 网络..." -ForegroundColor Cyan
    docker network rm catga-cluster 2>$null
    Write-Host "✅ 网络已删除" -ForegroundColor Green
} else {
    Write-Host "ℹ️  保留网络" -ForegroundColor Blue
}

Write-Host ""
Write-Host "✅ Catga 集群已完全停止" -ForegroundColor Green
Write-Host ""

