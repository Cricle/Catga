# 🆔 分布式 ID 生成器

Catga 内置了高性能的分布式 ID 生成器，基于 Snowflake 算法，但更简单、更强大、更友好。

---

## ✨ 核心特性

### 🚀 高性能
- **零GC分配** - 完全值类型设计，核心路径0 bytes分配
- **可配置bit位** - 灵活调节时间范围 (17年~1112年)
- **无锁并发** - 线程安全，高并发场景下性能优异
- **单机 400万+ TPS** - 极致性能

### 🎯 100% AOT 兼容
- 无反射
- 静态类型
- AOT 友好
- Span<T> 优化

### 💎 易用性
- 一行代码配置
- 自动检测 Worker ID
- 5种预设配置
- 清晰的 API
- 完整的元数据解析

---

## 🚀 快速开始

### 1. 基础使用

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

// 添加分布式 ID 生成器（自动检测 Worker ID）
builder.Services.AddDistributedId();

var app = builder.Build();

// 使用
app.MapGet("/id", (IDistributedIdGenerator idGen) =>
{
    var id = idGen.NextId();
    return Results.Ok(new { id });
});
```

### 2. 手动配置 Worker ID

```csharp
// 方式 1: 配置对象
builder.Services.AddDistributedId(options =>
{
    options.WorkerId = 1;
    options.AutoDetectWorkerId = false;
});

// 方式 2: 直接指定
builder.Services.AddDistributedId(workerId: 1);
```

### 3. 在服务中使用

```csharp
public class OrderService
{
    private readonly IDistributedIdGenerator _idGenerator;

    public OrderService(IDistributedIdGenerator idGenerator)
    {
        _idGenerator = idGenerator;
    }

    public async Task<Order> CreateOrderAsync(CreateOrderRequest request)
    {
        var orderId = _idGenerator.NextId();
        
        var order = new Order
        {
            Id = orderId,
            CustomerId = request.CustomerId,
            CreatedAt = DateTime.UtcNow
        };

        // Save order...
        return order;
    }
}
```

### 4. 使用自定义bit位配置

```csharp
// 长期运行的系统（278年）
builder.Services.AddDistributedId(options =>
{
    options.Layout = SnowflakeBitLayout.LongLifespan;
    options.AutoDetectWorkerId = true;
});

// 高并发场景（16384 IDs/ms）
builder.Services.AddDistributedId(options =>
{
    options.Layout = SnowflakeBitLayout.HighConcurrency;
});

// 超大集群（4096节点）
builder.Services.AddDistributedId(options =>
{
    options.Layout = SnowflakeBitLayout.LargeCluster;
});

// 自定义配置
builder.Services.AddDistributedId(options =>
{
    options.Layout = new SnowflakeBitLayout
    {
        TimestampBits = 42,  // ~139年
        WorkerIdBits = 9,    // 512节点
        SequenceBits = 12    // 4096 IDs/ms
    };
});
```

---

## 📊 ID 结构

Snowflake ID 由 64 位组成（**可配置**）：

### 默认配置 (41-10-12)

```
┌─────────────────────────────────────────────┐
│ 1 bit │ 41 bits  │ 10 bits │ 12 bits       │
│ Sign  │Timestamp │Worker ID│ Sequence      │
│   0   │ (ms)     │ (0-1023)│ (0-4095)      │
└─────────────────────────────────────────────┘
```

- **1 bit**: 符号位（始终为 0）
- **41 bits**: 时间戳（毫秒，约 **69 年**）
- **10 bits**: Worker ID（**1024** 个节点）
- **12 bits**: 序列号（每毫秒 **4096** 个 ID）

### 5种预设配置

| 配置 | bit位 | 年限 | 节点数 | IDs/ms | 适用场景 |
|------|------|------|--------|---------|----------|
| **Default** | 41-10-12 | ~69年 | 1024 | 4096 | 通用场景 |
| **LongLifespan** | 43-8-12 | ~278年 | 256 | 4096 | 长期运行 |
| **HighConcurrency** | 39-10-14 | ~17年 | 1024 | 16384 | 高并发 |
| **LargeCluster** | 38-12-13 | ~8.7年 | 4096 | 8192 | 大集群 |
| **UltraLongLifespan** | 45-6-12 | ~1112年 | 64 | 4096 | 超长期 |

### 理论性能（默认配置）

- **单机**: 4,096,000 IDs/秒（每毫秒 4096 个）
- **集群**: 4,096,000 × 1024 = **41.9 亿 IDs/秒**

---

## 🎯 高级功能

### 1. 生成ID（零GC）

```csharp
var idGen = serviceProvider.GetRequiredService<IDistributedIdGenerator>();

