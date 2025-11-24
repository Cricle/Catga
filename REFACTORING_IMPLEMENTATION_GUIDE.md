# 🛠️ Catga 重构实施指南

**详细的代码改进步骤和示例**

---

## 🔗 MassTransit Parity Implementation Track

### Scope
- Developer-experience parity for core flows: Publish/Subscribe, Request/Response, Retry/CircuitBreaker, Outbox/Inbox, Dead Letter Queue, Observability, and endpoint naming across transports.
- Transports limited to InMemory/Redis/NATS; RabbitMQ/ASB/Kafka and others are excluded.
- Out of scope (this phase): Full Saga DSL, advanced topology for specific brokers. Can be evaluated later.

### API Surface (use existing, add minimal Proposed options)
- Use existing APIs:
  - `builder.Services.AddCatga()`
  - `builder.Services.AddInMemoryTransport()` / `AddRedisTransport(...)` / `AddNatsTransport(...)`
  - `ICatgaMediator.SendAsync<TReq,TRes>(...)`, `SendAsync<TReq>(...)`, `PublishAsync<TEvent>(...)`
- Proposed options (to add under CatgaOptions or equivalent):
  - `EndpointNamingConvention: Func<Type, string>` — default `{app}.{boundedContext}.{messageType}` lower-case dot-separated.
  - `EnableOutbox`, `EnableInbox`, `EnableDeadLetterQueue` — convenience toggles that register behaviors only if required services are present.
  - `DefaultRetryPolicy` and `EnableCircuitBreaker` — convenience bindings to existing behaviors.

### Endpoint Naming (Proposed)
- Default convention: `{app}.{boundedContext}.{messageType}` (lowercase, dot-separated).
- Transport mapping:
  - NATS → subject; Redis → channel; InMemory → topic.
- Provide override hook via `EndpointNamingConvention` option.

### Reliability
- Outbox behavior: requires `IOutboxStore + IMessageTransport + IMessageSerializer`.
- Inbox behavior: requires `IInboxStore + IMessageSerializer`.
- Dead Letter Queue: requires `IDeadLetterQueue` store.
- Action: add simple DI extensions (Proposed) that register the above behaviors only when dependencies exist; otherwise no-op.

### Observability (defaults)
- Ensure Activity + Metrics enabled by default; propagate `CorrelationId` via baggage.
- Minimal tags: `request_type`, `event_type`, `message_id`, `correlation_id`.

### Transport Alignment Tasks
1) InMemory: validate Publish/Subscribe and Request/Response parity using the naming convention.
2) Redis: map convention to channels; verify fan-out and consumer groups if applicable.
3) NATS: map convention to subjects; verify JetStream usage in persistence package where relevant.

### Tests (acceptance & conformance)
- Publish/Subscribe conformance across transports (same API, same naming, messages received once, order not guaranteed unless transport supports it).
- Request/Response: strong-typed response roundtrip; failure path returns `CatgaResult` with consistent errors.
- Outbox/Inbox/DLQ: integration tests verifying success, failure, deduplication, and replay.
- Retry/CircuitBreaker: policy configuration roundtrip; observable in logs/metrics.
- Observability: trace shows end-to-end spans in Jaeger.

### Examples
- Minimal “Hello Bus” (InMemory): one command handler and one event subscriber.
- Redis/NATS variants showing the same code with different transport registration.

### Acceptance Criteria
- Same API across transports with consistent endpoint naming (default + override).
- Reliability features usable with one-line toggles (when dependencies present) or explicit registration.
- Full trace for typical flows in Jaeger (with correlation id).
- No performance regression vs baseline; document any improvements with benchmarks.

---
## 第一阶段: 代码量减少

### 任务 1.1: 消除重复代码

#### 问题 1: CatgaMediator 中的重复 SendAsync 重载

**当前代码** (`CatgaMediator.cs` 第 52-194 行):

