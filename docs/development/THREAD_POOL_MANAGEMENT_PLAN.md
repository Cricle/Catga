# 🔒 线程池管理与防护计划 v2.0

## 📊 现状分析

### 当前风险点

1. **QoS 0 (AtMostOnce) 误用** ⚠️ 高优先级
   - ❌ 当前实现：Fire-and-forget，立即返回，不等待完成
   - ❌ 问题：违背了 QoS 语义，QoS 0 应该是"尽力而为，不保证送达"，但应该等待处理完成
   - ✅ 正确理解：QoS 0 = 不保证通讯质量（网络可靠性），但本地处理应该等待完成

2. **无限制的 Task.WhenAll**
   - CatgaMediator: Event Handler 无并发控制
   - BatchOperationHelper: 可能一次创建数千个任务
   - 可能导致线程池饥饿和内存激增

3. **缺少熔断机制**
   - 下游服务失败时持续重试
   - 雪崩效应：一个服务故障导致整个系统崩溃
   - 需要断路器保护

4. **缺少背压机制**
   - 消息生产速度 > 消费速度时无限制堆积
   - 没有队列深度限制
   - OOM 风险

---

## 🎯 核心原则

### 1. 不要重新发明轮子
- ❌ 不要自己实现线程池监控
- ✅ 使用 .NET 内置的诊断工具（EventCounters, Metrics API）
- ✅ 依赖成熟的 APM 工具（Application Insights, Prometheus）

### 2. QoS 语义正确性
- **QoS 0 (AtMostOnce)**: 尽力交付，不保证可靠性，但本地处理**应该等待**
- **QoS 1 (AtLeastOnce)**: 至少交付一次，需要重试机制
- **QoS 2 (ExactlyOnce)**: 恰好一次，需要幂等性保证

### 3. 防御式编程
- 使用 SemaphoreSlim 限制并发
- 使用熔断器防止雪崩
- 使用超时避免资源泄漏
- 使用分块处理防止内存激增

---

## 🎯 改进策略

### 1. 使用 .NET 内置监控（不重新发明轮子）

#### 1.1 使用 EventCounters 和 Metrics API

```csharp
// src/Catga/Observability/CatgaDiagnostics.cs (扩展)
public static class CatgaDiagnostics
{
    // 现有指标...

    // ✅ 使用 .NET 内置的线程池指标，不自己实现监控
    // 线程池信息可通过以下方式获取：
    // - dotnet-counters: System.Runtime 的 threadpool-* 计数器
    // - Application Insights: 自动收集
    // - Prometheus: dotnet_threadpool_* 指标

    // 业务指标：任务队列状态
    public static readonly Counter<long> TasksQueued =
        Meter.CreateCounter<long>("catga.tasks.queued",
            description: "Total number of tasks queued for processing");

    public static readonly Counter<long> TasksRejected =
        Meter.CreateCounter<long>("catga.tasks.rejected",
            description: "Total number of tasks rejected due to backpressure");

    public static readonly Histogram<int> ConcurrentTasks =
        Meter.CreateHistogram<int>("catga.tasks.concurrent",
            description: "Number of concurrent tasks being processed");

    public static readonly Counter<long> CircuitBreakerOpened =
        Meter.CreateCounter<long>("catga.circuit_breaker.opened",
            description: "Number of times circuit breaker opened");
}
```

#### 1.2 集成 APM 工具

```csharp
// appsettings.json
{
  "ApplicationInsights": {
    "ConnectionString": "InstrumentationKey=...",
    "EnableDependencyTracking": true,
    "EnablePerformanceCountersCollection": true  // 自动收集线程池指标
  },
  "Prometheus": {
    "Enabled": true,
    "Port": 9090
  }
}

// Program.cs
builder.Services.AddApplicationInsightsTelemetry();  // 自动监控线程池
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics =>
    {
        metrics.AddRuntimeInstrumentation();  // 包含线程池指标
        metrics.AddPrometheusExporter();
    });
```

---

### 2. 并发限制与背压

#### 2.1 引入 SemaphoreSlim 限流

