# Catga 用户体验提升计划

## 🎯 目标
提升 Catga 框架的易用性、可发现性和开发体验

---

## 📊 当前痛点分析

### 1. **序列化器配置不清晰** 🔴 高优先级
**问题**:
- 用户必须显式注册 `IMessageSerializer`，但文档/错误提示不明确
- 缺少友好的错误消息
- 没有默认实现或自动检测

**影响**: 新用户第一次使用会遇到运行时错误

### 2. **扩展方法分散** 🟡 中优先级
**问题**:
- `AddCatga()`, `AddNatsTransport()`, `AddRedisDistributedCache()` 等方法分散在不同命名空间
- 用户需要记住多个 using 语句
- 没有统一的 Fluent API

**影响**: 配置代码冗长，不够直观

### 3. **缺少快速启动模板** 🟡 中优先级
**问题**:
- 没有 `dotnet new` 模板
- 示例项目过于复杂（Aspire）
- 缺少简单的 QuickStart 示例

**影响**: 学习曲线陡峭

### 4. **错误消息不友好** 🟡 中优先级
**问题**:
- 缺少序列化器时的错误消息不明确
- 没有配置验证
- 缺少诊断工具

**影响**: 调试困难

### 5. **配置选项过于复杂** 🟢 低优先级
**问题**:
- `CatgaOptions` 有很多属性
- 缺少预设配置（Development/Production）
- 没有配置验证

**影响**: 用户不知道如何正确配置

---

## 🚀 改进方案

### Phase 1: 序列化器体验优化 (P0 - 立即执行)

#### 1.1 添加友好的启动检查
```csharp
// src/Catga.InMemory/DependencyInjection/TransitServiceCollectionExtensions.cs
public static IServiceCollection AddCatga(this IServiceCollection services, Action<CatgaOptions>? configureOptions = null)
{
    // ... existing code ...

    // Add startup validation
    services.AddHostedService<CatgaStartupValidator>();

    return services;
}

// New: CatgaStartupValidator.cs
public class CatgaStartupValidator : IHostedService
{
    private readonly IServiceProvider _serviceProvider;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Check if IMessageSerializer is registered
        var serializer = _serviceProvider.GetService<IMessageSerializer>();
        if (serializer == null)
        {
            throw new CatgaConfigurationException(
                "IMessageSerializer is not registered. " +
                "Please add one of the following packages and register it:\n" +
                "  - Catga.Serialization.MemoryPack (recommended for AOT)\n" +
                "  - Catga.Serialization.Json\n\n" +
                "Example:\n" +
                "  services.AddSingleton<IMessageSerializer, MemoryPackMessageSerializer>();\n\n" +
                "Or use the convenience method:\n" +
                "  services.AddCatga(options => options.UseMemoryPack());");
        }
        return Task.CompletedTask;
    }
}
```

#### 1.2 添加便捷扩展方法
```csharp
// New: src/Catga.Serialization.MemoryPack/DependencyInjection/MemoryPackSerializerExtensions.cs
public static class MemoryPackSerializerExtensions
{
    /// <summary>
    /// Use MemoryPack serializer (recommended for Native AOT)
    /// </summary>
    public static IServiceCollection UseMemoryPackSerializer(this IServiceCollection services)
    {
        services.TryAddSingleton<IMessageSerializer, MemoryPackMessageSerializer>();
        return services;
    }
}

// New: src/Catga.Serialization.Json/DependencyInjection/JsonSerializerExtensions.cs
public static class JsonSerializerExtensions
{
    /// <summary>
    /// Use JSON serializer (requires JsonSerializerContext for AOT)
    /// </summary>
    public static IServiceCollection UseJsonSerializer(
        this IServiceCollection services,
        JsonSerializerOptions? options = null)
    {
        services.TryAddSingleton<IMessageSerializer>(sp =>
            new JsonMessageSerializer(options ?? new JsonSerializerOptions()));
        return services;
    }
}
```

