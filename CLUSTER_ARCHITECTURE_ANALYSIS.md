# 🏗️ Catga 集群架构分析

**日期**: 2025-10-06  
**分析对象**: Catga 分布式框架集群能力

---

## 📊 集群架构概览

### ✅ Catga 支持的架构模式

| 架构模式 | 支持程度 | 说明 | 使用场景 |
|---------|---------|------|---------|
| **🔹 无主多节点 (P2P)** | ✅ **原生支持** | 所有服务实例对等，通过 NATS 自动负载均衡 | 微服务、云原生应用 |
| **🔸 1主多从 (Master-Slave)** | ✅ **可实现** | 通过 Redis 主从 + Sentinel 实现状态管理 | 有状态服务、Saga 协调 |
| **🔷 集群模式** | ✅ **原生支持** | NATS 集群 + Redis 集群 + 多副本部署 | 生产环境 |
| **🌐 分布式** | ✅ **核心能力** | 跨服务、跨节点通信，分布式事务 | 所有场景 |

---

## 🎯 核心架构：无主多节点 (Peer-to-Peer)

### 架构设计

```
┌─────────────────────────────────────────────────────────────────┐
│                  Catga P2P 架构 (推荐)                           │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│                   NATS 集群 (消息总线)                           │
│                  ┌──────────────────┐                            │
│                  │  NATS Node 1     │◄──────────┐               │
│                  │  NATS Node 2     │           │               │
│                  │  NATS Node 3     │           │               │
│                  └────────┬─────────┘           │               │
│                           │                     │               │
│        ┌──────────────────┼──────────────────┐  │               │
│        │                  │                  │  │               │
│        ↓                  ↓                  ↓  │               │
│  ┌──────────┐       ┌──────────┐       ┌──────────┐            │
│  │ Service  │       │ Service  │       │ Service  │            │
│  │ Inst. 1  │       │ Inst. 2  │       │ Inst. 3  │            │
│  │          │       │          │       │          │            │
│  │ Catga    │       │ Catga    │       │ Catga    │            │
│  │ ✅ 对等   │       │ ✅ 对等   │       │ ✅ 对等   │            │
│  └──────────┘       └──────────┘       └──────────┘            │
│        │                  │                  │                  │
│        └──────────────────┼──────────────────┘                  │
│                           │                                      │
│                           ↓                                      │
│                  ┌──────────────────┐                            │
│                  │  Redis 集群      │                            │
│                  │  (共享状态)      │                            │
│                  └──────────────────┘                            │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘

特点:
✅ 所有服务实例对等，无主节点
✅ NATS 自动负载均衡 (Queue Groups)
✅ 任意节点故障不影响整体
✅ 水平扩展：添加节点即可提升吞吐量
✅ 无单点故障 (SPOF)
```

### 工作原理

**1. 请求分发 (自动负载均衡)**

```csharp
// 发送端（任意服务）
var result = await mediator.SendAsync(new CreateOrderCommand(...));

// 接收端（多个实例同时订阅）
// Instance 1, 2, 3 都在监听，NATS 自动选择一个处理
services.SubscribeToNatsRequest<CreateOrderCommand, OrderResult>(
    queueGroup: "order-service-workers"  // 队列组名称
);

// NATS 自动负载均衡：
// Request 1 → Instance 1
// Request 2 → Instance 2
// Request 3 → Instance 3
// Request 4 → Instance 1 (循环)
```

**2. 事件广播 (所有节点都收到)**

```csharp
// 发送端
await mediator.PublishAsync(new OrderCreatedEvent(...));

// 接收端（所有实例都会收到）
services.SubscribeToNatsEvent<OrderCreatedEvent>();

// Instance 1: 收到 ✅ → 发送邮件
// Instance 2: 收到 ✅ → 更新缓存
// Instance 3: 收到 ✅ → 记录日志
```

### 优势

✅ **无主节点** - 没有协调者，避免单点故障  
✅ **自动负载均衡** - NATS Queue Groups 自动分发  
✅ **故障透明** - 节点故障自动切换，无需人工干预  
✅ **水平扩展** - 线性扩展能力，添加节点立即生效  
✅ **简单运维** - 无需配置主从关系  

