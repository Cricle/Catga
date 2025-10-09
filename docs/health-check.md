# 健康检查 (Health Check)

Catga 提供了完整的健康检查功能，用于监控应用程序和依赖服务的健康状态。

---

## 📦 安装

```bash
# 核心包
dotnet add package Catga

# Redis 健康检查
dotnet add package Catga.Persistence.Redis
```

---

## 🚀 快速开始

### 基本配置

```csharp
using Catga.HealthCheck;

// 注册健康检查服务
builder.Services.AddCatgaHealthChecks();

// 添加框架健康检查
builder.Services.AddCatgaFrameworkHealthCheck();

// 使用健康检查
var app = builder.Build();

app.MapGet("/health", async (HealthCheckService healthCheck) =>
{
    var report = await healthCheck.CheckAllAsync();
    return report.IsHealthy
        ? Results.Ok(report)
        : Results.StatusCode(503); // Service Unavailable
});

app.Run();
```

---

## 📖 核心概念

### IHealthCheck

自定义健康检查接口：

```csharp
public interface IHealthCheck
{
    string Name { get; }
    ValueTask<HealthCheckResult> CheckAsync(CancellationToken ct = default);
}
```

### HealthCheckResult

健康检查结果：

```csharp
public record HealthCheckResult
{
    public HealthStatus Status { get; init; }
    public string? Description { get; init; }
    public TimeSpan Duration { get; init; }
    public IReadOnlyDictionary<string, object>? Data { get; init; }
}
```

### HealthStatus

健康状态枚举：

```csharp
public enum HealthStatus
{
    Healthy = 0,    // 健康
    Degraded = 1,   // 降级
    Unhealthy = 2   // 不健康
}
```

---

## 💡 使用示例

### 1. 创建自定义健康检查

```csharp
public class DatabaseHealthCheck : IHealthCheck
{
    private readonly IDbConnection _connection;

    public string Name => "Database";

    public DatabaseHealthCheck(IDbConnection connection)
    {
        _connection = connection;
    }

    public async ValueTask<HealthCheckResult> CheckAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _connection.ExecuteScalarAsync<int>(
                "SELECT 1",
                cancellationToken);

            return HealthCheckResult.Healthy("Database is responsive");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                "Database is not responsive",
                ex);
        }
    }
}

// 注册
builder.Services.AddHealthCheck<DatabaseHealthCheck>();
```

### 2. 带数据的健康检查

```csharp
public class ApiHealthCheck : IHealthCheck
{
    private readonly HttpClient _httpClient;

    public string Name => "External API";

    public async ValueTask<HealthCheckResult> CheckAsync(
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            var response = await _httpClient.GetAsync(
                "/health",
                cancellationToken);

            sw.Stop();

            var data = new Dictionary<string, object>
            {
                ["status_code"] = (int)response.StatusCode,
                ["response_time_ms"] = sw.ElapsedMilliseconds
            };

            if (response.IsSuccessStatusCode)
            {
                if (sw.ElapsedMilliseconds < 100)
                {
                    return HealthCheckResult.Healthy(
                        "API is fast and responsive",
                        data);
                }
                else
                {
                    return HealthCheckResult.Degraded(
                        $"API is slow ({sw.ElapsedMilliseconds}ms)",
                        data);
                }
            }

            return HealthCheckResult.Unhealthy(
                $"API returned {response.StatusCode}",
                null,
                data);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                "API is unreachable",
                ex);
        }
    }
}
```

### 3. 内置健康检查

#### Catga 框架健康检查

```csharp
// 自动注册
builder.Services.AddCatgaFrameworkHealthCheck();

// 检查内容：
// - Handler 缓存统计
// - 请求成功/失败率
// - 事件发布统计
```

#### Redis 健康检查

```csharp
builder.Services.AddHealthCheck<RedisHealthCheck>();

// 检查内容：
// - 连接状态
// - Ping 延迟
// - 端点数量
```

---

## 🎯 健康端点设计

### 1. 主健康端点

```csharp
app.MapGet("/health", async (HealthCheckService healthCheck) =>
{
    var report = await healthCheck.CheckAllAsync();

    return report.Status switch
    {
        HealthStatus.Healthy => Results.Ok(new
        {
            status = "healthy",
            checks = report.Entries,
            duration_ms = report.TotalDuration.TotalMilliseconds
        }),

        HealthStatus.Degraded => Results.Ok(new
        {
            status = "degraded",
            checks = report.Entries,
            duration_ms = report.TotalDuration.TotalMilliseconds
        }),

        HealthStatus.Unhealthy => Results.StatusCode(503),

        _ => Results.StatusCode(500)
    };
});
```

### 2. Kubernetes 健康端点

```csharp
// Liveness probe - 应用是否存活
app.MapGet("/health/live", () => Results.Ok(new
{
    status = "alive",
    timestamp = DateTime.UtcNow
}));

// Readiness probe - 应用是否就绪
app.MapGet("/health/ready", async (HealthCheckService healthCheck) =>
{
    var report = await healthCheck.CheckAllAsync();
    return report.IsHealthy
        ? Results.Ok(new { status = "ready" })
        : Results.StatusCode(503);
});
```

