# Catga 框架快速入门指南

> 5 分钟上手高性能 .NET 9+ CQRS 框架

---

## 🚀 快速开始

### 1. 使用项目模板（推荐）

```bash
# 安装模板包（开发中，暂不可用）
dotnet new install Catga.Templates

# 创建新项目
dotnet new catga-api -n MyAwesomeApi
cd MyAwesomeApi
dotnet run
```

### 2. 手动创建项目

```bash
# 创建项目
dotnet new webapi -n MyApi
cd MyApi

# 添加 Catga 包
dotnet add package Catga
dotnet add package Catga.SourceGenerator
dotnet add package Catga.Analyzers

# 运行
dotnet run
```

---

## 📦 核心包说明

| 包 | 用途 | 必需 |
|---|------|------|
| `Catga` | 核心框架 | ✅ |
| `Catga.SourceGenerator` | 代码生成器 | 推荐 |
| `Catga.Analyzers` | 静态分析 | 推荐 |
| `Catga.DistributedId` | 分布式 ID | 可选 |

---

## ⚡ 核心概念

### 1. 定义 Command

```csharp
public record CreateUserCommand(string Name, string Email) 
    : IRequest<CreateUserResponse>;

public record CreateUserResponse(long UserId, string Name);
```

### 2. 实现 Handler

```csharp
public class CreateUserHandler 
    : IRequestHandler<CreateUserCommand, CreateUserResponse>
{
    public async ValueTask<CreateUserResponse> Handle(
        CreateUserCommand request,
        CancellationToken cancellationToken)
    {
        // 业务逻辑
        var userId = GenerateUserId();
        
        return new CreateUserResponse(userId, request.Name);
    }
}
```

### 3. 注册服务

```csharp
var builder = WebApplication.CreateBuilder(args);

// 注册 Catga
builder.Services.AddCatgaMediator(options =>
{
    options.ScanHandlers = true;
    options.EnableSourceGenerator = true;
});

var app = builder.Build();
app.Run();
```

### 4. 发送请求

```csharp
app.MapPost("/users", async (
    ICatgaMediator mediator,
    CreateUserCommand command) =>
{
    var response = await mediator.SendAsync(command);
    return Results.Ok(response);
});
```

---

## 🎯 常用功能

### 分布式 ID 生成

```csharp
// 注册服务
builder.Services.AddSnowflakeId(options =>
{
    options.WorkerId = 1;
    options.DataCenterId = 1;
});

// 使用
app.MapGet("/next-id", (ISnowflakeIdGenerator idGen) =>
{
    return idGen.NextId();
});
```

### Pipeline 行为

```csharp
public class ValidationBehavior<TRequest, TResponse> 
    : IPipelineBehavior<TRequest, TResponse>
{
    public async ValueTask<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        // 前置处理
        Validate(request);
        
        // 调用下一个
        var response = await next();
        
        // 后置处理
        return response;
    }
}

// 注册
builder.Services.AddCatgaPipelineBehavior<ValidationBehavior<,>>();
```

### 弹性组件

```csharp
// 熔断器
builder.Services.AddCircuitBreaker(options =>
{
    options.FailureThreshold = 5;
    options.SuccessThreshold = 2;
    options.Timeout = TimeSpan.FromSeconds(30);
});

// 并发限制
builder.Services.AddConcurrencyLimiter(options =>
{
    options.MaxConcurrency = 100;
});

// 使用
app.MapGet("/resilient", async (
    ICircuitBreaker breaker,
    IConcurrencyLimiter limiter) =>
{
    return await limiter.ExecuteAsync(async () =>
        await breaker.ExecuteAsync(async () =>
        {
            // 受保护的逻辑
            return "OK";
        })
    );
});
```

---

## 📊 可观测性

### 监控指标

```csharp
// 注册
builder.Services.AddSingleton<CatgaMetrics>();

// 使用
app.MapGet("/metrics", (CatgaMetrics metrics) =>
{
    var snapshot = metrics.GetSnapshot();
    return Results.Ok(snapshot);
});
```

### 日志追踪

```csharp
builder.Services.AddCatgaPipelineBehavior<LoggingBehavior<,>>();
builder.Services.AddCatgaPipelineBehavior<TracingBehavior<,>>();
```

---

## 🔧 配置选项

### CatgaOptions

```csharp
builder.Services.AddCatgaMediator(options =>
{
    // 自动扫描 Handler
    options.ScanHandlers = true;
    
    // 启用源生成器
    options.EnableSourceGenerator = true;
    
    // 配置超时
    options.DefaultTimeout = TimeSpan.FromSeconds(30);
    
    // 配置并发
    options.MaxConcurrency = 1000;
});
```

### SnowflakeIdOptions

```csharp
builder.Services.AddSnowflakeId(options =>
{
    // 工作节点 ID (0-31)
    options.WorkerId = 1;
    
    // 数据中心 ID (0-31)
    options.DataCenterId = 1;
    
    // 自定义 Epoch (可选)
    options.Epoch = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    
    // 自定义位布局 (可选)
    options.Layout = new BitLayout
    {
        TimestampBits = 41,
        DataCenterIdBits = 5,
        WorkerIdBits = 5,
        SequenceBits = 12
    };
});
```

---

## 🎨 源生成器

### MessageContract 生成

```csharp
[GenerateMessageContract]
public partial record CreateUserCommand
{
    [Required]
    [MaxLength(100)]
    public string Name { get; init; }
    
    [Required]
    [EmailAddress]
    public string Email { get; init; }
}

// 自动生成:
// - Validate() 方法
// - ToString() 方法
// - JSON 序列化支持
```

### ConfigurationValidator 生成

