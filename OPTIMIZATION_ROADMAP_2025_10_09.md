# Catga 优化路线图

**制定日期**: 2025-10-09  
**基于**: 全面代码审查报告  
**目标**: 从 4.0/5.0 提升到 5.0/5.0

---

## 🎯 总体目标

将 Catga 从"良好"提升到"完美"，重点关注：
1. 开发体验（源生成器、分析器、Template）
2. 分布式能力（锁、Saga、Event Sourcing）
3. 生产就绪（线程池、监控、健康检查）

---

## 📋 优化计划概览

| 阶段 | 任务 | 优先级 | 工期 | 状态 |
|------|------|--------|------|------|
| **P0-1** | 源生成器重构 | P0 | 1周 | 📋 待开始 |
| **P0-2** | 分析器扩展 | P0 | 1周 | 📋 待开始 |
| **P0-3** | Template 创建 | P0 | 3天 | 📋 待开始 |
| **P1-1** | 分布式锁 | P1 | 3天 | 📋 待开始 |
| **P1-2** | Saga 模式 | P1 | 5天 | 📋 待开始 |
| **P1-3** | 健康检查 | P1 | 2天 | 📋 待开始 |
| **P2-1** | 线程池优化 | P2 | 2天 | 📋 待开始 |
| **P2-2** | Event Sourcing | P2 | 1周 | 📋 待开始 |
| **P2-3** | 分布式缓存 | P2 | 3天 | 📋 待开始 |

**总工期**: 约 5 周

---

## 🚀 P0-1: 源生成器重构

### 目标
简化现有生成器，添加更有价值的生成器

### 任务清单

#### 1.1 删除低价值生成器

- [ ] 删除 `CatgaBehaviorGenerator.cs`
  - 理由: Behaviors 数量少，手动注册更清晰
  - 影响: 无（可选功能）

- [ ] 删除 `CatgaPipelineGenerator.cs`
  - 理由: 当前 PipelineExecutor 已足够高效
  - 影响: 无（性能提升 <1%）

**预期**: 减少 40% 生成器代码

---

#### 1.2 提取生成器基类

**新增**: `src/Catga.SourceGenerator/BaseSourceGenerator.cs`

```csharp
public abstract class BaseSourceGenerator : IIncrementalGenerator
{
    protected abstract string GeneratorName { get; }
    
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // 通用初始化逻辑
        var provider = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: FilterSyntaxNode,
                transform: TransformSyntaxNode)
            .Where(x => x != null);
            
        context.RegisterSourceOutput(provider, GenerateSource);
    }
    
    protected abstract bool FilterSyntaxNode(SyntaxNode node, CancellationToken ct);
    protected abstract object? TransformSyntaxNode(GeneratorSyntaxContext ctx, CancellationToken ct);
    protected abstract void GenerateSource(SourceProductionContext ctx, object model);
}
```

**重构**: `CatgaHandlerGenerator` 继承基类

---

#### 1.3 新增 MessageContractGenerator

**功能**: 为消息类型生成样板代码

**触发器**: `[GenerateMessageContract]` 特性

```csharp
[GenerateMessageContract]
public partial class CreateUserCommand : IRequest<CreateUserResponse>
{
    public required string Username { get; init; }
    public required string Email { get; init; }
}
```

**生成代码**:
```csharp
// Auto-generated
partial class CreateUserCommand
{
    // Validation
    public IEnumerable<ValidationError> Validate()
    {
        if (string.IsNullOrWhiteSpace(Username))
            yield return new ValidationError(nameof(Username), "Username is required");
        if (string.IsNullOrWhiteSpace(Email))
            yield return new ValidationError(nameof(Email), "Email is required");
    }
    
    // JSON Serialization Context (AOT)
    [JsonSerializable(typeof(CreateUserCommand))]
    [JsonSerializable(typeof(CreateUserResponse))]
    internal partial class JsonContext : JsonSerializerContext { }
    
    // MemoryPack (AOT)
    [MemoryPackable]
    partial class CreateUserCommand { }
}
```

