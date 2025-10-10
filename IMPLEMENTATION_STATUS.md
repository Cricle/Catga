# Catga 简化实现进度

**日期**: 2025-10-10  
**目标**: 简单、AOT、高性能、分布式

---

## ✅ Phase 1: 核心清理（完成）

- [x] 删除 Catga.Cluster（过于复杂）
- [x] 删除所有 Cluster 相关文档
- [x] 修复编译错误
- [x] 核心库编译成功

**成果**:
- ✅ 删除 ~5000行复杂代码
- ✅ 8个核心库编译成功（53个警告，0个错误）
- ✅ 2个示例编译成功

---

## 🚧 Phase 2: 分布式传输（进行中）

### 目标
实现简单的分布式消息传输，支持 NATS 和 Redis

### 任务清单

#### 2.1 NATS 传输 ⭐
- [ ] 扩展 NatsMessageTransport
  - [ ] 添加节点发现功能
  - [ ] 添加自动订阅机制
  - [ ] 添加广播支持
- [ ] 创建 NatsClusterOptions
- [ ] 创建 DI 扩展方法

#### 2.2 Redis 传输 ⭐
- [ ] 创建 RedisMessageTransport
  - [ ] 基于 Redis Pub/Sub
  - [ ] 支持消息路由
  - [ ] 支持广播
- [ ] 创建 RedisClusterOptions
- [ ] 创建 DI 扩展方法

#### 2.3 节点自动发现
- [ ] NATS 节点发现（基于 KV Store）
- [ ] Redis 节点发现（基于 Redis Key）
- [ ] 心跳机制（30秒）
- [ ] 节点元数据

#### 2.4 自动故障转移
- [ ] 简单重试（3次）
- [ ] 超时检测（30秒）
- [ ] 节点切换

---

## 📦 核心接口设计

### IDistributedTransport

```csharp
public interface IDistributedTransport : IMessageTransport
{
    // 节点管理
    Task<IReadOnlyList<NodeInfo>> GetNodesAsync(CancellationToken ct = default);
    Task<NodeInfo> GetCurrentNodeAsync(CancellationToken ct = default);
    
    // 路由策略
    Task PublishToNodeAsync<T>(T message, string nodeId, CancellationToken ct = default)
        where T : IMessage;
    
    Task BroadcastAsync<T>(T message, CancellationToken ct = default)
        where T : IMessage;
}

public record NodeInfo(
    string NodeId,
    string Endpoint,
    DateTime LastSeen,
    Dictionary<string, string>? Metadata = null);
```

---

## 🎯 用户使用示例

### 方案1: NATS（推荐）

```csharp
// Program.cs
builder.Services.AddCatga();
builder.Services.AddGeneratedHandlers();
builder.Services.AddNatsCluster("nats://localhost:4222");

// ✅ 节点自动发现
// ✅ 消息自动路由
// ✅ 故障自动转移
```

### 方案2: Redis（备选）

```csharp
// Program.cs
builder.Services.AddCatga();
builder.Services.AddGeneratedHandlers();
builder.Services.AddRedisCluster("localhost:6379");

// ✅ 节点自动发现
// ✅ 消息自动路由
// ✅ 故障自动转移
```

---

## 🚀 下一步

**当前焦点**: Phase 2.1 - 实现 NATS 分布式传输

**实现策略**:
1. 扩展现有 `Catga.Transport.Nats`
2. 添加节点发现（NATS KV Store）
3. 添加自动订阅和路由
4. 添加简单示例

**预计时间**: 2-3小时

