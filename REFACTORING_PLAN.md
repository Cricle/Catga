# 🔄 Catga 系统性重构计划

**目标**: 性能优化、代码量减少、架构更清晰、注释更规范

**当前代码库规模**:
- 源代码: 206 个 C# 文件
- 测试代码: 61 个 C# 文件
- 总计: 267 个文件

---

## 📊 重构目标

| 维度 | 当前状态 | 目标 | 优先级 |
|------|---------|------|--------|
| **代码行数** | ~15,000 LOC | -15% (12,750 LOC) | 🔴 高 |
| **性能** | 维持基线（不退化） | 按测量逐步优化（无过度优化） | 🔴 高 |
| **注释覆盖** | 部分 | 100% (关键路径) | 🟡 中 |
| **架构清晰度** | 良好 | 优秀 (分层更清晰) | 🟡 中 |
| **代码重复** | 低 | 零重复 (DRY) | 🟡 中 |
| **测试覆盖** | ~90% | 95%+ | 🟢 低 |

---

## 🔗 MassTransit 对标计划

### 目标与范围
- **目标**: 与 MassTransit 在核心使用体验与可靠性上保持一致的“感知等价”（developer experience parity），同时保持 Catga 的 CQRS/ES 优势与极简心智负担。
- **范围**: 发布/订阅、请求/响应、重试与熔断、Outbox/Inbox、死信队列、分布式追踪、端点命名约定、跨传输的一致 API 体验。
- **传输范围**: 仅聚焦 InMemory / Redis / NATS；不包含 RabbitMQ/ASB/Kafka 等其他代理。
- **非目标（本阶段）**: 完整 Saga 状态机 DSL、消息调度器的复杂拓扑、RabbitMQ/Azure Service Bus 专属拓扑（若需要，后续阶段评估）。

### 概念映射（MassTransit → Catga）
- **Consumer** → `IRequestHandler<TReq, TRes>` / `IEventHandler<TEvent>`
- **Publish/Send** → `PublishAsync(@event)` / `SendAsync(command)`
- **Request/Response** → `SendAsync<TReq, TRes>(req)`（强类型响应）
- **Middleware/Filters** → Pipeline Behaviors（Logging/Retry/Validation/Tracing/Idempotency）
- **Outbox/Inbox/DLQ** → OutboxBehavior/InboxBehavior/DeadLetterQueue（已有/可扩展）
- **Observability (OTel)** → ActivitySource + Metrics（已内置）
- **Endpoint/Topology** → 通过“命名约定与主题/频道规则”实现（见下方“端点命名”）

### 端点命名与主题/频道约定（Proposed）
- 统一默认规则：`{app}.{boundedContext}.{messageType}`（全小写，`.` 分隔）。
- 允许覆盖：提供“命名约定委托”的配置入口（Proposed API: endpoint naming convention）。
- 传输映射：
  - NATS: 使用 `subject = convention(messageType)`
  - Redis: 使用 `channel = convention(messageType)`
  - InMemory: 使用 `topic = convention(messageType)`

### 可观测性与可追踪（对标）
- 默认启用 OpenTelemetry Activity 与 Metrics。
- 传播 `CorrelationId`（Baggage & tags），事件-处理链路完整；提供最少即用默认标签（request_type/event_type/message_id）。

### 可靠性（对标）
- Outbox（发布前先落库）与 Inbox（去重+结果缓存）；
- 重试（指数退避，基于策略管道）与熔断（系统性故障保护）；
- Dead Letter Queue（失败记录与观测）。

### 易用性（对标）
- 5 分钟上手：`builder.Services.AddCatga();` + `AddInMemoryTransport()` 即可跑通 Pub/Sub 与 Req/Res。
- 端点命名约定开箱即用，覆盖配置可选。
- 示例与文档直达：提供最小工作示例（InMemory），并给出 Redis/NATS 样例。

### 验收标准（Acceptance Criteria）
- 发布/订阅：在 InMemory/Redis/NATS 下均可正常工作（同一套 API，无分支差异）。
- 请求/响应：强类型响应，失败有一致的错误模型（CatgaResult）。
- Outbox/Inbox/DLQ：在集成测试中可验证（成功/失败/重放/去重）。
- 重试/熔断：策略可配置，触发路径在负载测试中可观测（日志/指标/追踪）。
- 追踪：在 Jaeger/Zipkin 可看到完整链路（请求→处理→事件发布→事件处理）。
- 端点命名：默认规则一致，允许覆盖；在不同传输下表现一致。
- 示例：提供“Hello Bus”（最小示例）与电商示例的 Catga 版本。