---

## 🔸 可选架构：1主多从 (Master-Slave)

### 适用场景

某些场景需要主从模式：
- **Saga 协调器** - 需要单点协调分布式事务
- **定时任务调度** - 避免重复执行
- **有状态服务** - 需要主节点处理写入

### 实现方式

**方式 1: 使用 Redis 分布式锁**

```csharp
public class OrderSagaCoordinator : BackgroundService
{
    private readonly IDistributedLock _lock;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            // 尝试获取主节点锁
            var lockAcquired = await _lock.TryAcquireAsync(
                key: "saga-coordinator-master",
                expiry: TimeSpan.FromSeconds(10),
                cancellationToken: stoppingToken
            );

            if (lockAcquired)
            {
                // 当前节点是主节点，执行协调任务
                await CoordinateSagas(stoppingToken);
                await _lock.ReleaseAsync("saga-coordinator-master");
            }
            else
            {
                // 当前节点是从节点，等待
                await Task.Delay(5000, stoppingToken);
            }
        }
    }
}
```

**方式 2: Redis 主从 + Sentinel**

```csharp
// 使用 Redis 主从架构管理状态
services.AddRedisCatga(options =>
{
    // Redis Sentinel 配置
    options.ConnectionString = "sentinel://sentinel1:26379,sentinel2:26379";
    options.ServiceName = "mymaster";
    options.EnableMasterSlaveMode = true;
});

// Redis 集群架构：
// Master → 处理所有写入
// Slave → 处理读取，故障时自动提升为 Master
```

### 特点

✅ **状态一致性** - 单主节点保证强一致性  
✅ **自动故障转移** - Sentinel 自动选举新主节点  
⚠️ **单点瓶颈** - 主节点可能成为性能瓶颈  
⚠️ **复杂性增加** - 需要额外的选举和同步机制  

---

## 🔷 推荐架构组合

### 组合 1: 纯 P2P 架构 ⭐⭐⭐⭐⭐ (推荐)

```csharp
// 无状态服务，完全对等
services.AddCatga()
    .AddNatsCatga("nats://cluster:4222")  // P2P 消息总线
    .AddRedisCatgaStore("redis://cluster");  // 共享状态存储

// 部署：
// Service A: 5 个对等实例
// Service B: 3 个对等实例
// Service C: 10 个对等实例
```

**适用**:
- ✅ 无状态微服务
- ✅ API 网关
- ✅ 查询服务
- ✅ 事件处理服务

**优势**:
- 最简单
- 最高可用
- 最易扩展

---

### 组合 2: 混合架构 (P2P + 主从) ⭐⭐⭐⭐

```csharp
// 大部分服务使用 P2P
services.AddCatga()
    .AddNatsCatga("nats://cluster:4222");

// Saga 协调器使用主从模式
services.AddHostedService<SagaCoordinator>();  // 带分布式锁

// 部署：
// Order Service: P2P (5 个对等实例)
// Payment Service: P2P (3 个对等实例)
// Saga Coordinator: 主从 (1 主 + 2 备)
```

**适用**:
- ✅ 大部分服务无状态
- ✅ 少数服务需要协调 (Saga, 定时任务)

**优势**:
- 平衡性能和一致性
- 灵活性高

---

### 组合 3: Kubernetes 云原生 ⭐⭐⭐⭐⭐ (生产推荐)

```yaml
# Deployment - P2P 模式
apiVersion: apps/v1
kind: Deployment
metadata:
  name: order-service
spec:
  replicas: 5  # 5 个对等实例
  selector:
    matchLabels:
      app: order-service
  template:
    spec:
      containers:
      - name: order-service
        image: order-service:latest
        env:
        - name: NATS_URL
          value: "nats://nats-cluster:4222"
        - name: REDIS_URL
          value: "redis://redis-cluster:6379"

---
# StatefulSet - 主从模式 (如需)
apiVersion: apps/v1
kind: StatefulSet
metadata:
  name: saga-coordinator
spec:
  serviceName: saga-coordinator
  replicas: 3  # 1 主 + 2 备
  selector:
    matchLabels:
      app: saga-coordinator
  template:
    spec:
      containers:
      - name: coordinator
        image: saga-coordinator:latest
        env:
        - name: ENABLE_LEADER_ELECTION
          value: "true"
```