#### 1.3 更新 CatgaOptions
```csharp
public class CatgaOptions
{
    // ... existing properties ...

    /// <summary>
    /// Use MemoryPack serializer (recommended for Native AOT)
    /// </summary>
    public CatgaOptions UseMemoryPack()
    {
        // Marker method - actual registration happens in AddCatga
        _serializerType = SerializerType.MemoryPack;
        return this;
    }

    /// <summary>
    /// Use JSON serializer
    /// </summary>
    public CatgaOptions UseJson(JsonSerializerOptions? options = null)
    {
        _serializerType = SerializerType.Json;
        _jsonOptions = options;
        return this;
    }
}
```

---

### Phase 2: 统一 Fluent API (P1 - 高优先级)

#### 2.1 创建统一的 Builder
```csharp
// New: src/Catga/DependencyInjection/CatgaServiceBuilder.cs
public class CatgaServiceBuilder
{
    private readonly IServiceCollection _services;
    private readonly CatgaOptions _options;

    public CatgaServiceBuilder(IServiceCollection services, CatgaOptions options)
    {
        _services = services;
        _options = options;
    }

    /// <summary>
    /// Use MemoryPack serializer (recommended for Native AOT)
    /// </summary>
    public CatgaServiceBuilder UseMemoryPack()
    {
        _services.UseMemoryPackSerializer();
        return this;
    }

    /// <summary>
    /// Use JSON serializer
    /// </summary>
    public CatgaServiceBuilder UseJson(JsonSerializerOptions? options = null)
    {
        _services.UseJsonSerializer(options);
        return this;
    }

    /// <summary>
    /// Add NATS transport
    /// </summary>
    public CatgaServiceBuilder AddNatsTransport(Action<NatsTransportOptions>? configure = null)
    {
        _services.AddNatsTransport(configure);
        return this;
    }

    /// <summary>
    /// Add Redis persistence
    /// </summary>
    public CatgaServiceBuilder AddRedisPersistence(Action<RedisOptions>? configure = null)
    {
        _services.AddRedisPersistence(configure);
        return this;
    }

    /// <summary>
    /// Add Redis distributed cache
    /// </summary>
    public CatgaServiceBuilder AddRedisCache(Action<RedisOptions>? configure = null)
    {
        _services.AddRedisDistributedCache();
        return this;
    }

    /// <summary>
    /// Configure for development environment
    /// </summary>
    public CatgaServiceBuilder ForDevelopment()
    {
        _options.ForDevelopment();
        return this;
    }

    /// <summary>
    /// Configure for production environment
    /// </summary>
    public CatgaServiceBuilder ForProduction()
    {
        _options.EnableLogging = true;
        _options.EnableTracing = true;
        _options.EnableIdempotency = true;
        _options.EnableRetry = true;
        return this;
    }

    /// <summary>
    /// Configure for high performance scenarios
    /// </summary>
    public CatgaServiceBuilder ForHighPerformance()
    {
        _options.WithHighPerformance();
        return this;
    }
}

// Update AddCatga to return builder
public static CatgaServiceBuilder AddCatga(
    this IServiceCollection services,
    Action<CatgaOptions>? configureOptions = null)
{
    var options = new CatgaOptions();
    configureOptions?.Invoke(options);

    services.AddSingleton(options);
    services.TryAddSingleton<ICatgaMediator, CatgaMediator>();
    // ... rest of registration ...

    return new CatgaServiceBuilder(services, options);
}
```

#### 2.2 新的用户体验
```csharp
// Before (verbose, scattered)
services.AddSingleton<IMessageSerializer, MemoryPackMessageSerializer>();
services.AddCatga(options => options.EnableRetry = true);
services.AddNatsTransport();
services.AddRedisDistributedCache();

// After (fluent, intuitive)
services.AddCatga()
    .UseMemoryPack()
    .AddNatsTransport()
    .AddRedisCache()
    .ForProduction();
```

---

### Phase 3: 快速启动模板 (P1 - 高优先级)

