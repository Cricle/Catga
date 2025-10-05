#!/bin/bash

# Catga 集群一键启动脚本（Linux/macOS）

set -e

echo "🚀 启动 Catga 集群..."
echo ""

# 颜色定义
GREEN='\033[0;32m'
CYAN='\033[0;36m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
GRAY='\033[0;37m'
NC='\033[0m' # No Color

# 检查 Docker
echo -e "${CYAN}📋 检查 Docker 环境...${NC}"
if ! command -v docker &> /dev/null; then
    echo -e "${RED}❌ Docker 未安装，请先安装 Docker${NC}"
    exit 1
fi

if ! command -v docker-compose &> /dev/null; then
    echo -e "${RED}❌ Docker Compose 未安装${NC}"
    exit 1
fi

echo -e "${GREEN}✅ Docker 环境检查通过${NC}"
echo ""

# 清理旧容器
echo -e "${CYAN}🧹 清理旧容器...${NC}"
docker-compose -f docker-compose.infra.yml down -v 2>/dev/null || true
docker-compose -f docker-compose.apps.yml down -v 2>/dev/null || true
docker network prune -f 2>/dev/null || true
echo -e "${GREEN}✅ 清理完成${NC}"
echo ""

# 创建 Docker 网络
echo -e "${CYAN}🌐 创建 Docker 网络...${NC}"
docker network create catga-cluster --subnet=172.20.0.0/16 2>/dev/null || true
echo -e "${GREEN}✅ 网络创建完成${NC}"
echo ""

# 启动基础设施（NATS + Redis + 监控）
echo -e "${CYAN}🏗️  启动基础设施（NATS 集群 + Redis 集群 + 监控）...${NC}"
docker-compose -f docker-compose.infra.yml up -d

if [ $? -ne 0 ]; then
    echo -e "${RED}❌ 基础设施启动失败${NC}"
    exit 1
fi

echo -e "${GREEN}✅ 基础设施启动成功${NC}"
echo ""

# 等待基础设施就绪
echo -e "${CYAN}⏳ 等待基础设施就绪（30秒）...${NC}"
sleep 30

# 检查 NATS 集群状态
echo -e "${CYAN}🔍 检查 NATS 集群状态...${NC}"
for i in 1 2 3; do
    if docker exec cluster-nats-$i wget -q -O- http://localhost:8222/healthz 2>/dev/null | grep -q "ok"; then
        echo -e "  ${GREEN}✅ NATS-$i 健康${NC}"
    else
        echo -e "  ${YELLOW}⚠️  NATS-$i 未就绪${NC}"
    fi
done

# 检查 Redis 集群状态
echo -e "${CYAN}🔍 检查 Redis 集群状态...${NC}"
for i in 1 2 3; do
    if docker exec cluster-redis-$i redis-cli ping 2>/dev/null | grep -q "PONG"; then
        echo -e "  ${GREEN}✅ Redis-$i 健康${NC}"
    else
        echo -e "  ${YELLOW}⚠️  Redis-$i 未就绪${NC}"
    fi
done

echo ""

# 构建应用镜像
echo -e "${CYAN}🔨 构建应用镜像...${NC}"
docker-compose -f docker-compose.apps.yml build

if [ $? -ne 0 ]; then
    echo -e "${RED}❌ 应用镜像构建失败${NC}"
    exit 1
fi

echo -e "${GREEN}✅ 应用镜像构建完成${NC}"
echo ""

# 启动应用集群
echo -e "${CYAN}🚀 启动应用集群...${NC}"
echo -e "  • 3x OrderApi"
echo -e "  • 3x OrderService（NATS 队列组）"
echo -e "  • 2x NotificationService"
echo ""

docker-compose -f docker-compose.apps.yml up -d --scale order-service=3 --scale notification-service=2

if [ $? -ne 0 ]; then
    echo -e "${RED}❌ 应用集群启动失败${NC}"
    exit 1
fi

echo -e "${GREEN}✅ 应用集群启动成功${NC}"
echo ""

# 等待应用就绪
echo -e "${CYAN}⏳ 等待应用就绪（20秒）...${NC}"
sleep 20

# 显示集群状态
echo ""
echo -e "${GREEN}╔═══════════════════════════════════════════════════════════════╗${NC}"
echo -e "${GREEN}║           🎉 Catga 集群启动完成！                              ║${NC}"
echo -e "${GREEN}╚═══════════════════════════════════════════════════════════════╝${NC}"
echo ""

# 服务访问地址
echo -e "${CYAN}📡 服务访问地址：${NC}"
echo ""
echo "  🌐 OrderApi (负载均衡):  http://localhost:8080"
echo -e "${GRAY}     - OrderApi-1:         http://localhost:5001${NC}"
echo -e "${GRAY}     - OrderApi-2:         http://localhost:5002${NC}"
echo -e "${GRAY}     - OrderApi-3:         http://localhost:5003${NC}"
echo ""
echo "  📊 Grafana 监控:         http://localhost:3000"
echo -e "${GRAY}     用户名: admin  密码: admin${NC}"
echo ""
echo "  📈 Prometheus:           http://localhost:9090"
echo ""
echo "  🔍 Jaeger 追踪:          http://localhost:16686"
echo ""
echo "  💬 NATS 监控:"
echo -e "${GRAY}     - NATS-1:             http://localhost:8222${NC}"
echo -e "${GRAY}     - NATS-2:             http://localhost:8223${NC}"
echo -e "${GRAY}     - NATS-3:             http://localhost:8224${NC}"
echo ""

# 测试命令
echo -e "${CYAN}🧪 测试命令：${NC}"
echo ""
echo "  # 创建订单"
echo -e "${GRAY}  curl -X POST http://localhost:8080/api/orders \\
    -H 'Content-Type: application/json' \\
    -d '{
      \"customerId\": \"test-customer\",
      \"items\": [
        {\"productId\": \"prod-1\", \"quantity\": 2, \"price\": 100.0}
      ]
    }'${NC}"
echo ""

echo "  # 查看所有容器状态"
echo -e "${GRAY}  docker-compose -f docker-compose.infra.yml -f docker-compose.apps.yml ps${NC}"
echo ""

echo "  # 查看 OrderService 日志"
echo -e "${GRAY}  docker-compose -f docker-compose.apps.yml logs -f order-service${NC}"
echo ""

# 管理命令
echo -e "${CYAN}🛠️  管理命令：${NC}"
echo ""
echo "  # 停止集群"
echo -e "${GRAY}  ./stop-cluster.sh${NC}"
echo ""
echo "  # 扩容 OrderService 到 5 个实例"
echo -e "${GRAY}  docker-compose -f docker-compose.apps.yml up -d --scale order-service=5${NC}"
echo ""
echo "  # 查看集群状态"
echo -e "${GRAY}  docker-compose -f docker-compose.infra.yml -f docker-compose.apps.yml ps${NC}"
echo ""

echo -e "${GREEN}✨ 集群已准备就绪，开始测试吧！${NC}"
echo ""

