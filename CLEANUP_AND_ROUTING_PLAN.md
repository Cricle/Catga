# Catga 清理和路由优化计划

**日期**: 2025-10-10
**目标**: 清理无用代码/文档 + 实现完整路由 + 充分利用 NATS/Redis 原生功能

---

## 📋 问题分析

### 当前问题

1. **无用代码和文档过多**
   - 大量临时文档（SESSION_*.md, FINAL_*.md, SIMPLIFICATION_*.md）
   - 重复的架构文档
   - 已删除功能的残留文档
   - 未使用的示例（AotDemo 已移除但文件夹还在）

2. **路由功能不完整**
   - DistributedMediator 只有 Round-Robin
   - 缺少一致性哈希（Consistent Hashing）
   - 缺少基于主题的路由（Topic-based Routing）
   - 缺少基于键的路由（Key-based Routing）

3. **降级到内存实现**
   - NatsNodeDiscovery 使用 ConcurrentDictionary（内存）
   - RedisNodeDiscovery 使用 ConcurrentDictionary（内存）
   - 应该直接使用 NATS JetStream KV Store 和 Redis 原生功能

---

## 🎯 优化计划

### Phase 1: 清理无用文档和代码（1小时）

#### 1.1 删除临时文档 ⭐

**要删除的文档**（~15个）:
```
根目录:
- CATGA_CORE_FOCUS.md              # 已过时
- CATGA_SIMPLIFIED_PLAN.md         # 已完成
- CODE_REVIEW_OPTIMIZATION_POINTS.md  # 临时文档
- FINAL_CODE_REVIEW.md             # 临时文档
- FINAL_STATUS.md                  # 临时文档
- P0_OPTIMIZATION_COMPLETE.md      # 临时文档
- PHASE2_PROGRESS.md               # 临时文档
- QOS_IMPLEMENTATION_PLAN.md       # 临时文档
- QUICK_START.md                   # 重复（已有 docs/QuickStart.md）
- SESSION_FINAL.md                 # 临时文档
- SESSION_FINAL_SIMPLIFICATION.md  # 临时文档
- SIMPLIFICATION_FINAL.md          # 临时文档

保留:
- CATGA_V2_COMPLETE.md             # 项目完成报告（重要）
- CATGA_VS_MASSTRANSIT.md          # 对比分析（重要）
- DISTRIBUTED_MESSAGING_GUARANTEES.md  # 核心文档（重要）
- LOCK_FREE_DISTRIBUTED_DESIGN.md  # 核心文档（重要）
- SESSION_SUMMARY_2025_10_10.md    # 会话总结（重要）
- IMPLEMENTATION_STATUS.md         # 进度跟踪（重要）
- ARCHITECTURE.md                  # 架构文档（重要）
- README.md                        # 主文档（重要）
```

#### 1.2 整理 docs/ 文件夹

**要删除的文档**:
```
docs/:
- Architecture.md                  # 重复（已有 architecture/ARCHITECTURE.md）
- BestPractices.md                 # 空或过时
- Migration.md                     # 空或过时
- PerformanceTuning.md            # 空或过时
- QuickStart.md                    # 重复（根目录有 README.md）

docs/architecture/:
- ARCHITECTURE.md                  # 重复（根目录有 ARCHITECTURE.md）
```

#### 1.3 清理空文件夹和未使用代码

**要删除的项目/文件夹**:
```
src/:
- src/Catga.Cluster/               # 已删除但文件夹可能还在
- src/Catga.ServiceDiscovery.Kubernetes/  # 不再需要（已简化）

examples/:
- examples/DistributedCluster/     # 已删除
- examples/AotDemo/                # 已移除但可能还有文件夹

benchmarks/:
- BenchmarkDotNet.Artifacts/       # 临时文件（应在 .gitignore）
```

---

### Phase 2: 实现完整路由功能（2小时）⭐

#### 2.1 路由策略接口