### 3. 详细健康报告

```csharp
app.MapGet("/health/detailed", async (HealthCheckService healthCheck) =>
{
    var report = await healthCheck.CheckAllAsync();

    return Results.Ok(new
    {
        status = report.Status.ToString().ToLowerInvariant(),
        totalDuration = report.TotalDuration.TotalMilliseconds,
        timestamp = DateTime.UtcNow,
        checks = report.Entries.Select(e => new
        {
            name = e.Key,
            status = e.Value.Status.ToString().ToLowerInvariant(),
            duration = e.Value.Duration.TotalMilliseconds,
            description = e.Value.Description,
            data = e.Value.Data,
            exception = e.Value.Exception?.Message
        })
    });
});
```

---

## 🔧 高级用法

### 带缓存的健康检查

```csharp
public class CachedHealthCheckService
{
    private readonly HealthCheckService _healthCheck;
    private readonly IMemoryCache _cache;

    public async ValueTask<HealthReport> CheckAllAsync(
        CancellationToken ct = default)
    {
        return await _cache.GetOrCreateAsync(
            "health-check",
            async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30);
                return await _healthCheck.CheckAllAsync(ct);
            }) ?? await _healthCheck.CheckAllAsync(ct);
    }
}
```

### 条件健康检查

```csharp
public class ConditionalHealthCheck : IHealthCheck
{
    public string Name => "ConditionalCheck";

    public async ValueTask<HealthCheckResult> CheckAsync(
        CancellationToken ct = default)
    {
        if (!IsFeatureEnabled())
        {
            return HealthCheckResult.Healthy("Feature disabled, skipping check");
        }

        return await PerformActualCheckAsync(ct);
    }
}
```

---

## 📊 健康检查最佳实践

### 1. 设置合理的超时

```csharp
public async ValueTask<HealthCheckResult> CheckAsync(
    CancellationToken ct = default)
{
    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    cts.CancelAfter(TimeSpan.FromSeconds(5)); // 5秒超时

    try
    {
        await CheckDependencyAsync(cts.Token);
        return HealthCheckResult.Healthy();
    }
    catch (OperationCanceledException)
    {
        return HealthCheckResult.Unhealthy("Health check timed out");
    }
}
```

### 2. 返回有用的诊断信息

```csharp
var data = new Dictionary<string, object>
{
    ["version"] = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown",
    ["uptime_seconds"] = (DateTime.UtcNow - Process.GetCurrentProcess().StartTime).TotalSeconds,
    ["memory_mb"] = GC.GetTotalMemory(false) / 1024 / 1024,
    ["thread_count"] = Process.GetCurrentProcess().Threads.Count
};

return HealthCheckResult.Healthy("Service is running", data);
```

### 3. 区分 Liveness 和 Readiness

```csharp
// Liveness - 只检查应用基本状态
public class LivenessCheck : IHealthCheck
{
    public string Name => "Liveness";

    public ValueTask<HealthCheckResult> CheckAsync(CancellationToken ct = default)
    {
        // 简单检查，快速返回
        return ValueTask.FromResult(HealthCheckResult.Healthy("Alive"));
    }
}

// Readiness - 检查依赖服务
public class ReadinessCheck : IHealthCheck
{
    public string Name => "Readiness";

    public async ValueTask<HealthCheckResult> CheckAsync(CancellationToken ct = default)
    {
        // 检查数据库、缓存、外部 API 等
        var dbHealthy = await _db.PingAsync(ct);
        var cacheHealthy = await _cache.PingAsync(ct);

        if (dbHealthy && cacheHealthy)
        {
            return HealthCheckResult.Healthy("Ready");
        }

        return HealthCheckResult.Unhealthy("Not ready");
    }
}
```

---

## 🐛 故障排查

### 健康检查超时

**问题**：健康检查总是超时

**解决方案**：
- 增加超时时间
- 优化健康检查逻辑
- 使用异步操作

### 误报健康状态

**问题**：实际健康但报告不健康

**解决方案**：
- 检查健康检查逻辑
- 降低健康阈值
- 添加重试机制

---

## 📚 Kubernetes 配置示例

```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: catga-app
spec:
  template:
    spec:
      containers:
      - name: app
        image: catga-app:latest
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
          initialDelaySeconds: 10
          periodSeconds: 5
```

---

## 🎯 性能特征

- **异步设计** - 全异步执行
- **并发检查** - 多个检查并发执行
- **超时控制** - 防止阻塞
- **详细数据** - 丰富的诊断信息

---

## 📚 相关文档

- [可观测性](observability.md)
- [分布式锁](distributed-lock.md)
- [Saga 模式](saga-pattern.md)

---

**需要帮助？** 查看 [Catga 文档](../README.md) 或提交 issue。

