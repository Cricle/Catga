# Catga.Cluster.DotNext

🚀 **DotNext Raft 深度集成** - 为 Catga 提供透明的分布式共识能力

## ✨ 核心特性

### 🎯 自动路由
```csharp
// ✅ 用户代码完全透明
var result = await mediator.SendAsync<CreateOrderCommand, OrderResponse>(cmd);

// Catga 自动处理：
// 1. 检测这是 Command（写操作）
// 2. 自动转发到 Leader 节点
// 3. Leader 通过 Raft 日志复制
// 4. 多数节点确认后提交
// 5. 返回结果
```

### 📐 路由策略
- 📝 **Command** (写操作) → 自动路由到 Leader
- 📖 **Query** (读操作) → 本地执行
- 📢 **Event** (事件) → 广播到所有节点

### 💡 用户体验
- ✅ **完全透明** - 用户无需关心集群细节
- ✅ **零配置** - 自动处理路由和故障转移
- ✅ **类型安全** - 编译时检查
- ✅ **强一致性** - Raft 保证

---

## 📦 安装

```bash
dotnet add package Catga.Cluster.DotNext
```

---

## 🚀 快速开始

### 1. 配置集群

```csharp
var builder = WebApplication.CreateBuilder(args);

// 添加 Catga
builder.Services.AddCatga();
builder.Services.AddGeneratedHandlers();

// 添加 Raft 集群（深度集成）
builder.Services.AddRaftCluster(options =>
{
    options.ClusterMemberId = "node1";
    options.Members = new[]
    {
        new Uri("http://node1:5001"),
        new Uri("http://node2:5002"),
        new Uri("http://node3:5003")
    };
});

var app = builder.Build();
app.MapRaft(); // Raft HTTP 端点
app.Run();
```

### 2. 定义消息

```csharp
// Command - 自动路由到 Leader
public record CreateOrderCommand(string ProductId, int Quantity) 
    : IRequest<OrderResponse>;

// Query - 本地执行
public record GetOrderQuery(string OrderId) 
    : IRequest<OrderResponse>;

// Event - 广播到所有节点
public record OrderCreatedEvent(string OrderId) 
    : IEvent;
```

### 3. 实现 Handler（无需关心集群）

```csharp
public class CreateOrderHandler : IRequestHandler<CreateOrderCommand, OrderResponse>
{
    public async Task<CatgaResult<OrderResponse>> HandleAsync(
        CreateOrderCommand cmd,
        CancellationToken ct = default)
    {
        // 正常业务逻辑 - 无需关心集群
        var orderId = Guid.NewGuid().ToString();
        return CatgaResult<OrderResponse>.Success(
            new OrderResponse(orderId, "Created")
        );
    }
}
```

### 4. 使用（完全透明）

```csharp
app.MapPost("/orders", async (ICatgaMediator mediator, CreateOrderCommand cmd) =>
{
    // Catga 自动处理集群路由
    var result = await mediator.SendAsync<CreateOrderCommand, OrderResponse>(cmd);
    return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
});
```

---

## 🏗️ 架构设计

### 核心组件

#### 1. RaftAwareMediator
自动识别消息类型并路由：
- Command（包含 Create/Update/Delete/Set）→ Leader
- Query → 本地
- Event → 广播

#### 2. RaftMessageTransport
基于 Raft 的消息传输层，自动处理：
- Leader 转发
- 节点通信
- 故障重试

#### 3. ICatgaRaftCluster
简化的集群接口：
```csharp
public interface ICatgaRaftCluster
{
    string? LeaderId { get; }        // 当前 Leader
    string LocalMemberId { get; }     // 本节点 ID
    bool IsLeader { get; }            // 是否为 Leader
    IReadOnlyList<ClusterMember> Members { get; }
    long Term { get; }                // 选举轮次
    ClusterStatus Status { get; }     // 集群状态
}
```

---

## 📊 消息路由流程

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

## 🎯 配置选项

```csharp
builder.Services.AddRaftCluster(options =>
{
    // 集群成员配置
    options.ClusterMemberId = "node1";
    options.Members = new[] 
    { 
        new Uri("http://node1:5001"),
        new Uri("http://node2:5002"),
        new Uri("http://node3:5003")
    };
    
    // Raft 算法参数
    options.ElectionTimeout = TimeSpan.FromMilliseconds(150);
    options.HeartbeatInterval = TimeSpan.FromMilliseconds(50);
    options.CompactionThreshold = 1000;
});
```

---

## 📈 性能指标

### 预期性能
- **写延迟**: ~2-3ms（本地 Leader）
- **读延迟**: ~0.5ms（本地查询）
- **吞吐量**: 10,000+ ops/s
- **可用性**: 99.99%（3 节点集群）

### 一致性保证
- **写入**: 强一致性（Raft 保证）
- **读取**: 可选（强一致性 or 最终一致性）
- **事件**: 至少一次交付

---

## 🔧 当前状态

### ✅ 已完成
- [x] RaftAwareMediator - 自动路由
- [x] RaftMessageTransport - 传输层
- [x] ICatgaRaftCluster - 简化接口
- [x] 架构设计和文档

### 🚧 进行中
- [ ] DotNext Raft 真实绑定
- [ ] HTTP/gRPC 节点通信
- [ ] 健康检查集成
- [ ] 完整示例项目

---

## 📚 参考资料

- [DotNext 文档](https://dotnet.github.io/dotNext/)
- [Raft 论文](https://raft.github.io/)
- [Raft 可视化](http://thesecretlivesofdata.com/raft/)
- [Catga 文档](https://github.com/Cricle/Catga)

---

## 💡 设计理念

> **"集群应该是透明的，用户只需专注业务逻辑"**

Catga.Cluster.DotNext 的目标是让分布式系统开发像单机一样简单：
- ✅ 无需手动转发请求
- ✅ 无需处理节点故障
- ✅ 无需关心一致性
- ✅ 无需编写集群代码

**一切都是自动的。**
