# Design Document: Microsoft.Extensions.Hosting Integration

## Overview

本设计将 Catga 框架重构为完全利用 Microsoft.Extensions.Hosting 的生命周期管理基础设施。主要目标是：

1. **消除重复代码**：移除自定义的 GracefulShutdownCoordinator，使用 IHostApplicationLifetime
2. **标准化生命周期**：所有长期运行的服务转换为 IHostedService 或 BackgroundService
3. **改进互操作性**：与 ASP.NET Core、Worker Services 等标准 .NET 应用程序无缝集成
4. **增强可测试性**：利用 Microsoft.Extensions.Hosting.Testing 进行集成测试
5. **简化配置**：提供统一的服务注册和配置 API

## Architecture

### 当前架构问题

```
当前架构：
┌─────────────────────────────────────┐
│  Application (手动管理生命周期)      │
├─────────────────────────────────────┤
│  GracefulShutdownCoordinator        │  ← 自定义实现
│  GracefulRecoveryManager            │  ← 自定义实现
├─────────────────────────────────────┤
│  Transport Services                 │  ← 手动启动/停止
│  Persistence Services               │  ← 手动启动/停止
│  Outbox Processor                   │  ← 手动启动/停止
└─────────────────────────────────────┘
```

### 目标架构

```
目标架构：
┌─────────────────────────────────────┐
│  IHost / WebApplication             │
├─────────────────────────────────────┤
│  IHostApplicationLifetime           │  ← .NET 标准
│  ├─ ApplicationStarted              │
│  ├─ ApplicationStopping             │
│  └─ ApplicationStopped              │
├─────────────────────────────────────┤
│  Hosted Services (IHostedService)   │
│  ├─ RecoveryHostedService           │  ← 新增
│  ├─ TransportHostedService          │  ← 新增
│  ├─ OutboxProcessorService          │  ← 新增
│  └─ PersistenceInitializerService   │  ← 新增
├─────────────────────────────────────┤
│  Health Checks (IHealthCheck)       │
│  ├─ TransportHealthCheck            │  ← 新增
│  ├─ PersistenceHealthCheck          │  ← 新增
│  └─ RecoveryHealthCheck             │  ← 新增
└─────────────────────────────────────┘
```

## Components and Interfaces

### 1. RecoveryHostedService

替代 GracefulRecoveryManager，作为托管服务运行。

```csharp
namespace Catga.Hosting;

/// <summary>
/// 后台恢复服务 - 定期检查组件健康状态并自动恢复
/// </summary>
public sealed class RecoveryHostedService : BackgroundService
{
    private readonly ILogger<RecoveryHostedService> _logger;
    private readonly IEnumerable<IRecoverableComponent> _components;
    private readonly RecoveryOptions _options;
    
    public RecoveryHostedService(
        ILogger<RecoveryHostedService> logger,
        IEnumerable<IRecoverableComponent> components,
        RecoveryOptions options)
    {
        _logger = logger;
        _components = components;
        _options = options;
    }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 定期健康检查和恢复逻辑
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(_options.CheckInterval, stoppingToken);
            await CheckAndRecoverAsync(stoppingToken);
        }
    }
    
    private async Task CheckAndRecoverAsync(CancellationToken cancellationToken)
    {
        foreach (var component in _components)
        {
            if (!component.IsHealthy)
            {
                await RecoverComponentAsync(component, cancellationToken);
            }
        }
    }
}

/// <summary>
/// 恢复服务配置选项
/// </summary>
public sealed class RecoveryOptions
{
    public TimeSpan CheckInterval { get; set; } = TimeSpan.FromSeconds(30);
    public int MaxRetries { get; set; } = 3;
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(5);
    public bool EnableAutoRecovery { get; set; } = true;
}
```

### 2. TransportHostedService

管理传输层的生命周期。