```csharp
// ❌ 重复的 SendAsync<TRequest, TResponse>
public async ValueTask<CatgaResult<TResponse>> SendAsync<TRequest, TResponse>(
    TRequest request,
    CancellationToken cancellationToken = default)
    where TRequest : IRequest<TResponse>
{
    // 52-127 行: 完整实现
}

// ❌ 重复的 SendAsync<TRequest> (无返回值)
public async Task<CatgaResult> SendAsync<TRequest>(
    TRequest request,
    CancellationToken cancellationToken = default)
    where TRequest : IRequest
{
    // 187-194 行: 类似实现
}
```

**改进方案**:

```csharp
// ✅ 统一的内部实现
private async ValueTask<CatgaResult<TResponse>> SendInternalAsync<TRequest, TResponse>(
    TRequest request,
    CancellationToken cancellationToken)
    where TRequest : IRequest<TResponse>
{
    var startTimestamp = Stopwatch.GetTimestamp();
    var reqType = TypeNameCache<TRequest>.Name;
    var message = request as IMessage;

    using var activity = CatgaActivitySource.Source.HasListeners()
        ? CatgaActivitySource.Source.StartActivity($"Command: {reqType}", ActivityKind.Internal)
        : null;

    // ... 统一的实现逻辑

    return result;
}

// ✅ 公开 API 委托给内部实现
public async ValueTask<CatgaResult<TResponse>> SendAsync<TRequest, TResponse>(
    TRequest request,
    CancellationToken cancellationToken = default)
    where TRequest : IRequest<TResponse>
    => await SendInternalAsync<TRequest, TResponse>(request, cancellationToken);

// ✅ 无返回值版本也委托
public async Task<CatgaResult> SendAsync<TRequest>(
    TRequest request,
    CancellationToken cancellationToken = default)
    where TRequest : IRequest
{
    var result = await SendInternalAsync<TRequest, Unit>(request, cancellationToken);
    return result.IsSuccess
        ? CatgaResult.Success()
        : CatgaResult.Failure(result.Error ?? "Unknown error", result.Exception);
}
```

**预期收益**: -50 LOC

---

#### 问题 2: Pipeline Behaviors 中的日志重复

**当前代码** (多个 Behavior 中重复):

```csharp
// ❌ LoggingBehavior.cs
var reqName = GetRequestName();
var msgId = TryGetMessageId(request) ?? 0;
var corrId = TryGetCorrelationId(request) ?? 0;
LogRequestStarted(reqName, msgId, corrId);

// ❌ RetryBehavior.cs
LogWarning("Retry {AttemptNumber}/{MaxAttempts} for {RequestType}",
    args.AttemptNumber, options.MaxRetryAttempts, GetRequestName());

// ❌ IdempotencyBehavior.cs
LogInformation("Message {MessageId} already processed - returning cached result", id);
```

**改进方案**:

```csharp
// ✅ Place an internal static helper in existing Pipeline namespace (no new folders)
public static class LoggingHelper
{
    public static void LogRequestStarted(
        ILogger logger,
        string requestType,
        long messageId,
        long correlationId)
    {
        logger.LogInformation(
            "Request started {RequestType} [MessageId={MessageId}, CorrelationId={CorrelationId}]",
            requestType, messageId, correlationId);
    }

    public static void LogRequestSucceeded(
        ILogger logger,
        string requestType,
        long messageId,
        long durationMs,
        long correlationId)
    {
        logger.LogInformation(
            "Request succeeded {RequestType} [MessageId={MessageId}, Duration={DurationMs}ms, CorrelationId={CorrelationId}]",
            requestType, messageId, durationMs, correlationId);
    }

    public static void LogRetry(
        ILogger logger,
        int attemptNumber,
        int maxAttempts,
        string requestType)
    {
        logger.LogWarning(
            "Retry {AttemptNumber}/{MaxAttempts} for {RequestType}",
            attemptNumber, maxAttempts, requestType);
    }

    public static void LogMessageAlreadyProcessed(
        ILogger logger,
        long messageId)
    {
        logger.LogInformation(
            "Message {MessageId} already processed - returning cached result",
            messageId);
    }
}

// ✅ 在各个 Behavior 中使用
public override async ValueTask<CatgaResult<TResponse>> HandleAsync(
    TRequest request,
    PipelineDelegate<TResponse> next,
    CancellationToken cancellationToken = default)
{
    var reqName = GetRequestName();
    var msgId = TryGetMessageId(request) ?? 0;
    var corrId = TryGetCorrelationId(request) ?? 0;

    LoggingHelper.LogRequestStarted(Logger, reqName, msgId, corrId);
    // ...
}
```

