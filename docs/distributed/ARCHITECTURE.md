# Catga 分布式架构设计

## 🎯 核心理念

**云原生架构 - 依赖基础设施，而非重复造轮子**

Catga 专注于 **CQRS 消息调度**，分布式能力完全依赖云原生基础设施：

```
┌─────────────────────────────────────────────────┐
│          应用层 (Application Layer)              │
│  ✅ CQRS 消息调度 (Command/Query/Event)          │
│  ✅ 业务逻辑处理 (Handlers)                       │
│  ✅ 消息管道 (Pipeline Behaviors)                │
└─────────────────────────────────────────────────┘
                        ↓
┌─────────────────────────────────────────────────┐
│          传输层 (Transport Layer)                │
│  ✅ IMessageTransport 抽象                       │
│  ✅ NatsMessageTransport (Catga.Transport.Nats)  │
│  ✅ RedisStreamTransport (Catga.Persistence.Redis)│
└─────────────────────────────────────────────────┘
                        ↓
┌─────────────────────────────────────────────────┐
│        基础设施层 (Infrastructure)               │
│  ✅ Kubernetes (服务发现、负载均衡、健康检查)      │
│  ✅ NATS JetStream (消息持久化、Consumer Groups)  │
│  ✅ Redis Cluster (高可用、分片、Streams)         │
│  ✅ .NET Aspire (编排、观测性、服务发现)          │
└─────────────────────────────────────────────────┘
```

## 📦 包结构

### 核心包
- **Catga** - CQRS 核心抽象
- **Catga.InMemory** - 单体应用实现
- **Catga.Distributed** - 分布式接口（只有接口，无实现）

### 传输层包（自选）
- **Catga.Transport.Nats** - NATS 消息传输
- **Catga.Persistence.Redis** - Redis 持久化和传输

### ❌ 不再需要的包
- ~~Catga.Distributed.Nats~~ - 已删除（使用 Catga.Transport.Nats）
- ~~Catga.Distributed.Redis~~ - 已删除（使用 Catga.Persistence.Redis）

## 🚀 Kubernetes 集成方式

### 方式 1: Kubernetes + NATS JetStream（推荐）

#### 1.1 部署 NATS 集群

```yaml
# nats-cluster.yaml
apiVersion: v1
kind: Service
metadata:
  name: nats
  namespace: default
spec:
  selector:
    app: nats
  ports:
  - name: client
    port: 4222
  - name: cluster
    port: 6222
  - name: monitor
    port: 8222
---
apiVersion: apps/v1
kind: StatefulSet
metadata:
  name: nats
spec:
  serviceName: nats
  replicas: 3
  selector:
    matchLabels:
      app: nats
  template:
    metadata:
      labels:
        app: nats
    spec:
      containers:
      - name: nats
        image: nats:latest
        args:
        - "-js"  # Enable JetStream
        - "-cluster"
        - "nats://0.0.0.0:6222"
        ports:
        - containerPort: 4222
        - containerPort: 6222
        - containerPort: 8222
```

#### 1.2 Catga 微服务配置

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

// 1. 注册 Catga 核心
builder.Services.AddCatga();

// 2. 注册消息序列化
builder.Services.AddCatgaJsonSerialization();

// 3. 注册 NATS 传输（通过 K8s DNS）
builder.Services.AddSingleton<INatsConnection>(sp =>
{
    var natsUrl = builder.Configuration["NATS_URL"] ?? "nats://nats:4222";
    var opts = NatsOpts.Default with { Url = natsUrl };
    return new NatsConnection(opts);
});

builder.Services.AddSingleton<IMessageTransport>(sp =>
{
    var connection = sp.GetRequiredService<INatsConnection>();
    var serializer = sp.GetRequiredService<IMessageSerializer>();
    var logger = sp.GetRequiredService<ILogger<NatsMessageTransport>>();
    return new NatsMessageTransport(connection, serializer, logger);
});

// 4. 注册分布式中介器
builder.Services.AddSingleton<IDistributedMediator, DistributedMediator>();
```

#### 1.3 Kubernetes Deployment

```yaml
# order-service.yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: order-service
spec:
  replicas: 3  # 自动水平扩展
  selector:
    matchLabels:
      app: order-service
  template:
    metadata:
      labels:
        app: order-service
    spec:
      containers:
      - name: api
        image: order-service:latest
        env:
        - name: NATS_URL
          value: "nats://nats:4222"  # K8s DNS 自动解析
        ports:
        - containerPort: 8080
        livenessProbe:
          httpGet:
            path: /health
            port: 8080
        readinessProbe:
          httpGet:
            path: /ready
            port: 8080
---
apiVersion: v1
kind: Service
metadata:
  name: order-service
spec:
  selector:
    app: order-service
  ports:
  - port: 80
    targetPort: 8080
  type: LoadBalancer  # 或 ClusterIP
```

### 方式 2: Kubernetes + Redis Cluster

#### 2.1 部署 Redis 集群

```yaml
# redis-cluster.yaml
apiVersion: v1
kind: Service
metadata:
  name: redis
spec:
  selector:
    app: redis
  ports:
  - port: 6379
---
apiVersion: apps/v1
kind: StatefulSet
metadata:
  name: redis
spec:
  serviceName: redis
  replicas: 6  # 3 master + 3 replica
  selector:
    matchLabels:
      app: redis
  template:
    metadata:
      labels:
        app: redis
    spec:
      containers:
      - name: redis
        image: redis:latest
        command:
        - redis-server
        - --cluster-enabled
        - "yes"
        ports:
        - containerPort: 6379