**文件**: `src/Catga.SourceGenerator/MessageContractGenerator.cs`

---

#### 1.4 新增 ConfigurationValidatorGenerator

**功能**: 为配置类生成验证代码

**触发器**: `IValidatableConfiguration` 接口

```csharp
public partial class CatgaOptions : IValidatableConfiguration
{
    public int MaxConcurrentRequests { get; set; } = 100;
    public int RateLimitBurstCapacity { get; set; } = 100;
}
```

**生成代码**:
```csharp
// Auto-generated
partial class CatgaOptions
{
    public ValidationResult Validate()
    {
        var errors = new List<string>();
        
        if (MaxConcurrentRequests <= 0)
            errors.Add("MaxConcurrentRequests must be positive");
        if (RateLimitBurstCapacity <= 0)
            errors.Add("RateLimitBurstCapacity must be positive");
            
        return errors.Count == 0 
            ? ValidationResult.Success() 
            : ValidationResult.Failure(errors);
    }
}
```

**文件**: `src/Catga.SourceGenerator/ConfigurationValidatorGenerator.cs`

---

### 验收标准

- [ ] 生成器代码减少 40%
- [ ] MessageContractGenerator 正常工作
- [ ] ConfigurationValidatorGenerator 正常工作
- [ ] 所有测试通过
- [ ] 文档更新

---

## 🔍 P0-2: 分析器扩展

### 目标
从 15 规则扩展到 35 规则，覆盖所有关键场景

### 任务清单

#### 2.1 GCPressureAnalyzer

**新增**: `src/Catga.Analyzers/GCPressureAnalyzer.cs`

**规则**:

| 规则 ID | 严重性 | 描述 |
|---------|--------|------|
| CATGA101 | Warning | 热路径中使用 ToArray() |
| CATGA102 | Info | 可以使用 ArrayPool |
| CATGA103 | Warning | 字符串拼接应使用插值 |
| CATGA104 | Info | 可以使用 Span<T> |
| CATGA105 | Warning | 不必要的装箱 |

**示例**:
```csharp
// CATGA101: 热路径中使用 ToArray()
public async Task<CatgaResult<TResponse>> SendAsync<TRequest, TResponse>(...)
{
    var handlers = GetHandlers().ToArray(); // ❌ 警告
    // 建议: 使用 ArrayPool 或 Span
}

// CATGA102: 可以使用 ArrayPool
public byte[] Compress(byte[] data)
{
    var buffer = new byte[data.Length * 2]; // ❌ 提示
    // 建议: var buffer = ArrayPool<byte>.Shared.Rent(data.Length * 2);
}
```

---

#### 2.2 ConcurrencySafetyAnalyzer

**新增**: `src/Catga.Analyzers/ConcurrencySafetyAnalyzer.cs`

**规则**:

| 规则 ID | 严重性 | 描述 |
|---------|--------|------|
| CATGA201 | Error | 非线程安全集合在并发环境 |
| CATGA202 | Warning | 缺少 volatile/Interlocked |
| CATGA203 | Error | 潜在死锁 |
| CATGA204 | Warning | 双重检查锁定错误 |

**示例**:
```csharp
// CATGA201: 非线程安全集合
private Dictionary<string, int> _cache = new(); // ❌ 错误
// 建议: ConcurrentDictionary<string, int>

// CATGA202: 缺少 volatile
private bool _isRunning; // ❌ 警告
// 建议: private volatile bool _isRunning;
```

---

#### 2.3 AotCompatibilityAnalyzer

**新增**: `src/Catga.Analyzers/AotCompatibilityAnalyzer.cs`

**规则**:

| 规则 ID | 严重性 | 描述 |
|---------|--------|------|
| CATGA301 | Error | 使用反射 |
| CATGA302 | Error | 动态代码生成 |
| CATGA303 | Warning | JSON 序列化缺少 Context |
| CATGA304 | Info | 建议使用 MemoryPack |
| CATGA305 | Warning | 不支持的 API |
| CATGA306 | Error | 缺少 AOT 特性标记 |

