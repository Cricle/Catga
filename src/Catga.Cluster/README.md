# Catga.Cluster

Catga 集群支持库 - 提供节点发现、负载均衡、自动路由等分布式集群功能。

## 🌟 核心特性

- ✅ **节点发现** - 自动注册/注销节点，实时监控节点状态
- ✅ **心跳检测** - 自动心跳，节点健康监控（5秒间隔，30秒超时）
- ✅ **健康检查** - 自动检测故障节点，节点状态自动更新
- ✅ **故障转移** - 自动重试和故障转移（默认最多重试2次）
- ✅ **负载均衡** - 多种路由策略（轮询、加权、一致性哈希、最少连接）
- ✅ **自动路由** - 消息自动路由到最优节点
- ✅ **远程调用** - HTTP 远程调用，自动序列化/反序列化
- ✅ **负载上报** - 实时 CPU 负载监控和上报
- ✅ **优雅下线** - 停止时自动注销节点
- ✅ **零 GC** - 关键路径零内存分配
- ✅ **高性能** - 异步处理，自动重试，超时控制

## 📦 安装

```bash
dotnet add package Catga.Cluster
```

## 🚀 快速开始

### 基础使用

```csharp
var builder = WebApplication.CreateBuilder(args);

// 添加 Catga Mediator
builder.Services.AddCatgaMediator();

// 添加集群支持
builder.Services.AddCluster(options =>
{
    options.NodeId = "node-1";
    options.Endpoint = "http://localhost:5000";
    options.HeartbeatInterval = TimeSpan.FromSeconds(5);
    options.EnableFailover = true;  // 启用故障转移（默认：true）
    options.MaxRetries = 2;         // 最大重试次数（默认：2）
    options.RetryDelay = TimeSpan.FromMilliseconds(100);  // 重试延迟
});

var app = builder.Build();

// 使用集群中间件（处理远程调用）
app.UseCluster();

app.Run();
```

### 自定义路由策略

```csharp
// 使用加权轮询（基于节点负载）
builder.Services.UseMessageRouter<WeightedRoundRobinRouter>();

// 使用一致性哈希（会话亲和性）
builder.Services.UseMessageRouter<ConsistentHashRouter>();

// 使用最少连接
builder.Services.UseMessageRouter<LeastConnectionsRouter>();
```

### 自定义节点发现

```csharp
// 使用 Redis 发现（需实现 INodeDiscovery）
builder.Services.UseNodeDiscovery<RedisNodeDiscovery>();
```

## 🎯 路由策略对比

| 策略 | 适用场景 | 优点 | 缺点 |
|------|---------|------|------|
| **RoundRobinRouter** | 均匀负载 | 简单高效 | 不考虑节点负载 |
| **WeightedRoundRobinRouter** | 异构节点 | 自动避开高负载节点 | 需要负载上报 |
| **ConsistentHashRouter** | 需要会话亲和性 | 同消息总路由到同节点 | 节点变化时部分请求会重新路由 |
| **LeastConnectionsRouter** | 长连接场景 | 均衡连接数 | 需要手动管理连接计数 |

### 1. 轮询路由（RoundRobinRouter）

```csharp
// 默认策略，均匀分配请求
builder.Services.AddCluster(); // 默认使用轮询
```

**特性**:
- 简单高效
- 零状态管理
- 请求均匀分布

### 2. 加权轮询（WeightedRoundRobinRouter）

```csharp
builder.Services.UseMessageRouter<WeightedRoundRobinRouter>();
```

**特性**:
- 基于节点负载动态分配
- 负载低的节点获得更多请求
- 权重 = 100 - Load（0-100）

**示例**:
```
Node1 (Load=10) → 权重 90 → 45% 请求
Node2 (Load=50) → 权重 50 → 25% 请求  
Node3 (Load=80) → 权重 20 → 10% 请求
```

### 3. 一致性哈希（ConsistentHashRouter）

```csharp
builder.Services.UseMessageRouter<ConsistentHashRouter>();

// 或自定义虚拟节点数
builder.Services.AddSingleton<IMessageRouter>(
    _ => new ConsistentHashRouter(virtualNodeCount: 200));
```

**特性**:
- 同样的消息总是路由到同一个节点
- 虚拟节点提高分布均匀性（默认150个）
- 适用于缓存、会话亲和性场景

**示例**:
```csharp
// 同一个 userId 总是路由到同一个节点
var request = new GetUserRequest { UserId = "user123" };
```

### 4. 最少连接（LeastConnectionsRouter）

```csharp
builder.Services.UseMessageRouter<LeastConnectionsRouter>();
```

**特性**:
- 选择活跃连接数最少的节点
- 适用于长连接、WebSocket 场景
- 需要手动管理连接计数