**预期收益**: -150 LOC

---

#### 问题 3: 异常处理模式重复

**当前代码** (多个地方):

```csharp
// ❌ CatgaMediator.cs
catch (Exception ex)
{
    var tags = new TagList { { "request_type", reqType }, { "success", "false" } };
    CatgaDiagnostics.CommandsExecuted.Add(1, tags);
    RecordException(activity, ex);
    CatgaLog.CommandFailed(_logger, ex, reqType, message?.MessageId, ex.Message);
    return CatgaResult<TResponse>.Failure(ErrorInfo.FromException(ex, ErrorCodes.PipelineFailed, isRetryable: false));
}

// ❌ OutboxBehavior.cs
catch (Exception ex)
{
    _logger.LogError(ex, "[Outbox] Error in outbox behavior for {RequestType}", TypeNameCache<TRequest>.Name);
    return CatgaResult<TResponse>.Failure(ErrorInfo.FromException(ex, ErrorCodes.PersistenceFailed, isRetryable: true));
}
```

**改进方案**:

```csharp
// ✅ Place an internal static helper in existing Core namespace (no new folders)
public static class ExceptionHelper
{
    public static CatgaResult<T> HandleException<T>(
        Exception ex,
        ILogger logger,
        string context,
        string requestType,
        string errorCode,
        bool isRetryable = false,
        Activity? activity = null)
    {
        logger.LogError(ex, "[{Context}] Error in {RequestType}", context, requestType);

        if (activity != null)
        {
            activity.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity.AddTag("exception.type", ex.GetType().Name);
            activity.AddTag("exception.message", ex.Message);
        }

        return CatgaResult<T>.Failure(
            ErrorInfo.FromException(ex, errorCode, isRetryable));
    }

    public static void RecordExceptionMetrics(
        string requestType,
        bool success,
        Activity? activity = null)
    {
        var tags = new TagList { { "request_type", requestType }, { "success", success ? "true" : "false" } };
        CatgaDiagnostics.CommandsExecuted.Add(1, tags);

        activity?.SetStatus(success ? ActivityStatusCode.Ok : ActivityStatusCode.Error);
    }
}

// ✅ 在各个地方使用
catch (Exception ex)
{
    return ExceptionHelper.HandleException<TResponse>(
        ex,
        _logger,
        "Outbox",
        TypeNameCache<TRequest>.Name,
        ErrorCodes.PersistenceFailed,
        isRetryable: true,
        activity);
}
```

**预期收益**: -200 LOC

---

### 任务 1.2: 提取 Helper 方法

#### 问题 4: Activity 标签设置重复

**当前代码** (CatgaMediator.cs 中多个地方):

```csharp
// ❌ SendAsync 中
activity.SetTag(CatgaActivitySource.Tags.CatgaType, "command");
activity.SetTag(CatgaActivitySource.Tags.RequestType, reqType);
activity.SetTag(CatgaActivitySource.Tags.MessageType, reqType);
if (message != null)
{
    activity.SetTag(CatgaActivitySource.Tags.MessageId, message.MessageId);
    if (message.CorrelationId.HasValue)
    {
        var correlationId = message.CorrelationId.Value;
        activity.SetTag(CatgaActivitySource.Tags.CorrelationId, correlationId);
        Span<char> buffer = stackalloc char[20];
        correlationId.TryFormat(buffer, out int written);
        activity.SetBaggage(CatgaActivitySource.Tags.CorrelationId, new string(buffer[..written]));
    }
}

// ❌ PublishAsync 中 (类似代码)
activity.SetTag(CatgaActivitySource.Tags.CatgaType, "event");
activity.SetTag(CatgaActivitySource.Tags.EventType, eventType);
// ... 更多重复
```

