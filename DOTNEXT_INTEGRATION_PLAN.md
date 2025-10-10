# DotNext 与 Catga 完美集成方案

## 🎯 目标

将 DotNext Raft 集群深度集成到 Catga，而不仅仅是简单封装：
- ✅ **自动路由** - Command 自动路由到 Leader，Query 本地处理
- ✅ **状态同步** - 使用 Raft 日志同步关键状态
- ✅ **透明集群** - 用户无需关心集群细节
- ✅ **故障转移** - Leader 故障时自动重新选举和恢复

---

## 📋 集成架构

### 当前问题
```csharp
// ❌ 当前：简单封装，用户需要手动处理集群逻辑
builder.Services.AddDotNextCluster(options => { /* ... */ });

// 用户需要自己判断 Leader、转发请求等
```

### 目标架构
```csharp
// ✅ 目标：透明集成，自动处理一切
builder.Services.AddCatga();
builder.Services.AddGeneratedHandlers();
builder.Services.AddRaftCluster(options =>
{
    options.Members = new[] { "http://node1:5001", "http://node2:5002" };
});

// Catga 自动：
// 1. Command → 路由到 Leader
// 2. Query → 本地执行
// 3. Event → 广播到所有节点
// 4. 状态同步 → 通过 Raft 日志
```

---

## 🏗️ 核心组件

### 1. RaftMessageTransport
**作用**: 基于 Raft 的消息传输层

```csharp
public class RaftMessageTransport : IMessageTransport
{
    private readonly IRaftCluster _cluster;
    private readonly ILogger<RaftMessageTransport> _logger;

    // Command: 转发到 Leader
    public async Task<TResponse> SendAsync<TRequest, TResponse>(
        TRequest request, 
        CancellationToken ct)
        where TRequest : IRequest<TResponse>
    {
        // 1. 检查是否为 Leader
        if (_cluster.Leader?.Equals(_cluster.LocalMember) == true)
        {
            // 本地处理
            return await HandleLocallyAsync<TRequest, TResponse>(request, ct);
        }
        
        // 2. 转发到 Leader
        return await ForwardToLeaderAsync<TRequest, TResponse>(request, ct);
    }

    // Query: 本地处理（可选转发）
    // Event: 广播到所有节点
}
```

### 2. RaftStateMachine
**作用**: Raft 状态机，用于状态同步

```csharp
public class CatgaStateMachine : PersistentState
{
    // 将关键操作写入 Raft 日志
    public async ValueTask<TResponse> ApplyCommandAsync<TRequest, TResponse>(
        TRequest command,
        CancellationToken ct)
    {
        // 1. 序列化 Command
        var logEntry = SerializeCommand(command);
        
        // 2. 写入 Raft 日志（自动复制到 Followers）
        await AppendAsync(logEntry, ct);
        
        // 3. 等待提交（多数节点确认）
        await CommitAsync(ct);
        
        // 4. 应用到本地状态
        return await ApplyToLocalState<TResponse>(logEntry, ct);
    }
}
```

### 3. RaftAwareMediator
**作用**: Raft 感知的 Mediator

```csharp
public class RaftAwareMediator : ICatgaMediator
{
    private readonly IRaftCluster _cluster;
    private readonly RaftStateMachine _stateMachine;
    private readonly ICatgaMediator _localMediator;

    public async ValueTask<CatgaResult<TResponse>> SendAsync<TRequest, TResponse>(
        TRequest request,
        CancellationToken ct = default)
        where TRequest : IRequest<TResponse>
    {
        // 智能路由
        if (IsCommand(request))
        {
            // Command: 通过 Raft 处理（写操作）
            return await _stateMachine.ApplyCommandAsync<TRequest, TResponse>(request, ct);
        }
        else
        {
            // Query: 本地处理（读操作）
            return await _localMediator.SendAsync<TRequest, TResponse>(request, ct);
        }
    }

    public async Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default)
        where TEvent : IEvent
    {
        // Event: 广播到所有节点
        await BroadcastToAllNodesAsync(@event, ct);
    }
}
```

### 4. RaftHealthCheck
**作用**: 集群健康检查

```csharp
public class RaftHealthCheck : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct)
    {
        var data = new Dictionary<string, object>
        {
            ["role"] = _cluster.Leader?.Equals(_cluster.LocalMember) == true ? "Leader" : "Follower",
            ["term"] = _cluster.Term,
            ["members"] = _cluster.Members.Count(),
            ["consensus"] = _cluster.Readiness == ClusterMemberStatus.Available
        };

        return HealthCheckResult.Healthy("Raft cluster is operational", data);
    }
}
```

---

## 📐 消息路由策略

### Command（写操作）
```
┌─────────┐     Command      ┌─────────┐
│ Client  │ ─────────────→   │  Node1  │ (Follower)
└─────────┘                  └─────────┘
                                   │
                            Forward │
                                   ↓
                             ┌─────────┐
                             │  Node2  │ (Leader)
                             └─────────┘
                                   │
                            Apply & │ Replicate
                            Commit  │
                                   ↓
                             ┌─────────┐
                             │  Raft   │
                             │  Log    │
                             └─────────┘
```

### Query（读操作）
```
┌─────────┐     Query        ┌─────────┐
│ Client  │ ─────────────→   │  Node1  │
└─────────┘                  └─────────┘
                                   │
                            Local  │ Read
                                   ↓
                             ┌─────────┐
                             │  Local  │
                             │  State  │
                             └─────────┘
```

