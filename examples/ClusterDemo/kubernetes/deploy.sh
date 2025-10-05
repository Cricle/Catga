#!/bin/bash

# Catga Kubernetes 一键部署脚本

set -e

GREEN='\033[0;32m'
CYAN='\033[0;36m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m'

echo -e "${GREEN}🚀 Catga Kubernetes 部署脚本${NC}"
echo ""

# 检查 kubectl
echo -e "${CYAN}📋 检查 kubectl...${NC}"
if ! command -v kubectl &> /dev/null; then
    echo -e "${RED}❌ kubectl 未安装${NC}"
    exit 1
fi

# 检查集群连接
if ! kubectl cluster-info &> /dev/null; then
    echo -e "${RED}❌ 无法连接到 Kubernetes 集群${NC}"
    exit 1
fi

echo -e "${GREEN}✅ kubectl 检查通过${NC}"
echo ""

# 创建命名空间
echo -e "${CYAN}📦 创建命名空间...${NC}"
kubectl apply -f namespace.yml
echo -e "${GREEN}✅ 命名空间创建完成${NC}"
echo ""

# 部署 NATS 集群
echo -e "${CYAN}💬 部署 NATS 集群（3 节点）...${NC}"
kubectl apply -f nats-cluster.yml
echo -e "${GREEN}✅ NATS 配置已应用${NC}"
echo ""

# 部署 Redis 集群
echo -e "${CYAN}💾 部署 Redis 集群（主从复制）...${NC}"
kubectl apply -f redis-cluster.yml
echo -e "${GREEN}✅ Redis 配置已应用${NC}"
echo ""

# 等待基础设施就绪
echo -e "${CYAN}⏳ 等待 NATS 集群就绪...${NC}"
kubectl wait --for=condition=ready pod -l app=nats -n catga-cluster --timeout=300s || {
    echo -e "${YELLOW}⚠️  NATS 集群启动超时，请检查日志${NC}"
}

echo -e "${CYAN}⏳ 等待 Redis 集群就绪...${NC}"
kubectl wait --for=condition=ready pod -l app=redis -n catga-cluster --timeout=300s || {
    echo -e "${YELLOW}⚠️  Redis 集群启动超时，请检查日志${NC}"
}

echo -e "${GREEN}✅ 基础设施就绪${NC}"
echo ""

# 部署监控栈
echo -e "${CYAN}📊 部署监控栈（Prometheus + Grafana + Jaeger）...${NC}"
kubectl apply -f monitoring.yml
echo -e "${GREEN}✅ 监控配置已应用${NC}"
echo ""

# 部署 Catga 应用
echo -e "${CYAN}🚀 部署 Catga 应用服务...${NC}"
kubectl apply -f catga-apps.yml
echo -e "${GREEN}✅ 应用配置已应用${NC}"
echo ""

# 等待应用就绪
echo -e "${CYAN}⏳ 等待应用服务就绪...${NC}"
kubectl wait --for=condition=ready pod -l app=order-api -n catga-cluster --timeout=300s || {
    echo -e "${YELLOW}⚠️  OrderApi 启动超时${NC}"
}

echo -e "${GREEN}✅ 应用服务就绪${NC}"
echo ""

# 显示部署状态
echo ""
echo -e "${GREEN}╔═══════════════════════════════════════════════════════════════╗${NC}"
echo -e "${GREEN}║       🎉 Catga Kubernetes 部署完成！                          ║${NC}"
echo -e "${GREEN}╚═══════════════════════════════════════════════════════════════╝${NC}"
echo ""

# 获取 Service 信息
echo -e "${CYAN}📡 服务访问地址：${NC}"
echo ""

echo -e "${CYAN}获取 LoadBalancer 外部 IP（可能需要几分钟）...${NC}"
echo ""

# OrderApi
ORDER_API_IP=$(kubectl get svc order-api -n catga-cluster -o jsonpath='{.status.loadBalancer.ingress[0].ip}' 2>/dev/null || echo "Pending")
echo "  🌐 OrderApi:     http://${ORDER_API_IP} (或使用 kubectl port-forward)"

# Prometheus
PROM_IP=$(kubectl get svc prometheus -n catga-cluster -o jsonpath='{.status.loadBalancer.ingress[0].ip}' 2>/dev/null || echo "Pending")
echo "  📈 Prometheus:   http://${PROM_IP}:9090"

# Grafana
GRAFANA_IP=$(kubectl get svc grafana -n catga-cluster -o jsonpath='{.status.loadBalancer.ingress[0].ip}' 2>/dev/null || echo "Pending")
echo "  📊 Grafana:      http://${GRAFANA_IP}:3000 (admin/admin)"

# Jaeger
JAEGER_IP=$(kubectl get svc jaeger-ui -n catga-cluster -o jsonpath='{.status.loadBalancer.ingress[0].ip}' 2>/dev/null || echo "Pending")
echo "  🔍 Jaeger:       http://${JAEGER_IP}:16686"

echo ""
echo -e "${YELLOW}💡 提示：如果 LoadBalancer IP 为 Pending，可以使用 Port Forward：${NC}"
echo ""
echo "  kubectl port-forward svc/order-api 8080:80 -n catga-cluster"
echo "  kubectl port-forward svc/prometheus 9090:9090 -n catga-cluster"
echo "  kubectl port-forward svc/grafana 3000:3000 -n catga-cluster"
echo "  kubectl port-forward svc/jaeger-ui 16686:16686 -n catga-cluster"
echo ""

# 显示 Pod 状态
echo -e "${CYAN}📋 Pod 状态：${NC}"
kubectl get pods -n catga-cluster
echo ""

# 显示 HPA 状态
echo -e "${CYAN}📊 HPA 状态：${NC}"
kubectl get hpa -n catga-cluster
echo ""

# 管理命令
echo -e "${CYAN}🛠️  管理命令：${NC}"
echo ""
echo "  # 查看所有资源"
echo "  kubectl get all -n catga-cluster"
echo ""
echo "  # 查看日志"
echo "  kubectl logs -f deployment/order-api -n catga-cluster"
echo ""
echo "  # 扩容服务"
echo "  kubectl scale deployment order-api -n catga-cluster --replicas=5"
echo ""
echo "  # 删除所有资源"
echo "  kubectl delete namespace catga-cluster"
echo ""

echo -e "${GREEN}✨ 部署完成，开始使用 Catga！${NC}"
echo ""