```csharp
// src/Catga.Distributed/Routing/IRoutingStrategy.cs

public interface IRoutingStrategy
{
    /// <summary>
    /// 选择目标节点
    /// </summary>
    Task<NodeInfo?> SelectNodeAsync(
        IReadOnlyList<NodeInfo> nodes,
        object message,
        CancellationToken ct = default);
}

// 实现策略:
// 1. RoundRobinRoutingStrategy      - 轮询（已有）
// 2. ConsistentHashRoutingStrategy  - 一致性哈希（新增）
// 3. RandomRoutingStrategy          - 随机（新增）
// 4. LoadBasedRoutingStrategy       - 基于负载（新增）
// 5. LocalFirstRoutingStrategy      - 本地优先（新增）
```

#### 2.2 一致性哈希路由（核心）

```csharp
// src/Catga.Distributed/Routing/ConsistentHashRoutingStrategy.cs

/// <summary>
/// 一致性哈希路由（用于分片、会话保持）
/// 无锁实现，使用 SortedList
/// </summary>
public class ConsistentHashRoutingStrategy : IRoutingStrategy
{
    private readonly int _virtualNodes = 150; // 虚拟节点数
    private readonly Func<object, string> _keyExtractor;

    public Task<NodeInfo?> SelectNodeAsync(
        IReadOnlyList<NodeInfo> nodes,
        object message,
        CancellationToken ct = default)
    {
        // 1. 提取消息的路由键
        var key = _keyExtractor(message);

        // 2. 计算哈希值
        var hash = GetHash(key);

        // 3. 在哈希环上查找节点（二分查找，无锁）
        var node = FindNode(hash, nodes);

        return Task.FromResult(node);
    }

    private static int GetHash(string key)
    {
        // 使用 xxHash 或 MurmurHash（快速、均匀）
        return HashCode.Combine(key);
    }
}
```

#### 2.3 基于主题的路由

```csharp
// src/Catga.Distributed/Routing/TopicRoutingStrategy.cs

/// <summary>
/// 基于主题的路由（发布/订阅模式）
/// 直接使用 NATS/Redis 的主题功能
/// </summary>
public class TopicRoutingStrategy : IRoutingStrategy
{
    public Task<NodeInfo?> SelectNodeAsync(
        IReadOnlyList<NodeInfo> nodes,
        object message,
        CancellationToken ct = default)
    {
        // 1. 提取消息的主题
        var topic = GetTopic(message);

        // 2. 广播到所有订阅该主题的节点
        // （由 NATS/Redis 自动处理）

        return Task.FromResult<NodeInfo?>(null); // 广播，不需要选择节点
    }
}
```

#### 2.4 更新 DistributedMediator

```csharp
// src/Catga.Distributed/DistributedMediator.cs

public sealed class DistributedMediator : IDistributedMediator
{
    private readonly IRoutingStrategy _routingStrategy; // 可配置路由策略

    public DistributedMediator(
        ICatgaMediator localMediator,
        IMessageTransport transport,
        INodeDiscovery discovery,
        ILogger logger,
        NodeInfo currentNode,
        IRoutingStrategy? routingStrategy = null) // 新增参数
    {
        _routingStrategy = routingStrategy ?? new RoundRobinRoutingStrategy(); // 默认 Round-Robin
    }

    public async ValueTask<CatgaResult<TResponse>> SendAsync<TRequest, TResponse>(
        TRequest request,
        CancellationToken ct = default)
        where TRequest : IRequest<TResponse>
    {
        // 1. 尝试本地处理
        try
        {
            return await _localMediator.SendAsync<TRequest, TResponse>(request, ct);
        }
        catch
        {
            // 2. 本地失败，使用路由策略选择节点
            var nodes = await _discovery.GetNodesAsync(ct);
            var remoteNodes = nodes.Where(n => n.NodeId != _currentNode.NodeId).ToList();

            if (remoteNodes.Count == 0)
                return CatgaResult<TResponse>.Failure("No available nodes");

            // 3. 使用可配置的路由策略（无锁）
            var targetNode = await _routingStrategy.SelectNodeAsync(remoteNodes, request, ct);

            if (targetNode == null)
                return CatgaResult<TResponse>.Failure("No suitable node found");

            return await SendToNodeAsync<TRequest, TResponse>(request, targetNode.NodeId, ct);
        }
    }
}
```