**改进方案**:

```csharp
// ✅ Add internal static helper within Observability namespace (no new folders)
public static class ActivityHelper
{
    public static void SetCommandTags(
        Activity? activity,
        string requestType,
        IMessage? message)
    {
        if (activity == null) return;

        activity.SetTag(CatgaActivitySource.Tags.CatgaType, "command");
        activity.SetTag(CatgaActivitySource.Tags.RequestType, requestType);
        activity.SetTag(CatgaActivitySource.Tags.MessageType, requestType);

        if (message != null)
        {
            SetMessageTags(activity, message);
        }
    }

    public static void SetEventTags(
        Activity? activity,
        string eventType,
        IMessage? message)
    {
        if (activity == null) return;

        activity.SetTag(CatgaActivitySource.Tags.CatgaType, "event");
        activity.SetTag(CatgaActivitySource.Tags.EventType, eventType);
        activity.SetTag(CatgaActivitySource.Tags.EventName, eventType);
        activity.SetTag(CatgaActivitySource.Tags.MessageType, eventType);

        if (message != null)
        {
            SetMessageTags(activity, message);
        }
    }

    private static void SetMessageTags(Activity activity, IMessage message)
    {
        activity.SetTag(CatgaActivitySource.Tags.MessageId, message.MessageId);

        if (message.CorrelationId.HasValue)
        {
            var correlationId = message.CorrelationId.Value;
            activity.SetTag(CatgaActivitySource.Tags.CorrelationId, correlationId);

            // Avoid boxing: format long directly to stack-allocated buffer
            Span<char> buffer = stackalloc char[20];
            correlationId.TryFormat(buffer, out int written);
            activity.SetBaggage(
                CatgaActivitySource.Tags.CorrelationId,
                new string(buffer[..written]));
        }
    }

    public static void SetSuccess(Activity? activity, bool success, double durationMs = 0)
    {
        if (activity == null) return;

        activity.SetTag(CatgaActivitySource.Tags.Success, success);
        if (durationMs > 0)
            activity.SetTag(CatgaActivitySource.Tags.Duration, durationMs);

        activity.SetStatus(success ? ActivityStatusCode.Ok : ActivityStatusCode.Error);
    }
}

// ✅ 在 CatgaMediator 中使用
using var activity = CatgaActivitySource.Source.HasListeners()
    ? CatgaActivitySource.Source.StartActivity($"Command: {reqType}", ActivityKind.Internal)
    : null;

ActivityHelper.SetCommandTags(activity, reqType, message);

// ... 执行逻辑

ActivityHelper.SetSuccess(activity, result.IsSuccess, duration);
```

**预期收益**: -180 LOC

---

#### 问题 5: 时间计算重复

**当前代码** (多个地方):

```csharp
// ❌ CatgaMediator.cs
var elapsed = Stopwatch.GetTimestamp() - startTimestamp;
return elapsed * 1000.0 / Stopwatch.Frequency;

// ❌ LoggingBehavior.cs
var elapsed = Stopwatch.GetTimestamp() - startTimestamp;
return elapsed * 1000.0 / Stopwatch.Frequency;

// ❌ DistributedTracingBehavior.cs
var elapsed = Stopwatch.GetTimestamp() - startTimestamp;
return elapsed * 1000.0 / Stopwatch.Frequency;
```

**改进方案**:

```csharp
// ✅ Add a small internal static Timing helper inside an existing file (no new folders)
public static class TimingHelper
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double GetElapsedMilliseconds(long startTimestamp)
    {
        var elapsed = Stopwatch.GetTimestamp() - startTimestamp;
        return elapsed * 1000.0 / Stopwatch.Frequency;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long GetElapsedMillisecondsLong(long startTimestamp)
    {
        return (long)GetElapsedMilliseconds(startTimestamp);
    }
}

// ✅ 在各个地方使用
var duration = TimingHelper.GetElapsedMilliseconds(startTimestamp);
```

**预期收益**: -100 LOC

---

### 任务 1.3: 简化 Pipeline Executor

**当前代码** (`Pipeline/PipelineExecutor.cs`):

