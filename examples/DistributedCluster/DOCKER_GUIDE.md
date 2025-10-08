# 🐳 Catga分布式集群Docker部署指南

本指南演示如何使用Docker Compose快速启动Catga分布式集群。

---

## 📋 架构说明

```
┌──────────────────────────────────────────────────────┐
│              Catga Distributed Cluster               │
├──────────────────────────────────────────────────────┤
│                                                      │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐ │
│  │  Node 1     │  │  Node 2     │  │  Node 3     │ │
│  │  :8081      │  │  :8082      │  │  :8083      │ │
│  └──────┬──────┘  └──────┬──────┘  └──────┬──────┘ │
│         │                │                │         │
│         └────────────────┼────────────────┘         │
│                          │                          │
│         ┌────────────────┴────────────────┐         │
│         │                                 │         │
│    ┌────▼────┐                      ┌────▼────┐    │
│    │  NATS   │◄────────────────────►│  Redis  │    │
│    │ :4222   │   消息传输/持久化     │ :6379   │    │
│    └─────────┘                      └─────────┘    │
│                                                      │
└──────────────────────────────────────────────────────┘
```

### 组件说明

- **3个Catga节点**: 分布式集群节点（端口8081-8083）
- **NATS服务器**: 消息传输和JetStream持久化
- **Redis服务器**: Outbox/Inbox/Idempotency持久化

---

## 🚀 快速开始

### 前提条件

- Docker Desktop 或 Docker Engine
- Docker Compose v2.0+

### 启动集群

```bash
# 在examples/DistributedCluster目录下执行
cd examples/DistributedCluster

# 启动所有服务
docker-compose up -d

# 查看日志
docker-compose logs -f

# 查看服务状态
docker-compose ps
```

### 访问服务

- **节点1**: http://localhost:8081/swagger
- **节点2**: http://localhost:8082/swagger
- **节点3**: http://localhost:8083/swagger
- **NATS管理**: http://localhost:8222
- **Redis**: localhost:6379

---

## 🧪 测试分布式功能

### 1. 发送命令到节点1

```bash
curl -X POST http://localhost:8081/api/orders \
  -H "Content-Type: application/json" \
  -d '{
    "orderId": "order-001",
    "customerId": "customer-123",
    "amount": 99.99
  }'
```

### 2. 在节点2或节点3查看事件

```bash
# 查看节点2的日志
docker-compose logs cluster-node-2 | grep "order-001"

# 应该能看到事件被传播到了所有节点
```

### 3. 测试负载均衡

```bash
# 循环发送请求到不同节点
for i in {1..10}; do
  PORT=$((8080 + (i % 3) + 1))
  curl -X POST http://localhost:$PORT/api/orders \
    -H "Content-Type: application/json" \
    -d "{\"orderId\": \"order-$i\", \"customerId\": \"customer-123\", \"amount\": 100.0}"
  echo " -> Node $(($PORT - 8080))"
done
```

---

## 📊 监控和健康检查

### 查看健康状态

```bash
# 节点1健康检查
curl http://localhost:8081/health

# NATS健康检查
curl http://localhost:8222/healthz

# Redis健康检查
docker-compose exec redis redis-cli ping
```

### 查看NATS JetStream状态

```bash
# 进入NATS容器
docker-compose exec nats sh

# 查看Stream信息
nats stream ls
nats stream info CATGA_CLUSTER

# 查看Consumer信息
nats consumer ls CATGA_CLUSTER
```

### 查看Redis数据

```bash
# 进入Redis容器
docker-compose exec redis redis-cli

# 查看Outbox消息
KEYS catga:outbox:*

# 查看Inbox消息
KEYS catga:inbox:*

# 查看Idempotency记录
KEYS catga:idempotency:*
```

---

## 🔧 扩缩容

### 扩展节点

```bash
# 扩展到5个节点
docker-compose up -d --scale cluster-node-1=5

# 注意：需要修改docker-compose.yml支持动态端口映射
```

### 停止特定节点（测试容错）

```bash
# 停止节点2
docker-compose stop cluster-node-2

# 验证其他节点继续工作
curl http://localhost:8081/health
curl http://localhost:8083/health

# 重启节点2
docker-compose start cluster-node-2
```

---

## 🐛 故障排查

### 查看容器日志

```bash
# 所有服务
docker-compose logs

# 特定服务
docker-compose logs cluster-node-1
docker-compose logs nats
docker-compose logs redis
```

### 进入容器调试

```bash
# 进入节点容器
docker-compose exec cluster-node-1 /bin/sh

# 进入NATS容器
docker-compose exec nats sh

# 进入Redis容器
docker-compose exec redis sh
```

### 常见问题

**问题1**: 节点启动失败
```bash
# 检查NATS和Redis是否健康
docker-compose ps

# 如果不健康，重启基础设施
docker-compose restart nats redis
```

**问题2**: 消息未传播
```bash
# 检查NATS连接
docker-compose exec nats nats account info

# 检查Stream状态
docker-compose exec nats nats stream info CATGA_CLUSTER
```

**问题3**: Outbox消息堆积
```bash
# 进入Redis检查
docker-compose exec redis redis-cli
KEYS catga:outbox:pending:*

# 查看消息详情
HGETALL catga:outbox:pending:message-id
```

---

## 🧹 清理环境

### 停止服务

```bash
# 停止所有服务
docker-compose down

# 停止并删除卷（清理数据）
docker-compose down -v

# 停止并删除镜像
docker-compose down --rmi all
```

### 完全清理

```bash
# 删除所有相关容器、网络、卷
docker-compose down -v --rmi all

# 删除悬空镜像
docker image prune -f

# 删除所有Catga相关卷
docker volume ls | grep catga | awk '{print $2}' | xargs docker volume rm
```

---

## 📈 性能测试

### 使用hey进行压力测试

```bash
# 安装hey
go install github.com/rakyll/hey@latest

# 压测节点1
hey -n 10000 -c 100 -m POST \
  -H "Content-Type: application/json" \
  -d '{"orderId":"order-test","customerId":"customer-123","amount":100.0}' \
  http://localhost:8081/api/orders

# 压测所有节点（轮询）
for i in {8081..8083}; do
  echo "Testing node at port $i..."
  hey -n 1000 -c 50 \
    -m POST \
    -H "Content-Type: application/json" \
    -d '{"orderId":"order-test","customerId":"customer-123","amount":100.0}' \
    http://localhost:$i/api/orders
done
```

---

## 🎯 生产环境建议

### 1. 资源限制

在`docker-compose.yml`中添加资源限制：

```yaml
services:
  cluster-node-1:
    # ... 其他配置
    deploy:
      resources:
        limits:
          cpus: '1.0'
          memory: 512M
        reservations:
          cpus: '0.5'
          memory: 256M
```

### 2. 持久化配置

```yaml
volumes:
  nats_data:
    driver: local
    driver_opts:
      type: none
      o: bind
      device: /data/catga/nats

  redis_data:
    driver: local
    driver_opts:
      type: none
      o: bind
      device: /data/catga/redis
```

### 3. 网络隔离

使用Docker网络隔离内部和外部流量

### 4. 日志管理

配置日志驱动和轮转策略

### 5. 监控集成

集成Prometheus、Grafana等监控工具

---

## 📚 相关文档

- [Catga分布式集群架构](../../docs/Architecture.md)
- [NATS配置指南](https://docs.nats.io/)
- [Redis持久化配置](https://redis.io/docs/management/persistence/)
- [Docker Compose参考](https://docs.docker.com/compose/)

---

**🚀 现在您有了一个完整的Catga分布式集群环境！**