---

### Phase 3: 充分利用 NATS/Redis 原生功能（3小时）⭐⭐⭐

#### 3.1 NATS JetStream KV Store（节点发现）

**问题**: 当前 NatsNodeDiscovery 使用内存 ConcurrentDictionary

**解决**: 直接使用 NATS JetStream KV Store

```csharp
// src/Catga.Distributed/Nats/NatsNodeDiscovery.cs

public sealed class NatsNodeDiscovery : INodeDiscovery
{
    private readonly INatsConnection _connection;
    private readonly ILogger _logger;
    private INatsJSContext? _jetStream;  // JetStream 上下文
    private INatsKVStore? _kvStore;      // KV Store（原生功能）

    public async Task RegisterAsync(NodeInfo node, CancellationToken ct = default)
    {
        var js = await GetJetStreamAsync(ct);
        var kv = await GetKVStoreAsync(ct);

        // 使用 NATS KV Store（原生，持久化）
        var json = JsonSerializer.Serialize(node);
        await kv.PutAsync(node.NodeId, json, cancellationToken: ct);

        // 设置 TTL（30秒自动过期）
        await kv.PutAsync(node.NodeId, json, new NatsKVPutOpts
        {
            TTL = TimeSpan.FromSeconds(30)
        }, cancellationToken: ct);
    }

    public async Task<IReadOnlyList<NodeInfo>> GetNodesAsync(CancellationToken ct = default)
    {
        var kv = await GetKVStoreAsync(ct);
        var nodes = new List<NodeInfo>();

        // 从 NATS KV Store 读取所有节点（原生）
        await foreach (var key in kv.GetKeysAsync(cancellationToken: ct))
        {
            var entry = await kv.GetEntryAsync<string>(key, cancellationToken: ct);
            if (entry?.Value != null)
            {
                var node = JsonSerializer.Deserialize<NodeInfo>(entry.Value);
                if (node != null && node.IsOnline)
                    nodes.Add(node);
            }
        }

        return nodes;
    }

    private async Task<INatsJSContext> GetJetStreamAsync(CancellationToken ct)
    {
        if (_jetStream != null) return _jetStream;

        // 创建 JetStream 上下文（原生）
        _jetStream = new NatsJSContext(_connection);
        return _jetStream;
    }

    private async Task<INatsKVStore> GetKVStoreAsync(CancellationToken ct)
    {
        if (_kvStore != null) return _kvStore;

        var js = await GetJetStreamAsync(ct);

        // 创建或获取 KV Store（原生，持久化）
        var config = new NatsKVConfig("catga-nodes")
        {
            History = 5,                          // 保留 5 个历史版本
            MaxAge = TimeSpan.FromMinutes(2),     // 2 分钟后过期
            Storage = StreamConfigStorage.File    // 持久化到文件
        };

        try
        {
            _kvStore = await js.GetKeyValueAsync("catga-nodes", cancellationToken: ct);
        }
        catch
        {
            _kvStore = await js.CreateKeyValueAsync(config, cancellationToken: ct);
        }

        return _kvStore;
    }
}
```

**优势**:
- ✅ 持久化（文件存储）
- ✅ 分布式一致性（NATS 集群自动同步）
- ✅ TTL 自动过期（不需要手动清理）
- ✅ 历史版本（可回溯）
- ❌ 移除内存 ConcurrentDictionary（降级）

#### 3.2 Redis Streams（消息路由）

**问题**: 当前只使用 Redis Pub/Sub，没有利用 Streams

**解决**: 使用 Redis Streams + Consumer Groups