#### 3.1 创建 dotnet new 模板
```bash
# templates/catga-quickstart/
├── Catga.QuickStart.csproj
├── Program.cs
├── Messages.cs
├── Handlers.cs
└── .template.config/
    └── template.json
```

```json
// .template.config/template.json
{
  "name": "Catga QuickStart",
  "identity": "Catga.Templates.QuickStart",
  "shortName": "catga",
  "tags": {
    "language": "C#",
    "type": "project"
  },
  "sourceName": "Catga.QuickStart",
  "preferNameDirectory": true
}
```

```csharp
// Program.cs (template)
var builder = WebApplication.CreateBuilder(args);

// Configure Catga with MemoryPack (AOT-friendly)
builder.Services.AddCatga()
    .UseMemoryPack()
    .ForDevelopment();

// Register handlers (use source generator in production)
builder.Services.AddTransient<IRequestHandler<CreateOrder, OrderResult>, CreateOrderHandler>();

var app = builder.Build();

// Example endpoint
app.MapPost("/orders", async (CreateOrder command, ICatgaMediator mediator) =>
{
    var result = await mediator.SendAsync(command);
    return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
});

app.Run();

// Messages
[MemoryPackable]
public partial record CreateOrder(string OrderId, decimal Amount) : IRequest<OrderResult>;

[MemoryPackable]
public partial record OrderResult(string OrderId, bool Success);

// Handler
public class CreateOrderHandler : IRequestHandler<CreateOrder, OrderResult>
{
    public async ValueTask<CatgaResult<OrderResult>> HandleAsync(
        CreateOrder request,
        CancellationToken cancellationToken = default)
    {
        // TODO: Add your business logic here
        var result = new OrderResult(request.OrderId, Success: true);
        return CatgaResult<OrderResult>.Success(result);
    }
}
```

#### 3.2 安装命令
```bash
dotnet new install Catga.Templates
dotnet new catga -n MyProject
cd MyProject
dotnet run
```

---

### Phase 4: 改进错误消息 (P2 - 中优先级)

#### 4.1 自定义异常
```csharp
// New: src/Catga/Core/CatgaConfigurationException.cs
public class CatgaConfigurationException : CatgaException
{
    public CatgaConfigurationException(string message) : base(message) { }

    public static CatgaConfigurationException SerializerNotRegistered()
    {
        return new CatgaConfigurationException(
            "IMessageSerializer is not registered.\n\n" +
            "Quick Fix:\n" +
            "  services.AddCatga().UseMemoryPack();\n\n" +
            "Or manually:\n" +
            "  services.AddSingleton<IMessageSerializer, MemoryPackMessageSerializer>();\n\n" +
            "Available serializers:\n" +
            "  - MemoryPack (recommended for AOT): Catga.Serialization.MemoryPack\n" +
            "  - JSON: Catga.Serialization.Json\n\n" +
            "See: https://github.com/catga/docs/serialization");
    }
}
```

#### 4.2 运行时验证
```csharp
// In CatgaMediator
public async ValueTask<CatgaResult<TResponse>> SendAsync<TRequest, TResponse>(
    TRequest request,
    CancellationToken cancellationToken = default)
    where TRequest : class, IRequest<TResponse>
{
    // Validate serializer if using distributed features
    if (_transport != null && _serializer == null)
    {
        throw CatgaConfigurationException.SerializerNotRegistered();
    }

    // ... rest of logic ...
}
```

---

### Phase 5: 诊断工具 (P2 - 中优先级)

#### 5.1 健康检查
```csharp
// New: src/Catga/Diagnostics/CatgaHealthCheck.cs
public class CatgaHealthCheck : IHealthCheck
{
    private readonly IServiceProvider _serviceProvider;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var data = new Dictionary<string, object>();

        // Check serializer
        var serializer = _serviceProvider.GetService<IMessageSerializer>();
        data["Serializer"] = serializer?.Name ?? "Not Registered";

        // Check transport
        var transport = _serviceProvider.GetService<IMessageTransport>();
        data["Transport"] = transport?.Name ?? "In-Memory";

        // Check mediator
        var mediator = _serviceProvider.GetService<ICatgaMediator>();
        data["Mediator"] = mediator != null ? "Registered" : "Not Registered";

        return HealthCheckResult.Healthy("Catga is healthy", data);
    }
}

// Extension
public static IHealthChecksBuilder AddCatgaHealthCheck(this IHealthChecksBuilder builder)
{
    return builder.AddCheck<CatgaHealthCheck>("catga");
}
```

