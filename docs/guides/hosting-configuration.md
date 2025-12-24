# Catga 托管服务配置指南

## 概述

Catga 完全集成了 Microsoft.Extensions.Hosting，提供了三个核心托管服务来管理应用程序的生命周期：

- **RecoveryHostedService**: 自动健康检查和组件恢复
- **TransportHostedService**: 消息传输层生命周期管理
- **OutboxProcessorService**: Outbox 模式后台处理器

本指南详细介绍如何配置和使用这些托管服务。

---

## 快速开始

### 基本配置

```csharp
var builder = WebApplication.CreateBuilder(args);

// 添加 Catga 服务
builder.Services.AddCatga()
    .UseInMemory()
    .AddInMemoryTransport()
    .AddHostedServices(); // 启用托管服务（使用默认配置）

// 添加健康检查
builder.Services.AddHealthChecks()
    .AddCatgaHealthChecks();

var app = builder.Build();

// 映射健康检查端点
app.MapHealthChecks("/health");

app.Run();
```

### 自定义配置

```csharp
builder.Services.AddCatga()
    .UseInMemory()
    .AddInMemoryTransport()
    .AddHostedServices(options =>
    {
        // 配置恢复服务
        options.Recovery.CheckInterval = TimeSpan.FromSeconds(60);
        options.Recovery.MaxRetries = 5;
        
        // 配置 Outbox 处理器
        options.OutboxProcessor.ScanInterval = TimeSpan.FromSeconds(10);
        options.OutboxProcessor.BatchSize = 50;
        
        // 配置停机超时
        options.ShutdownTimeout = TimeSpan.FromSeconds(45);
    });
```

---

## 托管服务详解

### 1. RecoveryHostedService

自动监控组件健康状态并在检测到故障时尝试恢复。

#### 配置选项

| 选项 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `EnableAutoRecovery` | bool | true | 是否启用自动恢复 |
| `CheckInterval` | TimeSpan | 30秒 | 健康检查间隔 |
| `MaxRetries` | int | 3 | 最大重试次数 |
| `RetryDelay` | TimeSpan | 5秒 | 重试延迟 |

#### 配置示例

```csharp
.AddHostedServices(options =>
{
    options.EnableAutoRecovery = true;
    options.Recovery.CheckInterval = TimeSpan.FromMinutes(1);
    options.Recovery.MaxRetries = 5;
    options.Recovery.RetryDelay = TimeSpan.FromSeconds(10);
});
```


#### 工作原理

1. 服务启动后，按配置的间隔定期检查所有注册的 `IRecoverableComponent`
2. 检测到不健康的组件时，尝试调用其 `RecoverAsync` 方法
3. 使用指数退避策略进行重试，直到成功或达到最大重试次数
4. 记录所有恢复尝试和结果

#### 实现可恢复组件

```csharp
public class MyCustomComponent : IRecoverableComponent
{
    public bool IsHealthy { get; private set; }
    public string? HealthStatus { get; private set; }
    
    public async Task<bool> RecoverAsync(CancellationToken cancellationToken)
    {
        try
        {
            // 执行恢复逻辑（重新连接、重新初始化等）
            await ReconnectAsync(cancellationToken);
            
            IsHealthy = true;
            HealthStatus = "Recovered successfully";
            return true;
        }
        catch (Exception ex)
        {
            IsHealthy = false;
            HealthStatus = $"Recovery failed: {ex.Message}";
            return false;
        }
    }
}

// 注册组件
builder.Services.AddSingleton<IRecoverableComponent, MyCustomComponent>();
```

---

### 2. TransportHostedService

管理消息传输层的完整生命周期，包括启动、停机和优雅关闭。

#### 配置选项

| 选项 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `EnableTransportHosting` | bool | true | 是否启用传输层托管 |
| `ShutdownTimeout` | TimeSpan | 30秒 | 停机超时时间 |

#### 配置示例

```csharp
.AddHostedServices(options =>
{
    options.EnableTransportHosting = true;
    options.ShutdownTimeout = TimeSpan.FromSeconds(60);
});
```

#### 生命周期行为

