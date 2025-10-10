# Catga 分布式集群框架 - 完整计划

**目标**: 构建轻量级、高性能、超级易用的分布式集群框架
**定位**: 比 Orleans 简单，比 Akka.NET 轻量，比自己搭建更易用

---

## 🎯 核心特性

### 1. 超级易用
```csharp
// ✅ 3 行启动集群
builder.Services.AddCatga();
builder.Services.AddCluster(options => {
    options.Nodes = ["http://node1:5000", "http://node2:5000"];
});

// ✅ 代码完全不变
await _mediator.SendAsync(command);  // 自动路由到正确的节点
```

### 2. 轻量级
```
依赖:    只需 NATS/Redis（可选）
内存:    < 50MB
启动:    < 100ms
代码:    < 5000 行
```

### 3. 高性能
```
吞吐量:  100万+ msg/s
延迟:    < 5ms (跨节点)
并发:    100万+ 并发连接
扩展:    线性扩展（加节点即可）
```

---

## 📋 核心功能（必须实现）

### 1. 节点发现（Service Discovery）
**目标**: 节点自动发现，无需手动配置

**实现方式**:
```csharp
// 方案1: NATS（推荐）
builder.Services.AddCluster(options => {
    options.Transport = "nats://localhost:4222";
    options.NodeId = "node1";
});

// 方案2: Redis
builder.Services.AddCluster(options => {
    options.Transport = "redis://localhost:6379";
    options.NodeId = "node1";
});

// ✅ 节点自动发现，自动加入集群
```

**特性**:
- ✅ 心跳检测（每 5 秒）
- ✅ 故障检测（30 秒无心跳 = 节点下线）
- ✅ 自动重连
- ✅ 节点元数据（IP、端口、负载等）

---

### 2. 消息路由（Message Routing）
**目标**: 消息自动路由到正确的节点

**路由策略**:
```csharp
// ✅ 策略1: 轮询（默认）
// 均匀分布到所有节点

// ✅ 策略2: 一致性哈希
// 相同 Key 总是路由到同一节点
[Route(Strategy = RoutingStrategy.ConsistentHash, Key = nameof(OrderId))]
public record ProcessOrderCommand(string OrderId, decimal Amount) : IRequest<Result>;

// ✅ 策略3: 本地优先
// 优先本地处理，本地无法处理再转发
[Route(Strategy = RoutingStrategy.LocalFirst)]
public record GetOrderQuery(string OrderId) : IRequest<Order>;

// ✅ 策略4: 广播
// 发送到所有节点
[Route(Strategy = RoutingStrategy.Broadcast)]
public record ClearCacheCommand : IRequest;
```

**实现**:
- ✅ 基于 Attribute 声明路由策略
- ✅ 编译时验证（Analyzer）
- ✅ 零配置（默认轮询）

---

### 3. 负载均衡（Load Balancing）
**目标**: 智能负载均衡，避免单节点过载

**策略**:
```csharp
public enum LoadBalancingStrategy
{
    RoundRobin,      // 轮询（默认）
    LeastConnections, // 最少连接
    LeastLoad,        // 最低负载
    Random,           // 随机
    ConsistentHash    // 一致性哈希
}
```

**实现**:
- ✅ 节点负载实时统计（CPU、内存、消息数）
- ✅ 自动选择最优节点
- ✅ 支持自定义负载算法

---

### 4. 故障转移（Failover）
**目标**: 节点故障自动转移，业务无感知

**特性**:
```csharp
// ✅ 自动重试（3 次）
// ✅ 自动切换节点
// ✅ 断路器保护（防雪崩）
// ✅ 优雅降级

// 配置
builder.Services.AddCluster(options => {
    options.Failover.MaxRetries = 3;
    options.Failover.RetryDelay = TimeSpan.FromSeconds(1);
    options.Failover.CircuitBreakerThreshold = 5;
});
```

---

### 5. 分片（Sharding）
**目标**: 数据分片，支持海量数据

**实现**:
```csharp
// ✅ 基于 Key 自动分片
[Shard(ShardKey = nameof(UserId), ShardCount = 16)]
public record GetUserCommand(string UserId) : IRequest<User>;

// ✅ 自动路由到正确的分片节点
var user = await _mediator.SendAsync(new GetUserCommand("user123"));
```