### KPI（以“无退化”为底线，逐步优化）
- InMemory 端到端（Publish→Handle）P99 ≤ 5ms（本阶段）；后续阶段按测量优化。
- 无内存与吞吐退化（对比当前基线）；如有退化，定位并最小化修复。

### 里程碑（建议）
- **M1（1 周）**: Pub/Sub 与 Req/Res 在 InMemory 上统一 API 跑通；端点命名默认实现；基础追踪标签。
- **M2（1 周）**: Outbox/Inbox/DLQ 验收测试通过；重试/熔断策略校验；最小示例与文档。
- **M3（1 周）**: Redis/NATS 传输对齐（一致的命名规则与观测），CI 集成端到端测试。
- **M4（1 周）**: 基准对比与无退化门禁，优化 DX（错误消息/日志/模板项目）。

---

## �️ 设计原则（强大 / 简单 / 性能好 / 可跟踪 / 结构好 / 易用 / 友好）

- **Powerful（强大）**
  - Reliability first: Outbox/Inbox/DLQ + Retry + CircuitBreaker 均可一键启用（默认启用建议在非开发环境）。
  - Transport-agnostic: InMemory/Redis/NATS 同一 API 与命名约定。

- **Simple（简单）**
  - 1-liner 启动：`AddCatga()` + 单行传输注册即可跑通 Pub/Sub 与 Req/Res。
  - 约定优于配置：端点命名默认约定，可选覆盖。

- **Performant（性能好）**
  - Measurement-first：保持基线不退化；仅在证据驱动下优化热点。
  - 轻量 Pipeline：仅注册必要行为，避免过度中间层。

- **Traceable（可跟踪）**
  - 默认 OTel：Activity + Metrics + Baggage（CorrelationId）。
  - 最少可用标签：request_type、event_type、message_id、correlation_id。

- **Well-structured（结构好）**
  - 现有目录保持不变；必要时在现有命名空间内放置 `internal static` helper。
  - 单一职责：Mediator 仅负责路由与执行，其他通过行为/工具类实现。

- **Easy & Friendly（易用 / 友好）**
  - 错误信息可读、一致；失败路径有明确诊断（日志/指标/追踪）。
  - 提供最小示例与分步文档；API 命名贴近直觉。

---

## �🎯 重构阶段

### 第一阶段: 代码量减少 (1-2 周)

#### 1.1 消除重复代码 (目标: -800 LOC)

**问题识别**:
- `CatgaMediator.cs` 中多个相似的 `SendAsync` 重载
- Pipeline Behaviors 中重复的日志记录逻辑
- 多个地方重复的异常处理模式
- 类型名称缓存逻辑分散

**改进方案**:

```csharp
// ❌ 当前: 重复的 SendAsync 重载
public async ValueTask<CatgaResult<TResponse>> SendAsync<TRequest, TResponse>(TRequest request, ...)
public async Task<CatgaResult> SendAsync<TRequest>(TRequest request, ...)

// ✅ 改进: 统一的内部实现
private async ValueTask<CatgaResult<T>> SendInternalAsync<TRequest, T>(TRequest request, ...)
  where TRequest : IRequest<T>
{
    // 统一实现
}

// 公开 API 委托给内部实现
public async ValueTask<CatgaResult<TResponse>> SendAsync<TRequest, TResponse>(TRequest request, ...)
    => await SendInternalAsync<TRequest, TResponse>(request, ...);
```

**预期收益**: -200 LOC, 提高可维护性

#### 1.2 提取通用 Helper 方法 (目标: -600 LOC)

**问题识别**:
- 日志记录逻辑重复 (LoggingBehavior, CatgaMediator, HandleEventSafelyAsync)
- 异常处理模式重复 (多个 try-catch 块)
- Activity 标签设置重复
- 时间计算重复

**改进方案**:

```csharp
// 新建 ActivityHelper.cs
public static class ActivityHelper
{
    public static void SetRequestTags(Activity? activity, string requestType, IMessage? message)
    {
        if (activity == null) return;
        activity.SetTag(Tags.RequestType, requestType);
        activity.SetTag(Tags.MessageType, requestType);
        if (message != null)
        {
            activity.SetTag(Tags.MessageId, message.MessageId);
            // ... 其他标签
        }
    }
}

// 新建 ExceptionHelper.cs
public static class ExceptionHelper
{
    public static CatgaResult<T> HandleException<T>(Exception ex, string context, ILogger logger)
    {
        logger.LogError(ex, "Error in {Context}", context);
        return CatgaResult<T>.Failure(ErrorInfo.FromException(ex, ...));
    }
}
```

**预期收益**: -400 LOC, 提高代码复用率

#### 1.3 简化 Pipeline Executor (目标: -200 LOC)

