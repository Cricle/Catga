# Catga QoS 保证审查报告

## 📋 审查目标

验证 Catga 是否正确区分：
1. **最多一次 (At-Most-Once, QoS 0)** - CQRS 场景，可用内存辅助
2. **至少一次 (At-Least-Once, QoS 1)** - Catga 保证，必须确保持久化和重试

---

## ✅ 当前实现状态

### 1. **消息契约层** ✅

```csharp
// src/Catga/Messages/MessageContracts.cs

// ✅ 基础消息 - 默认 QoS 1 (至少一次)
public interface IMessage
{
    QualityOfService QoS => QualityOfService.AtLeastOnce; // 👍 正确默认
}

// ✅ Event - QoS 0 (最多一次) - 适合 CQRS
public interface IEvent : IMessage
{
    QualityOfService QoS => QualityOfService.AtMostOnce;  // 👍 CQRS 语义
}

// ✅ ReliableEvent - QoS 1 (至少一次) - Catga 保证
public interface IReliableEvent : IEvent
{
    new QualityOfService QoS => QualityOfService.AtLeastOnce; // 👍 Catga 保证
}
```

**评分**: ✅ **10/10** - 设计完美，清晰区分

---

### 2. **NATS Transport** ⚠️

```csharp
// src/Catga.Transport.Nats/NatsMessageTransport.cs

switch (qos)
{
    case QualityOfService.AtMostOnce:
        // QoS 0: Fire-and-forget (NATS Publish)
        await _connection.PublishAsync(subject, payload, headers: headers);
        break;

    case QualityOfService.AtLeastOnce:
        // QoS 1: Request/Reply (wait for ACK)
        await _connection.PublishAsync(subject, payload, replyTo: replySubject);
        // ⚠️ 问题：只发送了 ReplyTo，但没有等待 ACK！
        break;

    case QualityOfService.ExactlyOnce:
        // QoS 2: Request/Reply + Deduplication
        await _connection.PublishAsync(subject, payload, replyTo: replySubject2);
        _processedMessages.TryAdd(context.MessageId, true);
        // ⚠️ 问题：同样没有等待 ACK！
        break;
}
```

**问题**:
1. ❌ **QoS 1 没有真正等待 ACK** - 只设置了 `replyTo`，但没有 `await RequestAsync` 或监听回复
2. ❌ **QoS 1 没有重试机制** - 如果消息丢失，不会重试
3. ❌ **QoS 1 没有持久化** - 只使用内存 `PublishAsync`，没有使用 JetStream 持久化
4. ⚠️ **QoS 2 去重逻辑不完整** - 只在发送端去重，接收端没有去重

**评分**: ⚠️ **4/10** - QoS 0 正确，但 QoS 1/2 没有真正实现

---

### 3. **Redis Streams Transport** ✅✅✅

```csharp
// src/Catga.Distributed/Redis/RedisStreamTransport.cs

public async Task SubscribeAsync<TMessage>(...)
{
    // ✅ 使用 Consumer Groups（原生负载均衡）
    var messages = await db.StreamReadGroupAsync(
        _streamKey, _consumerGroup, _consumerId,
        ">",        // 只读取新消息
        count: 10); // 批量读取

    foreach (var streamEntry in messages)
    {
        await ProcessMessageAsync(db, streamEntry, handler, cancellationToken);
    }
}

private async Task ProcessMessageAsync<TMessage>(...)
{
    // 调用处理器
    await handler(message, context);

    // ✅ ACK 消息（标记已处理）
    await db.StreamAcknowledgeAsync(_streamKey, _consumerGroup, streamEntry.Id);

    // ✅ 如果处理失败，不 ACK，消息会进入 Pending List
    // ✅ Redis Streams 原生支持重试和持久化
}
```

**优点**:
1. ✅ **真正的 ACK 机制** - 使用 `StreamAcknowledgeAsync`
2. ✅ **自动重试** - 失败的消息自动进入 Pending List
3. ✅ **持久化** - Redis Streams 原生持久化
4. ✅ **Consumer Groups** - 原生负载均衡和 at-least-once 保证
5. ✅ **死信队列支持** - 可以查询 Pending List

