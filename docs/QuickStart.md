# 🚀 Catga - 快速入门指南

欢迎使用 **Catga** - 全球最快、最易用的.NET CQRS框架！

---

## 📦 安装

### NuGet包管理器

```bash
dotnet add package Catga
dotnet add package Catga.Serialization.Json
```

### 源生成器 (推荐)

```bash
dotnet add package Catga.SourceGenerator
dotnet add package Catga.Analyzers
```

---

## ⚡ 最简示例 (1分钟)

### 1. 配置服务 (1行代码！)

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

// ⚡ 添加Catga - 生产就绪！
builder.Services
    .AddCatga()
    .UseProductionDefaults()
    .AddGeneratedHandlers();

var app = builder.Build();
app.Run();
```

### 2. 定义Command

```csharp
// 创建用户命令
public record CreateUserCommand : IRequest<CreateUserResponse>
{
    public string UserName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
}

public record CreateUserResponse
{
    public string UserId { get; init; } = string.Empty;
    public string UserName { get; init; } = string.Empty;
}
```

### 3. 实现Handler

```csharp
// Handler自动注册 - 无需手动配置！
public class CreateUserCommandHandler
    : IRequestHandler<CreateUserCommand, CreateUserResponse>
{
    private readonly ILogger<CreateUserCommandHandler> _logger;

    public CreateUserCommandHandler(ILogger<CreateUserCommandHandler> logger)
    {
        _logger = logger;
    }

    public async Task<CatgaResult<CreateUserResponse>> HandleAsync(
        CreateUserCommand request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Creating user: {UserName}", request.UserName);

        // 业务逻辑
        var userId = Guid.NewGuid().ToString();

        return CatgaResult<CreateUserResponse>.Success(new CreateUserResponse
        {
            UserId = userId,
            UserName = request.UserName
        });
    }
}
```

### 4. 使用Mediator

```csharp
// 在Controller或Minimal API中使用
app.MapPost("/users", async (
    CreateUserCommand command,
    ICatgaMediator mediator) =>
{
    var result = await mediator.SendAsync(command);

    return result.IsSuccess
        ? Results.Ok(result.Data)
        : Results.BadRequest(result.Error);
});
```

**就这样！** 🎉 您已经拥有一个生产就绪的CQRS应用！

---

## 📚 核心概念

### Command (命令)

```csharp
// 有返回值的命令
public record UpdateUserCommand : IRequest<UpdateUserResponse>
{
    public string UserId { get; init; } = string.Empty;
    public string NewName { get; init; } = string.Empty;
}

// 无返回值的命令
public record DeleteUserCommand : IRequest
{
    public string UserId { get; init; } = string.Empty;
}
```

### Query (查询)

```csharp
public record GetUserQuery : IRequest<UserDto>
{
    public string UserId { get; init; } = string.Empty;
}

public record UserDto
{
    public string UserId { get; init; } = string.Empty;
    public string UserName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
}
```

### Event (事件)

```csharp
// 定义事件
public record UserCreatedEvent : IEvent
{
    public string UserId { get; init; } = string.Empty;
    public string UserName { get; init; } = string.Empty;
}

// 事件处理器 (可以有多个!)
public class UserCreatedEventHandler : IEventHandler<UserCreatedEvent>
{
    public async Task HandleAsync(
        UserCreatedEvent @event,
        CancellationToken cancellationToken = default)
    {
        // 处理事件 (例如: 发送欢迎邮件)
        await SendWelcomeEmailAsync(@event.UserName);
    }
}

// 发布事件
await _mediator.PublishAsync(new UserCreatedEvent
{
    UserId = userId,
    UserName = userName
});
```

---

## 🎨 配置选项

### Fluent API配置

```csharp
builder.Services
    .AddCatga()
    .WithLogging()                                  // 启用日志
    .WithCircuitBreaker(                            // 熔断器
        failureThreshold: 5,
        resetTimeoutSeconds: 30)
    .WithRateLimiting(                              // 限流
        requestsPerSecond: 1000,
        burstCapacity: 100)
    .WithConcurrencyLimit(100)                      // 并发限制
    .ValidateConfiguration()                         // 配置验证
    .AddGeneratedHandlers();
```

### 预设配置

```csharp
// 开发环境 (无限制，易调试)
builder.Services.AddCatga()
    .UseDevelopmentDefaults()
    .AddGeneratedHandlers();

// 生产环境 (稳定配置)
builder.Services.AddCatga()
    .UseProductionDefaults()
    .AddGeneratedHandlers();

// 高性能 (极致性能)
builder.Services.AddCatga(SmartDefaults.GetHighPerformanceDefaults())
    .AddGeneratedHandlers();

// 保守配置 (最大稳定性)
builder.Services.AddCatga(SmartDefaults.GetConservativeDefaults())
    .AddGeneratedHandlers();

// 自动调优 (根据CPU/内存自动配置)
builder.Services.AddCatga(SmartDefaults.AutoTune())
    .AddGeneratedHandlers();
```

---

## 🔧 高级特性

### 1. Pipeline Behaviors (管道行为)

```csharp
// 自定义行为
public class LoggingBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly ILogger _logger;

    public async ValueTask<CatgaResult<TResponse>> HandleAsync(
        TRequest request,
        PipelineDelegate<TResponse> next,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Handling {RequestType}", typeof(TRequest).Name);

        var result = await next();

        _logger.LogInformation("Handled {RequestType}: {Success}",
            typeof(TRequest).Name, result.IsSuccess);

        return result;
    }
}