```csharp
[GenerateConfigurationValidator]
public partial class AppSettings
{
    [Required]
    [Range(1, 100)]
    public int WorkerId { get; set; }
    
    [Required]
    [Url]
    public string ApiEndpoint { get; set; }
}

// 自动生成启动验证
```

---

## 🔍 静态分析器

### 35 个规则自动检查

```csharp
// ❌ CATGA101: ToArray() in hot path
public void Process(List<int> items)
{
    var array = items.ToArray(); // 警告
}

// ✅ 修复
public void Process(List<int> items)
{
    var span = CollectionsMarshal.AsSpan(items);
}

// ❌ CATGA201: Non-thread-safe collection
private readonly List<int> _items = new(); // 警告

// ✅ 修复
private readonly ConcurrentBag<int> _items = new();
```

---

## 📚 示例项目

### 最小 API

```csharp
var builder = WebApplication.CreateBuilder(args);

// 注册 Catga
builder.Services.AddCatgaMediator();

var app = builder.Build();

// 定义端点
app.MapPost("/api/users", async (
    ICatgaMediator mediator,
    CreateUserCommand command) =>
{
    var result = await mediator.SendAsync(command);
    return Results.Created($"/api/users/{result.UserId}", result);
});

app.Run();
```

### 完整示例

参考 `examples/SimpleWebApi/` 目录

---

## 🚀 性能特性

### 零 GC 热路径

- ✅ `ValueTask` 返回类型
- ✅ `Span<T>` 和 `Memory<T>`
- ✅ `ArrayPool<T>` 复用
- ✅ 无装箱操作

### 100% 无锁并发

- ✅ `Interlocked` 原子操作
- ✅ `Volatile.Read/Write`
- ✅ Lock-free 数据结构

### 百万级 TPS

- ✅ 热路径优化
- ✅ 内联方法
- ✅ 缓存优化

---

## 🎯 最佳实践

### 1. Handler 设计

```csharp
// ✅ 推荐：轻量级 record
public record MyCommand(string Data) : IRequest<MyResponse>;

// ❌ 避免：复杂的可变类
public class MyCommand : IRequest<MyResponse>
{
    public string Data { get; set; }
    public List<object> Items { get; set; }
}
```

### 2. 异步操作

```csharp
// ✅ 推荐：真正的异步
public async ValueTask<Response> Handle(Request request, CancellationToken ct)
{
    var data = await _repository.GetAsync(request.Id, ct);
    return new Response(data);
}

// ❌ 避免：假异步
public async ValueTask<Response> Handle(Request request, CancellationToken ct)
{
    return await Task.FromResult(new Response());
}
```

### 3. 取消令牌

```csharp
// ✅ 推荐：传递取消令牌
public async ValueTask<Response> Handle(Request request, CancellationToken ct)
{
    ct.ThrowIfCancellationRequested();
    var data = await _service.ProcessAsync(request, ct);
    return new Response(data);
}
```

### 4. 异常处理

```csharp
// ✅ 推荐：特定异常
public async ValueTask<Response> Handle(Request request, CancellationToken ct)
{
    var user = await _repo.FindAsync(request.UserId, ct)
        ?? throw new UserNotFoundException(request.UserId);
    return new Response(user);
}
```

---

## 🐛 常见问题

### Q: Handler 未被发现？

**A**: 确保启用了自动扫描
```csharp
builder.Services.AddCatgaMediator(options =>
{
    options.ScanHandlers = true; // ← 确保启用
});
```

### Q: 源生成器不工作？

**A**: 检查项目配置
```xml
<PropertyGroup>
    <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
</PropertyGroup>

<ItemGroup>
    <PackageReference Include="Catga.SourceGenerator" OutputItemType="Analyzer" />
</ItemGroup>
```

### Q: AOT 发布失败？

**A**: 检查警告并修复
```bash
dotnet publish -c Release /p:PublishAot=true
```

---

## 📖 进阶主题

### 分布式系统

- [Outbox/Inbox 模式](docs/patterns/outbox-inbox.md)
- [分布式 ID](docs/distributed-id.md)
- [事件溯源](docs/event-sourcing.md)

### 性能优化

- [零 GC 设计](docs/performance/zero-gc.md)
- [无锁并发](docs/performance/lock-free.md)
- [基准测试](benchmarks/README.md)

### AOT 部署

- [Native AOT 指南](docs/aot/native-aot.md)
- [Trim 兼容性](docs/aot/trimming.md)

---

## 🔗 有用链接

- **文档**: `docs/` 目录
- **示例**: `examples/` 目录
- **基准测试**: `benchmarks/` 目录
- **健康报告**: `PROJECT_HEALTH_REPORT.md`
- **优化路线图**: `OPTIMIZATION_ROADMAP_2025_10_09.md`

---

## 💡 小贴士

### 开发时

1. ✅ 使用源生成器减少样板代码
2. ✅ 启用所有分析器规则
3. ✅ 查看 IDE 中的实时提示
4. ✅ 参考示例项目

### 性能调优

1. ✅ 查看 `CatgaMetrics` 指标
2. ✅ 运行基准测试对比
3. ✅ 使用 `dotnet-counters` 监控
4. ✅ 遵循分析器建议

### 生产部署

1. ✅ 启用可观测性
2. ✅ 配置熔断器
3. ✅ 设置并发限制
4. ✅ 监控关键指标

---

## 🎉 开始构建

现在您已经准备好使用 Catga 构建高性能应用了！

```bash
# 创建您的第一个项目
dotnet new catga-api -n MyFirstApp
cd MyFirstApp
dotnet run

# 访问
curl http://localhost:5000/api/sample
```

**享受编码的乐趣！** 🚀

---

**需要帮助？**
- 查看 `docs/` 文档
- 参考 `examples/` 示例
- 阅读健康报告了解最佳实践