**示例**:
```csharp
// CATGA301: 使用反射
var method = type.GetMethod("Execute"); // ❌ 错误
// 建议: 使用源生成器

// CATGA303: JSON 序列化缺少 Context
JsonSerializer.Serialize(obj); // ❌ 警告
// 建议: JsonSerializer.Serialize(obj, MyJsonContext.Default.MyType);
```

---

#### 2.4 DistributedPatternAnalyzer

**新增**: `src/Catga.Analyzers/DistributedPatternAnalyzer.cs`

**规则**:

| 规则 ID | 严重性 | 描述 |
|---------|--------|------|
| CATGA401 | Warning | Outbox 模式使用错误 |
| CATGA402 | Error | 缺少幂等性 |
| CATGA403 | Warning | 消息丢失风险 |
| CATGA404 | Info | 建议使用分布式锁 |
| CATGA405 | Warning | 缺少重试策略 |

**示例**:
```csharp
// CATGA402: 缺少幂等性
public class CreateUserHandler : IRequestHandler<CreateUserCommand, CreateUserResponse>
{
    public async Task<CatgaResult<CreateUserResponse>> Handle(...) // ❌ 错误
    {
        // 没有幂等性检查
        await _db.Users.AddAsync(user);
    }
    // 建议: 添加 [Idempotent] 特性或检查
}
```

---

### 验收标准

- [ ] 4 个新分析器实现
- [ ] 20 个新规则
- [ ] 所有规则有 CodeFix
- [ ] 单元测试覆盖
- [ ] 文档更新

---

## 📦 P0-3: Template 创建

### 目标
提供 4 个项目模板，5 分钟快速开始

### 任务清单

#### 3.1 创建 Template 项目结构

**目录结构**:
```
templates/
├── catga-api/
│   ├── .template.config/
│   │   └── template.json
│   ├── Program.cs
│   ├── appsettings.json
│   ├── Dockerfile
│   └── Commands/
│       └── SampleCommand.cs
├── catga-distributed/
│   ├── .template.config/
│   │   └── template.json
│   ├── Program.cs
│   ├── docker-compose.yml
│   └── k8s/
│       └── deployment.yaml
├── catga-microservice/
│   └── ...
└── catga-handler/
    └── ...
```

---

#### 3.2 catga-api Template

**命令**: `dotnet new catga-api -n MyApi`

**生成文件**:

**Program.cs**:
```csharp
var builder = WebApplication.CreateBuilder(args);

// Add Catga
builder.Services.AddCatga(options =>
{
    options.MaxConcurrentRequests = 100;
    options.EnableRateLimiting = true;
});

// Add handlers from assembly
builder.Services.AddCatgaHandlers();

var app = builder.Build();

app.MapCatgaEndpoints();
app.Run();
```

**Commands/SampleCommand.cs**:
```csharp
public record SampleCommand(string Name) : IRequest<SampleResponse>;

public record SampleResponse(string Message);

public class SampleCommandHandler : IRequestHandler<SampleCommand, SampleResponse>
{
    public async Task<CatgaResult<SampleResponse>> Handle(
        SampleCommand request, 
        CancellationToken cancellationToken)
    {
        return CatgaResult<SampleResponse>.Success(
            new SampleResponse($"Hello, {request.Name}!"));
    }
}
```

---

#### 3.3 catga-distributed Template

**命令**: `dotnet new catga-distributed -n MyDistributedApp`

**生成文件**:

**docker-compose.yml**:
```yaml
version: '3.8'
services:
  app:
    build: .
    environment:
      - NATS_URL=nats://nats:4222
      - REDIS_URL=redis:6379
    depends_on:
      - nats
      - redis
  
  nats:
    image: nats:latest
    ports:
      - "4222:4222"
  
  redis:
    image: redis:latest
    ports:
      - "6379:6379"
```