```csharp
// src/Catga/Core/ConcurrencyLimiter.cs
public sealed class ConcurrencyLimiter : IDisposable
{
    private readonly SemaphoreSlim _semaphore;
    private readonly int _maxConcurrency;
    private readonly ILogger<ConcurrencyLimiter>? _logger;

    public ConcurrencyLimiter(int maxConcurrency, ILogger<ConcurrencyLimiter>? logger = null)
    {
        if (maxConcurrency <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxConcurrency));

        _maxConcurrency = maxConcurrency;
        _semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        _logger = logger;
    }

    public int CurrentCount => _semaphore.CurrentCount;
    public int MaxConcurrency => _maxConcurrency;
    public int ActiveTasks => _maxConcurrency - _semaphore.CurrentCount;

    public async ValueTask<IDisposable> AcquireAsync(CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        CatgaDiagnostics.TasksQueued.Add(1);

        return new SemaphoreReleaser(_semaphore, () =>
        {
            var active = ActiveTasks;
            CatgaDiagnostics.QueueDepth.Record(active);

            if (active >= _maxConcurrency * 0.8)
                _logger?.LogWarning("High concurrency: {Active}/{Max}", active, _maxConcurrency);
        });
    }

    public bool TryAcquire(out IDisposable? releaser, TimeSpan timeout = default)
    {
        if (_semaphore.Wait(timeout == default ? TimeSpan.Zero : timeout))
        {
            releaser = new SemaphoreReleaser(_semaphore, null);
            return true;
        }

        CatgaDiagnostics.TasksRejected.Add(1);
        releaser = null;
        return false;
    }

    public void Dispose() => _semaphore?.Dispose();

    private sealed class SemaphoreReleaser : IDisposable
    {
        private SemaphoreSlim? _semaphore;
        private readonly Action? _onRelease;

        public SemaphoreReleaser(SemaphoreSlim semaphore, Action? onRelease)
        {
            _semaphore = semaphore;
            _onRelease = onRelease;
        }

        public void Dispose()
        {
            _onRelease?.Invoke();
            Interlocked.Exchange(ref _semaphore, null)?.Release();
        }
    }
}
```

#### 2.2 改进 InMemoryMessageTransport（正确的 QoS 语义）