**评分**: ✅ **10/10** - 完美实现 QoS 1

---

### 4. **JetStream KV Store** ⚠️ (占位符)

```csharp
// src/Catga.Distributed/Nats/NatsJetStreamKVNodeDiscovery.cs

private object? _kvStore; // ⚠️ 占位符，未实现

public async Task RegisterAsync(NodeInfo node, ...)
{
    // 无锁更新本地缓存
    _nodes.AddOrUpdate(node.NodeId, node, (_, _) => node);

    // ⚠️ 未持久化到 KV Store
    // TODO: await _kvStore.PutAsync(key, json, cancellationToken: cancellationToken);
}
```

**问题**:
1. ❌ **只使用内存** - `ConcurrentDictionary`，没有持久化
2. ❌ **节点重启数据丢失** - 不符合"至少一次"要求
3. ⚠️ **KV Store API 未实现** - 使用 `object?` 占位符

**评分**: ⚠️ **3/10** - 当前是"最多一次"，需要实现持久化

---

## 🔴 关键问题

### **问题 1: NATS Transport QoS 1 实现不正确** ⚠️

**当前问题**:
```csharp
// 当前代码（错误）
case QualityOfService.AtLeastOnce:
    await _connection.PublishAsync(subject, payload, replyTo: replySubject);
    // ❌ 没有等待 ACK，无法保证"至少一次"
```

**正确实现（需要修复）**:

#### 方案 1: 使用 NATS Request/Reply (轻量级)
```csharp
case QualityOfService.AtLeastOnce:
    // ✅ 使用 RequestAsync 等待 ACK
    try
    {
        var reply = await _connection.RequestAsync<string, byte[]>(
            subject,
            payload,
            headers: headers,
            requestOpts: new NatsRequestOpts
            {
                Timeout = TimeSpan.FromSeconds(5)
            },
            cancellationToken: cancellationToken);

        if (reply.Data == null || !IsAckMessage(reply.Data))
        {
            throw new Exception("No ACK received");
        }

        _logger.LogDebug("Message {MessageId} ACKed (QoS 1)", context.MessageId);
    }
    catch (Exception ex)
    {
        // ✅ 可以在这里实现重试逻辑
        _logger.LogError(ex, "Failed to send QoS 1 message, retrying...");
        // TODO: 重试 3 次
    }
    break;
```

#### 方案 2: 使用 NATS JetStream (推荐 - 持久化)
```csharp
case QualityOfService.AtLeastOnce:
    // ✅ 使用 JetStream Publish（持久化 + ACK）
    var jsContext = new NatsJSContext(_connection);
    var stream = await jsContext.GetStreamAsync("catga-messages");

    var ack = await stream.PublishAsync(
        subject,
        payload,
        opts: new NatsPubOpts
        {
            MsgId = context.MessageId, // 去重
            ExpectLastMsgId = null      // 无序列要求
        },
        headers: headers,
        cancellationToken: cancellationToken);

    if (ack.Duplicate)
    {
        _logger.LogDebug("Message {MessageId} is duplicate, skipped", context.MessageId);
    }
    else
    {
        _logger.LogDebug("Message {MessageId} persisted to JetStream (QoS 1)", context.MessageId);
    }
    break;
```

---

### **问题 2: JetStream KV Store 未实现持久化** ❌

**当前问题**:
```csharp
// 当前：只使用内存
private readonly ConcurrentDictionary<string, NodeInfo> _nodes = new();
```

**需要实现** (推荐方案):

