# OrderSystem 分布式部署指南

本指南演示如何配置 OrderSystem 以支持**分布式和集群部署**。

## 🎯 核心概念：WorkerId

Catga 使用 **Snowflake 分布式 ID 算法** 生成唯一的 MessageId：

```
┌─────────────────────────────────────────────────────────────────┐
│ Snowflake ID 结构 (64 bits)                                      │
├──────────────┬──────────────┬──────────────┬────────────────────┤
│  Timestamp   │  WorkerId    │  Sequence    │                    │
│  (44 bits)   │  (8 bits)    │  (11 bits)   │                    │
│  ~280 years  │  0-255 nodes │  0-4095/ms   │                    │
└──────────────┴──────────────┴──────────────┴────────────────────┘
```

**WorkerId 的重要性**：
- 每个节点/实例必须有**唯一的 WorkerId**（0-255）
- 这确保了在分布式环境下生成的 ID 不会冲突
- 单节点可以使用随机 WorkerId，集群必须显式配置

## 📋 配置方式

### 方式 1：DI 配置（推荐用于 ASP.NET Core 应用）

```csharp
// Node 1
builder.Services.AddCatga()
    .UseWorkerId(1)  // 节点 1 使用 WorkerId=1
    .UseMemoryPack()
    .ForDevelopment();

// Node 2
builder.Services.AddCatga()
    .UseWorkerId(2)  // 节点 2 使用 WorkerId=2
    .UseMemoryPack()
    .ForDevelopment();

// Node 3
builder.Services.AddCatga()
    .UseWorkerId(3)  // 节点 3 使用 WorkerId=3
    .UseMemoryPack()
    .ForDevelopment();
```

### 方式 1.5：静态配置（推荐用于非 DI 场景）

```csharp
// 在应用启动时设置（影响全局）
MessageExtensions.UseWorkerId(1);  // 节点 1

// 或使用自定义 generator
MessageExtensions.SetIdGenerator(new SnowflakeIdGenerator(workerId: 1));

// 之后所有的 NewMessageId() 都会使用这个 WorkerId
var id = MessageExtensions.NewMessageId();
```

### 方式 2：环境变量（推荐用于生产/容器）

```csharp
// 所有节点使用相同代码
builder.Services.AddCatga()
    .UseWorkerIdFromEnvironment()  // 从 CATGA_WORKER_ID 环境变量读取
    .UseMemoryPack()
    .ForProduction();
```

**Docker Compose 示例**：

```yaml
version: '3.8'
services:
  ordersystem-node1:
    image: ordersystem:latest
    environment:
      - CATGA_WORKER_ID=1  # 节点 1
      - ASPNETCORE_URLS=http://+:5001
    ports:
      - "5001:5001"

  ordersystem-node2:
    image: ordersystem:latest
    environment:
      - CATGA_WORKER_ID=2  # 节点 2
      - ASPNETCORE_URLS=http://+:5002
    ports:
      - "5002:5002"

  ordersystem-node3:
    image: ordersystem:latest
    environment:
      - CATGA_WORKER_ID=3  # 节点 3
      - ASPNETCORE_URLS=http://+:5003
    ports:
      - "5003:5003"
```

**Kubernetes 示例**：

```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: ordersystem
spec:
  replicas: 3
  template:
    spec:
      containers:
      - name: ordersystem
        image: ordersystem:latest
        env:
        - name: POD_NAME
          valueFrom:
            fieldRef:
              fieldPath: metadata.name
        - name: CATGA_WORKER_ID
          value: "$(POD_ORDINAL)"  # 使用 StatefulSet 的序号
```

### 方式 3：自定义环境变量名

```csharp
builder.Services.AddCatga()
    .UseWorkerIdFromEnvironment("MY_NODE_ID")  // 自定义环境变量
    .UseMemoryPack()
    .ForProduction();
```

## 🚀 本地多节点测试

### 步骤 1：修改 `Program.cs`

```csharp
// 从命令行参数或环境变量获取 WorkerId
var workerId = args.Length > 0 && int.TryParse(args[0], out var id) ? id : 1;

builder.Services.AddCatga()
    .UseWorkerId(workerId)
    .UseMemoryPack()
    .ForDevelopment();

builder.WebHost.UseUrls($"http://localhost:{5000 + workerId}");

app.Logger.LogInformation($"🚀 OrderSystem Node {workerId} started on http://localhost:{5000 + workerId}");
```