#### 5.2 配置诊断端点
```csharp
app.MapGet("/catga/diagnostics", (IServiceProvider sp) =>
{
    var serializer = sp.GetService<IMessageSerializer>();
    var transport = sp.GetService<IMessageTransport>();
    var options = sp.GetRequiredService<CatgaOptions>();

    return new
    {
        Serializer = serializer?.Name ?? "⚠️ Not Registered",
        Transport = transport?.Name ?? "In-Memory",
        Configuration = new
        {
            options.EnableLogging,
            options.EnableTracing,
            options.EnableRetry,
            options.EnableIdempotency,
            options.DefaultQoS
        }
    };
});
```

---

## 📈 预期收益

| 改进项 | 当前体验 | 改进后体验 | 提升 |
|--------|---------|-----------|------|
| **首次配置时间** | 15-30分钟 | 2-5分钟 | ✅ 80% |
| **错误定位时间** | 10-20分钟 | 1-2分钟 | ✅ 90% |
| **代码行数** | 10-15行 | 3-5行 | ✅ 60% |
| **学习曲线** | 陡峭 | 平缓 | ✅ 70% |
| **文档查阅次数** | 5-10次 | 1-2次 | ✅ 80% |

---

## 🎯 实施优先级

### 立即执行 (本周)
1. ✅ **序列化器启动检查** - 防止运行时错误
2. ✅ **便捷扩展方法** - UseMemoryPack() / UseJson()
3. ✅ **统一 Fluent API** - CatgaServiceBuilder

### 短期 (2周内)
4. ⏳ **QuickStart 模板** - dotnet new catga
5. ⏳ **改进错误消息** - 友好的异常信息
6. ⏳ **更新文档** - 反映新的 API

### 中期 (1个月内)
7. ⏳ **诊断工具** - Health Check + Diagnostics endpoint
8. ⏳ **配置验证** - Startup validation
9. ⏳ **更多示例** - 不同场景的示例项目

---

## 🚀 下一步行动

**立即开始 Phase 1**:
1. 创建 `CatgaStartupValidator`
2. 添加 `UseMemoryPack()` / `UseJson()` 扩展方法
3. 创建 `CatgaServiceBuilder`
4. 更新 README 示例

**预计完成时间**: 2-3小时
**预期影响**: 新用户体验提升 80%

---

## 📝 示例对比

### Before (当前)
```csharp
// 用户需要知道很多细节
using Catga.DependencyInjection;
using Catga.Serialization;
using Catga.Serialization.MemoryPack;
using Catga.Transport.Nats;
using Catga.Persistence.Redis;

var builder = WebApplication.CreateBuilder(args);

// 分散的配置，容易遗漏
services.AddSingleton<IMessageSerializer, MemoryPackMessageSerializer>();
services.AddCatga(options => {
    options.EnableRetry = true;
    options.EnableIdempotency = true;
});
services.AddNatsTransport(options => options.SubjectPrefix = "myapp");
services.AddRedisDistributedCache();
```

### After (改进后)
```csharp
// 简洁、直观、不易出错
using Catga;

var builder = WebApplication.CreateBuilder(args);

// 一行代码，完整配置
services.AddCatga()
    .UseMemoryPack()
    .AddNatsTransport()
    .AddRedisCache()
    .ForProduction();
```

**代码减少**: 15行 → 5行 (67% ↓)
**using 语句**: 5个 → 1个 (80% ↓)
**出错可能**: 高 → 低 (90% ↓)

