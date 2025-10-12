# Catga 快速参考

## 🚀 5分钟快速开始

### 1. 安装

```bash
dotnet add package Catga.InMemory
dotnet add package Catga.SourceGenerator
```

### 2. 定义消息

```csharp
// Command (有返回值)
public record CreateOrderCommand(string OrderId, decimal Amount) 
    : IRequest<OrderResult>;

// Event (无返回值)
public record OrderCreatedEvent(string OrderId, DateTime CreatedAt) 
    : INotification;
```

### 3. 编写 Handler

```csharp
public class CreateOrderHandler : IRequestHandler<CreateOrderCommand, OrderResult>
{
    public async Task<CatgaResult<OrderResult>> Handle(
        CreateOrderCommand request, 
        CancellationToken cancellationToken)
    {
        // 业务逻辑
        return CatgaResult<OrderResult>.Success(new OrderResult());
    }
}
```

### 4. 配置服务

```csharp
services.AddCatga()
    .UseInMemoryTransport()
    .AddGeneratedHandlers();  // 使用源生成器，AOT 友好
```

### 5. 使用

```csharp
var mediator = serviceProvider.GetRequiredService<IMediator>();

// 发送 Command
var result = await mediator.SendAsync(
    new CreateOrderCommand("ORD-001", 99.99m));

// 发布 Event
await mediator.PublishAsync(
    new OrderCreatedEvent("ORD-001", DateTime.UtcNow));
```

✅ **完成！3行配置，开始使用**

---

## 📊 常用场景

### CQRS 模式

```csharp
// Command: 修改状态
public record UpdateUserCommand(string UserId, string Name) : IRequest<bool>;

// Query: 只读查询
public record GetUserQuery(string UserId) : IRequest<UserDto>;

// Event: 领域事件
public record UserUpdatedEvent(string UserId) : INotification;
```

### Pipeline 行为

```csharp
// 自定义 Behavior
public class LoggingBehavior<TRequest, TResponse> 
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<CatgaResult<TResponse>> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        // 前置处理
        Console.WriteLine($"Processing: {typeof(TRequest).Name}");
        
        var result = await next();
        
        // 后置处理
        Console.WriteLine($"Completed: {result.IsSuccess}");
        
        return result;
    }
}

// 注册
services.AddCatga()
    .AddBehavior(typeof(LoggingBehavior<,>));
```

### 分布式消息

```csharp
// NATS
services.AddCatga()
    .UseNatsTransport("nats://localhost:4222")
    .AddGeneratedHandlers();

// Redis
services.AddCatga()
    .UseRedisTransport("localhost:6379")
    .AddGeneratedHandlers();
```

### RPC 调用

```csharp
// 服务端
services.AddCatgaRpcServer(options =>
{
    options.ServiceName = "order-service";
    options.Port = 5001;
});

// 客户端
var client = serviceProvider.GetRequiredService<IRpcClient>();
var result = await client.CallAsync<GetUserQuery, UserDto>(
    "user-service", 
    new GetUserQuery("user-123"));
```

---

## ⚡ Native AOT 配置

### 快速 AOT (MemoryPack)

```xml
<!-- .csproj -->
<PublishAot>true</PublishAot>
```

```csharp
// 消息定义
[MemoryPackable]
public partial record CreateOrderCommand(string OrderId) : IRequest<bool>;

// 配置
services.AddCatga()
    .UseMemoryPackSerializer()  // 零配置 AOT
    .AddGeneratedHandlers();
```

### 发布

```bash
dotnet publish -c Release -r win-x64
# 输出: ~8MB, 启动 <50ms
```

---

## 🎯 性能优化清单

### ✅ 必做

- [x] 使用 `AddGeneratedHandlers()` 而不是 `ScanHandlers()`
- [x] 使用 `ShardedIdempotencyStore` 而不是 `MemoryIdempotencyStore`
- [x] 使用 MemoryPack 或配置 JsonSerializerContext

### ⚡ 推荐

- [ ] 启用 `PublishAot=true`
- [ ] 使用 `ValueTask` 而不是 `Task`
- [ ] 避免闭包和装箱
- [ ] 使用对象池复用对象

### 📊 基准测试

```bash
dotnet run -c Release --project benchmarks/Catga.Benchmarks
```

---

## 🔧 常用配置

### 完整配置示例

```csharp
services.AddCatga(options =>
{
    options.DefaultTimeout = TimeSpan.FromSeconds(30);
    options.EnableMetrics = true;
})
.UseInMemoryTransport()
.UseShardedIdempotencyStore(options =>
{
    options.ShardCount = 16;
    options.RetentionPeriod = TimeSpan.FromHours(24);
})
.UseMemoryPackSerializer()
.AddBehavior<LoggingBehavior>()
.AddBehavior<ValidationBehavior>()
.AddBehavior<TransactionBehavior>()
.AddGeneratedHandlers();
```