**启动阶段 (StartAsync)**:
1. 如果传输层实现了 `IAsyncInitializable`，调用 `InitializeAsync` 建立连接
2. 注册 `ApplicationStopping` 事件处理器

**运行阶段**:
- 传输层正常处理消息

**停机阶段 (ApplicationStopping)**:
1. 触发 `ApplicationStopping` 事件
2. 调用传输层的 `StopAcceptingMessages` 方法（如果实现了 `IStoppable`）
3. 拒绝所有新消息

**关闭阶段 (StopAsync)**:
1. 等待正在处理的消息完成（调用 `WaitForCompletionAsync`）
2. 关闭传输层连接
3. 释放资源


#### 优雅停机示例

```csharp
// 传输层会自动处理优雅停机
// 按 Ctrl+C 或发送 SIGTERM 信号时：
// 1. 停止接受新消息
// 2. 等待当前消息处理完成
// 3. 关闭连接

// 在 Docker 中：
// docker stop <container>  # 发送 SIGTERM，等待优雅停机
```

---

### 3. OutboxProcessorService

后台服务，定期扫描并处理 Outbox 表中的待发送消息。

#### 配置选项

| 选项 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `EnableOutboxProcessor` | bool | true | 是否启用 Outbox 处理器 |
| `ScanInterval` | TimeSpan | 5秒 | 扫描间隔 |
| `BatchSize` | int | 100 | 每批处理的消息数量 |
| `ErrorDelay` | TimeSpan | 10秒 | 错误后的延迟时间 |

#### 配置示例

```csharp
.AddHostedServices(options =>
{
    options.EnableOutboxProcessor = true;
    options.OutboxProcessor.ScanInterval = TimeSpan.FromSeconds(3);
    options.OutboxProcessor.BatchSize = 200;
    options.OutboxProcessor.ErrorDelay = TimeSpan.FromSeconds(15);
});
```

#### 工作原理

1. 按配置的间隔扫描 Outbox 存储
2. 获取待处理的消息（最多 BatchSize 条）
3. 逐条发送消息到传输层
4. 标记已成功发送的消息
5. 如果发生错误，等待 ErrorDelay 后重试

#### 性能调优

**高吞吐量场景**:
```csharp
options.OutboxProcessor.ScanInterval = TimeSpan.FromSeconds(1);
options.OutboxProcessor.BatchSize = 500;
```

**低延迟场景**:
```csharp
options.OutboxProcessor.ScanInterval = TimeSpan.FromMilliseconds(500);
options.OutboxProcessor.BatchSize = 50;
```

**资源受限场景**:
```csharp
options.OutboxProcessor.ScanInterval = TimeSpan.FromSeconds(10);
options.OutboxProcessor.BatchSize = 20;
```

---

## 健康检查集成

### 配置健康检查

```csharp
// 基本配置
builder.Services.AddHealthChecks()
    .AddCatgaHealthChecks();

// 自定义配置
builder.Services.AddHealthChecks()
    .AddCatgaHealthChecks()
    .AddCheck("custom_check", () => HealthCheckResult.Healthy());

// 映射健康检查端点
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


### 健康检查详解

#### 1. TransportHealthCheck

检查消息传输层的连接状态。

**标签**: `catga`, `transport`, `ready`

**状态**:
- `Healthy`: 传输层已连接且正常工作
- `Unhealthy`: 传输层断开连接或不可用

**示例响应**:
```json
{
  "status": "Healthy",
  "results": {
    "catga_transport": {
      "status": "Healthy",
      "description": "Transport is connected",
      "data": {}
    }
  }
}
```

#### 2. PersistenceHealthCheck

检查持久化层的存储状态。

**标签**: `catga`, `persistence`, `ready`

**状态**:
- `Healthy`: 持久化层正常工作
- `Unhealthy`: 持久化层不可用

**示例响应**:
```json
{
  "status": "Healthy",
  "results": {
    "catga_persistence": {
      "status": "Healthy",
      "description": "Persistence is available",
      "data": {}
    }
  }
}
```

#### 3. RecoveryHealthCheck

检查恢复服务的状态。

**标签**: `catga`, `recovery`, `live`

**状态**:
- `Healthy`: 恢复服务正常运行
- `Degraded`: 恢复服务运行但有组件不健康
- `Unhealthy`: 恢复服务本身不可用

**示例响应**:
```json
{
  "status": "Degraded",
  "results": {
    "catga_recovery": {
      "status": "Degraded",
      "description": "Recovery service is running but some components are unhealthy",
      "data": {
        "unhealthyComponents": 1
      }
    }
  }
}
```

### Kubernetes 集成

```yaml
apiVersion: v1
kind: Pod
metadata:
  name: catga-app
