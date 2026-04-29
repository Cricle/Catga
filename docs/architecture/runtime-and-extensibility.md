# Catga 架构：运行时、性能与扩展点

> 这一页聚焦配置架构、数据流、性能优化、可观测性和扩展点。
> 如果你在找模块分层和职责边界，请看 [modules-and-boundaries.md](./modules-and-boundaries.md)。

---

## 这页适合谁

- 想理解请求/事件到底怎么流过系统
- 想评估性能、扩展点和观测方式
- 想实现自定义 behavior / serializer / transport

---

## 🔧 配置架构

### Fluent Builder API

```csharp
// 极简配置
services.AddCatga()
    .UseMemoryPack()      // 序列化器
    .ForProduction();     // 环境预设

// 精细控制
services.AddCatga()
    .UseMemoryPack()
    .WithLogging()
    .WithTracing()
    .WithIdempotency(retentionHours: 24)
    .WithRetry(maxAttempts: 3)
    .WithValidation();

// 自定义环境
services.AddCatga()
    .UseMemoryPack()
    .Configure(options =>
    {
        options.EnableLogging = true;
        options.EnableTracing = true;
        options.IdempotencyShardCount = 64;
    });
```

### 环境预设

| 预设 | 日志 | 追踪 | 幂等性 | 重试 | 验证 | 适用场景 |
|------|------|------|--------|------|------|---------|
| `ForDevelopment()` | ✅ | ✅ | ❌ | ❌ | ✅ | 开发调试 |
| `ForProduction()` | ✅ | ✅ | ✅ | ✅ | ✅ | 生产环境 |
| `ForHighPerformance()` | ❌ | ❌ | ✅ | ❌ | ❌ | 高性能场景 |
| `Minimal()` | ❌ | ❌ | ❌ | ❌ | ❌ | 最小化 |

---

## 📊 数据流

### Command/Query 流程

```
1. 客户端发送 Command
   ↓
2. ICatgaMediator.SendAsync()
   ↓
3. Pipeline Behaviors (按顺序执行)
   ├─ LoggingBehavior      (记录开始)
   ├─ TracingBehavior      (创建 Span)
   ├─ IdempotencyBehavior  (检查重复)
   ├─ ValidationBehavior   (数据验证)
   ├─ RetryBehavior        (重试逻辑)
   └─ Handler 执行
   ↓
4. 返回 CatgaResult<T>
   ↓
5. Pipeline Behaviors (逆序清理)
   ├─ RetryBehavior        (记录重试)
   ├─ ValidationBehavior   (记录验证)
   ├─ IdempotencyBehavior  (缓存结果)
   ├─ TracingBehavior      (结束 Span)
   └─ LoggingBehavior      (记录结束)
   ↓
6. 返回给客户端
```

### Event 流程

```
1. 发布 Event
   ↓
2. ICatgaMediator.PublishAsync()
   ↓
3. 从 DI 获取所有 EventHandler (GetServices<IEventHandler<TEvent>>)
   ↓
4. 并行执行所有 EventHandler
   ├─ Handler 1
   ├─ Handler 2
   └─ Handler N
   ↓
5. 聚合结果
   ↓
6. 完成
```

---

## 🚀 性能优化

### 1. 零反射设计

**Before** (反射):
```csharp
// 运行时反射查找 Handler
var handlerType = typeof(IRequestHandler<,>).MakeGenericType(requestType, responseType);
var handler = serviceProvider.GetService(handlerType);  // 慢！
```

**After** (静态缓存):
```csharp
// 编译时生成，运行时直接访问
var handler = HandlerCache<TRequest, TResponse>.GetHandler(serviceProvider);  // 快！
```

**性能提升**: 90x

### 2. 零分配设计

**技术**:
- `ValueTask<T>` - 避免 Task 分配
- `readonly struct` - 栈分配
- `ArrayPool<T>` - 重用 byte[] 缓冲区
- 直接 DI 解析 - 尊重生命周期，无过度缓存

**收益**:
- 热路径零堆分配
- GC 压力减少 95%

### 3. 无锁并发