```csharp
// src/Catga.Distributed/Redis/RedisStreamTransport.cs

/// <summary>
/// 基于 Redis Streams 的消息传输（原生功能）
/// 支持: 消费组、ACK、死信队列、持久化
/// </summary>
public class RedisStreamTransport : IMessageTransport
{
    private readonly IConnectionMultiplexer _redis;
    private readonly string _streamKey = "catga:messages";
    private readonly string _consumerGroup = "catga-group";

    public async Task PublishAsync<TMessage>(
        TMessage message,
        TransportContext? context = null,
        CancellationToken ct = default)
        where TMessage : class
    {
        var db = _redis.GetDatabase();

        // 使用 Redis Streams（原生，持久化）
        var fields = new NameValueEntry[]
        {
            new("type", typeof(TMessage).FullName!),
            new("payload", JsonSerializer.Serialize(message)),
            new("messageId", context?.MessageId ?? Guid.NewGuid().ToString()),
            new("timestamp", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
        };

        // 添加到 Stream（自动持久化，无需手动配置）
        await db.StreamAddAsync(_streamKey, fields);
    }

    public async Task SubscribeAsync<TMessage>(
        Func<TMessage, TransportContext, Task> handler,
        CancellationToken ct = default)
        where TMessage : class
    {
        var db = _redis.GetDatabase();

        // 创建消费组（如果不存在）
        try
        {
            await db.StreamCreateConsumerGroupAsync(
                _streamKey,
                _consumerGroup,
                StreamPosition.NewMessages);
        }
        catch (RedisServerException ex) when (ex.Message.Contains("BUSYGROUP"))
        {
            // 消费组已存在，忽略
        }

        // 消费消息（Consumer Groups，自动负载均衡）
        var consumerId = Guid.NewGuid().ToString();

        while (!ct.IsCancellationRequested)
        {
            // 从 Stream 读取消息（使用 Consumer Group）
            var messages = await db.StreamReadGroupAsync(
                _streamKey,
                _consumerGroup,
                consumerId,
                ">",              // 只读取新消息
                count: 10);       // 批量读取

            foreach (var streamEntry in messages)
            {
                try
                {
                    // 解析消息
                    var payload = streamEntry.Values.FirstOrDefault(v => v.Name == "payload").Value;
                    var message = JsonSerializer.Deserialize<TMessage>(payload!);

                    // 调用处理器
                    await handler(message!, new TransportContext());

                    // ACK 消息（标记已处理，原生功能）
                    await db.StreamAcknowledgeAsync(_streamKey, _consumerGroup, streamEntry.Id);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to process message {MessageId}", streamEntry.Id);
                    // 不 ACK，消息会自动进入待处理列表（Pending List）
                }
            }

            // 无消息时等待
            if (messages.Length == 0)
                await Task.Delay(100, ct);
        }
    }
}
```

**优势**:
- ✅ 持久化（Redis 自动持久化）
- ✅ 消费组（自动负载均衡）
- ✅ ACK 机制（至少一次送达）
- ✅ Pending List（自动重试）
- ✅ 死信队列（可配置）
- ❌ 移除 Pub/Sub（降级）

#### 3.3 Redis Sorted Set（节点发现）

**问题**: 当前 RedisNodeDiscovery 使用内存 ConcurrentDictionary

**解决**: 使用 Redis Sorted Set + TTL

```csharp
// src/Catga.Distributed/Redis/RedisNodeDiscovery.cs

public sealed class RedisNodeDiscovery : INodeDiscovery
{
    private readonly IConnectionMultiplexer _redis;
    private readonly string _nodesKey = "catga:nodes";

    public async Task RegisterAsync(NodeInfo node, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();

        // 使用 Redis Sorted Set（原生，按时间戳排序）
        var score = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var json = JsonSerializer.Serialize(node);

        // 添加到 Sorted Set（自动去重）
        await db.SortedSetAddAsync(_nodesKey, json, score);

        // 设置 TTL（2 分钟）
        await db.KeyExpireAsync(_nodesKey, TimeSpan.FromMinutes(2));
    }

    public async Task<IReadOnlyList<NodeInfo>> GetNodesAsync(CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();

        // 从 Sorted Set 读取所有节点（原生，已排序）
        var entries = await db.SortedSetRangeByScoreAsync(_nodesKey);

        var nodes = new List<NodeInfo>();
        var now = DateTimeOffset.UtcNow;

        foreach (var entry in entries)
        {
            var node = JsonSerializer.Deserialize<NodeInfo>(entry!);
            if (node != null && (now - node.LastSeen).TotalSeconds < 30)
                nodes.Add(node);
        }

        return nodes;
    }

    public async Task HeartbeatAsync(string nodeId, double load, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();

        // 更新节点的 score（时间戳）
        var score = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // 查找并更新节点
        var entries = await db.SortedSetRangeByScoreAsync(_nodesKey);
        foreach (var entry in entries)
        {
            var node = JsonSerializer.Deserialize<NodeInfo>(entry!);
            if (node?.NodeId == nodeId)
            {
                // 更新节点信息
                var updated = node with { LastSeen = DateTime.UtcNow, Load = load };
                var json = JsonSerializer.Serialize(updated);

                // 删除旧条目，添加新条目（原子操作）
                var batch = db.CreateBatch();
                batch.SortedSetRemoveAsync(_nodesKey, entry);
                batch.SortedSetAddAsync(_nodesKey, json, score);
                batch.Execute();

                break;
            }
        }
    }
}
```