spec:
  containers:
  - name: app
    image: myapp:latest
    ports:
    - containerPort: 8080
    livenessProbe:
      httpGet:
        path: /health/live
        port: 8080
      initialDelaySeconds: 30
      periodSeconds: 10
    readinessProbe:
      httpGet:
        path: /health/ready
        port: 8080
      initialDelaySeconds: 5
      periodSeconds: 5
```

---

## 完整配置示例

### 生产环境配置

```csharp
var builder = WebApplication.CreateBuilder(args);

// Catga 配置
builder.Services.AddCatga()
    .UseNats()  // 使用 NATS 持久化
    .AddNatsTransport("nats://nats-cluster:4222")
    .AddHostedServices(options =>
    {
        // 恢复服务 - 生产环境使用更长的间隔
        options.Recovery.CheckInterval = TimeSpan.FromMinutes(2);
        options.Recovery.MaxRetries = 5;
        options.Recovery.RetryDelay = TimeSpan.FromSeconds(10);
        
        // Outbox 处理器 - 高吞吐量配置
        options.OutboxProcessor.ScanInterval = TimeSpan.FromSeconds(2);
        options.OutboxProcessor.BatchSize = 500;
        options.OutboxProcessor.ErrorDelay = TimeSpan.FromSeconds(30);
        
        // 停机超时 - 给予足够时间完成消息处理
        options.ShutdownTimeout = TimeSpan.FromSeconds(60);
    });

// 健康检查
builder.Services.AddHealthChecks()
    .AddCatgaHealthChecks();

var app = builder.Build();

// 健康检查端点
app.MapHealthChecks("/health");
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live")
});

app.Run();
```


### 开发环境配置

```csharp
var builder = WebApplication.CreateBuilder(args);

// Catga 配置 - 开发环境使用内存存储
builder.Services.AddCatga()
    .UseInMemory()
    .AddInMemoryTransport()
    .AddHostedServices(options =>
    {
        // 恢复服务 - 更频繁的检查以便快速发现问题
        options.Recovery.CheckInterval = TimeSpan.FromSeconds(10);
        options.Recovery.MaxRetries = 3;
        
        // Outbox 处理器 - 快速处理以便测试
        options.OutboxProcessor.ScanInterval = TimeSpan.FromSeconds(1);
        options.OutboxProcessor.BatchSize = 10;
        
        // 停机超时 - 开发环境可以更短
        options.ShutdownTimeout = TimeSpan.FromSeconds(15);
    });

// 健康检查
builder.Services.AddHealthChecks()
    .AddCatgaHealthChecks();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapHealthChecks("/health");
app.Run();
```

### 禁用特定服务

```csharp
// 禁用自动恢复（例如在测试环境中）
.AddHostedServices(options =>
{
    options.EnableAutoRecovery = false;
    options.EnableTransportHosting = true;
    options.EnableOutboxProcessor = true;
});

// 仅启用传输层托管
.AddHostedServices(options =>
{
    options.EnableAutoRecovery = false;
    options.EnableTransportHosting = true;
    options.EnableOutboxProcessor = false;
});
```

---

## 故障排查

### 常见问题

#### 1. 托管服务未启动

**症状**: 应用程序启动但托管服务没有运行

**原因**: 
- 未调用 `AddHostedServices()`
- 配置中禁用了服务

**解决方案**:
```csharp
// 确保调用了 AddHostedServices
builder.Services.AddCatga()
    .UseInMemory()
    .AddInMemoryTransport()
    .AddHostedServices();  // 必须调用