```csharp
// src/Catga.Transport.InMemory/InMemoryMessageTransport.cs
public class InMemoryMessageTransport : IMessageTransport, IDisposable
{
    private readonly InMemoryIdempotencyStore _idempotencyStore = new();
    private readonly ConcurrencyLimiter _concurrencyLimiter;
    private readonly CircuitBreaker _circuitBreaker;
    private readonly ILogger<InMemoryMessageTransport>? _logger;

    public InMemoryMessageTransport(
        InMemoryTransportOptions? options = null,
        ILogger<InMemoryMessageTransport>? logger = null)
    {
        _logger = logger;
        options ??= new InMemoryTransportOptions();

        _concurrencyLimiter = new ConcurrencyLimiter(
            options.MaxConcurrency,
            logger);

        _circuitBreaker = new CircuitBreaker(
            options.CircuitBreakerThreshold,
            options.CircuitBreakerDuration,
            logger);
    }

    public async Task PublishAsync<TMessage>(TMessage message, TransportContext? context = null, CancellationToken cancellationToken = default)
        where TMessage : class
    {
        using var activity = CatgaDiagnostics.ActivitySource.StartActivity("Message.Publish", ActivityKind.Producer);

        var handlers = TypedSubscribers<TMessage>.GetHandlers();
        if (handlers.Count == 0) return;

        var ctx = context ?? new TransportContext
        {
            MessageId = MessageExtensions.NewMessageId(),
            MessageType = TypeNameCache<TMessage>.FullName,
            SentAt = DateTime.UtcNow
        };

        var msg = message as IMessage;
        var qos = msg?.QoS ?? QualityOfService.AtLeastOnce;

        CatgaDiagnostics.IncrementActiveMessages();
        try
        {
            switch (qos)
            {
                case QualityOfService.AtMostOnce:
                    // ✅ QoS 0: 尽力而为，本地处理等待完成，但不保证网络可靠性
                    // 不重试，失败即丢弃，但会等待本地处理完成
                    using (await _concurrencyLimiter.AcquireAsync(cancellationToken))
                    {
                        try
                        {
                            await _circuitBreaker.ExecuteAsync(() =>
                                ExecuteHandlersAsync(handlers, message, ctx));
                        }
                        catch (Exception ex)
                        {
                            // QoS 0: 失败即丢弃，记录日志但不抛出异常
                            _logger?.LogWarning(ex,
                                "QoS 0 message processing failed, discarding. MessageId: {MessageId}",
                                ctx.MessageId);
                        }
                    }
                    break;

                case QualityOfService.AtLeastOnce:
                    if ((msg?.DeliveryMode ?? DeliveryMode.WaitForResult) == DeliveryMode.WaitForResult)
                    {
                        // 同步模式：等待结果
                        using (await _concurrencyLimiter.AcquireAsync(cancellationToken))
                        {
                            await _circuitBreaker.ExecuteAsync(() =>
                                ExecuteHandlersAsync(handlers, message, ctx));
                        }
                    }
                    else
                    {
                        // 异步模式：后台重试
                        _ = DeliverWithRetryAsync(handlers, message, ctx, cancellationToken);
                    }
                    break;

                case QualityOfService.ExactlyOnce:
                    if (ctx.MessageId.HasValue && _idempotencyStore.IsProcessed(ctx.MessageId.Value))
                    {
                        activity?.SetTag("catga.idempotent", true);
                        return;
                    }

                    using (await _concurrencyLimiter.AcquireAsync(cancellationToken))
                    {
                        await _circuitBreaker.ExecuteAsync(() =>
                            ExecuteHandlersAsync(handlers, message, ctx));
                    }

                    if (ctx.MessageId.HasValue)
                        _idempotencyStore.MarkAsProcessed(ctx.MessageId.Value);
                    break;
            }

            CatgaDiagnostics.MessagesPublished.Add(1,
                new KeyValuePair<string, object?>("message_type", TypeNameCache<TMessage>.Name),
                new KeyValuePair<string, object?>("qos", qos.ToString()));
        }
        finally
        {
            CatgaDiagnostics.DecrementActiveMessages();
        }
    }

    private async Task DeliverWithRetryAsync<TMessage>(
        IReadOnlyList<Delegate> handlers,
        TMessage message,
        TransportContext context,
        CancellationToken cancellationToken)
        where TMessage : class
    {
        _ = Task.Run(async () =>
        {
            using (await _concurrencyLimiter.AcquireAsync(cancellationToken).ConfigureAwait(false))
            {
                for (int attempt = 0; attempt <= 3; attempt++)
                {
                    try
                    {
                        await _circuitBreaker.ExecuteAsync(() =>
                            ExecuteHandlersAsync(handlers, message, context)).ConfigureAwait(false);
                        return;
                    }
                    catch (CircuitBreakerOpenException)
                    {
                        // 熔断器打开，停止重试
                        _logger?.LogWarning("Circuit breaker open, stopping retry for message {MessageId}",
                            context.MessageId);
                        break;
                    }
                    catch when (attempt < 3)
                    {
                        await Task.Delay(
                            TimeSpan.FromMilliseconds(100 * Math.Pow(2, attempt)),
                            cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "Final retry failed for message {MessageId}", context.MessageId);
                    }
                }
            }
        }, cancellationToken);
    }

    public void Dispose()
    {
        _concurrencyLimiter?.Dispose();
        _circuitBreaker?.Dispose();
    }
}

public class InMemoryTransportOptions
{
    /// <summary>最大并发任务数（默认：CPU核心数 * 2，最小16）</summary>
    public int MaxConcurrency { get; set; } = Math.Max(Environment.ProcessorCount * 2, 16);

    /// <summary>熔断器：连续失败次数阈值（默认：5次）</summary>
    public int CircuitBreakerThreshold { get; set; } = 5;

    /// <summary>熔断器：打开状态持续时间（默认：30秒）</summary>
    public TimeSpan CircuitBreakerDuration { get; set; } = TimeSpan.FromSeconds(30);
}
```

---

### 3. 优化批量操作

#### 3.1 分块处理 Task.WhenAll