```csharp
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

public sealed class NatsJetStreamKVNodeDiscovery : INodeDiscovery
{
    private INatsJSContext? _jsContext;
    private INatsKV<string>? _kvStore;  // ✅ 正确类型

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        // 创建 JetStream Context
        _jsContext = new NatsJSContext(_connection);

        // ✅ 创建 KV Store（持久化）
        var config = new NatsKVConfig(_bucketName)
        {
            History = 10,                          // 保留 10 个历史版本
            Ttl = _nodeTtl,                        // 自动过期
            MaxBytes = 1024 * 1024 * 10,           // 最大 10MB
            Storage = StreamConfigStorage.File,    // ✅ 持久化到文件
        };

        _kvStore = await _jsContext.CreateKeyValueAsync<string>(config, cancellationToken);

        _logger.LogInformation("JetStream KV Store '{Bucket}' initialized with persistence", _bucketName);

        // ✅ 启动监听器
        _ = WatchNodesAsync(cancellationToken);

        // ✅ 加载现有节点
        await LoadExistingNodesAsync(cancellationToken);
    }

    public async Task RegisterAsync(NodeInfo node, CancellationToken cancellationToken = default)
    {
        // ✅ 更新本地缓存
        _nodes.AddOrUpdate(node.NodeId, node, (_, _) => node);

        // ✅ 持久化到 KV Store
        var key = GetNodeKey(node.NodeId);
        var json = JsonSerializer.Serialize(node);

        await _kvStore!.PutAsync(key, json, cancellationToken: cancellationToken);

        _logger.LogDebug("Node {NodeId} registered and persisted", node.NodeId);
    }

    private async Task LoadExistingNodesAsync(CancellationToken cancellationToken)
    {
        // ✅ 从 KV Store 加载现有节点
        await foreach (var key in _kvStore!.GetKeysAsync(cancellationToken: cancellationToken))
        {
            try
            {
                var entry = await _kvStore.GetEntryAsync(key, cancellationToken: cancellationToken);
                if (entry?.Value != null)
                {
                    var node = JsonSerializer.Deserialize<NodeInfo>(entry.Value);
                    if (node != null)
                    {
                        _nodes.TryAdd(node.NodeId, node);
                        _logger.LogDebug("Loaded node {NodeId} from KV Store", node.NodeId);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load node with key {Key}", key);
            }
        }
    }

    private async Task WatchNodesAsync(CancellationToken cancellationToken)
    {
        // ✅ 监听 KV Store 变更
        await foreach (var entry in _kvStore!.WatchAsync<string>(cancellationToken: cancellationToken))
        {
            try
            {
                if (entry.Operation == NatsKVWatchOp.Put && entry.Value != null)
                {
                    var node = JsonSerializer.Deserialize<NodeInfo>(entry.Value);
                    if (node != null)
                    {
                        _nodes.AddOrUpdate(node.NodeId, node, (_, _) => node);

                        await _events.Writer.WriteAsync(new NodeChangeEvent
                        {
                            Type = NodeChangeType.Joined,
                            Node = node
                        }, cancellationToken);
                    }
                }
                else if (entry.Operation == NatsKVWatchOp.Delete)
                {
                    var nodeId = GetNodeIdFromKey(entry.Key);
                    if (_nodes.TryRemove(nodeId, out var node))
                    {
                        await _events.Writer.WriteAsync(new NodeChangeEvent
                        {
                            Type = NodeChangeType.Left,
                            Node = node
                        }, cancellationToken);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to process KV Watch event");
            }
        }
    }
}
```

---

## 📊 QoS 保证对比表

| 传输层 | QoS 0 (最多一次) | QoS 1 (至少一次) | 持久化 | 重试 | ACK |
|--------|------------------|------------------|--------|------|-----|
| **NATS Pub/Sub** | ✅ 正确 | ❌ **不正确** | ❌ 无 | ❌ 无 | ❌ 无 |
| **NATS JetStream** | ✅ 正确 | ⚠️ **需实现** | ✅ 有 | ✅ 有 | ✅ 有 |
| **Redis Streams** | ✅ 正确 | ✅ **完美** | ✅ 有 | ✅ 有 | ✅ 有 |
| **InMemory** | ✅ 正确 | ⚠️ 部分 | ❌ 无 | ⚠️ 部分 | ❌ 无 |

---

## 🎯 改进建议

### **高优先级（必须修复）**