// 检查配置
.AddHostedServices(options =>
{
    options.EnableAutoRecovery = true;  // 确保启用
    options.EnableTransportHosting = true;
    options.EnableOutboxProcessor = true;
});
```

#### 2. 健康检查总是返回 Unhealthy

**症状**: `/health` 端点返回 503 Unhealthy

**原因**:
- 传输层或持久化层未正确配置
- 连接失败

**解决方案**:
```csharp
// 检查日志
// 确保传输层和持久化层配置正确
builder.Services.AddCatga()
    .UseNats()  // 确保 NATS 服务器可访问
    .AddNatsTransport("nats://localhost:4222");  // 检查连接字符串

// 测试连接
// docker run -d --name nats -p 4222:4222 nats:latest
```

#### 3. 优雅停机超时

**症状**: 应用程序停止时等待很长时间或强制终止

**原因**:
- 消息处理时间过长
- ShutdownTimeout 设置过短

**解决方案**:
```csharp
.AddHostedServices(options =>
{
    // 增加停机超时时间
    options.ShutdownTimeout = TimeSpan.FromMinutes(2);
});

// 或者在 appsettings.json 中配置
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(2);
});
```


#### 4. Outbox 消息未被处理

**症状**: 消息保存到 Outbox 但从未发送

**原因**:
- OutboxProcessorService 未启用
- 扫描间隔过长
- 批次大小配置不当

**解决方案**:
```csharp
.AddHostedServices(options =>
{
    options.EnableOutboxProcessor = true;  // 确保启用
    options.OutboxProcessor.ScanInterval = TimeSpan.FromSeconds(5);
    options.OutboxProcessor.BatchSize = 100;
});

// 检查日志
// 应该看到类似的日志：
// [Information] Outbox processor started
// [Information] Processing 10 outbox messages
```

#### 5. 恢复服务不工作

**症状**: 组件故障后未自动恢复

**原因**:
- 组件未实现 `IRecoverableComponent`
- 组件未注册到 DI 容器
- 恢复服务未启用

**解决方案**:
```csharp
// 1. 实现接口
public class MyComponent : IRecoverableComponent
{
    public bool IsHealthy { get; private set; }
    public string? HealthStatus { get; private set; }
    
    public async Task<bool> RecoverAsync(CancellationToken ct)
    {
        // 恢复逻辑
        return true;
    }
}

// 2. 注册组件
builder.Services.AddSingleton<IRecoverableComponent, MyComponent>();

// 3. 启用恢复服务
.AddHostedServices(options =>
{
    options.EnableAutoRecovery = true;
});
```

### 日志记录

启用详细日志以诊断问题：

```json
// appsettings.Development.json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Catga.Hosting": "Debug",
      "Catga.Transport": "Debug",
      "Catga.Persistence": "Debug"
    }
  }
}
```

### 监控指标

使用 OpenTelemetry 监控托管服务：

```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics =>
    {
        metrics.AddMeter("Catga.*");
    })
    .WithTracing(tracing =>
    {
        tracing.AddSource("Catga.*");
    });
```

---

## 最佳实践

### 1. 配置管理

使用配置文件管理环境特定的设置：

```csharp
// appsettings.json
{
  "Catga": {
    "Hosting": {
      "EnableAutoRecovery": true,
      "Recovery": {
        "CheckInterval": "00:01:00",
        "MaxRetries": 5
      },
      "OutboxProcessor": {
        "ScanInterval": "00:00:05",
        "BatchSize": 100
      },
      "ShutdownTimeout": "00:00:30"
    }
  }
}

// Program.cs
var hostingConfig = builder.Configuration.GetSection("Catga:Hosting");
builder.Services.AddCatga()
    .AddHostedServices(options =>
    {
        options.EnableAutoRecovery = hostingConfig.GetValue<bool>("EnableAutoRecovery");
        options.Recovery.CheckInterval = hostingConfig.GetValue<TimeSpan>("Recovery:CheckInterval");
        options.OutboxProcessor.ScanInterval = hostingConfig.GetValue<TimeSpan>("OutboxProcessor:ScanInterval");
        options.ShutdownTimeout = hostingConfig.GetValue<TimeSpan>("ShutdownTimeout");
    });