### Event（事件广播）
```
┌─────────┐                  ┌─────────┐
│  Node1  │ ───────────────→ │  Node2  │
└─────────┘ \                └─────────┘
             \
              \              ┌─────────┐
               ───────────→  │  Node3  │
                             └─────────┘
```

---

## 🔧 实现清单

### 核心组件（5个）
- [ ] RaftMessageTransport - Raft 消息传输
- [ ] RaftStateMachine - Raft 状态机
- [ ] RaftAwareMediator - Raft 感知的 Mediator
- [ ] RaftHealthCheck - 集群健康检查
- [ ] RaftClusterExtensions - 扩展方法

### 辅助功能（3个）
- [ ] RaftCommandLog - 命令日志序列化
- [ ] RaftLeaderElection - Leader 选举监听
- [ ] RaftFailover - 故障转移处理

### 配置和文档（3个）
- [ ] RaftClusterOptions - 配置选项
- [ ] 使用示例
- [ ] 完整文档

---

## 💡 使用体验

### 配置（自动集成）
```csharp
var builder = WebApplication.CreateBuilder(args);

// 1. 添加 Catga
builder.Services.AddCatga();
builder.Services.AddGeneratedHandlers();

// 2. 添加 Raft 集群（深度集成）
builder.Services.AddRaftCluster(options =>
{
    options.Members = new[]
    {
        new Uri("http://node1:5001"),
        new Uri("http://node2:5002"),
        new Uri("http://node3:5003")
    };
    
    // 可选：高级配置
    options.ElectionTimeout = TimeSpan.FromMilliseconds(150);
    options.HeartbeatInterval = TimeSpan.FromMilliseconds(50);
});

var app = builder.Build();
app.MapRaft(); // Raft HTTP 端点
app.Run();
```

### 使用（完全透明）
```csharp
// 定义 Command
public record CreateOrderCommand(string ProductId, int Quantity) 
    : IRequest<OrderResponse>;

// 实现 Handler（无需关心集群）
public class CreateOrderHandler : IRequestHandler<CreateOrderCommand, OrderResponse>
{
    public async Task<CatgaResult<OrderResponse>> HandleAsync(
        CreateOrderCommand cmd,
        CancellationToken ct = default)
    {
        // Catga 自动：
        // 1. 在 Follower 上接收请求
        // 2. 转发到 Leader
        // 3. Leader 通过 Raft 日志复制
        // 4. 多数节点确认后提交
        // 5. 返回结果
        
        var orderId = Guid.NewGuid().ToString();
        return CatgaResult<OrderResponse>.Success(
            new OrderResponse(orderId, "Created")
        );
    }
}

// API 调用（用户无需关心集群）
app.MapPost("/orders", async (ICatgaMediator mediator, CreateOrderCommand cmd) =>
{
    // Catga 自动处理集群路由
    var result = await mediator.SendAsync<CreateOrderCommand, OrderResponse>(cmd);
    return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
});
```

---

## 🎯 关键特性

### 1. 自动路由
- ✅ Command 自动转发到 Leader
- ✅ Query 本地执行（可配置）
- ✅ Event 自动广播
- ✅ 用户无需感知

### 2. 状态同步
- ✅ 关键操作写入 Raft 日志
- ✅ 自动复制到所有节点
- ✅ 多数节点确认后提交
- ✅ 强一致性保证

### 3. 故障转移
- ✅ Leader 故障自动重新选举
- ✅ 请求自动重试到新 Leader
- ✅ 无数据丢失
- ✅ 对用户透明

### 4. 性能优化
- ✅ Query 本地读取（无网络开销）
- ✅ Command 批量提交（减少往返）
- ✅ 管道化处理（提高吞吐量）
- ✅ 零拷贝传输

---

## 📊 预期效果

### 用户体验
- **配置复杂度**: 从 10 行 → 3 行
- **代码改动**: 0 行（完全透明）
- **学习成本**: 极低（无需理解 Raft）

### 性能指标
- **写延迟**: ~2-3ms（本地 Leader）
- **读延迟**: ~0.5ms（本地查询）
- **吞吐量**: 10,000+ ops/s
- **可用性**: 99.99%（3 节点集群）

### 一致性保证
- **写入**: 强一致性（Raft 保证）
- **读取**: 可选（强一致性 or 最终一致性）
- **事件**: 至少一次交付

---

## 🚀 实现步骤

### Phase 1: 核心集成（2-3天）
1. 实现 RaftMessageTransport
2. 实现 RaftStateMachine
3. 实现 RaftAwareMediator
4. 基本的路由逻辑

### Phase 2: 高级功能（2-3天）
1. 健康检查和监控
2. 故障转移处理
3. 性能优化（批量、管道）
4. 配置验证

### Phase 3: 测试和文档（1-2天）
1. 单元测试
2. 集成测试
3. 使用文档
4. 性能测试

**总计**: 5-8 天完整实现

---

## 📚 参考资料

- [DotNext 文档](https://dotnet.github.io/dotNext/)
- [Raft 论文](https://raft.github.io/)
- [Raft 可视化](http://thesecretlivesofdata.com/raft/)

---

**下一步**: 立即开始实现 Phase 1 核心集成？