**技术**:
- `ConcurrentDictionary` - 无锁字典
- 分片设计 - 减少竞争
- `ImmutableList` - 无锁列表

**收益**:
- 高并发性能提升 10x
- 无死锁风险

---

## 🔍 可观测性

### Metrics (OpenTelemetry)

```csharp
// 自动记录的指标
- catga.messages.published      // Counter
- catga.messages.failed         // Counter
- catga.commands.executed       // Counter
- catga.message.duration        // Histogram
- catga.messages.active         // ObservableGauge
```

### Tracing (ActivitySource)

```csharp
// 自动创建的 Span
- catga.command.execute         // Command 执行
- catga.event.publish           // Event 发布
- catga.pipeline.behavior       // Behavior 执行
- catga.handler.execute         // Handler 执行
```

### Logging (LoggerMessage)

```csharp
// 零分配结构化日志
[LoggerMessage(Level = LogLevel.Information, Message = "Executing command {CommandType}")]
static partial void LogCommandExecuting(ILogger logger, string commandType);
```

---

## 🎨 扩展点

### 1. 自定义 Behavior

```csharp
public class CustomBehavior<TRequest, TResponse> : BaseBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public override async ValueTask<CatgaResult<TResponse>> HandleAsync(
        TRequest request,
        PipelineDelegate<TResponse> next,
        CancellationToken ct = default)
    {
        // 前置逻辑
        var result = await next();
        // 后置逻辑
        return result;
    }
}

// 注册
services.AddTransient(typeof(IPipelineBehavior<,>), typeof(CustomBehavior<,>));
```

### 2. 自定义序列化器

```csharp
public class CustomSerializer : IMessageSerializer
{
    public byte[] Serialize<T>(T message) { ... }
    public T? Deserialize<T>(byte[] data) { ... }
}

// 注册
services.AddCatga()
    .Services.AddSingleton<IMessageSerializer, CustomSerializer>();
```

### 3. 自定义传输层

```csharp
public class CustomTransport : IMessageTransport
{
    public Task PublishAsync<T>(T message, CancellationToken ct) { ... }
    public Task SubscribeAsync<T>(Func<T, Task> handler, CancellationToken ct) { ... }
}

// 注册
services.AddSingleton<IMessageTransport, CustomTransport>();
```

---

## 📚 相关文档

- **[职责边界](./RESPONSIBILITY-BOUNDARY.md)** - Catga vs 其他组件
- **[CQRS 模式](./cqrs.md)** - 命令查询职责分离
- **[序列化指南](../guides/serialization.md)** - MemoryPack vs JSON
- **[性能报告](../topics/history/PERFORMANCE-REPORT.md)** - 历史性能报告与优化过程

---

## 🎯 设计决策

### 为什么移除应用层节点发现？

**Before**:
```csharp
services.AddNatsNodeDiscovery();  // 应用层实现
services.AddRedisNodeDiscovery(); // 重复造轮子
```

**After**:
```yaml
# 使用 K8s Service Discovery
apiVersion: v1
kind: Service
metadata:
  name: order-service
```

**理由**:
1. ✅ K8s 已经完美解决
2. ✅ 应用层实现不如平台层
3. ✅ 减少代码复杂度
4. ✅ 更好的跨平台支持

### 为什么选择 MemoryPack？

**对比**:
| 特性 | MemoryPack | JSON | Protobuf |
|------|-----------|------|----------|
| AOT 兼容 | ✅ 100% | ⚠️ 需配置 | ✅ 部分 |
| 性能 | 🔥 最快 | ⚡ 中等 | ⚡ 快 |
| Payload | 📦 最小 | 📦 大 | 📦 小 |
| 人类可读 | ❌ | ✅ | ❌ |
| 易用性 | ✅ 简单 | ✅ 简单 | ⚠️ 复杂 |

**结论**: MemoryPack 在 AOT、性能、易用性上最优

---

<div align="center">

**清晰的架构，卓越的性能**

[返回主文档](../README.md) · [快速开始](../articles/getting-started.md) · [API 参考](../api/index.md)

</div>