**Program.cs**:
```csharp
builder.Services.AddCatga(options =>
{
    options.EnableOutbox = true;
    options.EnableInbox = true;
});

// Add NATS transport
builder.Services.AddCatgaNats(options =>
{
    options.Url = builder.Configuration["NATS_URL"];
});

// Add Redis persistence
builder.Services.AddCatgaRedis(options =>
{
    options.ConnectionString = builder.Configuration["REDIS_URL"];
});

// Add distributed ID
builder.Services.AddDistributedId(options =>
{
    options.WorkerId = 1;
});
```

---

#### 3.4 catga-microservice Template

**命令**: `dotnet new catga-microservice -n MyService`

**生成内容**:
- 完整的微服务结构（API + Worker）
- 健康检查
- Prometheus 监控
- Kubernetes manifests
- CI/CD (GitHub Actions)

---

#### 3.5 catga-handler Template

**命令**: `dotnet new catga-handler -n CreateUser`

**生成文件**:
- `CreateUserCommand.cs`
- `CreateUserHandler.cs`
- `CreateUserValidator.cs`
- `CreateUserHandlerTests.cs`

---

#### 3.6 打包和发布

**创建 NuGet 包**:
```bash
dotnet pack templates/Catga.Templates.csproj
```

**发布到 NuGet**:
```bash
dotnet nuget push Catga.Templates.*.nupkg
```

**安装**:
```bash
dotnet new install Catga.Templates
```

---

### 验收标准

- [ ] 4 个模板创建完成
- [ ] 模板可以正常安装
- [ ] 生成的项目可以编译运行
- [ ] 文档完整
- [ ] 发布到 NuGet

---

## 🔒 P1-1: 分布式锁

### 目标
提供分布式锁抽象和实现

### 任务清单

#### 1.1 定义接口

**新增**: `src/Catga/DistributedLock/IDistributedLock.cs`

```csharp
public interface IDistributedLock
{
    /// <summary>
    /// Acquire a distributed lock
    /// </summary>
    Task<ILockHandle?> TryAcquireAsync(
        string key, 
        TimeSpan timeout, 
        CancellationToken cancellationToken = default);
}

public interface ILockHandle : IDisposable, IAsyncDisposable
{
    string Key { get; }
    DateTime AcquiredAt { get; }
    bool IsHeld { get; }
}
```

---

#### 1.2 内存实现

**新增**: `src/Catga/DistributedLock/MemoryDistributedLock.cs`

```csharp
public sealed class MemoryDistributedLock : IDistributedLock
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();
    
    public async Task<ILockHandle?> TryAcquireAsync(...)
    {
        var semaphore = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        var acquired = await semaphore.WaitAsync(timeout, cancellationToken);
        
        return acquired 
            ? new MemoryLockHandle(key, semaphore, () => _locks.TryRemove(key, out _))
            : null;
    }
}
```

---

#### 1.3 Redis 实现

**新增**: `src/Catga.Persistence.Redis/RedisDistributedLock.cs`

```csharp
public sealed class RedisDistributedLock : IDistributedLock
{
    private readonly IConnectionMultiplexer _redis;
    
    public async Task<ILockHandle?> TryAcquireAsync(...)
    {
        var db = _redis.GetDatabase();
        var lockId = Guid.NewGuid().ToString();
        
        // SET key value NX PX timeout
        var acquired = await db.StringSetAsync(
            key, 
            lockId, 
            timeout, 
            When.NotExists);
            
        return acquired 
            ? new RedisLockHandle(key, lockId, db)
            : null;
    }
}
```

---

#### 1.4 使用示例

```csharp
public class PaymentHandler : IRequestHandler<ProcessPaymentCommand, PaymentResponse>
{
    private readonly IDistributedLock _lock;
    
    public async Task<CatgaResult<PaymentResponse>> Handle(...)
    {
        // Acquire lock to prevent duplicate payments
        await using var lockHandle = await _lock.TryAcquireAsync(
            $"payment:{request.OrderId}", 
            TimeSpan.FromSeconds(30));
            
        if (lockHandle == null)
            return CatgaResult<PaymentResponse>.Failure("Payment already processing");
            
        // Process payment
        // ...
    }
}
```