**优势**:
- ✅ 持久化（Redis 自动持久化）
- ✅ 自动排序（按时间戳）
- ✅ 自动去重（Sorted Set 特性）
- ✅ TTL 自动过期
- ❌ 移除内存 ConcurrentDictionary（降级）

---

### Phase 4: DI 配置更新（30分钟）

#### 4.1 更新 DI 扩展

```csharp
// src/Catga.Distributed/DependencyInjection/DistributedServiceCollectionExtensions.cs

public static IServiceCollection AddNatsCluster(
    this IServiceCollection services,
    string natsUrl,
    string nodeId,
    string endpoint,
    Action<NatsClusterOptions>? configure = null)
{
    var options = new NatsClusterOptions
    {
        UseJetStream = true,          // 默认使用 JetStream KV Store
        RoutingStrategy = RoutingStrategyType.RoundRobin,  // 默认路由策略
        HeartbeatInterval = TimeSpan.FromSeconds(10),
        NodeTimeout = TimeSpan.FromSeconds(30)
    };

    configure?.Invoke(options);

    // 注册路由策略
    services.AddSingleton<IRoutingStrategy>(sp =>
    {
        return options.RoutingStrategy switch
        {
            RoutingStrategyType.RoundRobin => new RoundRobinRoutingStrategy(),
            RoutingStrategyType.ConsistentHash => new ConsistentHashRoutingStrategy(),
            RoutingStrategyType.LoadBased => new LoadBasedRoutingStrategy(),
            RoutingStrategyType.Random => new RandomRoutingStrategy(),
            _ => new RoundRobinRoutingStrategy()
        };
    });

    // 注册节点发现（使用 JetStream KV Store）
    services.AddSingleton<INodeDiscovery, NatsNodeDiscovery>();

    // 注册分布式 Mediator
    services.AddSingleton<IDistributedMediator, DistributedMediator>();

    return services;
}

public static IServiceCollection AddRedisCluster(
    this IServiceCollection services,
    string redisConnectionString,
    string nodeId,
    string endpoint,
    Action<RedisClusterOptions>? configure = null)
{
    var options = new RedisClusterOptions
    {
        UseStreams = true,            // 默认使用 Redis Streams
        RoutingStrategy = RoutingStrategyType.ConsistentHash,  // 默认一致性哈希
        HeartbeatInterval = TimeSpan.FromSeconds(10)
    };

    configure?.Invoke(options);

    // 注册路由策略
    services.AddSingleton<IRoutingStrategy>(/* ... */);

    // 注册节点发现（使用 Sorted Set）
    services.AddSingleton<INodeDiscovery, RedisNodeDiscovery>();

    // 注册分布式 Mediator
    services.AddSingleton<IDistributedMediator, DistributedMediator>();

    return services;
}
```

---

## 📊 优化效果对比

### 清理前 vs 清理后

| 指标 | 清理前 | 清理后 | 改进 |
|------|--------|--------|------|
| 根目录文档 | 25+ | 8个 | **-68%** |
| docs/ 文档 | 30+ | 15个 | **-50%** |
| 代码行数 | ~15,000 | ~13,000 | **-13%** |
| 核心项目 | 10个 | 8个 | **-20%** |