```csharp
// src/Catga/Core/BatchOperationHelper.cs (改进)
public static class BatchOperationHelper
{
    private const int DefaultChunkSize = 100; // 默认每批100个任务

    /// <summary>
    /// 批量执行操作，自动分块防止线程池饥饿
    /// </summary>
    public static async Task ExecuteBatchAsync<T>(
        IEnumerable<T> items,
        Func<T, Task> operation,
        int chunkSize = DefaultChunkSize,
        CancellationToken cancellationToken = default)
    {
        var itemList = items as IList<T> ?? items.ToList();
        if (itemList.Count == 0) return;

        // 小批量：直接执行
        if (itemList.Count <= chunkSize)
        {
            var tasks = new Task[itemList.Count];
            for (int i = 0; i < itemList.Count; i++)
                tasks[i] = operation(itemList[i]);

            await Task.WhenAll(tasks).ConfigureAwait(false);
            return;
        }

        // 大批量：分块执行
        for (int i = 0; i < itemList.Count; i += chunkSize)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var end = Math.Min(i + chunkSize, itemList.Count);
            var chunkTasks = new Task[end - i];

            for (int j = i; j < end; j++)
                chunkTasks[j - i] = operation(itemList[j]);

            await Task.WhenAll(chunkTasks).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 并发控制的批量操作（使用 SemaphoreSlim）
    /// </summary>
    public static async Task ExecuteConcurrentBatchAsync<T>(
        IEnumerable<T> items,
        Func<T, Task> operation,
        int maxConcurrency,
        CancellationToken cancellationToken = default)
    {
        using var semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        var tasks = new List<Task>();

        foreach (var item in items)
        {
            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    await operation(item).ConfigureAwait(false);
                }
                finally
                {
                    semaphore.Release();
                }
            }, cancellationToken));
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }
}
```

#### 3.2 优化 CatgaMediator 事件分发

```csharp
// src/Catga/CatgaMediator.cs (改进)
public class CatgaMediator : ICatgaMediator
{
    private readonly ConcurrencyLimiter _eventConcurrencyLimiter;

    public CatgaMediator(CatgaOptions options, ...)
    {
        // ...
        _eventConcurrencyLimiter = new ConcurrencyLimiter(
            options.MaxEventHandlerConcurrency ??
            Math.Max(Environment.ProcessorCount * 4, 32));
    }

    public async Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : class, IEvent
    {
        // ... 现有代码 ...

        var handlerList = handlers as IReadOnlyList<IEventHandler<TEvent>> ?? handlers.ToList();
        if (handlerList.Count == 0) return;

        // 小批量：直接并发
        if (handlerList.Count <= 10)
        {
            var tasks = new Task[handlerList.Count];
            for (int i = 0; i < handlerList.Count; i++)
                tasks[i] = HandleEventSafelyAsync(handlerList[i], @event, cancellationToken);

            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        else
        {
            // 大批量：使用并发控制
            await BatchOperationHelper.ExecuteConcurrentBatchAsync(
                handlerList,
                handler => HandleEventSafelyAsync(handler, @event, cancellationToken),
                maxConcurrency: _eventConcurrencyLimiter.MaxConcurrency,
                cancellationToken).ConfigureAwait(false);
        }

        CatgaLog.EventPublished(_logger, eventType, message?.MessageId, handlerList.Count);
    }
}

public class CatgaOptions
{
    // 现有选项...

    /// <summary>Event Handler 最大并发数（默认：CPU核心数 * 4，最小32）</summary>
    public int? MaxEventHandlerConcurrency { get; set; }
}
```

---

### 4. 熔断器模式（防止雪崩）

#### 4.1 轻量级熔断器实现

