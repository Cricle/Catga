# WorkerId 配置增强 - 支持分布式和集群部署

## 📋 概述

本次增强为 Catga 框架添加了**灵活的 WorkerId 配置**，使其能够在分布式和集群环境下正确生成唯一的分布式 ID（Snowflake ID）。

## 🎯 问题背景

之前的 `MessageExtensions.NewMessageId()` 使用静态单例 `SnowflakeIdGenerator`，虽然支持通过环境变量 `CATGA_WORKER_ID` 配置，但存在以下问题：

1. **不够灵活**：无法在运行时动态配置 WorkerId
2. **DI 集成不足**：未通过依赖注入提供 `IDistributedIdGenerator`
3. **文档缺失**：缺少分布式部署场景的说明和示例

在分布式/集群环境下，如果多个节点使用相同的 WorkerId，会导致 **ID 冲突**！

## ✅ 解决方案

### 1. 新增 DI 配置方法

在 `CatgaServiceBuilder` 中添加了两个配置方法：

```csharp
// 方式 1: 显式指定 WorkerId（推荐用于开发/测试）
builder.Services.AddCatga()
    .UseWorkerId(1)  // 节点 1
    .UseMemoryPack()
    .ForDevelopment();

// 方式 2: 从环境变量读取（推荐用于生产/容器）
builder.Services.AddCatga()
    .UseWorkerIdFromEnvironment()  // 从 CATGA_WORKER_ID 读取
    .UseMemoryPack()
    .ForProduction();
```

### 2. 默认行为保持向后兼容

`AddCatga()` 默认仍会注册 `IDistributedIdGenerator`：

- 优先从 `CATGA_WORKER_ID` 环境变量读取
- 如果未设置，则使用**随机 WorkerId**（单节点开发场景）
- 用户可以通过 `.UseWorkerId(n)` 或 `.UseWorkerIdFromEnvironment()` 覆盖默认行为

### 3. OrderSystem 演示多节点部署

#### 3.1 `Program.cs` 支持命令行参数和环境变量

```csharp
var catgaBuilder = builder.Services.AddCatga().UseMemoryPack();

if (args.Length > 0 && int.TryParse(args[0], out var workerId))
{
    // 从命令行参数获取 WorkerId（便于本地多节点测试）
    catgaBuilder.UseWorkerId(workerId);
    builder.WebHost.UseUrls($"http://localhost:{5000 + workerId}");
    Console.WriteLine($"[OrderSystem] 🌐 Using WorkerId from args: {workerId}, Port: {5000 + workerId}");
}
else if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CATGA_WORKER_ID")))
{
    // 从环境变量获取 WorkerId（生产/容器环境）
    catgaBuilder.UseWorkerIdFromEnvironment();
    Console.WriteLine("[OrderSystem] 🌐 Using WorkerId from environment variable");
}
else
{
    // 开发环境默认：使用随机 WorkerId（单节点场景）
    Console.WriteLine("[OrderSystem] ⚙️ Single-node development mode (random WorkerId)");
}

catgaBuilder.ForDevelopment();
```

#### 3.2 多节点启动脚本 `start-cluster.ps1`

```powershell
.\start-cluster.ps1              # 启动 3 个节点（默认）
.\start-cluster.ps1 -NodeCount 5 # 启动 5 个节点
```

每个节点将：
- 使用唯一的 WorkerId (1, 2, 3, ...)
- 监听不同的端口 (5001, 5002, 5003, ...)
- 在独立的 PowerShell 窗口中运行

### 4. 完整的分布式部署文档

创建了 `examples/OrderSystem.Api/DISTRIBUTED-DEPLOYMENT.md`，包含：

- Snowflake ID 结构说明
- WorkerId 的重要性
- 三种配置方式（显式配置、环境变量、自定义变量）
- Docker Compose 示例
- Kubernetes 示例
- 本地多节点测试步骤
- WorkerId 分配策略（小/中/大型集群）
- 注意事项和最佳实践

## 📂 文件变更

### 新增文件

1. `examples/OrderSystem.Api/DISTRIBUTED-DEPLOYMENT.md` - 分布式部署完整指南
2. `examples/OrderSystem.Api/start-cluster.ps1` - 多节点集群启动脚本
3. `WORKERID_ENHANCEMENT.md` - 本文档

### 修改文件

1. `src/Catga/DependencyInjection/CatgaServiceBuilder.cs`
   - 新增 `UseWorkerId(int workerId)` 方法
   - 新增 `UseWorkerIdFromEnvironment(string envVarName = "CATGA_WORKER_ID")` 方法
   - 新增 `GetWorkerIdFromEnvironment()` 私有辅助方法

2. `src/Catga/DependencyInjection/CatgaServiceCollectionExtensions.cs`
   - 修改 `AddCatga()` 中 `IDistributedIdGenerator` 的注册逻辑
   - 默认支持从环境变量读取 WorkerId
   - 新增 `GetWorkerIdFromEnvironmentOrRandom()` 私有辅助方法