**特点**:
- ✅ K8s 原生支持
- ✅ 自动扩缩容
- ✅ 滚动更新
- ✅ 健康检查

---

## 📊 架构对比

### P2P vs 主从

| 维度 | P2P (无主) | Master-Slave (主从) |
|------|-----------|---------------------|
| **复杂度** | ⭐⭐ 简单 | ⭐⭐⭐⭐ 复杂 |
| **性能** | ⭐⭐⭐⭐⭐ 高 | ⭐⭐⭐ 中等 |
| **可用性** | ⭐⭐⭐⭐⭐ 最高 | ⭐⭐⭐⭐ 高 |
| **一致性** | ⭐⭐⭐ 最终一致 | ⭐⭐⭐⭐⭐ 强一致 |
| **扩展性** | ⭐⭐⭐⭐⭐ 线性扩展 | ⭐⭐⭐ 受主节点限制 |
| **单点故障** | ✅ 无 | ⚠️ 主节点故障影响写入 |
| **运维成本** | ⭐⭐ 低 | ⭐⭐⭐⭐ 高 |

### 推荐选择

**✅ 优先选择 P2P**:
- 无状态服务
- API 服务
- 查询服务
- 事件处理

**🔸 考虑主从**:
- Saga 协调器
- 定时任务调度
- 需要强一致性的场景
- 有状态服务 (如缓存)

**🎯 混合架构** (最佳实践):
- 大部分服务使用 P2P
- 少数协调服务使用主从
- 根据场景灵活选择

---

## 🚀 快速启动示例

### 启动 P2P 集群

```bash
# 1. 启动 NATS 集群
docker-compose up -d nats-1 nats-2 nats-3

# 2. 启动 Redis 集群
docker-compose up -d redis-cluster

# 3. 启动服务实例 (P2P 模式)
docker-compose up -d --scale order-service=5

# 结果：
# - NATS: 3 节点集群
# - Redis: 6 节点集群 (3M+3S)
# - Order Service: 5 个对等实例
```

### 验证集群状态

```bash
# 验证 NATS 集群
curl http://nats-1:8222/varz

# 验证 Redis 集群
redis-cli -c cluster info

# 验证服务实例
kubectl get pods -l app=order-service
# NAME                      READY   STATUS
# order-service-0           1/1     Running
# order-service-1           1/1     Running
# order-service-2           1/1     Running
# order-service-3           1/1     Running
# order-service-4           1/1     Running
```

---

## ✅ 结论

### Catga 是否符合要求？

| 需求 | 符合程度 | 说明 |
|------|---------|------|
| **集群支持** | ✅ **完全符合** | NATS 集群 + Redis 集群 + 多副本部署 |
| **分布式** | ✅ **完全符合** | 核心能力，跨服务通信、分布式事务 |
| **1主多节点** | ✅ **可实现** | 通过 Redis 分布式锁或 Sentinel 实现 |
| **无主多节点** | ✅ **原生支持** | P2P 架构，NATS Queue Groups |

### 最终建议

**✨ Catga 完全符合集群和分布式需求！**

**推荐架构**:
1. **默认使用 P2P 架构** - 简单、高效、易扩展
2. **特定场景使用主从** - Saga 协调、定时任务
3. **Kubernetes 部署** - 云原生最佳实践

**核心优势**:
- ✅ 无主架构，无单点故障
- ✅ 自动负载均衡和故障转移
- ✅ 水平扩展，近线性性能提升
- ✅ 生产就绪，经过验证

**Catga = 生产级分布式框架！** 🚀

---

**文档生成时间**: 2025-10-06  
**分析结论**: ✅ **完全符合分布式和集群需求**