```csharp
namespace Catga.Hosting;

/// <summary>
/// 传输层托管服务 - 管理消息传输的启动和停止
/// </summary>
public sealed class TransportHostedService : IHostedService
{
    private readonly IMessageTransport _transport;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<TransportHostedService> _logger;
    private IDisposable? _stoppingRegistration;
    
    public TransportHostedService(
        IMessageTransport transport,
        IHostApplicationLifetime lifetime,
        ILogger<TransportHostedService> logger)
    {
        _transport = transport;
        _lifetime = lifetime;
        _logger = logger;
    }
    
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting transport service");
        
        // 如果传输层需要初始化连接
        if (_transport is IAsyncInitializable initializable)
        {
            await initializable.InitializeAsync(cancellationToken);
        }
        
        // 注册停机事件 - 停止接受新消息
        _stoppingRegistration = _lifetime.ApplicationStopping.Register(() =>
        {
            _logger.LogInformation("Application stopping - transport will stop accepting new messages");
            if (_transport is IStoppable stoppable)
            {
                stoppable.StopAcceptingMessages();
            }
        });
        
        _logger.LogInformation("Transport service started");
    }
    
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping transport service");
        
        // 等待正在处理的消息完成
        if (_transport is IWaitable waitable)
        {
            await waitable.WaitForCompletionAsync(cancellationToken);
        }
        
        // 关闭连接
        if (_transport is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
        }
        
        _stoppingRegistration?.Dispose();
        _logger.LogInformation("Transport service stopped");
    }
}

/// <summary>
/// 支持异步初始化的接口
/// </summary>
public interface IAsyncInitializable
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 支持停止接受新请求的接口
/// </summary>
public interface IStoppable
{
    void StopAcceptingMessages();
}

/// <summary>
/// 支持等待完成的接口
/// </summary>
public interface IWaitable
{
    Task WaitForCompletionAsync(CancellationToken cancellationToken = default);
}
```

### 3. OutboxProcessorService

Outbox 模式的后台处理器。

```csharp
namespace Catga.Hosting;

/// <summary>
/// Outbox 处理器后台服务
/// </summary>
public sealed class OutboxProcessorService : BackgroundService
{
    private readonly IOutboxStore _outboxStore;
    private readonly IMessageTransport _transport;
    private readonly ILogger<OutboxProcessorService> _logger;
    private readonly OutboxProcessorOptions _options;
    
    public OutboxProcessorService(
        IOutboxStore outboxStore,
        IMessageTransport transport,
        ILogger<OutboxProcessorService> logger,
        OutboxProcessorOptions options)
    {
        _outboxStore = outboxStore;
        _transport = transport;
        _logger = logger;
        _options = options;
    }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Outbox processor started");
        
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
                await Task.Delay(_options.ScanInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing outbox batch");
                await Task.Delay(_options.ErrorDelay, stoppingToken);
            }
        }
        
        _logger.LogInformation("Outbox processor stopped");
    }
    
    private async Task ProcessBatchAsync(CancellationToken cancellationToken)
    {
        var messages = await _outboxStore.GetPendingAsync(_options.BatchSize, cancellationToken);
        
        foreach (var message in messages)
        {
            if (cancellationToken.IsCancellationRequested)
                break;
                
            await _transport.PublishAsync(message, cancellationToken);
            await _outboxStore.MarkAsProcessedAsync(message.Id, cancellationToken);
        }
    }
}

/// <summary>
/// Outbox 处理器配置选项
/// </summary>
public sealed class OutboxProcessorOptions
{
    public TimeSpan ScanInterval { get; set; } = TimeSpan.FromSeconds(5);
    public int BatchSize { get; set; } = 100;
    public TimeSpan ErrorDelay { get; set; } = TimeSpan.FromSeconds(10);
}
```

### 4. Health Checks

集成 ASP.NET Core 健康检查。

```csharp
namespace Catga.Hosting.HealthChecks;

/// <summary>
/// 传输层健康检查
/// </summary>
public sealed class TransportHealthCheck : IHealthCheck
{
    private readonly IMessageTransport _transport;
    
    public TransportHealthCheck(IMessageTransport transport)
    {
        _transport = transport;
    }
    
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (_transport is IHealthCheckable healthCheckable)
        {
            var isHealthy = healthCheckable.IsHealthy;
            return Task.FromResult(isHealthy
                ? HealthCheckResult.Healthy("Transport is connected")
                : HealthCheckResult.Unhealthy("Transport is disconnected"));
        }
        
        return Task.FromResult(HealthCheckResult.Healthy("Transport does not support health checks"));
    }
}

/// <summary>
/// 支持健康检查的接口
/// </summary>
public interface IHealthCheckable
{
    bool IsHealthy { get; }
    string? HealthStatus { get; }
}
```

### 5. 服务注册扩展

简化的配置 API。