---

### 验收标准

- [ ] 接口定义完成
- [ ] 内存实现完成
- [ ] Redis 实现完成
- [ ] 单元测试
- [ ] 集成测试
- [ ] 文档和示例

---

## 🔄 P1-2: Saga 模式

### 目标
实现 Saga 编排器支持分布式事务

### 任务清单

#### 2.1 定义 Saga 接口

**新增**: `src/Catga/Saga/ISaga.cs`

```csharp
public interface ISaga
{
    string SagaId { get; }
    IReadOnlyList<ISagaStep> Steps { get; }
}

public interface ISagaStep
{
    string StepId { get; }
    Task<StepResult> ExecuteAsync(CancellationToken cancellationToken);
    Task CompensateAsync(CancellationToken cancellationToken);
}

public interface ISagaOrchestrator
{
    Task<SagaResult> ExecuteAsync(ISaga saga, CancellationToken cancellationToken = default);
}
```

---

#### 2.2 实现 Saga 编排器

**新增**: `src/Catga/Saga/SagaOrchestrator.cs`

```csharp
public sealed class SagaOrchestrator : ISagaOrchestrator
{
    private readonly ILogger<SagaOrchestrator> _logger;
    private readonly ISagaStateStore _stateStore;
    
    public async Task<SagaResult> ExecuteAsync(ISaga saga, CancellationToken ct)
    {
        var executedSteps = new List<ISagaStep>();
        
        try
        {
            foreach (var step in saga.Steps)
            {
                _logger.LogInformation("Executing step {StepId}", step.StepId);
                
                var result = await step.ExecuteAsync(ct);
                if (!result.IsSuccess)
                {
                    _logger.LogWarning("Step {StepId} failed, compensating...", step.StepId);
                    await CompensateAsync(executedSteps, ct);
                    return SagaResult.Failure(result.Error);
                }
                
                executedSteps.Add(step);
                await _stateStore.SaveProgressAsync(saga.SagaId, step.StepId);
            }
            
            return SagaResult.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Saga {SagaId} failed", saga.SagaId);
            await CompensateAsync(executedSteps, ct);
            throw;
        }
    }
    
    private async Task CompensateAsync(List<ISagaStep> steps, CancellationToken ct)
    {
        // Compensate in reverse order
        for (int i = steps.Count - 1; i >= 0; i--)
        {
            try
            {
                await steps[i].CompensateAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Compensation failed for step {StepId}", steps[i].StepId);
            }
        }
    }
}
```

---

#### 2.3 使用示例

```csharp
public class OrderSaga : ISaga
{
    public string SagaId => _orderId;
    
    public IReadOnlyList<ISagaStep> Steps => new ISagaStep[]
    {
        new ReserveInventoryStep(_orderId),
        new ProcessPaymentStep(_orderId),
        new CreateShipmentStep(_orderId)
    };
}

public class ReserveInventoryStep : ISagaStep
{
    public async Task<StepResult> ExecuteAsync(CancellationToken ct)
    {
        // Reserve inventory
        var reserved = await _inventory.ReserveAsync(_orderId, ct);
        return reserved 
            ? StepResult.Success() 
            : StepResult.Failure("Out of stock");
    }
    
    public async Task CompensateAsync(CancellationToken ct)
    {
        // Release inventory
        await _inventory.ReleaseAsync(_orderId, ct);
    }
}

// Usage
var saga = new OrderSaga(orderId);
var result = await _sagaOrchestrator.ExecuteAsync(saga);
```

---

### 验收标准

- [ ] Saga 接口定义
- [ ] 编排器实现
- [ ] 状态持久化
- [ ] 补偿逻辑
- [ ] 单元测试
- [ ] 示例项目
- [ ] 文档

---

## 💚 P1-3: 健康检查

### 目标
提供健康检查抽象和实现

### 任务清单

#### 3.1 定义接口

