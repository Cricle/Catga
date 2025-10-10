# Catga.Cluster.DotNext

DotNext Raft 集群集成，为 Catga 提供自动化的分布式集群管理。

## ✨ 特性

- 🚀 **零配置集群** - 自动 Leader 选举和故障转移
- 📊 **Raft 共识算法** - 基于成熟的 DotNext.Net.Cluster
- 🔄 **自动日志复制** - 数据一致性保证
- 💪 **高可用** - 节点故障自动恢复
- ⚡ **高性能** - 低延迟、零分配优化

## 📦 安装

```bash
dotnet add package Catga
dotnet add package Catga.Cluster.DotNext
```

## 🚀 快速开始

### 1. 配置集群

```csharp
var builder = WebApplication.CreateBuilder(args);

// ✨ Catga + DotNext 集群
builder.Services.AddCatga();
builder.Services.AddGeneratedHandlers();

// 🚀 自动集群管理
builder.Services.AddDotNextCluster(options =>
{
    options.ClusterMemberId = "node1";
    options.Members = new[]
    {
        "http://localhost:5001",
        "http://localhost:5002",
        "http://localhost:5003"
    };
});

var app = builder.Build();
app.MapRaft();  // 启用 Raft HTTP 端点
app.Run();
```

### 2. 定义消息

```csharp
public record CreateOrderCommand(string ProductId, int Quantity) 
    : IRequest<OrderResponse>;

public record OrderResponse(string OrderId, string Status);
```

### 3. 实现 Handler

```csharp
public class CreateOrderHandler : IRequestHandler<CreateOrderCommand, OrderResponse>
{
    public Task<CatgaResult<OrderResponse>> HandleAsync(
        CreateOrderCommand cmd, 
        CancellationToken ct = default)
    {
        // 自动路由到 Leader 节点
        var orderId = Guid.NewGuid().ToString();
        return Task.FromResult(CatgaResult<OrderResponse>.Success(
            new OrderResponse(orderId, "Created")
        ));
    }
}
```

## 🎯 工作原理

### 自动 Leader 选举
- 集群启动时自动选举 Leader
- Leader 故障时自动重新选举
- Follower 节点自动跟随 Leader

### 消息路由
- **Command（写操作）** → 自动路由到 Leader
- **Query（读操作）** → 任意节点读取
- **Event（事件）** → 广播到所有节点

### 日志复制
- Leader 接收写请求后写入日志
- 自动复制日志到 Followers
- 多数节点确认后提交

## 📚 配置选项

```csharp
builder.Services.AddDotNextCluster(options =>
{
    // 节点标识
    options.ClusterMemberId = "node1";
    
    // 集群成员
    options.Members = new[] { "http://node1:5001", "http://node2:5002" };
    
    // 选举超时（毫秒）
    options.ElectionTimeout = TimeSpan.FromMilliseconds(150);
    
    // 心跳间隔（毫秒）
    options.HeartbeatInterval = TimeSpan.FromMilliseconds(50);
    
    // 日志压缩阈值
    options.CompactionThreshold = 1000;
});
```

## 🔍 监控

```csharp
app.MapGet("/cluster/status", (IRaftCluster cluster) => new
{
    IsLeader = cluster.Leader?.Equals(cluster.LocalMember) ?? false,
    LeaderId = cluster.Leader?.Id,
    Term = cluster.Term,
    Members = cluster.Members.Select(m => m.Id)
});
```

## 📖 更多信息

- [DotNext 文档](https://dotnet.github.io/dotNext/)
- [Raft 论文](https://raft.github.io/)
- [Catga 文档](https://github.com/Cricle/Catga)