```csharp
namespace Microsoft.Extensions.DependencyInjection;

public static class CatgaHostingExtensions
{
    /// <summary>
    /// 添加 Catga 托管服务支持
    /// </summary>
    public static CatgaServiceBuilder AddHostedServices(
        this CatgaServiceBuilder builder,
        Action<HostingOptions>? configure = null)
    {
        var options = new HostingOptions();
        configure?.Invoke(options);
        
        builder.Services.AddSingleton(options);
        
        // 注册恢复服务
        if (options.EnableAutoRecovery)
        {
            builder.Services.AddSingleton<RecoveryOptions>(options.Recovery);
            builder.Services.AddHostedService<RecoveryHostedService>();
        }
        
        // 注册传输层托管服务
        if (options.EnableTransportHosting)
        {
            builder.Services.AddHostedService<TransportHostedService>();
        }
        
        // 注册 Outbox 处理器
        if (options.EnableOutboxProcessor)
        {
            builder.Services.AddSingleton(options.OutboxProcessor);
            builder.Services.AddHostedService<OutboxProcessorService>();
        }
        
        return builder;
    }
    
    /// <summary>
    /// 添加 Catga 健康检查
    /// </summary>
    public static IHealthChecksBuilder AddCatgaHealthChecks(
        this IHealthChecksBuilder builder)
    {
        builder.AddCheck<TransportHealthCheck>("catga_transport");
        builder.AddCheck<PersistenceHealthCheck>("catga_persistence");
        builder.AddCheck<RecoveryHealthCheck>("catga_recovery");
        
        return builder;
    }
}

/// <summary>
/// 托管服务配置选项
/// </summary>
public sealed class HostingOptions
{
    public bool EnableAutoRecovery { get; set; } = true;
    public bool EnableTransportHosting { get; set; } = true;
    public bool EnableOutboxProcessor { get; set; } = true;
    
    public RecoveryOptions Recovery { get; set; } = new();
    public OutboxProcessorOptions OutboxProcessor { get; set; } = new();
    
    /// <summary>
    /// 优雅停机超时时间
    /// </summary>
    public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(30);
}
```

## Data Models

### HostingOptions

```csharp
public sealed class HostingOptions
{
    // 是否启用自动恢复
    public bool EnableAutoRecovery { get; set; } = true;
    
    // 是否启用传输层托管
    public bool EnableTransportHosting { get; set; } = true;
    
    // 是否启用 Outbox 处理器
    public bool EnableOutboxProcessor { get; set; } = true;
    
    // 恢复选项
    public RecoveryOptions Recovery { get; set; } = new();
    
    // Outbox 处理器选项
    public OutboxProcessorOptions OutboxProcessor { get; set; } = new();
    
    // 优雅停机超时
    public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(30);
}
```

### RecoveryOptions

```csharp
public sealed class RecoveryOptions
{
    // 健康检查间隔
    public TimeSpan CheckInterval { get; set; } = TimeSpan.FromSeconds(30);
    
    // 最大重试次数
    public int MaxRetries { get; set; } = 3;
    
    // 重试延迟
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(5);
    
    // 是否启用自动恢复
    public bool EnableAutoRecovery { get; set; } = true;
}
```

### OutboxProcessorOptions

```csharp
public sealed class OutboxProcessorOptions
{
    // 扫描间隔
    public TimeSpan ScanInterval { get; set; } = TimeSpan.FromSeconds(5);
    
    // 批次大小
    public int BatchSize { get; set; } = 100;
    
    // 错误延迟
    public TimeSpan ErrorDelay { get; set; } = TimeSpan.FromSeconds(10);
}
```

## Correctness Properties

*A property is a characteristic or behavior that should hold true across all valid executions of a system-essentially, a formal statement about what the system should do. Properties serve as the bridge between human-readable specifications and machine-verifiable correctness guarantees.*

### Property 1: 托管服务生命周期管理

*For any* 注册的托管服务集合，当应用程序启动时，所有服务的 StartAsync 方法应该被调用并成功完成。

**Validates: Requirements 1.2**

### Property 2: 停机时拒绝新消息

*For any* 消息流，当 ApplicationStopping 被触发后，所有新到达的消息应该被拒绝，不应该被处理。

**Validates: Requirements 2.2**

### Property 3: 停机时完成进行中消息

*For any* 正在处理的消息集合，当 ApplicationStopping 被触发时，所有这些消息应该在停机完成前处理完毕。

**Validates: Requirements 2.3**

### Property 4: 停机超时强制终止

