#!/bin/bash

# Catga 集群停止脚本（Linux/macOS）

set -e

GREEN='\033[0;32m'
CYAN='\033[0;36m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m'

echo -e "${YELLOW}🛑 停止 Catga 集群...${NC}"
echo ""

# 停止应用集群
echo -e "${CYAN}📦 停止应用集群...${NC}"
docker-compose -f docker-compose.apps.yml down

if [ $? -eq 0 ]; then
    echo -e "${GREEN}✅ 应用集群已停止${NC}"
else
    echo -e "${YELLOW}⚠️  应用集群停止失败${NC}"
fi

echo ""

# 停止基础设施
echo -e "${CYAN}📦 停止基础设施...${NC}"
docker-compose -f docker-compose.infra.yml down

if [ $? -eq 0 ]; then
    echo -e "${GREEN}✅ 基础设施已停止${NC}"
else
    echo -e "${YELLOW}⚠️  基础设施停止失败${NC}"
fi

echo ""

# 询问是否删除数据卷
read -p "是否删除所有数据卷？(y/N): " delete_volumes
if [ "$delete_volumes" == "y" ] || [ "$delete_volumes" == "Y" ]; then
    echo -e "${CYAN}🗑️  删除数据卷...${NC}"
    docker-compose -f docker-compose.infra.yml down -v
    docker-compose -f docker-compose.apps.yml down -v
    echo -e "${GREEN}✅ 数据卷已删除${NC}"
else
    echo -e "${BLUE}ℹ️  保留数据卷${NC}"
fi

echo ""

# 询问是否删除网络
read -p "是否删除 Docker 网络？(y/N): " delete_network
if [ "$delete_network" == "y" ] || [ "$delete_network" == "Y" ]; then
    echo -e "${CYAN}🌐 删除 Docker 网络...${NC}"
    docker network rm catga-cluster 2>/dev/null || true
    echo -e "${GREEN}✅ 网络已删除${NC}"
else
    echo -e "${BLUE}ℹ️  保留网络${NC}"
fi

echo ""
echo -e "${GREEN}✅ Catga 集群已完全停止${NC}"
echo ""