### ASP.NET Core 集成

```csharp
// Program.cs
builder.Services.AddCatga()
    .UseInMemoryTransport()
    .AddGeneratedHandlers();

// 映射端点
app.MapCatgaEndpoints();  // 自动映射所有 Handler
```

---

## 🐛 常见问题

### Q: Handler 没有被调用？

**检查清单**:
1. ✅ Handler 是否注册？ `AddGeneratedHandlers()` 或 `AddHandler<>`
2. ✅ 消息类型是否匹配？ `IRequest<TResponse>` vs `INotification`
3. ✅ 是否在同一服务容器？

### Q: AOT 发布出现警告？

**解决方案**:
```csharp
// ❌ 避免反射
services.AddCatga().ScanHandlers();

// ✅ 使用源生成器
services.AddCatga().AddGeneratedHandlers();
```

### Q: 性能不如预期？

**优化步骤**:
1. 检查是否启用 Release 模式
2. 使用 `AddGeneratedHandlers()`
3. 使用 MemoryPack 序列化
4. 运行基准测试对比

---

## 📚 文档链接

| 主题 | 文档 |
|------|------|
| 反射优化 | [REFLECTION_OPTIMIZATION_SUMMARY.md](./REFLECTION_OPTIMIZATION_SUMMARY.md) |
| AOT 序列化 | [docs/aot/serialization-aot-guide.md](./docs/aot/serialization-aot-guide.md) |
| AOT 发布 | [docs/deployment/native-aot-publishing.md](./docs/deployment/native-aot-publishing.md) |
| 源生成器 | [docs/guides/source-generator-usage.md](./docs/guides/source-generator-usage.md) |
| 更新日志 | [CHANGELOG-REFLECTION-OPTIMIZATION.md](./CHANGELOG-REFLECTION-OPTIMIZATION.md) |
| 完整文档 | [README.md](./README.md) |

---

## 🎯 性能数据

| 操作 | 延迟 | 吞吐量 | 分配 |
|------|------|--------|------|
| Send Command | ~5ns | 200M ops/s | 0 B |
| Publish Event | ~10ns | 100M ops/s | 0 B |
| RPC Call | ~50ns | 20M ops/s | 32 B |
| Handler 注册 | 0.5ms | - | 0 B |

**Native AOT vs 传统 .NET**:
- 启动时间: **24x 更快**
- 文件大小: **8.5x 更小**
- 内存占用: **7x 更少**

---

## 🌟 推荐阅读顺序

1. **入门**: 本文档 (5分钟)
2. **配置**: [README.md](./README.md) (15分钟)
3. **AOT**: [serialization-aot-guide.md](./docs/aot/serialization-aot-guide.md) (10分钟)
4. **发布**: [native-aot-publishing.md](./docs/deployment/native-aot-publishing.md) (15分钟)
5. **优化**: [REFLECTION_OPTIMIZATION_SUMMARY.md](./REFLECTION_OPTIMIZATION_SUMMARY.md) (10分钟)

**总计**: ~1小时从零到精通

---

## 💡 最佳实践

### DO ✅

```csharp
// 使用 Record
public record CreateUserCommand(string Name) : IRequest<Guid>;

// 使用源生成器
services.AddCatga().AddGeneratedHandlers();

// 使用 MemoryPack
[MemoryPackable]
public partial record UserDto { }

// 返回 CatgaResult
return CatgaResult<Guid>.Success(userId);
```

### DON'T ❌

```csharp
// 不要用反射扫描 (AOT 不兼容)
services.AddCatga().ScanHandlers();

// 不要直接抛异常 (使用 CatgaResult)
throw new Exception("User not found");

// 不要在生产用测试实现
.UseMemoryIdempotencyStore()  // 仅测试用

// 不要忘记处理失败
if (result.IsSuccess) { /* ... */ }
// ❌ 没有处理 IsFailure 的情况
```

---

## 🚀 下一步

1. **基础**: 完成快速开始教程
2. **进阶**: 学习 Pipeline 和 Behavior
3. **分布式**: 配置 NATS 或 Redis
4. **优化**: 启用 Native AOT 发布
5. **监控**: 集成指标和追踪

**开始构建高性能微服务！** 🎉

---

**版本**: Catga v1.0  
**更新**: 2024-10-12  
**更多**: [GitHub](https://github.com/Cricle/Catga) | [文档](./README.md)