```

### 2. 环境特定配置

```csharp
.AddHostedServices(options =>
{
    if (builder.Environment.IsProduction())
    {
        // 生产环境：稳定性优先
        options.Recovery.CheckInterval = TimeSpan.FromMinutes(2);
        options.OutboxProcessor.ScanInterval = TimeSpan.FromSeconds(5);
        options.ShutdownTimeout = TimeSpan.FromMinutes(1);
    }
    else if (builder.Environment.IsDevelopment())
    {
        // 开发环境：快速反馈
        options.Recovery.CheckInterval = TimeSpan.FromSeconds(10);
        options.OutboxProcessor.ScanInterval = TimeSpan.FromSeconds(1);
        options.ShutdownTimeout = TimeSpan.FromSeconds(15);
    }
});
```


### 3. 健康检查策略

```csharp
// 区分 liveness 和 readiness
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live"),
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var result = JsonSerializer.Serialize(new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                description = e.Value.Description
            })
        });
        await context.Response.WriteAsync(result);
    }
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});
```

### 4. 优雅停机处理

```csharp
// 配置 Kestrel 超时
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(2);
    options.Limits.RequestHeadersTimeout = TimeSpan.FromMinutes(1);
});

// 配置托管服务超时
.AddHostedServices(options =>
{
    options.ShutdownTimeout = TimeSpan.FromSeconds(60);
});

// 在 Docker 中设置停止超时
// docker run --stop-timeout 90 myapp:latest
```

### 5. 错误处理

```csharp
// 实现自定义错误处理
public class CustomRecoverableComponent : IRecoverableComponent
{
    private readonly ILogger<CustomRecoverableComponent> _logger;
    
    public async Task<bool> RecoverAsync(CancellationToken ct)
    {
        try
        {
            await RecoverInternalAsync(ct);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Recovery failed");
            
            // 发送告警
            await SendAlertAsync(ex);
            
            return false;
        }
    }
}
```

---

## 性能优化

### 1. Outbox 处理器优化

```csharp
// 高吞吐量场景
.AddHostedServices(options =>
{
    options.OutboxProcessor.ScanInterval = TimeSpan.FromSeconds(1);
    options.OutboxProcessor.BatchSize = 1000;  // 增大批次
});

// 低延迟场景
.AddHostedServices(options =>
{
    options.OutboxProcessor.ScanInterval = TimeSpan.FromMilliseconds(500);
    options.OutboxProcessor.BatchSize = 50;  // 减小批次
});
```

### 2. 恢复服务优化

```csharp
// 减少健康检查频率以降低开销
.AddHostedServices(options =>
{
    options.Recovery.CheckInterval = TimeSpan.FromMinutes(5);
});

// 或者在组件较少时增加频率
.AddHostedServices(options =>
{
    options.Recovery.CheckInterval = TimeSpan.FromSeconds(15);
});
```

### 3. 内存优化

```csharp
// 使用对象池减少分配
builder.Services.AddSingleton<ObjectPoolProvider, DefaultObjectPoolProvider>();

// 配置合理的批次大小避免大对象堆
.AddHostedServices(options =>
{
    options.OutboxProcessor.BatchSize = 100;  // 避免过大
});
```

---

## 相关资源

- [托管服务迁移指南](hosting-migration.md) - 从旧 API 迁移到新托管服务
- [Getting Started](../articles/getting-started.md) - Catga 快速入门
- [架构概览](../architecture/overview.md) - 了解 Catga 架构设计
- [错误处理指南](error-handling.md) - 异常处理和错误恢复
- [OrderSystem 示例](../../examples/OrderSystem/README.md) - 完整的示例应用

---

## 总结

Catga 的托管服务集成提供了：

✅ **自动生命周期管理** - 无需手动启动/停止服务  
✅ **优雅停机** - 确保消息处理完成后再关闭  
✅ **自动恢复** - 检测并恢复故障组件  
✅ **健康检查** - 与 Kubernetes 等平台无缝集成  
✅ **灵活配置** - 适应不同环境和场景  

通过合理配置这些托管服务，您可以构建高可用、易维护的分布式应用程序。