**特性**:
- ✅ 一致性哈希（虚拟节点）
- ✅ 分片重平衡（节点增减时）
- ✅ 热点数据检测

---

### 6. 本地缓存（Local Cache）
**目标**: 减少跨节点通信，提升性能

**实现**:
```csharp
// ✅ 自动缓存查询结果
[Cache(Duration = 60)] // 缓存 60 秒
public record GetProductQuery(string ProductId) : IRequest<Product>;

// ✅ 自动失效缓存
public record UpdateProductCommand(string ProductId) : IRequest
{
    // 更新时自动清除缓存
}
```

---

### 7. 集群事件（Cluster Events）
**目标**: 集群状态变化通知

**事件**:
```csharp
// ✅ 节点加入
public record NodeJoinedEvent(string NodeId, string Endpoint) : IEvent;

// ✅ 节点离开
public record NodeLeftEvent(string NodeId) : IEvent;

// ✅ Leader 选举
public record LeaderElectedEvent(string LeaderId) : IEvent;

// ✅ 分片重平衡
public record ShardRebalancedEvent(int ShardId, string OldNode, string NewNode) : IEvent;
```

---

## 🏗️ 架构设计

### 分层架构

```
┌─────────────────────────────────────────┐
│         用户代码（完全不变）              │
├─────────────────────────────────────────┤
│         ICatgaMediator 接口              │
├─────────────────────────────────────────┤
│    ClusterMediator（集群路由）           │  ← 核心
│    • 节点发现                            │
│    • 消息路由                            │
│    • 负载均衡                            │
│    • 故障转移                            │
├─────────────────────────────────────────┤
│    传输层（NATS/Redis）                  │
├─────────────────────────────────────────┤
│    本地 Mediator（高性能执行）            │
└─────────────────────────────────────────┘
```

### 核心组件

```
Catga.Cluster/
├── Discovery/
│   ├── INodeDiscovery           // 节点发现接口
│   ├── NatsNodeDiscovery        // NATS 实现
│   └── RedisNodeDiscovery       // Redis 实现
│
├── Routing/
│   ├── IMessageRouter           // 消息路由接口
│   ├── RoundRobinRouter         // 轮询路由
│   ├── ConsistentHashRouter     // 一致性哈希
│   └── LocalFirstRouter         // 本地优先
│
├── LoadBalancing/
│   ├── ILoadBalancer            // 负载均衡接口
│   ├── LeastConnectionsBalancer // 最少连接
│   └── LeastLoadBalancer        // 最低负载
│
├── Failover/
│   ├── IFailoverStrategy        // 故障转移策略
│   └── RetryFailoverStrategy    // 重试策略
│
├── Sharding/
│   ├── IShardingStrategy        // 分片策略
│   └── ConsistentHashSharding   // 一致性哈希分片
│
└── ClusterMediator.cs           // 集群 Mediator
```

---

## 💡 用户使用示例

### 1. 启动集群（超简单）

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

// ✅ 只需 3 行
builder.Services.AddCatga();
builder.Services.AddCluster(options => {
    options.Transport = "nats://localhost:4222";
    options.NodeId = Environment.MachineName;
});
builder.Services.AddGeneratedHandlers();

var app = builder.Build();
app.Run();
```

### 2. 定义消息（无需改变）

```csharp
// ✅ 普通消息（轮询）
public record CreateOrderCommand(string ProductId, int Quantity) : IRequest<Order>;

// ✅ 分片消息（一致性哈希）
[Route(Strategy = RoutingStrategy.ConsistentHash, Key = nameof(UserId))]
public record GetUserCommand(string UserId) : IRequest<User>;

// ✅ 广播消息
[Route(Strategy = RoutingStrategy.Broadcast)]
public record ClearCacheCommand : IRequest;
```

### 3. 使用（完全不变）

```csharp
// ✅ 代码完全不变
var order = await _mediator.SendAsync(new CreateOrderCommand("prod-123", 5));

// ✅ 自动路由到正确的节点
var user = await _mediator.SendAsync(new GetUserCommand("user-456"));