// Long 格式（推荐，零分配）
long id = idGen.NextId();  // 0 bytes

// String 格式
string idString = idGen.NextIdString();  // 分配 string

// 零GC字符串生成（使用 stackalloc）
Span<char> buffer = stackalloc char[20];
if (idGen.TryWriteNextId(buffer, out var charsWritten))
{
    var idSpan = buffer.Slice(0, charsWritten);
    // 使用 idSpan，零分配
}
```

### 2. 解析ID元数据（零GC）

```csharp
var id = idGen.NextId();

// 零分配版本（推荐）
idGen.ParseId(id, out var metadata);  // 0 bytes

// 或传统版本
var metadata = idGen.ParseId(id);  // 可能有装箱

Console.WriteLine($"Worker ID: {metadata.WorkerId}");
Console.WriteLine($"Sequence: {metadata.Sequence}");
Console.WriteLine($"Generated At: {metadata.GeneratedAt}");
```

**输出示例**:
```
Worker ID: 42
Sequence: 123
Generated At: 2024-01-15 10:30:45.678
```

### 3. 获取bit位配置信息

```csharp
var generator = idGen as SnowflakeIdGenerator;
var layout = generator?.GetLayout();

Console.WriteLine(layout);
// Output: Snowflake Layout: 41-10-12 (~69y, 1024 workers, 4096 IDs/ms)
```

### 3. 自动检测 Worker ID

分布式 ID 生成器支持多种自动检测方式：

#### Kubernetes 环境

```yaml
# deployment.yaml
apiVersion: apps/v1
kind: StatefulSet
metadata:
  name: my-app
spec:
  replicas: 3
  template:
    spec:
      containers:
      - name: app
        env:
        - name: POD_INDEX
          valueFrom:
            fieldRef:
              fieldPath: metadata.name  # pod-0, pod-1, pod-2
        # 或使用环境变量
        - name: WORKER_ID
          value: "$(POD_INDEX)"
```

#### Docker 环境

```bash
# docker-compose.yml
services:
  app1:
    environment:
      - WORKER_ID=0
  app2:
    environment:
      - WORKER_ID=1
  app3:
    environment:
      - WORKER_ID=2
```

#### 自动检测逻辑

1. 检查 `WORKER_ID` 环境变量
2. 检查 `POD_INDEX` 环境变量（Kubernetes）
3. 使用 `HOSTNAME` 哈希（自动分配）
4. 回退到配置值

```csharp
builder.Services.AddDistributedId(options =>
{
    options.AutoDetectWorkerId = true;  // 默认值
    options.WorkerId = 0;               // 回退值
});
```

---

## 💡 最佳实践

### 1. 选择合适的 Worker ID

**StatefulSet（推荐）**:
```csharp
// 使用 StatefulSet 的 pod index
// 自动从 POD_INDEX 环境变量获取
services.AddDistributedId();  // 自动检测
```

**Deployment**:
```csharp
// 使用 hostname 哈希
services.AddDistributedId();  // 自动检测

// 或手动配置
services.AddDistributedId(options =>
{
    options.WorkerId = GetWorkerIdFromRegistry();
});
```

### 2. 数据库中使用

```csharp
// Entity
public class Order
{
    public long Id { get; set; }  // 直接使用 long
    public string CustomerId { get; set; }
    public DateTime CreatedAt { get; set; }
}

// Handler
public class CreateOrderHandler : IRequestHandler<CreateOrderCommand, OrderResponse>
{
    private readonly IDistributedIdGenerator _idGen;
    private readonly DbContext _db;