```csharp
// src/Catga/Resilience/CircuitBreaker.cs
public sealed class CircuitBreaker : IDisposable
{
    private readonly int _failureThreshold;
    private readonly TimeSpan _openDuration;
    private readonly ILogger? _logger;

    // 无锁设计
    private volatile int _consecutiveFailures;
    private volatile long _lastFailureTimeTicks;
    private volatile int _state; // 0=Closed, 1=Open, 2=HalfOpen

    public CircuitBreaker(
        int failureThreshold = 5,
        TimeSpan? openDuration = null,
        ILogger? logger = null)
    {
        if (failureThreshold <= 0)
            throw new ArgumentOutOfRangeException(nameof(failureThreshold));

        _failureThreshold = failureThreshold;
        _openDuration = openDuration ?? TimeSpan.FromSeconds(30);
        _logger = logger;
        _state = (int)CircuitState.Closed;
    }

    public CircuitState State => (CircuitState)Volatile.Read(ref _state);
    public int ConsecutiveFailures => Volatile.Read(ref _consecutiveFailures);

    public async Task ExecuteAsync(Func<Task> operation)
    {
        CheckState();

        try
        {
            await operation().ConfigureAwait(false);
            OnSuccess();
        }
        catch (Exception ex)
        {
            OnFailure(ex);
            throw;
        }
    }

    public async Task<T> ExecuteAsync<T>(Func<Task<T>> operation)
    {
        CheckState();

        try
        {
            var result = await operation().ConfigureAwait(false);
            OnSuccess();
            return result;
        }
        catch (Exception ex)
        {
            OnFailure(ex);
            throw;
        }
    }

    private void CheckState()
    {
        var currentState = (CircuitState)Volatile.Read(ref _state);

        if (currentState == CircuitState.Open)
        {
            var lastFailureTicks = Volatile.Read(ref _lastFailureTimeTicks);
            var elapsed = TimeSpan.FromTicks(DateTime.UtcNow.Ticks - lastFailureTicks);

            if (elapsed >= _openDuration)
            {
                // 尝试切换到 HalfOpen（只有一个线程能成功）
                var original = Interlocked.CompareExchange(
                    ref _state,
                    (int)CircuitState.HalfOpen,
                    (int)CircuitState.Open);

                if (original == (int)CircuitState.Open)
                {
                    _logger?.LogInformation("Circuit breaker transitioning to Half-Open state");
                }
            }
            else
            {
                CatgaDiagnostics.CircuitBreakerOpened.Add(1);
                throw new CircuitBreakerOpenException(
                    $"Circuit breaker is open. Retry after {(_openDuration - elapsed).TotalSeconds:F1}s");
            }
        }
    }

    private void OnSuccess()
    {
        var currentState = (CircuitState)Volatile.Read(ref _state);

        if (currentState != CircuitState.Closed)
        {
            Volatile.Write(ref _consecutiveFailures, 0);
            Volatile.Write(ref _state, (int)CircuitState.Closed);
            _logger?.LogInformation("Circuit breaker closed after successful operation");
        }
        else
        {
            // 正常情况下也重置失败计数
            Volatile.Write(ref _consecutiveFailures, 0);
        }
    }

    private void OnFailure(Exception ex)
    {
        Volatile.Write(ref _lastFailureTimeTicks, DateTime.UtcNow.Ticks);
        var failures = Interlocked.Increment(ref _consecutiveFailures);

        _logger?.LogWarning(ex,
            "Circuit breaker recorded failure #{Failures}/{Threshold}",
            failures, _failureThreshold);

        if (failures >= _failureThreshold)
        {
            var original = Interlocked.CompareExchange(
                ref _state,
                (int)CircuitState.Open,
                (int)CircuitState.Closed);

            if (original == (int)CircuitState.Closed)
            {
                CatgaDiagnostics.CircuitBreakerOpened.Add(1);
                _logger?.LogError(
                    "Circuit breaker opened after {Failures} consecutive failures. Duration: {Duration}s",
                    failures, _openDuration.TotalSeconds);
            }
        }
    }

    public void Reset()
    {
        Volatile.Write(ref _consecutiveFailures, 0);
        Volatile.Write(ref _state, (int)CircuitState.Closed);
        _logger?.LogInformation("Circuit breaker manually reset");
    }

    public void Dispose()
    {
        // 无资源需要释放，但保留接口用于未来扩展
    }

    public enum CircuitState
    {
        Closed = 0,   // 正常工作
        Open = 1,     // 熔断打开，拒绝请求
        HalfOpen = 2  // 半开状态，尝试恢复
    }
}

public class CircuitBreakerOpenException : Exception
{
    public CircuitBreakerOpenException(string message) : base(message) { }
}
```

#### 4.2 CatgaMediator 集成熔断器