**问题识别**:
- `PipelineExecutor` 中递归调用可优化
- `PipelineContext` 结构体可内联
- 不必要的泛型约束

**改进方案**:

```csharp
// ❌ 当前: 递归 + 结构体
private static async ValueTask<CatgaResult<TResponse>> ExecuteBehaviorAsync(
    PipelineContext<TRequest, TResponse> context, int index)

// ✅ 改进: 迭代 + 直接参数
private static async ValueTask<CatgaResult<TResponse>> ExecuteBehaviorAsync(
    TRequest request,
    IRequestHandler<TRequest, TResponse> handler,
    IList<IPipelineBehavior<TRequest, TResponse>> behaviors,
    int index,
    CancellationToken cancellationToken)
```

**预期收益**: -150 LOC, 更好的可读性

---

### 第二阶段: 性能策略（测量优先，避免过度优化）

**原则**:
- 以基准测试为先：使用 BenchmarkDotNet 维护稳定的基线，任何性能变更先测量后决策。
- 避免微优化：不做对可读性不友好的微调（如过度使用 `string.Create`、`TagList` 复用、过多的 `Volatile.Read`/`Interlocked` 细节）。
- 聚焦宏观收益：优先考虑去重、简化代码路径、改进算法与职责划分带来的性能收益。
- 性能门禁（可选）：在 CI 中对关键基准做“无退化”校验。

**执行**:
- 建立/更新基准项目，记录核心操作基线（Send/Publish/Batch）。
- 在重构完成后对比基线，若发现退化，再有针对性地优化热点；否则保持简洁实现。

---

### 第三阶段: 架构清晰化 (1 周)

#### 3.1 分层重构

**当前结构**:
```
src/Catga/
├── Abstractions/        (接口定义)
├── Core/               (核心实现)
├── Pipeline/           (管道)
├── Resilience/         (弹性)
├── Observability/      (可观测性)
├── DependencyInjection/(DI)
└── CatgaMediator.cs    (主类)
```

**简化方案（不新增顶层目录）**:
- 保持现有目录结构不变（Abstractions/Core/Pipeline/Resilience/Observability/DependencyInjection）。
- 如需提取共用方法，优先在现有文件内添加 `internal static` 辅助方法，或在现有命名空间下新增“单文件”工具类（例如 `Observability/Diagnostics.cs`）。
- 避免创建新的子层级目录，最小化移动文件，降低合并与回归风险。

**预期收益**: 架构更清晰但不打乱现有布局，降低改动面

#### 3.2 职责分离

**改进方案**:

| 类 | 当前职责 | 改进后职责 |
|-----|---------|----------|
| `CatgaMediator` | 路由、执行、日志、指标、追踪 | 仅路由和执行 |
| 新 `MediatorHelper` | - | 日志、指标、追踪 |
| 新 `ActivityHelper` | - | Activity 管理 |
| 新 `ExceptionHelper` | - | 异常处理 |

**预期收益**: 单一职责原则，更易测试

---

### 第四阶段: 注释规范化 (1 周)

#### 4.1 XML 文档注释标准（English-only comments）

**标准**:
```csharp
/// <summary>
/// One-line summary (<= 80 chars).
/// </summary>
/// <remarks>
/// Optional details.
/// - Performance characteristics (if relevant)
/// - Thread-safety notes (if relevant)
/// - AOT compatibility notes (if relevant)
/// </remarks>
/// <param name="request">The request message.</param>
/// <param name="cancellationToken">Cancellation token.</param>
/// <returns>Result wrapper describing success or failure.</returns>
/// <exception cref="ArgumentNullException">Thrown when request is null.</exception>
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

**覆盖范围**:
- ✅ 所有公开 API (100%)
- ✅ 所有公开类 (100%)
- ✅ 所有公开方法 (100%)
- ✅ 复杂的私有方法 (关键路径)
- ❌ 简单的私有方法 (不必要)

**预期收益**: 完整的 API 文档，改善开发体验

#### 4.2 代码注释规范

**Rules (English-only comments)**:
```csharp
// Good: explain WHY, not WHAT
// Optimize: Use stack-allocated buffer to avoid heap allocation on hot paths
Span<char> buffer = stackalloc char[20];

// Avoid: repeating code as comments
// Create a span
Span<char> buffer = stackalloc char[20];

// Good: mark hot paths explicitly
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public void HotPath() { }