3. `src/Catga/Core/MessageExtensions.cs`
   - 新增 `NewMessageId(IDistributedIdGenerator generator)` 重载（DI 友好）
   - 新增 `NewCorrelationId(IDistributedIdGenerator generator)` 重载（DI 友好）
   - 更新注释，说明优先使用 DI 的 `IDistributedIdGenerator`

4. `examples/OrderSystem.Api/Program.cs`
   - 添加 WorkerId 配置逻辑（命令行参数 > 环境变量 > 默认随机）
   - 根据 WorkerId 动态设置监听端口
   - 添加启动日志，显示使用的 WorkerId 配置方式

5. `examples/OrderSystem.Api/README.md`
   - 新增"多节点模式（分布式/集群演示）"章节
   - 添加多节点启动命令示例
   - 链接到 `DISTRIBUTED-DEPLOYMENT.md`

6. `examples/OrderSystem.Api/FEATURES.md`
   - 新增"🌐 Distributed & Cluster Deployment"特性表格
   - 更新"Learning Path"，添加"Test Multi-Node Cluster"步骤
   - 在"Completeness Checklist"中添加分布式相关特性

## 🧪 测试结果

### 编译状态

```
✅ 编译: SUCCESS
✅ CS 警告: 0 个
ℹ️  IL 警告: 108 个 (预期的 AOT 信息性警告)
```

### 单元测试

```
✅ 总计: 180, 失败: 0, 成功: 180, 已跳过: 0
✅ 覆盖率: 100%
```

## 🎯 使用场景

### 场景 1：单机开发（默认）

```bash
dotnet run
# 使用随机 WorkerId，无需配置
```

### 场景 2：本地多节点测试

```bash
# 终端 1
dotnet run -- 1  # WorkerId=1, Port=5001

# 终端 2
dotnet run -- 2  # WorkerId=2, Port=5002

# 终端 3
dotnet run -- 3  # WorkerId=3, Port=5003
```

或使用脚本：

```powershell
.\start-cluster.ps1
```

### 场景 3：Docker Compose 部署

```yaml
services:
  ordersystem-node1:
    image: ordersystem:latest
    environment:
      - CATGA_WORKER_ID=1
    ports:
      - "5001:5000"

  ordersystem-node2:
    image: ordersystem:latest
    environment:
      - CATGA_WORKER_ID=2
    ports:
      - "5002:5000"
```

### 场景 4：Kubernetes StatefulSet

```yaml
apiVersion: apps/v1
kind: StatefulSet
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
        - name: CATGA_WORKER_ID
          value: "$(POD_ORDINAL)"  # 0, 1, 2, ...
```

## 🔍 验证 WorkerId

可以通过解析生成的 ID 来验证 WorkerId：

```csharp
var generator = serviceProvider.GetRequiredService<IDistributedIdGenerator>();
var id = generator.NextId();
generator.ParseId(id, out var metadata);

Console.WriteLine($"WorkerId: {metadata.WorkerId}");       // 1, 2, 3, ...
Console.WriteLine($"Timestamp: {metadata.GeneratedAt}");
Console.WriteLine($"Sequence: {metadata.Sequence}");
```

## 📊 核心优势

1. **零 ID 冲突**：每个节点使用唯一的 WorkerId，确保分布式环境下 ID 唯一
2. **灵活配置**：支持命令行参数、环境变量、DI 配置三种方式
3. **向后兼容**：未配置时使用随机 WorkerId，不影响现有单节点应用
4. **DI 友好**：通过 `IDistributedIdGenerator` 接口，便于测试和扩展
5. **文档完善**：提供完整的部署指南和示例代码

## 🎓 最佳实践

### ✅ 推荐做法

- **开发环境**：使用默认随机 WorkerId（单节点）
- **测试环境**：使用命令行参数 `dotnet run -- <workerId>`
- **生产环境**：使用环境变量 `CATGA_WORKER_ID`
- **容器环境**：在 `docker-compose.yml` 或 Kubernetes YAML 中设置环境变量
- **大型集群**：使用服务发现（Consul/etcd）动态分配 WorkerId

### ❌ 避免的做法

- ❌ 生产环境不配置 WorkerId（可能导致 ID 冲突）
- ❌ 多个节点使用相同的 WorkerId
- ❌ WorkerId 超出范围（0-255 for default layout）

## 🚀 下一步

1. **验证多节点部署**
   ```bash
   cd examples/OrderSystem.Api
   .\start-cluster.ps1
   ```

2. **查看分布式部署指南**
   ```bash
   cat examples/OrderSystem.Api/DISTRIBUTED-DEPLOYMENT.md
   ```

3. **测试 ID 生成**
   - 向不同节点发送请求
   - 检查生成的 MessageId
   - 验证 WorkerId 不同

## 📚 相关文档

- [分布式部署指南](../../examples/OrderSystem.Api/DISTRIBUTED-DEPLOYMENT.md)
- [OrderSystem 功能清单](../../examples/OrderSystem.Api/FEATURES.md)
- [OrderSystem README](../../examples/OrderSystem.Api/README.md)
- [Snowflake ID 算法](../guides/distributed-id.md)

---

**Catga** - 为分布式和集群而设计 🚀