1. **修复 NATS Transport QoS 1** ⭐⭐⭐⭐⭐
   - 方案 1: 使用 `RequestAsync` 等待 ACK
   - 方案 2: 使用 JetStream Publish（推荐）
   - 添加重试机制（3 次重试）

2. **实现 JetStream KV Store 持久化** ⭐⭐⭐⭐⭐
   - 使用 `INatsKV<string>` 替换 `object?`
   - 实现 `PutAsync`、`GetAsync`、`WatchAsync`
   - 节点注册时持久化到 KV Store

3. **添加 QoS 验证测试** ⭐⭐⭐⭐
   - 测试 QoS 0: 允许丢失
   - 测试 QoS 1: 保证至少一次
   - 测试 QoS 2: 保证恰好一次

### **中优先级（建议实现）**

4. **统一 QoS 处理逻辑** ⭐⭐⭐
   - 抽取公共 QoS 处理接口
   - 确保所有 Transport 统一行为

5. **添加 Metrics** ⭐⭐⭐
   - QoS 0 丢失率
   - QoS 1 重试次数
   - QoS 2 去重命中率

6. **文档更新** ⭐⭐⭐
   - 明确说明各 Transport 的 QoS 支持
   - 添加使用建议

### **低优先级（可选）**

7. **性能优化** ⭐⭐
   - QoS 1 批量 ACK
   - QoS 2 去重缓存过期

---

## ✅ 最终评分

| 组件 | QoS 0 支持 | QoS 1 支持 | QoS 2 支持 | 总评 |
|------|-----------|-----------|-----------|------|
| **消息契约** | ✅ 10/10 | ✅ 10/10 | ✅ 10/10 | ✅ **10/10** |
| **NATS Pub/Sub** | ✅ 10/10 | ❌ 2/10 | ❌ 2/10 | ⚠️ **4.7/10** |
| **Redis Streams** | ✅ 10/10 | ✅ 10/10 | ⚠️ 7/10 | ✅ **9/10** |
| **JetStream KV** | ✅ 8/10 | ❌ 3/10 | ❌ 3/10 | ⚠️ **4.7/10** |
| **InMemory** | ✅ 10/10 | ⚠️ 5/10 | ⚠️ 5/10 | ⚠️ **6.7/10** |

**整体评分**: ⚠️ **7.0/10**

---

## 🚀 行动计划

### 第1步: 修复 NATS Transport QoS 1（关键）
```bash
1. 修改 NatsMessageTransport.cs
2. 实现 RequestAsync 或 JetStream Publish
3. 添加重试逻辑
4. 添加单元测试
```

### 第2步: 实现 JetStream KV Store 持久化（关键）
```bash
1. 修改 NatsJetStreamKVNodeDiscovery.cs
2. 实现 PutAsync、GetAsync、WatchAsync
3. 添加加载和监听逻辑
4. 添加集成测试
```

### 第3步: 验证和测试
```bash
1. 编写 QoS 验证测试
2. 运行端到端测试
3. 性能测试
4. 更新文档
```

---

## 📝 总结

### ✅ 做得好的地方
1. ✅ **消息契约设计完美** - 清晰区分 QoS 0/1/2
2. ✅ **Redis Streams 实现完美** - 真正的 at-least-once
3. ✅ **Lock-Free 设计** - 高性能

### ⚠️ 需要改进的地方
1. ❌ **NATS QoS 1 没有真正实现** - 只设置 ReplyTo，没有等待 ACK
2. ❌ **JetStream KV Store 未持久化** - 只使用内存，不符合 at-least-once
3. ⚠️ **缺少 QoS 验证测试** - 无法验证正确性

### 🎯 核心建议

**对于"至少一次"(QoS 1) 保证，必须满足**:
1. ✅ 持久化 - 数据不能只在内存
2. ✅ ACK 机制 - 确认消息已送达
3. ✅ 重试机制 - 失败时自动重试
4. ✅ 幂等性 - 支持重复处理

**当前只有 Redis Streams 真正满足这些要求！**

---

**优先级**: ⭐⭐⭐⭐⭐ **高优先级，建议立即修复**