*For any* 配置的停机超时时间，如果停机过程超过该时间，系统应该记录警告并强制停止。

**Validates: Requirements 2.5**

### Property 5: 恢复服务定期健康检查

*For any* 配置的检查间隔，恢复服务应该在该间隔内对所有注册的组件执行健康检查。

**Validates: Requirements 3.3**

### Property 6: 不健康组件自动恢复

*For any* 检测到的不健康组件，恢复服务应该尝试恢复该组件，直到成功或达到最大重试次数。

**Validates: Requirements 3.4**

### Property 7: 取消令牌响应

*For any* 托管服务，当 CancellationToken 被取消时，服务应该在合理时间内停止执行。

**Validates: Requirements 3.6, 6.5**

### Property 8: 传输层停止接受新消息

*For any* 传输服务，当 ApplicationStopping 触发后，尝试发送新消息应该失败或被拒绝。

**Validates: Requirements 4.4**

### Property 9: 传输层等待消息完成

*For any* 传输服务中正在处理的消息，在连接关闭前，所有这些消息应该处理完成。

**Validates: Requirements 4.5**

### Property 10: Outbox 处理器定期扫描

*For any* 配置的扫描间隔，Outbox 处理器应该在该间隔内扫描并处理待发送的消息。

**Validates: Requirements 6.3**

### Property 11: Outbox 批次完整性

*For any* 正在处理的 Outbox 批次，当停机请求到来时，当前批次应该完整处理完成后再停止。

**Validates: Requirements 6.4**

### Property 12: Outbox 配置生效

*For any* 配置的扫描间隔和批次大小，Outbox 处理器应该按照这些配置值运行。

**Validates: Requirements 6.6**

### Property 13: 健康检查反映传输状态

*For any* 传输服务的连接状态变化，健康检查应该返回与实际连接状态一致的结果。

**Validates: Requirements 7.2**

### Property 14: 健康检查反映持久化状态

*For any* 持久化服务的存储状态变化，健康检查应该返回与实际存储状态一致的结果。

**Validates: Requirements 7.3**

### Property 15: 健康检查反映恢复状态

*For any* 恢复服务的状态变化，健康检查应该返回与实际恢复状态一致的结果。

**Validates: Requirements 7.4**

### Property 16: 组件不健康时整体状态降级

*For any* 不健康的组件，整体健康检查应该返回 Degraded 或 Unhealthy 状态，而不是 Healthy。

**Validates: Requirements 7.6**

### Property 17: 自动注册必需服务

*For any* Catga 配置，调用 AddCatga() 后，所有必需的托管服务应该被自动注册到服务容器中。

**Validates: Requirements 9.2**

### Property 18: 默认配置有效性

*For any* 未显式配置的选项，使用默认值应该能够成功启动应用程序并正常运行。

**Validates: Requirements 9.4**

## Error Handling

### 启动失败处理

```csharp
// 如果任何托管服务的 StartAsync 失败，应用程序应该：
// 1. 记录详细的错误信息
// 2. 停止已启动的服务
// 3. 退出应用程序并返回非零退出码
```

### 运行时错误处理

```csharp
// 后台服务（如 RecoveryHostedService）的运行时错误应该：
// 1. 记录错误
// 2. 等待一段时间后重试
// 3. 不应该导致整个应用程序崩溃
```

### 停机超时处理

```csharp
// 如果停机超过配置的超时时间：
// 1. 记录警告，列出未完成的服务
// 2. 强制取消所有操作
// 3. 退出应用程序
```

### 恢复失败处理

```csharp
// 如果组件恢复失败：
// 1. 记录错误和重试次数
// 2. 使用指数退避重试
// 3. 达到最大重试次数后，标记组件为不健康
// 4. 通过健康检查暴露状态
```

## Testing Strategy

### Unit Tests

1. **托管服务生命周期测试**
   - 测试 StartAsync 和 StopAsync 的正确调用
   - 测试 CancellationToken 的正确传播
   - 测试错误处理逻辑

2. **恢复逻辑测试**
   - 测试健康检查逻辑
   - 测试恢复重试机制
   - 测试并发恢复的互斥

3. **配置验证测试**
   - 测试默认配置值
   - 测试配置验证逻辑
   - 测试无效配置的错误处理

### Property-Based Tests

使用 FsCheck 或 CsCheck 进行属性测试，最少 100 次迭代。