```

#### 2.2 Catga 配置

```csharp
// Program.cs
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var redisConnection = builder.Configuration["REDIS_CONNECTION"] ?? "redis:6379";
    return ConnectionMultiplexer.Connect(redisConnection);
});

builder.Services.AddSingleton<IMessageTransport>(sp =>
{
    var connection = sp.GetRequiredService<IConnectionMultiplexer>();
    var logger = sp.GetRequiredService<ILogger<RedisStreamTransport>>();
    var options = new RedisStreamOptions
    {
        StreamKey = "catga:messages",
        ConsumerGroup = "catga-group",
        ConsumerId = Environment.GetEnvironmentVariable("HOSTNAME") // K8s Pod Name
    };
    return new RedisStreamTransport(connection, logger, options);
});

builder.Services.AddSingleton<IDistributedMediator, DistributedMediator>();
```

### 方式 3: .NET Aspire（最简单）

#### 3.1 AppHost 配置

```csharp
// AppHost/Program.cs
var builder = DistributedApplication.CreateBuilder(args);

// 添加 NATS
var nats = builder.AddNats("nats")
    .WithJetStream()
    .WithDataVolume();

// 添加 Redis
var redis = builder.AddRedis("redis")
    .WithDataVolume();

// 添加微服务（自动注入连接字符串）
var orderService = builder.AddProject<Projects.OrderService>("order")
    .WithReference(nats)
    .WithReference(redis)
    .WithReplicas(3);

builder.Build().Run();
```

#### 3.2 微服务配置（零配置）

```csharp
// OrderService/Program.cs
var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();  // Aspire 自动配置

// Catga 配置
builder.Services.AddCatga();
builder.Services.AddCatgaJsonSerialization();

// NATS 传输（Aspire 自动注入连接字符串）
builder.Services.AddSingleton<INatsConnection>(sp =>
{
    var natsUrl = builder.Configuration.GetConnectionString("nats");
    return new NatsConnection(NatsOpts.Default with { Url = natsUrl });
});

builder.Services.AddSingleton<IMessageTransport, NatsMessageTransport>();
builder.Services.AddSingleton<IDistributedMediator, DistributedMediator>();
```

## 🔄 消息流程

### Event 发布流程（Kubernetes + NATS）

```
┌─────────────────┐
│  order-service  │
│    Pod 1        │
└────────┬────────┘
         │ PublishAsync(OrderCreatedEvent)
         ↓
┌─────────────────────────────────────┐
│     DistributedMediator             │
│  1. Local handlers (same pod)       │
│  2. NATS publish                    │
└──────┬──────────────────────────────┘
       │
       ↓ Subject: catga.events.OrderCreatedEvent
┌─────────────────────────────────────┐
│       NATS JetStream Cluster        │
│  - K8s Service: nats:4222           │
│  - Consumer Group: catga-group      │
│  - Load Balance: Automatic          │
└──────┬──────────────────────────────┘
       │ (K8s Load Balancer)
       ↓
┌──────────────┬──────────────┬──────────────┐
│ inventory    │ payment      │ shipping     │
│   Pod 1-3    │   Pod 1-3    │   Pod 1-3    │
└──────────────┴──────────────┴──────────────┘
```

## 📊 Kubernetes 提供的能力

### 服务发现
```bash
# DNS 自动解析
nats:4222          → nats-0.nats.default.svc.cluster.local
redis:6379         → redis-0.redis.default.svc.cluster.local
order-service:80   → order-service.default.svc.cluster.local
```

### 负载均衡
```yaml
# K8s Service 自动负载均衡
apiVersion: v1
kind: Service
spec:
  selector:
    app: order-service
  # 自动轮询到所有 Pod
```

### 健康检查与自愈
```yaml
# 自动重启不健康的 Pod
livenessProbe:
  httpGet:
    path: /health
readinessProbe:
  httpGet:
    path: /ready
```

### 自动扩缩容
```yaml
# Horizontal Pod Autoscaler
apiVersion: autoscaling/v2
kind: HorizontalPodAutoscaler
spec:
  scaleTargetRef:
    name: order-service
  minReplicas: 3
  maxReplicas: 10
  metrics:
  - type: Resource
    resource:
      name: cpu
      target:
        type: Utilization
        averageUtilization: 70
```

## ✅ 优势总结

| 能力 | Catga 职责 | K8s/基础设施职责 |
|------|-----------|-----------------|
| **CQRS** | ✅ 消息调度 | - |
| **服务发现** | - | ✅ DNS 解析 |
| **负载均衡** | - | ✅ Service LB + NATS/Redis |
| **健康检查** | - | ✅ Liveness/Readiness |
| **故障恢复** | - | ✅ 自动重启 Pod |
| **水平扩展** | - | ✅ HPA |
| **消息持久化** | - | ✅ NATS JetStream |
| **高可用** | - | ✅ StatefulSet + PV |

## 📚 相关资源

- [Kubernetes 服务发现](https://kubernetes.io/docs/concepts/services-networking/service/)
- [NATS JetStream on K8s](https://docs.nats.io/running-a-nats-service/nats-kubernetes)
- [Redis on Kubernetes](https://redis.io/docs/management/kubernetes/)
- [.NET Aspire 文档](https://learn.microsoft.com/dotnet/aspire/)

---

**设计哲学**：Catga 不重复造轮子，充分利用 Kubernetes 和云原生生态的成熟能力。