// Good: mark AOT compatibility where needed
[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public THandler GetHandler<THandler>() { }
```

**标记规范**:
- `// Optimize:` - 性能优化
- `// HACK:` - 临时解决方案
- `// TODO:` - 待办项
- `// NOTE:` - 重要说明
- `// WARN:` - 警告

**预期收益**: 更好的代码可读性

---

## 📈 重构时间表

| 阶段 | 任务 | 时间 | 优先级 |
|------|------|------|--------|
| **第一阶段** | 代码量减少 | 1-2 周 | 🔴 高 |
| 1.1 | 消除重复代码 | 3-4 天 | 🔴 高 |
| 1.2 | 提取 Helper 方法 | 3-4 天 | 🔴 高 |
| 1.3 | 简化 Pipeline | 2-3 天 | 🟡 中 |
| **第二阶段** | 性能优化 | 1-2 周 | 🔴 高 |
| 2.1 | 热路径优化 | 3-4 天 | 🔴 高 |
| 2.2 | 内存分配优化 | 3-4 天 | 🔴 高 |
| 2.3 | 并发优化 | 2-3 天 | 🟡 中 |
| **第三阶段** | 架构清晰化 | 1 周 | 🟡 中 |
| 3.1 | 分层重构 | 3-4 天 | 🟡 中 |
| 3.2 | 职责分离 | 2-3 天 | 🟡 中 |
| **第四阶段** | 注释规范化 | 1 周 | 🟡 中 |
| 4.1 | XML 文档注释 | 3-4 天 | 🟡 中 |
| 4.2 | 代码注释规范 | 2-3 天 | 🟡 中 |
| **验证** | 测试 + 基准 | 1 周 | 🔴 高 |

**总计**: 4-5 周

---

## ✅ 验证标准

### 代码质量指标

```
✅ 代码行数: 15,000 → 12,750 (-15%)
✅ 圈复杂度: 平均 < 5 (关键路径)
✅ 注释覆盖: 100% (公开 API)
✅ 重复代码: 0% (DRY 原则)
✅ 测试覆盖: 95%+
```

### 性能指标（简化为“无退化”）

```
✅ 基线对齐：核心基准（Send/Publish/Batch）不低于当前主分支
✅ 若有优化：在基准文档中记录新数据与差异说明
✅ 如发现退化：仅在测量证实后进行针对性优化
```

### 架构指标

```
✅ 分层清晰度: 5/5
✅ 职责分离: 5/5
✅ 可维护性: 5/5
✅ 可测试性: 5/5
```

---

## 🔧 实施步骤

### 第一步: 创建分支

```bash
git checkout -b refactor/code-reduction
git checkout -b refactor/performance-optimization
git checkout -b refactor/architecture-cleanup
git checkout -b refactor/documentation
```

### 第二步: 逐个实施

1. **代码量减少** → 运行测试 → 性能基准 → PR
2. **性能优化** → 运行测试 → 性能基准 → PR
3. **架构清晰化** → 运行测试 → PR
4. **注释规范化** → 文档生成 → PR

### 第三步: 验证

```bash
# 运行所有测试
dotnet test tests/Catga.Tests/Catga.Tests.csproj

# 运行性能基准
dotnet run -c Release --project benchmarks/Catga.Benchmarks/

# 生成覆盖率报告
dotnet test /p:CollectCoverage=true

# 生成文档
docfx docs/docfx.json
```

---

## 📋 检查清单

- [ ] 第一阶段: 代码量减少
  - [ ] 消除重复代码
  - [ ] 提取 Helper 方法
  - [ ] 简化 Pipeline
  - [ ] 运行测试 (通过率 100%)
  - [ ] 性能基准 (无退化)

- [ ] 第二阶段: 性能优化
  - [ ] 热路径优化
  - [ ] 内存分配优化
  - [ ] 并发优化
  - [ ] 运行测试 (通过率 100%)
  - [ ] 性能基准 (达到目标)

- [ ] 第三阶段: 架构清晰化
  - [ ] 分层重构
  - [ ] 职责分离
  - [ ] 运行测试 (通过率 100%)

- [ ] 第四阶段: 注释规范化
  - [ ] XML 文档注释
  - [ ] 代码注释规范
  - [ ] 文档生成

- [ ] 最终验证
  - [ ] 所有测试通过
  - [ ] 性能指标达标
  - [ ] 代码覆盖率 95%+
  - [ ] 文档完整

---

## 🎯 关键成果

**重构完成后**:

1. **代码量**: 15,000 → 12,750 LOC (-15%)
2. **性能**: 462 ns → 420 ns (-9%)
3. **内存**: 432 B → 380 B (-12%)
4. **吞吐量**: 2.2M → 2.4M QPS (+9%)
5. **可维护性**: 显著提升
6. **文档**: 100% 覆盖
7. **架构**: 清晰、分层、易扩展

---

## 📞 联系方式

有问题或建议? 请在 GitHub Issues 中提出。

---

**最后更新**: 2025-11-23
**状态**: 📋 计划中
**下一步**: 开始第一阶段 (代码量减少)