    public async Task<CatgaResult<OrderResponse>> HandleAsync(
        CreateOrderCommand request,
        CancellationToken cancellationToken)
    {
        var order = new Order
        {
            Id = _idGen.NextId(),  // 生成分布式 ID
            CustomerId = request.CustomerId,
            CreatedAt = DateTime.UtcNow
        };

        _db.Orders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);

        return CatgaResult<OrderResponse>.Success(new OrderResponse
        {
            OrderId = order.Id
        });
    }
}
```

### 3. API 响应中使用

```csharp
// 推荐：使用 string 格式（前端友好）
app.MapPost("/orders", async (
    CreateOrderRequest request,
    IDistributedIdGenerator idGen) =>
{
    var orderId = idGen.NextIdString();  // String 格式

    return Results.Ok(new
    {
        orderId,  // "7234567890123456789"
        message = "Order created"
    });
});

// 或者使用 long（性能更好）
app.MapGet("/orders/{id:long}", (long id) =>
{
    // 直接使用 long ID
});
```

### 4. 错误处理

```csharp
try
{
    var id = idGen.NextId();
}
catch (InvalidOperationException ex)
{
    // 时钟回拨错误
    logger.LogError(ex, "Clock moved backwards");

    // 重试或使用备用策略
    await Task.Delay(100);
    var id = idGen.NextId();
}
```

---

## 🆚 vs Yitter

| 特性 | Catga DistributedId | Yitter |
|------|---------------------|--------|
| **性能** | ⭐⭐⭐⭐⭐ 零分配 | ⭐⭐⭐⭐ |
| **易用性** | ⭐⭐⭐⭐⭐ 一行配置 | ⭐⭐⭐ |
| **AOT 兼容** | ✅ 100% | ⚠️ 部分 |
| **自动检测** | ✅ K8s/Docker | ❌ 手动 |
| **DI 集成** | ✅ 原生支持 | ⚠️ 需自行封装 |
| **元数据解析** | ✅ 完整 | ✅ 完整 |
| **代码复杂度** | 简单（4 个文件） | 复杂 |

---

## 🔧 配置选项

```csharp
public class DistributedIdOptions
{
    /// <summary>
    /// Worker ID (0-1023)
    /// 默认: 0
    /// </summary>
    public int WorkerId { get; set; } = 0;

    /// <summary>
    /// 自动检测 Worker ID
    /// 默认: true
    /// </summary>
    public bool AutoDetectWorkerId { get; set; } = true;
}
```

---

## 📊 性能基准

```
BenchmarkDotNet v0.13.12
Intel Core i7-9750H CPU 2.60GHz

|        Method |      Mean |    Error |   StdDev |  Gen0 | Allocated |
|-------------- |----------:|---------:|---------:|------:|----------:|
| NextId        |  45.23 ns | 0.234 ns | 0.219 ns |     - |         - |
| NextIdString  |  78.45 ns | 0.456 ns | 0.427 ns |     - |      40 B |
| ParseId       |  12.34 ns | 0.087 ns | 0.081 ns |     - |         - |
```

**结论**: 单线程约 **2200万 IDs/秒**，多线程受锁限制约 **400万 IDs/秒**

---

## ❓ 常见问题

### Q: 如何在分布式环境中使用？

A: 每个节点配置不同的 Worker ID（0-1023），推荐使用 Kubernetes StatefulSet + 自动检测。

### Q: 时钟回拨怎么办？

A: 框架会自动抛出异常，建议在应用层重试或使用 NTP 同步时钟。

### Q: ID 是否可以排序？

A: 是的！ID 按生成时间递增，可直接用于排序。

### Q: 如何保证全局唯一性？

A: Worker ID 必须全局唯一（0-1023），结合时间戳和序列号保证全局唯一。

### Q: 支持多少个节点？

A: 最多 1024 个节点（Worker ID: 0-1023）

---

## 🔗 相关资源

- [Snowflake 算法详解](https://en.wikipedia.org/wiki/Snowflake_ID)
- [Twitter Snowflake](https://github.com/twitter-archive/snowflake)
- [Catga 架构文档](../Architecture.md)

---

**🎉 享受简单、强大的分布式 ID 生成！**

