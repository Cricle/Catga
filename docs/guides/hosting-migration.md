# 托管服务迁移指南

本指南帮助您从旧的手动生命周期管理 API 迁移到基于 Microsoft.Extensions.Hosting 的新托管服务 API。

## 概述

Catga 框架已经重构为完全利用 Microsoft.Extensions.Hosting 的生命周期管理。这带来了以下好处：

- **标准化生命周期管理**：与 ASP.NET Core 和其他 .NET 应用程序使用相同的托管服务模式
- **自动恢复**：内置的健康检查和自动恢复机制
- **优雅停机**：自动处理应用程序停机时的资源清理
- **健康检查集成**：与 ASP.NET Core 健康检查无缝集成
- **简化配置**：通过链式 API 简化服务配置

## 破坏性变更

### 移除的类和接口

以下类和接口已被移除，因为它们的功能已被托管服务替代：

- `GracefulShutdownCoordinator` - 由 `TransportHostedService` 和 `IHostApplicationLifetime` 替代
- `GracefulRecoveryManager` - 由 `RecoveryHostedService` 替代
- 手动生命周期管理方法 - 由托管服务自动管理

### 新增的托管服务

- `RecoveryHostedService` - 自动健康检查和恢复
- `TransportHostedService` - 传输层生命周期管理
- `OutboxProcessorService` - Outbox 模式消息处理

### 新增的健康检查

- `TransportHealthCheck` - 传输层健康检查
- `PersistenceHealthCheck` - 持久化层健康检查
- `RecoveryHealthCheck` - 恢复服务健康检查

## 迁移步骤

### 步骤 1: 更新服务注册

**之前 (旧 API):**

```csharp
var builder = WebApplication.CreateBuilder(args);

// 手动注册各个组件
builder.Services.AddSingleton<IMessageTransport, NatsMessageTransport>();
builder.Services.AddSingleton<IEventStore, NatsJSEventStore>();
builder.Services.AddSingleton<IOutboxStore, NatsJSOutboxStore>();

// 手动管理生命周期
var app = builder.Build();

// 手动初始化
var transport = app.Services.GetRequiredService<IMessageTransport>();
await transport.InitializeAsync();

app.Run();
```

**之后 (新 API):**

```csharp
var builder = WebApplication.CreateBuilder(args);

// 使用 Catga 扩展方法注册所有服务
builder.Services.AddCatga()
    .AddNatsTransport(options => { /* 配置 */ })
    .AddNatsPersistence(options => { /* 配置 */ })
    .AddHostedServices(options =>
    {
        options.EnableAutoRecovery = true;
        options.EnableTransportHosting = true;
        options.EnableOutboxProcessor = true;
    });

var app = builder.Build();

// 托管服务自动管理生命周期
app.Run();
```

### 步骤 2: 移除手动生命周期管理代码

**之前 (旧 API):**

```csharp
// 手动停机处理
app.Lifetime.ApplicationStopping.Register(() =>
{
    var transport = app.Services.GetRequiredService<IMessageTransport>();
    transport.StopAcceptingMessages();
    transport.WaitForCompletionAsync().Wait();
});

// 手动恢复逻辑
var recoveryManager = new GracefulRecoveryManager(/* ... */);
await recoveryManager.StartAsync();
```

**之后 (新 API):**

```csharp
// 托管服务自动处理停机和恢复
// 无需手动代码
```

### 步骤 3: 添加健康检查

**之前 (旧 API):**

```csharp
// 手动实现健康检查
builder.Services.AddHealthChecks()
    .AddCheck("transport", () =>
    {
        var transport = /* 获取 transport */;
        return transport.IsHealthy 
            ? HealthCheckResult.Healthy() 
            : HealthCheckResult.Unhealthy();
    });
```

**之后 (新 API):**

```csharp
// 使用内置健康检查
builder.Services.AddCatga()
    .AddNatsTransport()
    .AddNatsPersistence()
    .AddHostedServices();

// 添加 Catga 健康检查
builder.Services.AddHealthChecks()
    .AddCatgaHealthChecks();

var app = builder.Build();

// 映射健康检查端点
app.MapHealthChecks("/health");
```

### 步骤 4: 配置托管服务选项

**之前 (旧 API):**