```csharp
// ❌ 递归调用 + 结构体
public static async ValueTask<CatgaResult<TResponse>> ExecuteAsync<TRequest, TResponse>(
    TRequest request, IRequestHandler<TRequest, TResponse> handler,
    IList<IPipelineBehavior<TRequest, TResponse>> behaviors, CancellationToken cancellationToken)
    where TRequest : IRequest<TResponse>
{
    if (behaviors.Count == 0)
        return await handler.HandleAsync(request, cancellationToken);

    var context = new PipelineContext<TRequest, TResponse>
    {
        Request = request,
        Handler = handler,
        Behaviors = behaviors,
        CancellationToken = cancellationToken
    };
    return await ExecuteBehaviorAsync(context, 0);
}

private static async ValueTask<CatgaResult<TResponse>> ExecuteBehaviorAsync<TRequest, TResponse>(
    PipelineContext<TRequest, TResponse> context, int index) where TRequest : IRequest<TResponse>
{
    if (index >= context.Behaviors.Count)
        return await context.Handler.HandleAsync(context.Request, context.CancellationToken);

    var behavior = context.Behaviors[index];
    ValueTask<CatgaResult<TResponse>> next() => ExecuteBehaviorAsync(context, index + 1);
    return await behavior.HandleAsync(context.Request, next, context.CancellationToken);
}

private struct PipelineContext<TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    public TRequest Request;
    public IRequestHandler<TRequest, TResponse> Handler;
    public IList<IPipelineBehavior<TRequest, TResponse>> Behaviors;
    public CancellationToken CancellationToken;
}
```

**改进方案**:

```csharp
// ✅ 迭代实现 (更简洁、更高效)
public static async ValueTask<CatgaResult<TResponse>> ExecuteAsync<TRequest, TResponse>(
    TRequest request,
    IRequestHandler<TRequest, TResponse> handler,
    IList<IPipelineBehavior<TRequest, TResponse>> behaviors,
    CancellationToken cancellationToken)
    where TRequest : IRequest<TResponse>
{
    if (behaviors.Count == 0)
        return await handler.HandleAsync(request, cancellationToken);

    // 构建委托链
    PipelineDelegate<TResponse> next = () => handler.HandleAsync(request, cancellationToken);

    // 从后向前构建行为链
    for (int i = behaviors.Count - 1; i >= 0; i--)
    {
        var behavior = behaviors[i];
        var currentNext = next;
        next = () => behavior.HandleAsync(request, currentNext, cancellationToken);
    }

    return await next();
}
```

**预期收益**: -50 LOC, 更好的可读性

---

## 第二阶段: 性能策略（Measurement-first, avoid micro-optimizations）

**Principles**:
- Benchmark before and after: maintain a stable baseline (BenchmarkDotNet) for Send/Publish/Batch.
- Prefer readability and maintainability over micro-optimizations.
- Optimize only when measurements show regression or a clear hotspot.
- Keep changes minimal during refactors; defer aggressive tuning.

**Actions**:
- Ensure benchmarks run and capture current numbers.
- After each refactor batch, compare against the baseline. If no regression, keep code simple.
- If regression is detected, localize the fix with the minimal readable change.

## 第三阶段: 架构清晰化

### 任务 3.1: 不创建新目录（最小化文件变更）

**Guidelines**:
- Do not add new top-level folders. Keep current layout.
- If a helper is needed, add internal static methods in existing files or create a single utility file under an existing namespace (e.g., Observability/Diagnostics.cs).
- Avoid moving files to reduce merge conflicts and regressions.

---

### 任务 3.2: 重构 CatgaMediator

**分离职责**:

```csharp
// ❌ 当前: CatgaMediator 做太多事情
public class CatgaMediator : ICatgaMediator
{
    // 路由、执行、日志、指标、追踪、异常处理...
}

// ✅ 改进: 职责分离
public class CatgaMediator : ICatgaMediator
{
    // 仅负责: 路由和执行

    private async ValueTask<CatgaResult<TResponse>> SendInternalAsync<TRequest, TResponse>(...)
    {
        // 核心路由和执行逻辑
    }
}

// 新建 MediatorHelper 处理日志、指标、追踪
public static class MediatorHelper
{
    public static void LogCommandStart(ILogger logger, string requestType, IMessage? message) { }
    public static void LogCommandEnd(ILogger logger, string requestType, CatgaResult result, double duration) { }
    public static void RecordMetrics(string requestType, bool success, double duration) { }
}
```

