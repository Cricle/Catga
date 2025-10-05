# Catga Kubernetes 部署指南

> 完整的 Kubernetes 生产级部署配置，包含 HPA 自动扩缩容、健康检查、监控和追踪。

## 📋 目录

- [前置要求](#前置要求)
- [快速部署](#快速部署)
- [部署架构](#部署架构)
- [配置说明](#配置说明)
- [扩缩容](#扩缩容)
- [监控和追踪](#监控和追踪)
- [故障排查](#故障排查)
- [生产最佳实践](#生产最佳实践)

---

## 🔧 前置要求

### 必需组件

- **Kubernetes 集群** v1.25+
- **kubectl** CLI
- **Helm** v3+ （可选，用于更高级的部署）
- **足够的资源**：
  - 至少 3 个 Worker 节点
  - 每个节点：4 CPU, 8GB RAM

### 可选组件

- **MetalLB** 或云提供商的 LoadBalancer（用于 Service 对外暴露）
- **Persistent Volume** 支持（用于 NATS 和 Redis 数据持久化）
- **Ingress Controller**（Nginx Ingress 或 Traefik）

---

## 🚀 快速部署

### 一键部署所有组件

```bash
# 1. 创建命名空间
kubectl apply -f namespace.yml

# 2. 部署 NATS 集群
kubectl apply -f nats-cluster.yml

# 3. 部署 Redis 集群
kubectl apply -f redis-cluster.yml

# 4. 等待基础设施就绪
kubectl wait --for=condition=ready pod -l app=nats -n catga-cluster --timeout=300s
kubectl wait --for=condition=ready pod -l app=redis -n catga-cluster --timeout=300s

# 5. 部署监控栈（Prometheus + Grafana + Jaeger）
kubectl apply -f monitoring.yml

# 6. 部署 Catga 应用服务
kubectl apply -f catga-apps.yml

# 7. 等待应用就绪
kubectl wait --for=condition=ready pod -l app=order-api -n catga-cluster --timeout=300s
```

### 验证部署

```bash
# 查看所有 Pod 状态
kubectl get pods -n catga-cluster

# 预期输出：
NAME                                    READY   STATUS    RESTARTS   AGE
nats-0                                  1/1     Running   0          5m
nats-1                                  1/1     Running   0          5m
nats-2                                  1/1     Running   0          5m
redis-0                                 1/1     Running   0          5m
redis-1                                 1/1     Running   0          5m
redis-2                                 1/1     Running   0          5m
order-api-xxxxxxxxxx-xxxxx              1/1     Running   0          3m
order-api-xxxxxxxxxx-xxxxx              1/1     Running   0          3m
order-api-xxxxxxxxxx-xxxxx              1/1     Running   0          3m
order-service-xxxxxxxxxx-xxxxx          1/1     Running   0          3m
order-service-xxxxxxxxxx-xxxxx          1/1     Running   0          3m
order-service-xxxxxxxxxx-xxxxx          1/1     Running   0          3m
notification-service-xxxxxxxxxx-xxxxx   1/1     Running   0          3m
notification-service-xxxxxxxxxx-xxxxx   1/1     Running   0          3m
prometheus-xxxxxxxxxx-xxxxx             1/1     Running   0          4m
grafana-xxxxxxxxxx-xxxxx                1/1     Running   0          4m
jaeger-xxxxxxxxxx-xxxxx                 1/1     Running   0          4m

# 查看所有 Service
kubectl get svc -n catga-cluster
```

---

## 🏗️ 部署架构

### Kubernetes 资源拓扑

```
catga-cluster (Namespace)
├── StatefulSet
│   ├── nats-0, nats-1, nats-2          (NATS Cluster)
│   └── redis-0, redis-1, redis-2       (Redis Master-Slave)
│
├── Deployment
│   ├── order-api (3 replicas)          → HPA (3-10)
│   ├── order-service (3 replicas)      → HPA (3-20)
│   ├── notification-service (2 replicas) → HPA (2-10)
│   ├── prometheus (1 replica)
│   ├── grafana (1 replica)
│   └── jaeger (1 replica)
│
├── Service (LoadBalancer)
│   ├── nats-client (4222, 8222)
│   ├── redis-client (6379)
│   ├── order-api (80 → 8080)
│   ├── prometheus (9090)
│   ├── grafana (3000)
│   └── jaeger-ui (16686)
│
└── HPA (自动扩缩容)
    ├── order-api-hpa (CPU 70%, Memory 80%)
    ├── order-service-hpa (CPU 70%)
    └── notification-service-hpa (CPU 70%)
```

### 网络流量

```
外部请求
    │
    ▼
LoadBalancer Service (order-api)
    │
    ▼
OrderApi Pods (3-10 replicas) ──────┐
    │                               │
    ▼                               │
NATS Cluster (3 nodes, P2P)         │
    │                               │
    ├──────────────────────┐        │
    │                      │        │
    ▼                      ▼        ▼
OrderService           NotificationService
(3-20 replicas)        (2-10 replicas)
    │                      
    ▼                      
Redis Cluster (Master + 2 Slaves)
```

---

## ⚙️ 配置说明

### NATS 集群配置

**文件**: `nats-cluster.yml`

关键配置：
- **StatefulSet** 用于保证网络标识稳定
- **3 节点集群**，完全对等（P2P）
- **JetStream** 启用（持久化消息）
- **PVC**：每个 Pod 10GB 存储
- **健康检查**：`/healthz` 端点
- **资源限制**：
  - Requests: 100m CPU, 128Mi Memory
  - Limits: 500m CPU, 512Mi Memory

**DNS 地址**：
```
nats-0.nats.catga-cluster.svc.cluster.local:4222
nats-1.nats.catga-cluster.svc.cluster.local:4222
nats-2.nats.catga-cluster.svc.cluster.local:4222
```

### Redis 集群配置

**文件**: `redis-cluster.yml`

关键配置：
- **StatefulSet** 3 节点
- **主从复制**：redis-0 为 Master，redis-1/2 为 Slave
- **持久化**：AOF + RDB
- **MaxMemory**：256MB（根据需求调整）
- **PVC**：每个 Pod 5GB 存储

**连接字符串**：
```
redis-master:6379  # 主节点（读写）
redis-0.redis.catga-cluster.svc.cluster.local:6379  # 直连主节点
```

### Catga 应用配置

**文件**: `catga-apps.yml`

#### OrderApi

- **副本数**：3（初始）
- **HPA**：3-10 副本，基于 CPU 70% 和 Memory 80%
- **健康检查**：`/health` 端点
- **资源**：
  - Requests: 100m CPU, 128Mi Memory
  - Limits: 1000m CPU, 512Mi Memory
- **环境变量**：
  ```yaml
  NATS__Url: "nats://nats-0...:4222,nats-1...:4222,nats-2...:4222"
  Redis__Configuration: "redis-master:6379"
  Catga__EnableIdempotency: "true"
  OpenTelemetry__JaegerEndpoint: "http://jaeger-collector:14268"
  ```

#### OrderService

- **副本数**：3（初始）
- **HPA**：3-20 副本，基于 CPU 70%
- **队列组**：`order-processing`（NATS 自动负载均衡）
- **资源**：
  - Requests: 100m CPU, 128Mi Memory
  - Limits: 500m CPU, 256Mi Memory

#### NotificationService

- **副本数**：2（初始）
- **HPA**：2-10 副本
- **资源**：
  - Requests: 50m CPU, 64Mi Memory
  - Limits: 200m CPU, 128Mi Memory

---

## 📊 扩缩容

### 手动扩缩容

```bash
# 扩展 OrderApi 到 5 个副本
kubectl scale deployment order-api -n catga-cluster --replicas=5

# 扩展 OrderService 到 10 个副本
kubectl scale deployment order-service -n catga-cluster --replicas=10

# 查看当前副本数
kubectl get deployment -n catga-cluster
```

### 自动扩缩容（HPA）

HPA 已配置，基于 CPU 和 Memory 使用率自动调整副本数。

**查看 HPA 状态**：
```bash
kubectl get hpa -n catga-cluster

# 预期输出：
NAME                        REFERENCE                  TARGETS         MINPODS   MAXPODS   REPLICAS   AGE
order-api-hpa               Deployment/order-api       45%/70%         3         10        3          10m
order-service-hpa           Deployment/order-service   30%/70%         3         20        3          10m
notification-service-hpa    Deployment/notification    20%/70%         2         10        2          10m
```

**触发自动扩容**：
```bash
# 使用压测工具生成负载
kubectl run -it --rm load-generator --image=busybox --restart=Never -n catga-cluster -- /bin/sh

# 在 Pod 中运行
while true; do wget -q -O- http://order-api/api/orders; done
```

**观察 HPA 行为**：
```bash
kubectl get hpa order-api-hpa -n catga-cluster --watch
```

### 配置 HPA 策略

编辑 `catga-apps.yml` 中的 HPA 配置：

```yaml
apiVersion: autoscaling/v2
kind: HorizontalPodAutoscaler
metadata:
  name: order-api-hpa
spec:
  minReplicas: 3
  maxReplicas: 10
  metrics:
    - type: Resource
      resource:
        name: cpu
        target:
          type: Utilization
          averageUtilization: 70  # CPU 达到 70% 时扩容
    - type: Resource
      resource:
        name: memory
        target:
          type: Utilization
          averageUtilization: 80  # Memory 达到 80% 时扩容
  behavior:  # 可选：控制扩缩容速度
    scaleDown:
      stabilizationWindowSeconds: 300  # 缩容前等待 5 分钟
      policies:
        - type: Percent
          value: 50  # 每次最多缩减 50% 副本
          periodSeconds: 60
    scaleUp:
      stabilizationWindowSeconds: 0
      policies:
        - type: Percent
          value: 100  # 每次最多增加 100% 副本
          periodSeconds: 30
```

---

## 📈 监控和追踪

### Prometheus

**访问地址**：
```bash
# 获取 LoadBalancer IP
kubectl get svc prometheus -n catga-cluster

# 或使用 Port Forward
kubectl port-forward svc/prometheus 9090:9090 -n catga-cluster
```

**常用查询**：
```promql
# 总请求速率
sum(rate(catga_requests_total{namespace="catga-cluster"}[1m]))

# 按服务分组的请求速率
sum(rate(catga_requests_total[1m])) by (service)

# P95 延迟
histogram_quantile(0.95, sum(rate(catga_request_duration_bucket[5m])) by (le))

# Pod CPU 使用率
sum(rate(container_cpu_usage_seconds_total{namespace="catga-cluster"}[5m])) by (pod)
```

### Grafana

**访问地址**：
```bash
# 获取 LoadBalancer IP
kubectl get svc grafana -n catga-cluster

# 默认凭据：admin / admin
```

**配置数据源**：
- Prometheus: `http://prometheus:9090`
- Jaeger: `http://jaeger-collector:16686`

### Jaeger

**访问地址**：
```bash
# 获取 LoadBalancer IP
kubectl get svc jaeger-ui -n catga-cluster
```

**查看追踪**：
1. 选择服务：`OrderApi`
2. 查找操作：`POST /api/orders`
3. 点击 Trace 查看完整调用链

---

## 🔍 故障排查

### 查看 Pod 日志

```bash
# 查看 OrderApi 日志
kubectl logs -f deployment/order-api -n catga-cluster

# 查看特定 Pod 日志
kubectl logs -f order-api-xxxxxxxxxx-xxxxx -n catga-cluster

# 查看之前的崩溃日志
kubectl logs --previous order-api-xxxxxxxxxx-xxxxx -n catga-cluster

# 查看所有 OrderService 实例的日志
kubectl logs -l app=order-service -n catga-cluster --tail=50
```

### 进入 Pod 调试

```bash
# 进入 OrderApi Pod
kubectl exec -it deployment/order-api -n catga-cluster -- /bin/bash

# 测试 NATS 连接
kubectl run nats-box --rm -it --image=natsio/nats-box:latest -n catga-cluster -- /bin/sh
nats pub test "hello"
nats sub test

# 测试 Redis 连接
kubectl run redis-cli --rm -it --image=redis:7-alpine -n catga-cluster -- redis-cli -h redis-master
```

### 检查服务健康

```bash
# 检查 OrderApi 健康
kubectl run curl --rm -it --image=curlimages/curl:latest -n catga-cluster -- \
  curl http://order-api/health

# 检查 NATS 状态
kubectl exec -it nats-0 -n catga-cluster -- nats-server --routes_status
```

### 常见问题

#### 1. Pod 无法启动（ImagePullBackOff）

```bash
# 查看详细错误
kubectl describe pod <pod-name> -n catga-cluster

# 解决方案：确保镜像已构建并推送到可访问的 Registry
docker build -t catga/order-api:latest examples/OrderApi
docker push catga/order-api:latest
```

#### 2. NATS 连接失败

```bash
# 检查 NATS Pod 状态
kubectl get pods -l app=nats -n catga-cluster

# 查看 NATS 日志
kubectl logs -f nats-0 -n catga-cluster

# 测试 NATS 集群连通性
kubectl exec -it nats-0 -n catga-cluster -- nats-server -m 8222 --check
```

#### 3. Redis 主从同步问题

```bash
# 检查 Redis 复制状态
kubectl exec -it redis-0 -n catga-cluster -- redis-cli INFO replication
kubectl exec -it redis-1 -n catga-cluster -- redis-cli INFO replication
```

#### 4. HPA 不工作

```bash
# 检查 metrics-server 是否安装
kubectl get deployment metrics-server -n kube-system

# 查看 HPA 详情
kubectl describe hpa order-api-hpa -n catga-cluster

# 查看 Pod 资源使用
kubectl top pods -n catga-cluster
```

---

## 🛡️ 生产最佳实践

### 1. 资源配置

```yaml
# 始终设置 requests 和 limits
resources:
  requests:  # 调度器用于决定 Pod 放置位置
    cpu: 100m
    memory: 128Mi
  limits:    # 防止单个 Pod 消耗过多资源
    cpu: 1000m
    memory: 512Mi
```

### 2. 健康检查

```yaml
livenessProbe:   # 检测 Pod 是否存活，失败则重启
  httpGet:
    path: /health
    port: 8080
  initialDelaySeconds: 30
  periodSeconds: 10

readinessProbe:  # 检测 Pod 是否就绪，失败则从 Service 移除
  httpGet:
    path: /health
    port: 8080
  initialDelaySeconds: 10
  periodSeconds: 5
```

### 3. 滚动更新策略

```yaml
spec:
  strategy:
    type: RollingUpdate
    rollingUpdate:
      maxSurge: 1        # 最多多启动 1 个 Pod
      maxUnavailable: 0  # 更新期间保证 0 个 Pod 不可用
```

### 4. Pod 反亲和性（分散到不同节点）

```yaml
spec:
  affinity:
    podAntiAffinity:
      preferredDuringSchedulingIgnoredDuringExecution:
        - weight: 100
          podAffinityTerm:
            labelSelector:
              matchExpressions:
                - key: app
                  operator: In
                  values:
                    - order-api
            topologyKey: kubernetes.io/hostname
```

### 5. PodDisruptionBudget（保证最少可用 Pod）

```yaml
apiVersion: policy/v1
kind: PodDisruptionBudget
metadata:
  name: order-api-pdb
  namespace: catga-cluster
spec:
  minAvailable: 2  # 至少保持 2 个 Pod 可用
  selector:
    matchLabels:
      app: order-api
```

### 6. 网络策略（Network Policy）

```yaml
apiVersion: networking.k8s.io/v1
kind: NetworkPolicy
metadata:
  name: order-api-netpol
  namespace: catga-cluster
spec:
  podSelector:
    matchLabels:
      app: order-api
  policyTypes:
    - Ingress
    - Egress
  ingress:
    - from:
        - podSelector:
            matchLabels:
              app: nginx-ingress
      ports:
        - protocol: TCP
          port: 8080
  egress:
    - to:
        - podSelector:
            matchLabels:
              app: nats
      ports:
        - protocol: TCP
          port: 4222
    - to:
        - podSelector:
            matchLabels:
              app: redis
      ports:
        - protocol: TCP
          port: 6379
```

### 7. 密钥管理

```bash
# 创建 Secret 存储敏感信息
kubectl create secret generic catga-secrets \
  --from-literal=redis-password=<password> \
  --from-literal=jwt-secret=<secret> \
  -n catga-cluster

# 在 Deployment 中引用
env:
  - name: REDIS_PASSWORD
    valueFrom:
      secretKeyRef:
        name: catga-secrets
        key: redis-password
```

### 8. 备份策略

```bash
# 备份 NATS 数据
kubectl exec -it nats-0 -n catga-cluster -- tar czf /tmp/nats-backup.tar.gz /data/jetstream
kubectl cp catga-cluster/nats-0:/tmp/nats-backup.tar.gz ./nats-backup.tar.gz

# 备份 Redis 数据
kubectl exec -it redis-0 -n catga-cluster -- redis-cli BGSAVE
kubectl cp catga-cluster/redis-0:/data/dump.rdb ./redis-backup.rdb
```

---

## 📚 相关文档

- [Catga 框架定义](../../../FRAMEWORK_DEFINITION.md)
- [分布式集群支持](../../../DISTRIBUTED_CLUSTER_SUPPORT.md)
- [无主架构说明](../../../PEER_TO_PEER_ARCHITECTURE.md)
- [Docker Compose 部署](../README.md)

---

**Catga on Kubernetes - 生产级分布式部署！** 🚀☸️