**新增**: `src/Catga/HealthCheck/IHealthCheck.cs`

```csharp
public interface IHealthCheck
{
    string Name { get; }
    Task<HealthCheckResult> CheckAsync(CancellationToken cancellationToken = default);
}

public sealed class HealthCheckResult
{
    public HealthStatus Status { get; init; }
    public string? Description { get; init; }
    public TimeSpan Duration { get; init; }
    public Dictionary<string, object>? Data { get; init; }
    
    public static HealthCheckResult Healthy(string? description = null) => ...
    public static HealthCheckResult Degraded(string? description = null) => ...
    public static HealthCheckResult Unhealthy(string? description = null) => ...
}

public enum HealthStatus
{
    Healthy,
    Degraded,
    Unhealthy
}
```

---

#### 3.2 内置健康检查

**新增**: `src/Catga/HealthCheck/CatgaHealthCheck.cs`

```csharp
public sealed class CatgaHealthCheck : IHealthCheck
{
    public string Name => "Catga";
    
    public async Task<HealthCheckResult> CheckAsync(CancellationToken ct)
    {
        var data = new Dictionary<string, object>
        {
            ["handlers"] = _handlerCache.GetStatistics().TotalRequests,
            ["cache_hit_rate"] = _handlerCache.GetStatistics().HitRate,
            ["circuit_breaker_state"] = _circuitBreaker?.State.ToString() ?? "N/A"
        };
        
        return HealthCheckResult.Healthy("Catga is running", data);
    }
}
```

**新增**: NATS, Redis, Database 健康检查

---

#### 3.3 健康检查服务

**新增**: `src/Catga/HealthCheck/HealthCheckService.cs`

```csharp
public sealed class HealthCheckService
{
    private readonly IEnumerable<IHealthCheck> _healthChecks;
    
    public async Task<HealthReport> CheckAllAsync(CancellationToken ct)
    {
        var results = new Dictionary<string, HealthCheckResult>();
        
        foreach (var check in _healthChecks)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var result = await check.CheckAsync(ct);
                results[check.Name] = result with { Duration = sw.Elapsed };
            }
            catch (Exception ex)
            {
                results[check.Name] = HealthCheckResult.Unhealthy(ex.Message);
            }
        }
        
        return new HealthReport(results);
    }
}
```

---

#### 3.4 ASP.NET Core 集成

```csharp
app.MapGet("/health", async (HealthCheckService healthCheck) =>
{
    var report = await healthCheck.CheckAllAsync();
    return report.Status == HealthStatus.Healthy 
        ? Results.Ok(report) 
        : Results.StatusCode(503);
});
```

---

### 验收标准

- [ ] 接口定义
- [ ] 内置健康检查
- [ ] 健康检查服务
- [ ] ASP.NET Core 集成
- [ ] 单元测试
- [ ] 文档

---

## 🧵 P2-1: 线程池优化

### 目标
更好的线程管理和资源利用

### 任务清单

#### 1.1 添加线程池配置

**修改**: `src/Catga/Configuration/CatgaOptions.cs`

```csharp
public class CatgaOptions
{
    // 新增
    public ThreadPoolOptions ThreadPool { get; set; } = new();
}

public class ThreadPoolOptions
{
    public bool UseDedicatedThreadForBackgroundTasks { get; set; } = true;
    public int MinWorkerThreads { get; set; } = 10;
    public int MinIOThreads { get; set; } = 10;
    public int MaxEventHandlerConcurrency { get; set; } = 100;
}
```

---

#### 1.2 应用线程池配置

**修改**: `src/Catga/DependencyInjection/CatgaServiceCollectionExtensions.cs`

```csharp
public static IServiceCollection AddCatga(
    this IServiceCollection services, 
    Action<CatgaOptions>? configure = null)
{
    var options = new CatgaOptions();
    configure?.Invoke(options);
    
    // Apply thread pool settings
    if (options.ThreadPool.MinWorkerThreads > 0 || options.ThreadPool.MinIOThreads > 0)
    {
        ThreadPool.GetMinThreads(out var currentWorker, out var currentIO);
        ThreadPool.SetMinThreads(
            Math.Max(currentWorker, options.ThreadPool.MinWorkerThreads),
            Math.Max(currentIO, options.ThreadPool.MinIOThreads));
    }
    
    // ...
}
```