```csharp
// 分散的配置
var recoveryOptions = new RecoveryOptions
{
    MaxRetries = 3,
    RetryDelay = TimeSpan.FromSeconds(5)
};

var outboxOptions = new OutboxProcessorOptions
{
    BatchSize = 100,
    ScanInterval = TimeSpan.FromSeconds(5)
};
```

**之后 (新 API):**

```csharp
// 统一的配置 API
builder.Services.AddCatga()
    .AddHostedServices(options =>
    {
        // 恢复服务配置
        options.Recovery.MaxRetries = 3;
        options.Recovery.RetryDelay = TimeSpan.FromSeconds(5);
        options.Recovery.CheckInterval = TimeSpan.FromSeconds(30);
        
        // Outbox 处理器配置
        options.OutboxProcessor.BatchSize = 100;
        options.OutboxProcessor.ScanInterval = TimeSpan.FromSeconds(5);
        
        // 停机超时
        options.ShutdownTimeout = TimeSpan.FromSeconds(30);
    });
```

## 常见迁移场景

### 场景 1: ASP.NET Core Web API

**之前:**

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddSingleton<IMessageTransport, NatsMessageTransport>();
builder.Services.AddSingleton<IEventStore, NatsJSEventStore>();

var app = builder.Build();

// 手动初始化
var transport = app.Services.GetRequiredService<IMessageTransport>();
await transport.InitializeAsync();

app.MapControllers();
app.Run();
```

**之后:**

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

// 添加 Catga 服务
builder.Services.AddCatga()
    .AddNatsTransport()
    .AddNatsPersistence()
    .AddHostedServices();

// 添加健康检查
builder.Services.AddHealthChecks()
    .AddCatgaHealthChecks();

var app = builder.Build();

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();
```

### 场景 2: Worker Service

**之前:**

```csharp
var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSingleton<IMessageTransport, NatsMessageTransport>();
builder.Services.AddHostedService<MyWorker>();

var host = builder.Build();

// 手动初始化
var transport = host.Services.GetRequiredService<IMessageTransport>();
await transport.InitializeAsync();

await host.RunAsync();
```

**之后:**

```csharp
var builder = Host.CreateApplicationBuilder(args);

// 添加 Catga 服务
builder.Services.AddCatga()
    .AddNatsTransport()
    .AddNatsPersistence()
    .AddHostedServices();

builder.Services.AddHostedService<MyWorker>();

var host = builder.Build();

// 托管服务自动初始化
await host.RunAsync();
```

### 场景 3: 自定义恢复逻辑

**之前:**

```csharp
public class CustomRecoveryManager
{
    public async Task RecoverAsync()
    {
        // 自定义恢复逻辑
    }
}

// 手动调用
var manager = new CustomRecoveryManager();
await manager.RecoverAsync();
```

**之后:**

```csharp
// 实现 IRecoverableComponent 接口
public class CustomComponent : IRecoverableComponent
{
    public string ComponentName => "CustomComponent";
    public bool IsHealthy { get; private set; }
    public string? HealthStatus { get; private set; }
    public DateTimeOffset? LastHealthCheck { get; private set; }
    
    public async Task RecoverAsync()
    {
        // 自定义恢复逻辑
        IsHealthy = true;
        HealthStatus = "Recovered";
        LastHealthCheck = DateTimeOffset.UtcNow;
    }
}

// 注册为可恢复组件
builder.Services.AddSingleton<IRecoverableComponent, CustomComponent>();

// RecoveryHostedService 会自动管理恢复
builder.Services.AddCatga()
    .AddHostedServices(options =>
    {
        options.EnableAutoRecovery = true;
    });
```

## 配置选项参考

### HostingOptions

```csharp
public class HostingOptions
{
    // 是否启用自动恢复 (默认: true)
    public bool EnableAutoRecovery { get; set; } = true;
    
    // 是否启用传输层托管 (默认: true)
    public bool EnableTransportHosting { get; set; } = true;
    
    // 是否启用 Outbox 处理器 (默认: true)
    public bool EnableOutboxProcessor { get; set; } = true;
    
    // 恢复选项
    public RecoveryOptions Recovery { get; set; } = new();
    
    // Outbox 处理器选项
    public OutboxProcessorOptions OutboxProcessor { get; set; } = new();
    
    // 优雅停机超时时间 (默认: 30秒)
    public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(30);
}
```