// ✅ 自动广播到所有节点
await _mediator.SendAsync(new ClearCacheCommand());
```

---

## 📊 性能目标

| 指标 | 目标 | 说明 |
|------|------|------|
| 吞吐量 | 100万+ msg/s | 单节点 |
| 延迟 | < 5ms | 跨节点 P99 |
| 启动时间 | < 100ms | 节点加入集群 |
| 内存占用 | < 50MB | 空闲状态 |
| 节点数 | 100+ | 支持节点数 |
| 故障转移 | < 1s | 节点故障检测 |

---

## ⏱️ 实现计划

### Phase 1: 核心基础（2-3 天）
- [ ] 节点发现（NATS）
- [ ] 心跳检测
- [ ] 节点元数据

### Phase 2: 消息路由（2 天）
- [ ] 轮询路由
- [ ] 一致性哈希
- [ ] 本地优先
- [ ] 广播

### Phase 3: 负载均衡（1 天）
- [ ] 最少连接
- [ ] 最低负载
- [ ] 负载统计

### Phase 4: 故障转移（1 天）
- [ ] 自动重试
- [ ] 断路器
- [ ] 节点切换

### Phase 5: 分片（1 天）
- [ ] 一致性哈希分片
- [ ] 分片重平衡

### Phase 6: 测试和优化（1 天）
- [ ] 单元测试
- [ ] 集成测试
- [ ] 性能测试
- [ ] 文档

**总计**: 8-9 天

---

## 🎯 核心理念

### 1. 超级简单

**配置**:
```csharp
// ✅ 只需 1 行
builder.Services.AddCluster("nats://localhost:4222");
```

**使用**:
```csharp
// ✅ 代码完全不变
await _mediator.SendAsync(command);
```

### 2. 轻量级

- ❌ 不依赖 gRPC（太重）
- ❌ 不依赖 Consul/Etcd（太复杂）
- ✅ 只依赖 NATS/Redis（已有）

### 3. 高性能

- ✅ 零拷贝（Span/Memory）
- ✅ 对象池（ArrayPool）
- ✅ 无锁设计
- ✅ 本地缓存

---

## 🚀 快速开始

### Docker Compose 部署

```yaml
version: '3.8'
services:
  nats:
    image: nats:latest
    ports:
      - "4222:4222"

  node1:
    image: myapp:latest
    environment:
      - CLUSTER_TRANSPORT=nats://nats:4222
      - NODE_ID=node1
    ports:
      - "5001:80"

  node2:
    image: myapp:latest
    environment:
      - CLUSTER_TRANSPORT=nats://nats:4222
      - NODE_ID=node2
    ports:
      - "5002:80"

  node3:
    image: myapp:latest
    environment:
      - CLUSTER_TRANSPORT=nats://nats:4222
      - NODE_ID=node3
    ports:
      - "5003:80"
```

启动：
```bash
docker-compose up -d
# ✅ 3 节点集群自动发现，自动负载均衡
```

---

## ✅ 对比其他框架

| 特性 | Catga.Cluster | Orleans | Akka.NET | Cap |
|------|---------------|---------|----------|-----|
| **易用性** | ⭐⭐⭐⭐⭐ | ⭐⭐⭐ | ⭐⭐ | ⭐⭐⭐ |
| **轻量级** | ⭐⭐⭐⭐⭐ | ⭐⭐ | ⭐⭐ | ⭐⭐⭐⭐ |
| **性能** | ⭐⭐⭐⭐⭐ | ⭐⭐⭐⭐ | ⭐⭐⭐⭐ | ⭐⭐⭐ |
| **配置** | 1 行 | 10+ 行 | 20+ 行 | 5+ 行 |
| **依赖** | NATS/Redis | 多个 | 多个 | RabbitMQ等 |
| **国内友好** | ✅ | ⚠️ | ⚠️ | ✅ |

---

## 🎊 总结

### Catga.Cluster = 最简单的分布式集群框架

**特点**:
- ✅ 超级简单 - 1 行配置，代码不变
- ✅ 轻量级 - < 50MB 内存，< 5000 行代码
- ✅ 高性能 - 100万+ msg/s，< 5ms 延迟
- ✅ 国内友好 - 只需 NATS/Redis

**目标**:
- 比 Orleans 简单 10 倍
- 比 Akka.NET 轻量 5 倍
- 比自己搭建快 100 倍

---

**🚀 准备开始实现！**