```csharp
// src/Catga/CatgaMediator.cs (部分)
public class CatgaMediator : ICatgaMediator
{
    private readonly CircuitBreaker _circuitBreaker;

    public CatgaMediator(CatgaOptions options, ...)
    {
        // ...
        _circuitBreaker = new CircuitBreaker(
            options.CircuitBreakerThreshold ?? 5,
            options.CircuitBreakerDuration ?? TimeSpan.FromSeconds(30),
            logger);
    }

    public async Task<TResponse> SendAsync<TResponse>(
        IRequest<TResponse> request,
        CancellationToken cancellationToken = default)
    {
        // ... validation ...

        // 使用熔断器保护
        return await _circuitBreaker.ExecuteAsync(async () =>
        {
            // ... 现有逻辑 ...
        });
    }
}

public class CatgaOptions
{
    // ... 现有选项 ...

    /// <summary>熔断器：连续失败次数阈值（默认：5次）</summary>
    public int? CircuitBreakerThreshold { get; set; }

    /// <summary>熔断器：打开状态持续时间（默认：30秒）</summary>
    public TimeSpan? CircuitBreakerDuration { get; set; }
}
```

---

### 5. 配置建议

#### 5.1 appsettings.json

```json
{
  "Catga": {
    "ThreadPool": {
      "MonitoringEnabled": true,
      "WarningThreshold": 80,
      "CriticalThreshold": 95
    },
    "Concurrency": {
      "MaxEventHandlers": 100,
      "MaxTransportTasks": 50,
      "MaxBatchSize": 100,
      "ChunkSize": 50
    },
    "Resilience": {
      "DefaultTimeout": "00:00:30",
      "CircuitBreakerThreshold": 5,
      "CircuitBreakerDuration": "00:00:30"
    }
  }
}
```

#### 5.2 DI 配置

```csharp
// Startup.cs or Program.cs
services.AddCatga(options =>
{
    options.MaxEventHandlerConcurrency = 100;
    options.DefaultCommandTimeout = TimeSpan.FromSeconds(30);
    options.EnableThreadPoolMonitoring = true;
})
.UseMemoryPack()
.ForDevelopment();

// 注册线程池监控
services.AddSingleton<ThreadPoolHealthMonitor>();
services.AddHostedService<ThreadPoolMonitoringService>();
```

---

## 📋 实施计划（简化版）

### Phase 1: 修复 QoS 0 语义（高优先级，1-2天）
- [ ] 修改 InMemoryMessageTransport.PublishAsync
  - QoS 0: 等待完成，但不重试（失败即丢弃）
  - QoS 1: 保留现有重试逻辑
  - QoS 2: 保留现有幂等性逻辑
- [ ] 更新单元测试
- [ ] 更新文档说明 QoS 语义

### Phase 2: 并发控制与熔断器（1周）
- [ ] 实现 ConcurrencyLimiter（基于 SemaphoreSlim）
- [ ] 实现 CircuitBreaker（无锁设计）
- [ ] 集成到 InMemoryMessageTransport
- [ ] 集成到 CatgaMediator
- [ ] 添加配置选项（InMemoryTransportOptions, CatgaOptions）

### Phase 3: 批量操作优化（3-5天）
- [ ] 改进 BatchOperationHelper.ExecuteBatchAsync（自动分块）
- [ ] 添加 ExecuteConcurrentBatchAsync（并发控制）
- [ ] 优化 CatgaMediator.PublishAsync 事件分发
- [ ] 性能基准测试

### Phase 4: 监控集成（2-3天）
- [ ] 扩展 CatgaDiagnostics 添加业务指标
- [ ] 集成 OpenTelemetry RuntimeInstrumentation
- [ ] 配置 Application Insights / Prometheus
- [ ] 验证指标可见性

### Phase 5: 测试与文档（1周）
- [ ] 单元测试（ConcurrencyLimiter, CircuitBreaker）
- [ ] 集成测试（QoS 行为验证）
- [ ] 压力测试（10K msg/s，100+ handlers）
- [ ] 更新文档和示例代码
- [ ] 性能报告

---

## 🧪 测试策略

### 压力测试场景

1. **高并发消息发布**
   - 10,000 消息/秒
   - 监控线程池使用率
   - 确保无死锁和饥饿

2. **大量事件订阅者**
   - 100+ handlers/event
   - 验证分块处理
   - 监控内存和 CPU

3. **慢处理器**
   - 模拟 5s 延迟的 handler
   - 验证超时机制
   - 确保不阻塞其他消息

4. **失败场景**
   - 模拟 handler 异常
   - 验证熔断器
   - 确保优雅降级

### 性能目标