---

#### 1.3 修复长时间任务

**修改**: `src/Catga.ServiceDiscovery.Kubernetes/KubernetesServiceDiscovery.cs`

```csharp
// 旧代码
_ = Task.Run(async () => { /* watch */ });

// 新代码
_ = Task.Factory.StartNew(
    async () => { /* watch */ },
    TaskCreationOptions.LongRunning);
```

---

#### 1.4 事件处理并发限制

**修改**: `src/Catga/CatgaMediator.cs`

```csharp
public async Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct)
{
    // ...
    
    // 新增: 并发限制
    if (_options.ThreadPool.MaxEventHandlerConcurrency > 0)
    {
        using var semaphore = new SemaphoreSlim(_options.ThreadPool.MaxEventHandlerConcurrency);
        
        for (int i = 0; i < handlerList.Count; i++)
        {
            await semaphore.WaitAsync(ct);
            tasks[i] = ExecuteWithSemaphore(handlerList[i], @event, semaphore, ct);
        }
    }
    else
    {
        // 原有逻辑
    }
}
```

---

### 验收标准

- [ ] 线程池配置选项
- [ ] 配置应用逻辑
- [ ] 长时间任务修复
- [ ] 事件处理并发限制
- [ ] 单元测试
- [ ] 文档更新

---

## 📚 P2-2: Event Sourcing

### 目标
支持事件溯源模式

### 任务清单

#### 2.1 定义接口

**新增**: `src/Catga/EventSourcing/IEventStore.cs`

```csharp
public interface IEventStore
{
    Task AppendAsync(
        string streamId, 
        IEvent[] events, 
        long expectedVersion = -1,
        CancellationToken cancellationToken = default);
        
    Task<EventStream> ReadAsync(
        string streamId,
        long fromVersion = 0,
        int maxCount = int.MaxValue,
        CancellationToken cancellationToken = default);
}

public sealed class EventStream
{
    public string StreamId { get; init; }
    public long Version { get; init; }
    public IReadOnlyList<StoredEvent> Events { get; init; }
}

public sealed class StoredEvent
{
    public long Version { get; init; }
    public IEvent Event { get; init; }
    public DateTime Timestamp { get; init; }
}
```

---

#### 2.2 内存实现

**新增**: `src/Catga/EventSourcing/MemoryEventStore.cs`

```csharp
public sealed class MemoryEventStore : IEventStore
{
    private readonly ConcurrentDictionary<string, List<StoredEvent>> _streams = new();
    
    public Task AppendAsync(string streamId, IEvent[] events, long expectedVersion, CancellationToken ct)
    {
        var stream = _streams.GetOrAdd(streamId, _ => new List<StoredEvent>());
        
        lock (stream)
        {
            if (expectedVersion >= 0 && stream.Count != expectedVersion)
                throw new ConcurrencyException($"Expected version {expectedVersion}, but was {stream.Count}");
                
            foreach (var @event in events)
            {
                stream.Add(new StoredEvent
                {
                    Version = stream.Count,
                    Event = @event,
                    Timestamp = DateTime.UtcNow
                });
            }
        }
        
        return Task.CompletedTask;
    }
}
```

---

#### 2.3 Redis 实现

**新增**: `src/Catga.Persistence.Redis/RedisEventStore.cs`

使用 Redis Streams 实现

---

#### 2.4 Snapshot 支持

**新增**: `src/Catga/EventSourcing/ISnapshotStore.cs`

```csharp
public interface ISnapshotStore
{
    Task SaveAsync<T>(string streamId, long version, T state, CancellationToken ct);
    Task<Snapshot<T>?> LoadAsync<T>(string streamId, CancellationToken ct);
}
```