// 注册行为
builder.Services.AddScoped(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
```

### 2. 验证

```csharp
// 使用内置ValidationBehavior
public class CreateUserValidator : IValidator<CreateUserCommand>
{
    public Task<IEnumerable<string>> ValidateAsync(
        CreateUserCommand request,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(request.UserName))
            errors.Add("UserName is required");

        if (string.IsNullOrWhiteSpace(request.Email))
            errors.Add("Email is required");

        return Task.FromResult<IEnumerable<string>>(errors);
    }
}

// 自动应用验证 (通过ValidationBehavior)
```

### 3. 分布式消息 (NATS)

```csharp
// 添加NATS传输
builder.Services.AddNatsTransport(options =>
{
    options.Url = "nats://localhost:4222";
    options.SubjectPrefix = "myapp.";
});

// Handler自动支持分布式！
```

### 4. 消息持久化 (Redis Outbox/Inbox)

```csharp
// 添加Redis持久化
builder.Services.AddRedisPersistence(options =>
{
    options.ConnectionString = "localhost:6379";
});

// 启用Outbox模式 (确保消息至少发送一次)
builder.Services.AddOutbox();

// 启用Inbox模式 (确保消息只处理一次)
builder.Services.AddInbox();
```

---

## 🚀 性能优化

### 批量处理

```csharp
// 批量发送 (50x更快!)
var commands = Enumerable.Range(1, 1000)
    .Select(i => new CreateUserCommand { UserName = $"User{i}" })
    .ToList();

// 使用批量传输 (如果已配置)
var transport = serviceProvider.GetService<IBatchMessageTransport>();
if (transport != null)
{
    await transport.SendBatchAsync(commands);
}
```

### 消息压缩

```csharp
// 启用压缩 (节省70%带宽)
var compressor = new MessageCompressor(CompressionAlgorithm.Brotli);
var compressed = await compressor.CompressAsync(messageBytes);

// 传输层自动支持压缩
```

---

## 📊 监控与观测

### OpenTelemetry集成

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource("Catga")                  // Catga追踪
        .AddAspNetCoreInstrumentation())
    .WithMetrics(metrics => metrics
        .AddMeter("Catga"));                 // Catga指标
```

### 健康检查

```csharp
builder.Services.AddCatgaHealthChecks();

app.MapHealthChecks("/health");
```

---

## 🎯 最佳实践

### ✅ DO (推荐)

```csharp
// ✅ 使用Record类型 (不可变)
public record CreateUserCommand : IRequest<CreateUserResponse>
{
    public string UserName { get; init; } = string.Empty;
}

// ✅ Handler使用async/await
public async Task<CatgaResult<TResponse>> HandleAsync(...)
{
    var result = await _repository.SaveAsync(...);
    return CatgaResult<TResponse>.Success(result);
}

// ✅ 使用CancellationToken
public async Task HandleAsync(
    TRequest request,
    CancellationToken cancellationToken = default)
{
    await _service.DoWorkAsync(cancellationToken);
}

// ✅ 使用源生成器 (自动注册)
builder.Services.AddGeneratedHandlers();
```

### ❌ DON'T (避免)

```csharp
// ❌ 不要阻塞调用
var result = _mediator.SendAsync(command).Result; // 会被分析器检测!

// ❌ 不要在Handler中直接访问HttpContext
public class MyHandler : IRequestHandler<...>
{
    private readonly IHttpContextAccessor _httpContext; // 不推荐
}

// ❌ 不要在事件处理器中抛异常
public async Task HandleAsync(MyEvent @event, ...)
{
    throw new Exception(); // 会中断其他事件处理器!
    // 应该: 记录错误但不抛异常
}
```

---

## 🐛 故障排查

### 问题: Handler未被调用

**原因**: Handler未注册

**解决方案**:
```csharp
// 确保调用了AddGeneratedHandlers()
builder.Services.AddCatga()
    .AddGeneratedHandlers(); // ← 必须!
```

### 问题: AOT编译警告

**原因**: 使用了反射

**解决方案**:
```csharp
// 使用源生成器代替反射
// ✅ 好
builder.Services.AddGeneratedHandlers();

// ❌ 差 (会有AOT警告)
builder.Services.Scan(scan => scan.FromAssemblies(...));
```

### 问题: 内存持续增长

**原因**: 未释放资源

**解决方案**:
```csharp
// 确保Handler实现了IDisposable (如果需要)
public class MyHandler : IRequestHandler<...>, IDisposable
{
    public void Dispose()
    {
        _resource?.Dispose();
    }
}

// 或使用Scoped生命周期
builder.Services.AddScoped<MyService>();
```

---

## 📚 下一步

- 📖 [架构指南](Architecture.md) - 深入理解Catga架构
- ⚡ [性能调优](PerformanceTuning.md) - 极致性能优化
- 🎯 [最佳实践](BestPractices.md) - 生产级应用指南
- 🔄 [迁移指南](Migration.md) - 从MediatR迁移

---

## 💬 获得帮助

- 📝 [GitHub Issues](https://github.com/YourOrg/Catga/issues)
- 💬 [Discussions](https://github.com/YourOrg/Catga/discussions)
- 📧 Email: support@catga.dev

---

**Catga - 让CQRS飞起来！** 🚀

