# Catga 分布式架构设计

## 🎯 核心理念

**关注点分离 (Separation of Concerns)**

Catga 遵循清晰的架构边界：

```
┌─────────────────────────────────────────────────┐
│          应用层 (Application Layer)              │
│  - CQRS 消息调度 (Command/Query/Event)          │
│  - 业务逻辑处理 (Handlers)                        │
│  - 消息管道 (Pipeline Behaviors)                 │
└─────────────────────────────────────────────────┘
                        ↓
┌─────────────────────────────────────────────────┐
│        基础设施层 (Infrastructure Layer)         │
│  - 消息传输 (NATS JetStream / Redis Streams)    │
│  - 服务发现 (K8s DNS / Consul / Aspire)         │
│  - 负载均衡 (NATS Consumer Groups / Redis)      │
│  - 高可用性 (Clustering / Replication)           │
└─────────────────────────────────────────────────┘
```

## ✅ 设计原则

### 1. **应用层不关心基础设施细节**

❌ **错误做法**（应用层实现节点发现）：
```csharp
// 不应该在应用层实现这些
public interface INodeDiscovery {
    Task RegisterAsync(NodeInfo node);
    Task HeartbeatAsync(string nodeId);
    Task<List<NodeInfo>> GetNodesAsync();
}
```

✅ **正确做法**（委托给基础设施）：
```csharp
// 应用层只需要发布事件
public interface IDistributedMediator : ICatgaMediator {
    // 消息会自动分发到订阅者
    Task PublishAsync<TEvent>(TEvent event);
}
```

### 2. **利用基础设施的原生能力**

#### NATS JetStream
- ✅ **内置集群** - 无需应用层管理节点
- ✅ **Consumer Groups** - 自动负载均衡
- ✅ **消息持久化** - 自动重放和恢复
- ✅ **At-Least-Once / Exactly-Once** - QoS 保证

#### Redis Cluster/Sentinel
- ✅ **自动分片** - 数据分布式存储
- ✅ **主从复制** - 高可用性
- ✅ **Sentinel 故障转移** - 自动主节点切换
- ✅ **Streams Consumer Groups** - 消息队列和负载均衡

#### Kubernetes / Service Mesh
- ✅ **DNS 服务发现** - `nats.default.svc.cluster.local`
- ✅ **Health Check** - Liveness / Readiness Probes
- ✅ **Load Balancing** - Service 自动负载均衡
- ✅ **Service Mesh (Istio/Linkerd)** - 流量管理、熔断、重试

### 3. **简化的分布式中介器**

```csharp
/// <summary>
/// Simplified Distributed Mediator
/// - Local Commands/Queries → Local Mediator
/// - Events → Local + Broadcast via Transport
/// - Distribution handled by infrastructure
/// </summary>
public class DistributedMediator : IDistributedMediator
{
    public async Task PublishAsync<TEvent>(TEvent @event)
    {
        // 1. Local publish
        await _localMediator.PublishAsync(@event);

        // 2. Broadcast via NATS/Redis
        //    Infrastructure handles:
        //    - Consumer group assignment
        //    - Load balancing
        //    - Message persistence
        await _transport.SendAsync(@event, $"catga.events.{typeof(TEvent).Name}");
    }
}
```

## 📦 部署模式

### 模式 1: Kubernetes + NATS JetStream

```yaml
# NATS cluster (managed by Helm)
apiVersion: v1
kind: Service
metadata:
  name: nats
spec:
  selector:
    app: nats
  ports:
  - port: 4222

---
# Catga microservice
apiVersion: apps/v1
kind: Deployment
metadata:
  name: order-service
spec:
  replicas: 3
  template:
    spec:
      containers:
      - name: api
        image: order-service:latest
        env:
        - name: NATS_URL
          value: "nats://nats:4222"  # K8s DNS
```

**无需应用层配置**：
```csharp
services.AddNatsDistributed("nats://nats:4222");
// NATS自动处理：集群、负载均衡、高可用
```

### 模式 2: Docker Compose + Redis Cluster

```yaml
version: '3.8'
services:
  redis-cluster:
    image: redis:latest
    command: redis-server --cluster-enabled yes
    ports:
      - "6379:6379"

  order-service:
    image: order-service:latest
    environment:
      REDIS_CONNECTION: "redis-cluster:6379"
    depends_on:
      - redis-cluster
```

**无需应用层配置**：
```csharp
services.AddRedisDistributed("redis-cluster:6379");
// Redis自动处理：分片、复制、故障转移
```

### 模式 3: .NET Aspire (推荐)

```csharp
// AppHost
var redis = builder.AddRedis("redis").WithDataVolume();
var nats = builder.AddNats("nats").WithJetStream();

var orderService = builder.AddProject<Projects.OrderService>("order")
    .WithReference(redis)
    .WithReference(nats)
    .WithReplicas(3);
```

**Aspire 自动提供**：
- ✅ 服务发现（通过服务名）
- ✅ 健康检查
- ✅ 日志聚合
- ✅ 分布式追踪

## 🔄 消息流程

### Event 发布流程

```
┌─────────────┐
│  Service A  │
│   (发布者)   │
└──────┬──────┘
       │ PublishAsync(OrderCreatedEvent)
       ↓
┌─────────────────────────────────────┐
│     DistributedMediator             │
│  1. Local handlers (same process)   │
│  2. Broadcast to NATS/Redis         │
└──────┬──────────────────────────────┘
       │
       ↓ Subject: catga.events.OrderCreatedEvent
┌─────────────────────────────────────┐
│  NATS JetStream / Redis Streams     │
│  - Consumer Group: "catga-group"    │
│  - Load Balance: Round-Robin        │
│  - Persistence: Enabled             │
└──────┬──────────────────────────────┘
       │
       ↓ (Load balanced)
┌──────────────┬──────────────┬──────────────┐
│  Service B   │  Service C   │  Service D   │
│  (订阅者 1)   │  (订阅者 2)   │  (订阅者 3)   │
└──────────────┴──────────────┴──────────────┘
```

### QoS 保证（由基础设施提供）

| QoS Level | NATS 实现 | Redis 实现 |
|-----------|-----------|-----------|
| **QoS 0** (AtMostOnce) | Core Pub/Sub | PUBLISH |
| **QoS 1** (AtLeastOnce) | JetStream ACK | Streams + ACK |
| **QoS 2** (ExactlyOnce) | JetStream + App Inbox | Streams + Inbox |

## 🚀 优势总结

### 应用层
- ✅ **极简代码** - 只关注 CQRS 业务逻辑
- ✅ **零配置集群** - 无需管理节点、心跳、路由
- ✅ **易于测试** - 可以使用 InMemory 替换
- ✅ **清晰职责** - 关注点分离

### 基础设施层
- ✅ **成熟可靠** - 使用经过验证的消息系统
- ✅ **高性能** - NATS/Redis 原生优化
- ✅ **易于运维** - 标准的 K8s/Docker 部署
- ✅ **灵活扩展** - 独立扩展消息系统

## 📚 相关资源

- [NATS JetStream 文档](https://docs.nats.io/nats-concepts/jetstream)
- [Redis Cluster 文档](https://redis.io/docs/management/scaling/)
- [Kubernetes Service Discovery](https://kubernetes.io/docs/concepts/services-networking/service/)
- [.NET Aspire 文档](https://learn.microsoft.com/dotnet/aspire/)

---

**核心思想**：让专业的事交给专业的系统 - Catga 专注于 CQRS，分布式交给基础设施。

