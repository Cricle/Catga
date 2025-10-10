# Catga 概念简化和 DotNext 集成计划

## 🎯 问题分析

### 当前概念过多
**消息类型**: 6个概念
- IMessage（基础消息）
- IRequest（请求）
- ICommand（命令）
- IQuery（查询）
- IEvent（事件）
- MessageBase / EventBase

**接口**: 16个
- ICatgaMediator
- IMessageTransport
- IMessageSerializer
- IDistributedLock
- IDistributedCache
- IDistributedIdGenerator
- IEventStore
- IOutboxStore
- IInboxStore
- IIdempotencyStore
- IDeadLetterQueue
- IHealthCheck
- ISaga
- IServiceDiscovery
- IPipelineBehavior
- IBufferedMessageSerializer

**总计**: 22个核心概念 ❌ 太多了！

---

## 🎯 简化目标

### 1. 消息类型简化（6 → 2）
```csharp
// Before: 6个概念
IMessage, IRequest<T>, ICommand<T>, IQuery<T>, IEvent, MessageBase

// After: 2个概念
IRequest<TResponse>  // 请求-响应
IEvent               // 事件通知
```

**理由**:
- Command 和 Query 的区分在实际使用中意义不大
- 用户只需要知道"请求-响应"和"事件通知"两种模式
- MessageBase 的属性（MessageId, CreatedAt）可以自动生成，用户无需关心

### 2. 接口简化（16 → 8）
保留核心接口，删除不常用的：

**保留（8个）**:
- ✅ ICatgaMediator - 核心中介者
- ✅ IMessageTransport - 消息传输
- ✅ IDistributedLock - 分布式锁
- ✅ IDistributedCache - 分布式缓存
- ✅ IDistributedIdGenerator - 分布式ID
- ✅ IEventStore - 事件存储
- ✅ IPipelineBehavior - 管道行为
- ✅ IHealthCheck - 健康检查

**删除/合并（8个）**:
- ❌ IMessageSerializer → 内置 JSON，用户无需关心
- ❌ IOutboxStore → 合并到 IMessageTransport
- ❌ IInboxStore → 合并到 IMessageTransport
- ❌ IIdempotencyStore → 内置实现，用户无需关心
- ❌ IDeadLetterQueue → 内置实现，用户无需关心
- ❌ ISaga → 太复杂，删除
- ❌ IServiceDiscovery → 用 DotNext.Net.Cluster 替代
- ❌ IBufferedMessageSerializer → 内部实现细节

---

## 🚀 DotNext 集成计划

### 为什么选择 DotNext？
- ✅ 成熟的 Raft 共识算法实现
- ✅ 高性能、低延迟
- ✅ 完整的集群管理（Leader 选举、日志复制、成员管理）
- ✅ .NET 9 原生支持
- ✅ 零分配、AOT 友好

### 新增库：Catga.Cluster.DotNext

**功能**:
1. **自动集群管理** - 基于 DotNext.Net.Cluster.Consensus.Raft
2. **分布式状态机** - 使用 Raft 日志复制
3. **Leader 选举** - 自动故障转移
4. **成员发现** - 自动节点注册和发现

**使用示例**:
```csharp
// 配置 Catga + DotNext Cluster
builder.Services.AddCatga();
builder.Services.AddDotNextCluster(options =>
{
    options.ClusterName = "catga-cluster";
    options.Members = new[] 
    { 
        "http://node1:5000", 
        "http://node2:5000", 
        "http://node3:5000" 
    };
});

// 自动：
// - Leader 选举
// - 消息路由到 Leader
// - 日志复制到 Followers
// - 故障转移
```

---

## 📋 执行计划

### Phase 1: 简化消息类型（30分钟）
- [ ] 删除 ICommand, IQuery 接口
- [ ] 简化为 IRequest<TResponse> 和 IEvent
- [ ] 删除 MessageBase（自动生成 MessageId）
- [ ] 更新示例

### Phase 2: 简化接口（1小时）
- [ ] 删除 ISaga 及相关实现
- [ ] 删除 IServiceDiscovery（用 DotNext 替代）
- [ ] 合并 IOutboxStore/IInboxStore 到 IMessageTransport
- [ ] 内置 IMessageSerializer（默认 JSON）
- [ ] 内置 IIdempotencyStore, IDeadLetterQueue

### Phase 3: 集成 DotNext（2小时）
- [ ] 创建 Catga.Cluster.DotNext 项目
- [ ] 添加 DotNext.Net.Cluster 依赖
- [ ] 实现 RaftMessageTransport
- [ ] 实现自动 Leader 选举
- [ ] 实现消息路由（Command → Leader, Event → All）
- [ ] 创建集群示例

### Phase 4: 更新文档和示例（30分钟）
- [ ] 更新 README
- [ ] 更新示例
- [ ] 创建集群示例

---

## 📊 简化效果预测

### 概念数量
- Before: 22个概念
- After: 10个概念
- 减少: **55%**

### 用户学习曲线
- Before: 需要理解 Command/Query 区分、Saga、Outbox/Inbox
- After: 只需理解 Request/Event、Cluster
- 降低: **70%**

### 代码示例
```csharp
// ===== Before: 复杂 =====
public record CreateUserCommand(string Username, string Email) 
    : MessageBase, ICommand<UserResponse>;  // 需要继承 MessageBase

public class CreateUserHandler : IRequestHandler<CreateUserCommand, UserResponse>
{
    // 需要手动处理 MessageId, CreatedAt
}

// ===== After: 简单 =====
public record CreateUserCommand(string Username, string Email) 
    : IRequest<UserResponse>;  // 无需继承，MessageId 自动生成

public class CreateUserHandler : IRequestHandler<CreateUserCommand, UserResponse>
{
    // 自动处理 MessageId, CreatedAt, CorrelationId
}
```

---

## 🎉 最终效果

### Catga v3.0 特性
1. ✅ **极简概念** - 只有 10 个核心概念
2. ✅ **自动集群** - DotNext Raft 集群，零配置
3. ✅ **高性能** - 热路径零分配 + Raft 共识
4. ✅ **易用性** - 配置 2 行，使用 1 行
5. ✅ **生产就绪** - 成熟的 Raft 实现

### 使用体验
```csharp
// 1. 安装
dotnet add package Catga
dotnet add package Catga.Cluster.DotNext

// 2. 配置（3行）
builder.Services.AddCatga();
builder.Services.AddGeneratedHandlers();
builder.Services.AddDotNextCluster();  // 自动集群！

// 3. 使用（1行）
var result = await mediator.SendAsync<CreateUserCommand, UserResponse>(cmd);
```

---

**下一步**: 执行 Phase 1-4，预计 4 小时完成