### RecoveryOptions

```csharp
public class RecoveryOptions
{
    // 健康检查间隔 (默认: 30秒)
    public TimeSpan CheckInterval { get; set; } = TimeSpan.FromSeconds(30);
    
    // 最大重试次数 (默认: 3)
    public int MaxRetries { get; set; } = 3;
    
    // 重试延迟 (默认: 5秒)
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(5);
    
    // 是否启用自动恢复 (默认: true)
    public bool EnableAutoRecovery { get; set; } = true;
    
    // 是否使用指数退避 (默认: true)
    public bool UseExponentialBackoff { get; set; } = true;
}
```

### OutboxProcessorOptions

```csharp
public class OutboxProcessorOptions
{
    // 扫描间隔 (默认: 5秒)
    public TimeSpan ScanInterval { get; set; } = TimeSpan.FromSeconds(5);
    
    // 批次大小 (默认: 100)
    public int BatchSize { get; set; } = 100;
    
    // 错误延迟 (默认: 10秒)
    public TimeSpan ErrorDelay { get; set; } = TimeSpan.FromSeconds(10);
    
    // 是否在停机时完成当前批次 (默认: true)
    public bool CompleteCurrentBatchOnShutdown { get; set; } = true;
}
```

## 最佳实践

### 1. 始终使用托管服务

不要手动管理组件的生命周期。让托管服务自动处理初始化、健康检查和停机。

### 2. 配置健康检查

始终添加健康检查端点，以便监控应用程序状态：

```csharp
builder.Services.AddHealthChecks()
    .AddCatgaHealthChecks();

app.MapHealthChecks("/health");
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live")
});
```

### 3. 调整超时配置

根据您的应用程序需求调整超时配置：

```csharp
builder.Services.AddCatga()
    .AddHostedServices(options =>
    {
        // 生产环境可能需要更长的停机超时
        options.ShutdownTimeout = TimeSpan.FromMinutes(2);
        
        // 调整健康检查间隔
        options.Recovery.CheckInterval = TimeSpan.FromMinutes(1);
    });
```

### 4. 监控恢复状态

使用日志和健康检查监控恢复状态：

```csharp
// 恢复服务会自动记录日志
// 检查健康检查端点以查看恢复状态
var healthCheckService = app.Services.GetRequiredService<HealthCheckService>();
var result = await healthCheckService.CheckHealthAsync();

foreach (var entry in result.Entries)
{
    Console.WriteLine($"{entry.Key}: {entry.Value.Status}");
}
```

## 故障排除

### 问题: 托管服务未启动

**症状**: 应用程序启动但托管服务未运行

**解决方案**: 确保调用了 `AddHostedServices()`：

```csharp
builder.Services.AddCatga()
    .AddHostedServices(); // 必须调用此方法
```

### 问题: 健康检查始终返回 Unhealthy

**症状**: `/health` 端点返回 503 Unhealthy

**解决方案**: 检查组件是否正确初始化：

```csharp
// 确保所有必需的服务都已注册
builder.Services.AddCatga()
    .AddNatsTransport() // 必须注册传输层
    .AddNatsPersistence() // 必须注册持久化层
    .AddHostedServices();
```

### 问题: 停机时消息丢失

**症状**: 应用程序停机时部分消息未处理

**解决方案**: 增加停机超时时间：

```csharp
builder.Services.AddCatga()
    .AddHostedServices(options =>
    {
        options.ShutdownTimeout = TimeSpan.FromMinutes(5);
        options.OutboxProcessor.CompleteCurrentBatchOnShutdown = true;
    });
```

## 总结

迁移到新的托管服务 API 可以简化代码并提高可靠性。主要变更包括：

1. 使用 `AddHostedServices()` 替代手动生命周期管理
2. 使用 `AddCatgaHealthChecks()` 添加健康检查
3. 移除手动初始化和停机代码
4. 通过 `HostingOptions` 统一配置

如有任何问题，请参考 [配置指南](hosting-configuration.md) 或查看 [示例项目](../../examples/OrderSystem/README.md)。