---

## 第四阶段: 注释规范化

### 任务 4.1: XML 文档注释模板（English-only comments）

**Template**:

```csharp
/// <summary>
/// One-line summary (<= 80 chars).
/// </summary>
/// <remarks>
/// Optional details:
/// - Performance characteristics (if relevant)
/// - Thread-safety notes (if relevant)
/// - AOT compatibility notes (if relevant)
/// </remarks>
/// <param name="request">The request message.</param>
/// <param name="cancellationToken">Cancellation token.</param>
/// <returns>Result wrapper describing success or failure.</returns>
/// <exception cref="ArgumentNullException">Thrown when request is null.</exception>
/// <exception cref="OperationCanceledException">Thrown when the operation is cancelled.</exception>
/// <example>
/// var result = await mediator.SendAsync(myRequest, ct);
/// if (result.IsSuccess) { /* ... */ }
/// </example>
public async ValueTask<CatgaResult<TResponse>> SendAsync<TRequest, TResponse>(
    TRequest request,
    CancellationToken cancellationToken = default)
    where TRequest : IRequest<TResponse>
{
    // ...
}
```

---

### 任务 4.2: 代码注释规范（English-only comments）

**Guidelines**:

```csharp
// Good: explain WHY, not WHAT
// Prefer clarity over clever micro-optimizations

// Good: mark hot paths explicitly when justified
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public void HotPath() { }

// Good: annotate AOT compatibility where needed
[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public THandler GetHandler<THandler>() { }

// Good: explain non-obvious control flow or decisions
// Fast path: singleton handler found; still need scope for behaviors
using var singletonScope = _serviceProvider.CreateScope();

// Avoid: repeating the code as comments or trivial notes
```

---

## 🎯 验证清单

### 代码质量检查

```bash
# 运行所有测试
dotnet test tests/Catga.Tests/Catga.Tests.csproj -v normal

# 检查代码覆盖率
dotnet test /p:CollectCoverage=true /p:CoverageFormat=opencover

# 分析代码复杂度
dotnet tool install -g Gendarme
gendarme src/Catga/bin/Release/net9.0/Catga.dll

# 检查代码重复
dotnet tool install -g Simian
simian src/Catga/**/*.cs
```

### 性能验证

```bash
# 运行性能基准
dotnet run -c Release --project benchmarks/Catga.Benchmarks/

# 对比性能变化
# 记录改进前后的数据:
# 改进前: 462 ns/op, 432 B
# 改进后: 420 ns/op, 380 B
```

### 文档验证

```bash
# 生成 API 文档
docfx docs/docfx.json

# 检查文档完整性
# 验证所有公开 API 都有 XML 注释
```

---

## 📋 实施顺序

1. **Refactor CatgaMediator** (deduplicate Send APIs; keep code readable)
2. **Refactor Pipeline Behaviors** (deduplicate logging/exception code; inline small helpers)
3. **Simplify PipelineExecutor** (iterative chain; improve readability)
4. **Extract minimal helpers** inside existing files or a single utility file (no new folders)
5. **Adopt English-only XML/docs/comments**
6. **Run full tests**
7. **Run benchmarks** and compare to baseline (no regression)
8. **Generate docs**

---

## 🔍 代码审查清单

在提交 PR 前检查:

- [ ] 所有测试通过 (100% 通过率)
- [ ] 代码覆盖率 >= 95%
- [ ] 没有新的警告信息
- [ ] 性能指标未退化
- [ ] XML 文档注释完整
- [ ] 代码注释清晰
- [ ] 没有代码重复
- [ ] 遵循命名规范
- [ ] 遵循编码风格

---

**最后更新**: 2025-11-23
**状态**: 📋 准备实施
