# Catga 集群一键启动脚本（Windows PowerShell）

Write-Host "🚀 启动 Catga 集群..." -ForegroundColor Green
Write-Host ""

# 检查 Docker
Write-Host "📋 检查 Docker 环境..." -ForegroundColor Cyan
if (!(Get-Command docker -ErrorAction SilentlyContinue)) {
    Write-Host "❌ Docker 未安装，请先安装 Docker Desktop" -ForegroundColor Red
    exit 1
}

if (!(Get-Command docker-compose -ErrorAction SilentlyContinue)) {
    Write-Host "❌ Docker Compose 未安装" -ForegroundColor Red
    exit 1
}

Write-Host "✅ Docker 环境检查通过" -ForegroundColor Green
Write-Host ""

# 清理旧容器
Write-Host "🧹 清理旧容器..." -ForegroundColor Cyan
docker-compose -f docker-compose.infra.yml down -v 2>$null
docker-compose -f docker-compose.apps.yml down -v 2>$null
docker network prune -f 2>$null
Write-Host "✅ 清理完成" -ForegroundColor Green
Write-Host ""

# 创建 Docker 网络
Write-Host "🌐 创建 Docker 网络..." -ForegroundColor Cyan
docker network create catga-cluster --subnet=172.20.0.0/16 2>$null
Write-Host "✅ 网络创建完成" -ForegroundColor Green
Write-Host ""

# 启动基础设施（NATS + Redis + 监控）
Write-Host "🏗️  启动基础设施（NATS 集群 + Redis 集群 + 监控）..." -ForegroundColor Cyan
docker-compose -f docker-compose.infra.yml up -d

if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ 基础设施启动失败" -ForegroundColor Red
    exit 1
}

Write-Host "✅ 基础设施启动成功" -ForegroundColor Green
Write-Host ""

# 等待基础设施就绪
Write-Host "⏳ 等待基础设施就绪（30秒）..." -ForegroundColor Cyan
Start-Sleep -Seconds 30

# 检查 NATS 集群状态
Write-Host "🔍 检查 NATS 集群状态..." -ForegroundColor Cyan
for ($i = 1; $i -le 3; $i++) {
    $natsHealth = docker exec cluster-nats-$i wget -q -O- http://localhost:8222/healthz 2>$null
    if ($natsHealth -eq "ok") {
        Write-Host "  ✅ NATS-$i 健康" -ForegroundColor Green
    } else {
        Write-Host "  ⚠️  NATS-$i 未就绪" -ForegroundColor Yellow
    }
}

# 检查 Redis 集群状态
Write-Host "🔍 检查 Redis 集群状态..." -ForegroundColor Cyan
for ($i = 1; $i -le 3; $i++) {
    $redisPing = docker exec cluster-redis-$i redis-cli ping 2>$null
    if ($redisPing -eq "PONG") {
        Write-Host "  ✅ Redis-$i 健康" -ForegroundColor Green
    } else {
        Write-Host "  ⚠️  Redis-$i 未就绪" -ForegroundColor Yellow
    }
}

Write-Host ""

# 构建应用镜像
Write-Host "🔨 构建应用镜像..." -ForegroundColor Cyan
docker-compose -f docker-compose.apps.yml build

if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ 应用镜像构建失败" -ForegroundColor Red
    exit 1
}

Write-Host "✅ 应用镜像构建完成" -ForegroundColor Green
Write-Host ""

# 启动应用集群
Write-Host "🚀 启动应用集群..." -ForegroundColor Cyan
Write-Host "  • 3x OrderApi" -ForegroundColor White
Write-Host "  • 3x OrderService（NATS 队列组）" -ForegroundColor White
Write-Host "  • 2x NotificationService" -ForegroundColor White
Write-Host ""

docker-compose -f docker-compose.apps.yml up -d --scale order-service=3 --scale notification-service=2

if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ 应用集群启动失败" -ForegroundColor Red
    exit 1
}

Write-Host "✅ 应用集群启动成功" -ForegroundColor Green
Write-Host ""

# 等待应用就绪
Write-Host "⏳ 等待应用就绪（20秒）..." -ForegroundColor Cyan
Start-Sleep -Seconds 20

# 显示集群状态
Write-Host ""
Write-Host "╔═══════════════════════════════════════════════════════════════╗" -ForegroundColor Green
Write-Host "║           🎉 Catga 集群启动完成！                              ║" -ForegroundColor Green
Write-Host "╚═══════════════════════════════════════════════════════════════╝" -ForegroundColor Green
Write-Host ""

# 服务访问地址
Write-Host "📡 服务访问地址：" -ForegroundColor Cyan
Write-Host ""
Write-Host "  🌐 OrderApi (负载均衡):  http://localhost:8080" -ForegroundColor White
Write-Host "     - OrderApi-1:         http://localhost:5001" -ForegroundColor Gray
Write-Host "     - OrderApi-2:         http://localhost:5002" -ForegroundColor Gray
Write-Host "     - OrderApi-3:         http://localhost:5003" -ForegroundColor Gray
Write-Host ""
Write-Host "  📊 Grafana 监控:         http://localhost:3000" -ForegroundColor White
Write-Host "     用户名: admin  密码: admin" -ForegroundColor Gray
Write-Host ""
Write-Host "  📈 Prometheus:           http://localhost:9090" -ForegroundColor White
Write-Host ""
Write-Host "  🔍 Jaeger 追踪:          http://localhost:16686" -ForegroundColor White
Write-Host ""
Write-Host "  💬 NATS 监控:" -ForegroundColor White
Write-Host "     - NATS-1:             http://localhost:8222" -ForegroundColor Gray
Write-Host "     - NATS-2:             http://localhost:8223" -ForegroundColor Gray
Write-Host "     - NATS-3:             http://localhost:8224" -ForegroundColor Gray
Write-Host ""

# 测试命令
Write-Host "🧪 测试命令：" -ForegroundColor Cyan
Write-Host ""
Write-Host "  # 创建订单" -ForegroundColor White
Write-Host @"
  Invoke-RestMethod -Method POST -Uri "http://localhost:8080/api/orders" ``
      -ContentType "application/json" ``
      -Body (@{
          customerId = "test-customer"
          items = @(
              @{ productId = "prod-1"; quantity = 2; price = 100.0 }
          )
      } | ConvertTo-Json)
"@ -ForegroundColor Gray
Write-Host ""

Write-Host "  # 查看所有容器状态" -ForegroundColor White
Write-Host "  docker-compose -f docker-compose.infra.yml -f docker-compose.apps.yml ps" -ForegroundColor Gray
Write-Host ""

Write-Host "  # 查看 OrderService 日志" -ForegroundColor White
Write-Host "  docker-compose -f docker-compose.apps.yml logs -f order-service" -ForegroundColor Gray
Write-Host ""

# 管理命令
Write-Host "🛠️  管理命令：" -ForegroundColor Cyan
Write-Host ""
Write-Host "  # 停止集群" -ForegroundColor White
Write-Host "  .\stop-cluster.ps1" -ForegroundColor Gray
Write-Host ""
Write-Host "  # 扩容 OrderService 到 5 个实例" -ForegroundColor White
Write-Host "  docker-compose -f docker-compose.apps.yml up -d --scale order-service=5" -ForegroundColor Gray
Write-Host ""
Write-Host "  # 查看集群状态" -ForegroundColor White
Write-Host "  docker-compose -f docker-compose.infra.yml -f docker-compose.apps.yml ps" -ForegroundColor Gray
Write-Host ""

Write-Host "✨ 集群已准备就绪，开始测试吧！" -ForegroundColor Green
Write-Host ""