### 步骤 2：启动多个节点

```powershell
# 终端 1 - 节点 1 (WorkerId=1)
dotnet run --project examples/OrderSystem.Api -- 1

# 终端 2 - 节点 2 (WorkerId=2)
dotnet run --project examples/OrderSystem.Api -- 2

# 终端 3 - 节点 3 (WorkerId=3)
dotnet run --project examples/OrderSystem.Api -- 3
```

### 步骤 3：验证 WorkerId

```bash
# 调用 Node 1 创建订单
curl -X POST http://localhost:5001/demo/order-success

# 调用 Node 2 创建订单
curl -X POST http://localhost:5002/demo/order-success

# 调用 Node 3 创建订单
curl -X POST http://localhost:5003/demo/order-success
```

每个节点生成的 MessageId 都包含其唯一的 WorkerId，可以通过解析 ID 来验证：

```csharp
var generator = app.Services.GetRequiredService<IDistributedIdGenerator>();
var id = generator.NextId();
generator.ParseId(id, out var metadata);

Console.WriteLine($"WorkerId: {metadata.WorkerId}");  // 应该是 1, 2, 或 3
Console.WriteLine($"Timestamp: {metadata.GeneratedAt}");
Console.WriteLine($"Sequence: {metadata.Sequence}");
```

## 📊 WorkerId 分配策略

### 小型集群（< 10 节点）
- **手动分配**：每个节点手动配置 `UseWorkerId(n)`
- **优点**：简单直接
- **缺点**：需要手动管理

### 中型集群（10-100 节点）
- **环境变量**：通过部署脚本设置 `CATGA_WORKER_ID`
- **优点**：灵活，易于自动化
- **缺点**：需要确保不重复

### 大型集群（> 100 节点）
- **使用 10-bit WorkerId 布局**：支持 0-1023 个节点

```csharp
var layout = new SnowflakeBitLayout(
    timestampBits: 43,
    workerIdBits: 10,  // 1024 nodes
    sequenceBits: 10   // 1024 IDs/ms
);

builder.Services.AddSingleton<IDistributedIdGenerator>(
    new SnowflakeIdGenerator(nodeId, layout));
```

### 动态扩展（推荐）
- **集成服务发现**：从 Consul/etcd 获取 WorkerId
- **租约机制**：自动分配和回收 WorkerId

```csharp
// 示例：从 Consul 获取 WorkerId
var consul = new ConsulClient();
var workerId = await consul.AcquireWorkerIdAsync("ordersystem", maxWorkerId: 255);

builder.Services.AddCatga()
    .UseWorkerId(workerId)
    .UseMemoryPack()
    .ForProduction();
```

## ⚠️ 注意事项

### ❌ 不要做的事

```csharp
// ❌ 错误：所有节点使用相同的 WorkerId
builder.Services.AddCatga()
    .UseWorkerId(0)  // 所有节点都是 0 会导致 ID 冲突！
```

```csharp
// ❌ 错误：不配置 WorkerId（生产环境）
builder.Services.AddCatga()
    .UseMemoryPack();
// 会使用随机 WorkerId，集群中可能冲突！
```

### ✅ 正确做法

```csharp
// ✅ 正确：每个节点不同的 WorkerId
builder.Services.AddCatga()
    .UseWorkerIdFromEnvironment()  // 从环境变量读取
    .UseMemoryPack()
    .ForProduction();
```

```csharp
// ✅ 正确：开发环境可以使用随机
if (app.Environment.IsDevelopment())
{
    builder.Services.AddCatga()
        .UseMemoryPack()
        .ForDevelopment();  // 单节点开发，随机 WorkerId 无妨
}
```

## 🔍 ID 冲突检测

如果怀疑 WorkerId 配置有问题，可以添加日志：

```csharp
var generator = app.Services.GetRequiredService<IDistributedIdGenerator>();
var snowflake = (SnowflakeIdGenerator)generator;
var layout = snowflake.GetLayout();

app.Logger.LogInformation("WorkerId: {WorkerId}, Max WorkerId: {MaxWorkerId}",
    snowflake.WorkerId, layout.MaxWorkerId);
```

## 📚 相关文档

- [Snowflake ID 算法详解](../../docs/guides/distributed-id.md)
- [集群部署最佳实践](../../docs/deployment/kubernetes.md)
- [性能基准测试](../../docs/BENCHMARK-RESULTS.md)

---

**Catga** - 为分布式和集群而设计 🚀