- 🎯 P99 延迟 < 100ms
- 🎯 线程池使用率 < 70%
- 🎯 零死锁/饥饿
- 🎯 吞吐量 > 10K msg/s

---

## 📊 监控仪表板

### 关键指标

```
[线程池健康]
- Worker Threads Available: 📊
- IO Threads Available: 📊
- Usage Percent: 📊

[并发控制]
- Active Tasks: 📊
- Queued Tasks: 📊
- Rejected Tasks: 📊

[性能]
- Message Throughput: 📊
- P99 Latency: 📊
- Error Rate: 📊
```

---

## ⚠️ 告警规则

1. **🔴 CRITICAL**
   - 线程池使用率 > 95%
   - 连续 5 分钟任务被拒绝
   - P99 延迟 > 1s

2. **🟠 WARNING**
   - 线程池使用率 > 80%
   - 队列深度 > 1000
   - 熔断器打开

3. **🟡 INFO**
   - 线程池使用率 > 60%
   - 批量操作分块
   - 超时重试

---

## 🎓 最佳实践

### ✅ DO

1. **使用 ConfigureAwait(false)**
   ```csharp
   await SomeMethodAsync().ConfigureAwait(false);
   ```

2. **使用 SemaphoreSlim 限流**
   ```csharp
   using (await _limiter.AcquireAsync(cancellationToken))
   {
       await ProcessAsync();
   }
   ```

3. **分块处理大批量**
   ```csharp
   await BatchOperationHelper.ExecuteBatchAsync(items, Process, chunkSize: 100);
   ```

4. **使用熔断器保护下游调用**
   ```csharp
   await _circuitBreaker.ExecuteAsync(async () => await DownstreamCallAsync());
   ```

5. **正确理解 QoS 语义**
   - QoS 0: 本地处理等待完成，但不保证网络可靠性
   - QoS 1: 至少一次交付，带重试
   - QoS 2: 恰好一次，带幂等性

6. **使用 .NET 内置监控工具**
   ```csharp
   // 使用 OpenTelemetry
   builder.Services.AddOpenTelemetry()
       .WithMetrics(m => m.AddRuntimeInstrumentation());

   // 使用 dotnet-counters
   dotnet-counters monitor --process-id <pid> System.Runtime
   ```

### ❌ DON'T

1. **避免 .Result / .Wait()**
   ```csharp
   var result = SomeAsync().Result; // ❌ 死锁风险
   ```

2. **避免无限制 Task.WhenAll**
   ```csharp
   await Task.WhenAll(millionsOfTasks); // ❌ 线程池饥饿
   ```

3. **避免长时间占用线程**
   ```csharp
   await Task.Run(() => Thread.Sleep(10000)); // ❌
   await Task.Delay(10000); // ✅
   ```

4. **避免未限制的 Task.Run**
   ```csharp
   for (int i = 0; i < 10000; i++)
       _ = Task.Run(Process); // ❌ 线程池饥饿
   ```

5. **不要重新发明轮子**
   ```csharp
   // ❌ 不要自己实现线程池监控
   public class MyThreadPoolMonitor { /* ... */ }

   // ✅ 使用 .NET 内置工具
   services.AddOpenTelemetry().WithMetrics(m => m.AddRuntimeInstrumentation());
   ```

6. **不要误解 QoS 0（Fire-and-forget）**
   ```csharp
   // ❌ 错误：立即返回，不等待
   _ = Task.Run(async () => await ProcessAsync());

   // ✅ 正确：等待完成，但不重试
   try {
       await ProcessAsync();
   } catch {
       // 失败即丢弃，记录日志
   }
   ```

---

## 📚 参考资料

- [.NET Thread Pool Best Practices](https://docs.microsoft.com/en-us/dotnet/standard/threading/the-managed-thread-pool)
- [Async/Await Best Practices](https://docs.microsoft.com/en-us/archive/msdn-magazine/2013/march/async-await-best-practices-in-asynchronous-programming)
- [ConfigureAwait FAQ](https://devblogs.microsoft.com/dotnet/configureawait-faq/)
- [Task Parallel Library (TPL)](https://docs.microsoft.com/en-us/dotnet/standard/parallel-programming/task-parallel-library-tpl)

---

**更新日期**: 2025-10-21
**版本**: 1.0
**负责人**: Catga Team