1. **Property 1: 启动顺序一致性**
   ```csharp
   // Feature: hosting-integration, Property 1
   // 生成随机数量的托管服务，验证启动顺序
   ```

2. **Property 2: 停机完整性**
   ```csharp
   // Feature: hosting-integration, Property 2
   // 生成随机的运行中消息，验证停机时都能完成
   ```

3. **Property 3: 恢复幂等性**
   ```csharp
   // Feature: hosting-integration, Property 3
   // 对同一组件多次调用恢复，验证状态一致
   ```

### Integration Tests

1. **完整生命周期测试**
   - 使用 WebApplicationFactory 测试完整的启动-运行-停机流程
   - 验证所有托管服务正确启动和停止

2. **健康检查集成测试**
   - 测试健康检查端点返回正确状态
   - 测试组件故障时健康检查的响应

3. **Outbox 处理器集成测试**
   - 测试消息从 Outbox 到传输层的完整流程
   - 测试失败重试机制

### 测试工具

```csharp
namespace Catga.Testing;

/// <summary>
/// 托管服务测试辅助类
/// </summary>
public static class HostedServiceTestHelper
{
    /// <summary>
    /// 创建测试用的 IHost
    /// </summary>
    public static IHost CreateTestHost(Action<IServiceCollection> configureServices)
    {
        return Host.CreateDefaultBuilder()
            .ConfigureServices(configureServices)
            .Build();
    }
    
    /// <summary>
    /// 模拟应用程序生命周期事件
    /// </summary>
    public static TestApplicationLifetime CreateTestLifetime()
    {
        return new TestApplicationLifetime();
    }
}

/// <summary>
/// 测试用的应用程序生命周期
/// </summary>
public sealed class TestApplicationLifetime : IHostApplicationLifetime
{
    private readonly CancellationTokenSource _startedSource = new();
    private readonly CancellationTokenSource _stoppingSource = new();
    private readonly CancellationTokenSource _stoppedSource = new();
    
    public CancellationToken ApplicationStarted => _startedSource.Token;
    public CancellationToken ApplicationStopping => _stoppingSource.Token;
    public CancellationToken ApplicationStopped => _stoppedSource.Token;
    
    public void StopApplication() => _stoppingSource.Cancel();
    
    public void SimulateStarted() => _startedSource.Cancel();
    public void SimulateStopping() => _stoppingSource.Cancel();
    public void SimulateStopped() => _stoppedSource.Cancel();
}
```

## Migration Guide

### 从自定义生命周期迁移

**Before:**
```csharp
var shutdown = new GracefulShutdownCoordinator(logger);
var recovery = new GracefulRecoveryManager(logger);

// 手动启动恢复
_ = recovery.StartAutoRecoveryAsync(TimeSpan.FromSeconds(30));

// 手动处理停机
Console.CancelKeyPress += (s, e) =>
{
    shutdown.RequestShutdown();
    e.Cancel = true;
};
```

**After:**
```csharp
var builder = Host.CreateDefaultBuilder(args)
    .ConfigureServices(services =>
    {
        services.AddCatga()
            .AddHostedServices(options =>
            {
                options.EnableAutoRecovery = true;
                options.Recovery.CheckInterval = TimeSpan.FromSeconds(30);
            });
    });

var host = builder.Build();
await host.RunAsync(); // 自动处理生命周期
```

### 迁移传输层

**Before:**
```csharp
var transport = new NatsMessageTransport(...);
// 手动初始化和清理
```

**After:**
```csharp
services.AddCatga()
    .AddNatsTransport(options => { ... })
    .AddHostedServices(); // 自动管理传输层生命周期
```

## Performance Considerations

1. **启动性能**：托管服务按顺序启动，确保依赖关系正确
2. **停机性能**：配置合理的超时时间，避免长时间等待
3. **恢复性能**：使用合理的检查间隔，避免过于频繁的健康检查
4. **内存使用**：托管服务应该正确释放资源，避免内存泄漏

## Security Considerations

1. **配置验证**：验证所有配置选项，防止无效值
2. **错误信息**：避免在日志中暴露敏感信息
3. **资源限制**：限制重试次数和超时时间，防止资源耗尽

## Future Enhancements

1. **分布式追踪**：集成 OpenTelemetry 追踪托管服务生命周期
2. **指标收集**：收集启动时间、停机时间等指标
3. **动态配置**：支持运行时修改配置
4. **优先级启动**：支持托管服务的启动优先级
