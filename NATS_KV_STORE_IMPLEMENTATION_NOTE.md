# NATS JetStream KV Store 实现说明

**日期**: 2025-10-11  
**状态**: 占位符实现（内存 + TTL）  
**原因**: NATS.Client.JetStream 2.5.2 API 版本与文档不完全匹配

---

## 📋 当前实现

`NatsJetStreamKVNodeDiscovery` 当前使用**内存 + TTL 清理**的占位符实现：

- ✅ 使用 `ConcurrentDictionary` 存储节点信息
- ✅ 后台任务定期清理过期节点
- ✅ Channel 提供无锁事件通知
- ⚠️ 节点重启后需要重新注册（无持久化）

### 为什么不使用原生 KV Store？

尝试实现原生 KV Store 时遇到以下问题：

1. **类型不匹配**: `INatsKVStore` 类型在 NATS.Client.JetStream 2.5.2 中未找到
2. **API 差异**: 文档中的 API（`CreateKeyValueAsync`, `GetKeysAsync` 等）无法编译
3. **版本对齐**: 需要进一步调研正确的 API 版本

---

## 📖 官方文档参考

根据 [NATS 官方文档](https://docs.nats.io/using-nats/developer/develop_jetstream/kv#tab-c-12)，C# 客户端应该使用以下 API：

### 创建 KV Store

```csharp
// dotnet add package NATS.Net

// Create a new KV store or get an existing one
INatsKVStore store = await js.CreateKeyValueAsync(new NatsKVConfig("my-kv")
{
    History = 5,
    MaxAge = TimeSpan.FromMinutes(5),
    Storage = StreamConfigStorage.File
});
```

### Put/Get/Delete 操作

```csharp
// Put a value
await store.PutAsync("key", "value");

// Get a value
NatsKVEntry<string> entry = await store.GetEntryAsync("key");

// Delete a value
await store.DeleteAsync("key");

// Purge a value (delete all history)
await store.PurgeAsync("key");
```

### 获取所有键

```csharp
// Get all keys
IAsyncEnumerable<string> keys = store.GetKeysAsync();

await foreach (var key in keys)
{
    Console.WriteLine(key);
}
```

### Watch 变更

```csharp
// Watch for changes on all keys
IAsyncEnumerable<NatsKVEntry<T>> watcher = store.WatchAsync();

await foreach (var entry in watcher)
{
    if (entry.Value == null)
        Console.WriteLine($"Key {entry.Key} deleted");
    else
        Console.WriteLine($"Key {entry.Key} = {entry.Value}");
}
```

---

## 🎯 计划的原生实现

### 目标架构

```csharp
public sealed class NatsJetStreamKVNodeDiscovery : INodeDiscovery
{
    private INatsJSContext? _jsContext;
    private INatsKVStore? _kvStore;  // 原生 KV Store
    
    // 本地缓存（减少 KV Store 查询）
    private readonly ConcurrentDictionary<string, NodeInfo> _localCache = new();
    
    // 事件流
    private readonly Channel<NodeChangeEvent> _events;
    
    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        _jsContext = new NatsJSContext(_connection);
        
        // 配置 KV Store（原生持久化 + TTL）
        var kvConfig = new NatsKVConfig(_bucketName)
        {
            History = 5,
            MaxAge = _nodeTtl,  // 原生 TTL
            Storage = StreamConfigStorage.File  // 持久化
        };
        
        // 创建或获取 KV Store
        _kvStore = await _jsContext.CreateKeyValueAsync(kvConfig);
        
        // 加载现有节点
        await LoadExistingNodesAsync();
        
        // 启动 Watch
        _ = WatchNodesAsync();
    }
    
    public async Task RegisterAsync(NodeInfo node)
    {
        var json = JsonSerializer.Serialize(node);
        
        // 使用原生 Put（自动持久化 + TTL）
        await _kvStore!.PutAsync(GetNodeKey(node.NodeId), json);
        
        // 更新本地缓存
        _localCache.AddOrUpdate(node.NodeId, node, (_, _) => node);
    }
    
    public async Task HeartbeatAsync(string nodeId, double load)
    {
        if (!_localCache.TryGetValue(nodeId, out var node))
            return;
            
        node = node with { LastSeen = DateTime.UtcNow, Load = load };
        var json = JsonSerializer.Serialize(node);
        
        // 使用原生 Put（自动刷新 TTL）
        await _kvStore!.PutAsync(GetNodeKey(nodeId), json);
        
        _localCache.AddOrUpdate(nodeId, node, (_, _) => node);
    }
    
    private async Task LoadExistingNodesAsync()
    {
        // 使用原生 GetKeysAsync
        await foreach (var key in _kvStore!.GetKeysAsync())
        {
            var entry = await _kvStore.GetEntryAsync<string>(key);
            if (entry?.Value != null)
            {
                var node = JsonSerializer.Deserialize<NodeInfo>(entry.Value);
                if (node != null)
                    _localCache.AddOrUpdate(node.NodeId, node, (_, _) => node);
            }
        }
    }
    
    private async Task WatchNodesAsync()
    {
        // 使用原生 WatchAsync（监听所有变更）
        await foreach (var entry in _kvStore!.WatchAsync<string>())
        {
            if (entry.Value == null)
            {
                // 删除事件
                var nodeId = GetNodeIdFromKey(entry.Key);
                if (_localCache.TryRemove(nodeId, out var node))
                {
                    await _events.Writer.WriteAsync(new NodeChangeEvent
                    {
                        Type = NodeChangeType.Left,
                        Node = node
                    });
                }
            }
            else
            {
                // 添加/更新事件
                var node = JsonSerializer.Deserialize<NodeInfo>(entry.Value);
                if (node != null)
                {
                    var isNew = !_localCache.ContainsKey(node.NodeId);
                    _localCache.AddOrUpdate(node.NodeId, node, (_, _) => node);
                    
                    await _events.Writer.WriteAsync(new NodeChangeEvent
                    {
                        Type = isNew ? NodeChangeType.Joined : NodeChangeType.Updated,
                        Node = node
                    });
                }
            }
        }
    }
}
```

---

## ✅ 优势

使用原生 KV Store 的优势：

1. **持久化**: 节点信息持久化到磁盘，节点重启后可恢复
2. **原生 TTL**: NATS 自动删除过期节点，无需手动清理
3. **Watch 机制**: 实时监听节点变更，响应更快
4. **历史记录**: 支持查看节点状态历史（History 配置）
5. **无锁设计**: KV Store 本身线程安全，配合 ConcurrentDictionary 实现完全无锁

---

## 🔧 待完成的工作

### 高优先级

1. **API 版本对齐**: 确认 NATS.Client.JetStream 包的正确 API
   - 测试不同版本的包
   - 查看官方 GitHub 示例代码
   - 联系 NATS 社区获取帮助

2. **类型适配**: 找到正确的 KV Store 类型
   - 可能是 `INatsKVStore`
   - 可能是 `NatsKVStore<T>`
   - 需要查看包的实际类型定义

3. **完整实现**: 基于正确的 API 实现所有方法
   - `RegisterAsync` -> `PutAsync`
   - `HeartbeatAsync` -> `PutAsync`（刷新 TTL）
   - `UnregisterAsync` -> `DeleteAsync`
   - `LoadExistingNodesAsync` -> `GetKeysAsync` + `GetEntryAsync`
   - `WatchNodesAsync` -> `WatchAsync`

### 中优先级

4. **测试**: 创建集成测试验证 KV Store 功能
   - 节点注册和发现
   - TTL 过期测试
   - Watch 机制测试
   - 持久化恢复测试

5. **性能优化**: 优化本地缓存策略
   - 减少不必要的 KV Store 查询
   - 批量操作优化

---

## 📝 当前代码状态

`NatsJetStreamKVNodeDiscovery.cs` 文件包含：

- ✅ 详细的类文档说明当前状态和计划
- ✅ 内存实现（功能完整）
- ✅ TODO 注释标记需要原生 API 的位置
- ✅ 官方文档链接
- ✅ 编译通过，测试通过（95/95）

### 文档注释

```csharp
/// <summary>
/// 基于 NATS JetStream KV Store 的持久化节点发现
/// 当前实现：内存 + TTL 清理（占位符）
/// 
/// TODO: 适配 NATS.Client.JetStream 的原生 KV Store API
/// 参考：https://docs.nats.io/using-nats/developer/develop_jetstream/kv#tab-c-12
/// 
/// 原生 API 用法（待适配）：
/// - await jsContext.CreateKeyValueAsync(kvConfig)  // 创建 KV Store
/// - await kvStore.PutAsync(key, value)             // 存储键值
/// - await kvStore.GetEntryAsync(key)               // 获取键值
/// - await kvStore.DeleteAsync(key)                 // 删除键
/// - await kvStore.GetKeysAsync()                   // 获取所有键
/// - await kvStore.WatchAsync()                     // 监听变更
/// 
/// 注意：NATS.Client.JetStream 2.5.2 的类型定义可能与文档不完全匹配
/// 需要根据实际包版本调整 API 调用
/// </summary>
```

---

## 🚀 下一步

1. 升级或降级 NATS.Client.JetStream 包，找到正确的 API 版本
2. 创建测试项目验证 API 可用性
3. 实现原生 KV Store 功能
4. 更新文档和示例
5. 创建迁移指南（从内存版本到原生版本）

---

## 📚 参考资料

- [NATS Key/Value Store 官方文档](https://docs.nats.io/using-nats/developer/develop_jetstream/kv)
- [NATS.Net GitHub 仓库](https://github.com/nats-io/nats.net.v2)
- [JetStream 概念](https://docs.nats.io/nats-concepts/jetstream)
- [NATS CLI 工具](https://docs.nats.io/using-nats/nats-tools/nats)

---

**状态**: ⏳ 等待 API 版本对齐  
**影响**: 当前内存实现功能完整，可用于开发和测试  
**优先级**: 中（不影响核心功能，但影响生产环境持久化能力）