**示例**:
```csharp
var router = serviceProvider.GetRequiredService<IMessageRouter>() as LeastConnectionsRouter;

// 请求完成后减少连接计数
router?.DecrementConnections(nodeId);

// 重置连接计数
router?.ResetConnections(nodeId);
```

## 🔧 负载上报

### 系统负载上报（默认）

```csharp
// 默认使用 SystemLoadReporter（基于 CPU 使用率）
builder.Services.AddCluster();
```

### 自定义负载上报

```csharp
public class CustomLoadReporter : ILoadReporter
{
    public Task<int> GetCurrentLoadAsync(CancellationToken ct = default)
    {
        // 自定义负载计算逻辑
        // 可以考虑：CPU、内存、磁盘IO、网络带宽、队列长度等
        var load = CalculateCustomLoad();
        return Task.FromResult(Math.Clamp(load, 0, 100));
    }
}

// 注册自定义负载上报器
builder.Services.Replace(ServiceDescriptor.Singleton<ILoadReporter, CustomLoadReporter>());
```

## 📊 节点发现

### InMemory 发现（默认，适用于测试和单机）

```csharp
builder.Services.AddCluster(); // 默认使用 InMemoryNodeDiscovery
```

### 自定义节点发现

实现 `INodeDiscovery` 接口：

```csharp
public interface INodeDiscovery
{
    Task RegisterAsync(ClusterNode node, CancellationToken ct = default);
    Task UnregisterAsync(string nodeId, CancellationToken ct = default);
    Task HeartbeatAsync(string nodeId, int load, CancellationToken ct = default);
    Task<IReadOnlyList<ClusterNode>> GetNodesAsync(CancellationToken ct = default);
    Task<IAsyncEnumerable<ClusterEvent>> WatchAsync(CancellationToken ct = default);
}
```

**示例：Redis 节点发现**

```csharp
public class RedisNodeDiscovery : INodeDiscovery
{
    private readonly IConnectionMultiplexer _redis;
    
    public async Task RegisterAsync(ClusterNode node, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var key = $"cluster:nodes:{node.NodeId}";
        await db.StringSetAsync(key, JsonSerializer.Serialize(node), TimeSpan.FromSeconds(30));
    }
    
    // ... 实现其他方法
}

// 注册
builder.Services.UseNodeDiscovery<RedisNodeDiscovery>();
```

## 📈 监控节点事件

```csharp
var discovery = serviceProvider.GetRequiredService<INodeDiscovery>();
var events = await discovery.WatchAsync(cancellationToken);

await foreach (var @event in events.WithCancellation(cancellationToken))
{
    switch (@event.Type)
    {
        case ClusterEventType.NodeJoined:
            Console.WriteLine($"节点加入: {@event.Node.NodeId}");
            break;
        case ClusterEventType.NodeLeft:
            Console.WriteLine($"节点离开: {@event.Node.NodeId}");
            break;
        case ClusterEventType.NodeFaulted:
            Console.WriteLine($"节点故障: {@event.Node.NodeId}");
            break;
    }
}
```

## 🎨 架构设计

```
┌─────────────────────────────────────────────────────┐
│                 ClusterMediator                      │
│  (实现 ICatgaMediator，无缝替换本地 Mediator)        │
└──────────────┬──────────────────────┬────────────────┘
               │                      │
       ┌───────▼────────┐    ┌────────▼────────┐
       │ INodeDiscovery │    │ IMessageRouter  │
       └───────┬────────┘    └────────┬────────┘
               │                      │
    ┌──────────┴─────────┐   ┌────────┴────────────┐
    │ InMemoryDiscovery  │   │ RoundRobinRouter    │
    │ RedisDiscovery     │   │ WeightedRouter      │
    │ K8sDiscovery       │   │ ConsistentHash      │
    └────────────────────┘   │ LeastConnections    │
                              └─────────────────────┘
```

## 🔐 配置选项

```csharp
public class ClusterOptions
{
    // 节点 ID（默认：机器名）
    public string NodeId { get; set; } = Environment.MachineName;
    
    // 节点端点（http://ip:port）
    public string? Endpoint { get; set; }
    
    // 心跳间隔（默认：5 秒）
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(5);
    
    // 心跳超时（默认：30 秒）
    public TimeSpan HeartbeatTimeout { get; set; } = TimeSpan.FromSeconds(30);
    
    // 节点元数据
    public Dictionary<string, string>? Metadata { get; set; }
}
```

## 📚 示例

查看 `examples/DistributedCluster/` 获取完整示例。

## 🤝 贡献

欢迎提交 Issue 和 Pull Request！

## 📄 许可证

MIT License