---

#### 2.5 使用示例

```csharp
public class OrderAggregate
{
    public string OrderId { get; private set; }
    public OrderStatus Status { get; private set; }
    private readonly List<IEvent> _uncommittedEvents = new();
    
    public void Create(string orderId, decimal amount)
    {
        Apply(new OrderCreatedEvent(orderId, amount));
    }
    
    public void Complete()
    {
        Apply(new OrderCompletedEvent(OrderId));
    }
    
    private void Apply(IEvent @event)
    {
        When(@event);
        _uncommittedEvents.Add(@event);
    }
    
    private void When(IEvent @event)
    {
        switch (@event)
        {
            case OrderCreatedEvent e:
                OrderId = e.OrderId;
                Status = OrderStatus.Created;
                break;
            case OrderCompletedEvent:
                Status = OrderStatus.Completed;
                break;
        }
    }
    
    public async Task SaveAsync(IEventStore eventStore)
    {
        await eventStore.AppendAsync(OrderId, _uncommittedEvents.ToArray());
        _uncommittedEvents.Clear();
    }
}
```

---

### 验收标准

- [ ] 接口定义
- [ ] 内存实现
- [ ] Redis 实现
- [ ] Snapshot 支持
- [ ] 单元测试
- [ ] 示例项目
- [ ] 文档

---

## 💾 P2-3: 分布式缓存

### 目标
提供分布式缓存抽象

### 任务清单

#### 3.1 定义接口

**新增**: `src/Catga/Caching/IDistributedCache.cs`

```csharp
public interface IDistributedCache
{
    Task<T?> GetAsync<T>(string key, CancellationToken ct = default);
    Task SetAsync<T>(string key, T value, TimeSpan expiration, CancellationToken ct = default);
    Task RemoveAsync(string key, CancellationToken ct = default);
    Task<bool> ExistsAsync(string key, CancellationToken ct = default);
}
```

---

#### 3.2 Redis 实现

**新增**: `src/Catga.Persistence.Redis/RedisDistributedCache.cs`

---

#### 3.3 缓存 Behavior

**新增**: `src/Catga/Pipeline/Behaviors/CachingBehavior.cs`

```csharp
public class CachingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : ICacheable
{
    private readonly IDistributedCache _cache;
    
    public async Task<CatgaResult<TResponse>> Handle(...)
    {
        var cacheKey = request.GetCacheKey();
        
        // Try get from cache
        var cached = await _cache.GetAsync<TResponse>(cacheKey);
        if (cached != null)
            return CatgaResult<TResponse>.Success(cached);
            
        // Execute
        var result = await next();
        
        // Cache result
        if (result.IsSuccess)
            await _cache.SetAsync(cacheKey, result.Value, request.CacheExpiration);
            
        return result;
    }
}
```

---

### 验收标准

- [ ] 接口定义
- [ ] Redis 实现
- [ ] 缓存 Behavior
- [ ] 单元测试
- [ ] 文档

---

## 📊 总体进度跟踪

### 里程碑

| 里程碑 | 目标日期 | 状态 |
|--------|---------|------|
| P0 完成 | Week 2 | 📋 待开始 |
| P1 完成 | Week 4 | 📋 待开始 |
| P2 完成 | Week 5 | 📋 待开始 |
| 发布 v2.0 | Week 6 | 📋 待开始 |

### 预期成果

| 指标 | 当前 | 目标 | 提升 |
|------|------|------|------|
| 综合评分 | 4.0 | 5.0 | +25% |
| 分析器规则 | 15 | 35 | +133% |
| 模板数量 | 0 | 4 | ∞ |
| 分布式功能 | 60% | 100% | +67% |
| 开发体验 | 70% | 95% | +36% |

---

## ✅ 下一步行动

1. **立即开始**: P0-1 源生成器重构
2. **并行进行**: P0-2 分析器扩展
3. **快速交付**: P0-3 Template 创建
4. **持续迭代**: P1/P2 功能

**让我们开始吧！** 🚀