### 内存实现 vs 原生功能

| 功能 | 内存实现 | 原生功能 | 优势 |
|------|---------|---------|------|
| 节点发现 | ConcurrentDictionary | NATS KV / Redis Sorted Set | **持久化** |
| 消息传输 | Channel | NATS JetStream / Redis Streams | **可靠性** |
| 负载均衡 | Round-Robin（内存） | Consumer Groups（原生） | **自动化** |

### 路由策略

| 策略 | 清理前 | 清理后 | 适用场景 |
|------|--------|--------|---------|
| Round-Robin | ✅ | ✅ | 通用 |
| Consistent Hash | ❌ | ✅ | 分片、会话保持 |
| Load-Based | ❌ | ✅ | 负载均衡 |
| Topic-Based | ❌ | ✅ | 发布/订阅 |
| Random | ❌ | ✅ | 简单场景 |

---

## 🎯 实施步骤

### Step 1: 清理文档（30分钟）

```bash
# 删除临时文档
rm CATGA_CORE_FOCUS.md
rm CATGA_SIMPLIFIED_PLAN.md
rm CODE_REVIEW_OPTIMIZATION_POINTS.md
rm FINAL_*.md
rm P0_OPTIMIZATION_COMPLETE.md
rm PHASE2_PROGRESS.md
rm QOS_IMPLEMENTATION_PLAN.md
rm SESSION_FINAL*.md
rm SIMPLIFICATION_FINAL.md

# 整理 docs/
rm docs/Architecture.md
rm docs/BestPractices.md
rm docs/Migration.md
rm docs/PerformanceTuning.md
```

### Step 2: 实现路由策略（2小时）

```bash
# 创建路由策略
mkdir src/Catga.Distributed/Routing
touch src/Catga.Distributed/Routing/IRoutingStrategy.cs
touch src/Catga.Distributed/Routing/RoundRobinRoutingStrategy.cs
touch src/Catga.Distributed/Routing/ConsistentHashRoutingStrategy.cs
touch src/Catga.Distributed/Routing/LoadBasedRoutingStrategy.cs
touch src/Catga.Distributed/Routing/TopicRoutingStrategy.cs
```

### Step 3: 重构为原生功能（3小时）

```bash
# 更新 NATS 节点发现
# 编辑: src/Catga.Distributed/Nats/NatsNodeDiscovery.cs
# - 移除 ConcurrentDictionary
# - 添加 INatsJSContext
# - 添加 INatsKVStore

# 更新 Redis 节点发现
# 编辑: src/Catga.Distributed/Redis/RedisNodeDiscovery.cs
# - 移除 ConcurrentDictionary
# - 使用 Sorted Set

# 创建 Redis Streams 传输
touch src/Catga.Distributed/Redis/RedisStreamTransport.cs
```

### Step 4: 测试和文档（1小时）

```bash
# 运行测试
dotnet test

# 更新文档
# - README.md
# - CATGA_V2_COMPLETE.md
# - examples/NatsClusterDemo/README.md
```

---

## ✅ 完成标准

1. ✅ 删除所有临时文档（~15个）
2. ✅ 清理 docs/ 文件夹（-50%）
3. ✅ 实现 5 种路由策略
4. ✅ NATS 使用 JetStream KV Store（移除内存）
5. ✅ Redis 使用 Streams + Sorted Set（移除内存）
6. ✅ 所有测试通过
7. ✅ 文档更新完整

---

## 🚀 预期成果

### 代码质量

- ✅ 文档数量 -50%
- ✅ 代码行数 -13%
- ✅ 核心项目 -20%
- ✅ 完全移除内存降级

### 功能完整性

- ✅ 5 种路由策略
- ✅ NATS JetStream 持久化
- ✅ Redis Streams 可靠消息传输
- ✅ 原生负载均衡

### 性能提升

- ✅ 持久化（容灾）
- ✅ 分布式一致性
- ✅ 自动负载均衡
- ✅ 至少一次送达保证

---

*计划创建时间: 2025-10-10*
*预计完成时间: 6-8 小时*
*Catga v2.1 - 清理优化版* 🚀

