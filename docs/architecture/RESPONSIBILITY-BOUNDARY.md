# Catga 职责边界：框架 vs 基础设施

## 🎯 设计原则

**不重复造轮子**：充分利用成熟基础设施（NATS/Redis/K8s）的原生能力，Catga专注于应用层CQRS和业务增值功能。

---

## 📊 职责分工表

| 功能领域 | NATS/Redis 负责 | Catga 负责 | 说明 |
|---------|----------------|-----------|------|
| **消息传输** | ✅ 网络传输、持久化、集群 | ❌ 不重复实现 | 使用NATS/Redis原生能力 |
| **QoS 0 (AtMostOnce)** | ✅ Fire-and-forget发布 | ❌ 透传到基础设施 | NATS Core Pub/Sub |
| **QoS 1 (AtLeastOnce)** | ✅ 消息持久化、重发、ACK | ❌ 透传到基础设施 | NATS JetStream / Redis Stream |
| **QoS 2 (ExactlyOnce)** | ✅ 传输层去重（MsgId） | ✅ **业务层幂等性** | NATS去重窗口2分钟，Catga持久化幂等性 |
| **服务发现** | ✅ K8s DNS、Service、Endpoints | ❌ 不在应用层实现 | 使用K8s原生服务发现 |
| **负载均衡** | ✅ NATS Consumer Groups、K8s Service | ❌ 不在应用层实现 | 基础设施自动负载均衡 |
| **幂等性** | ❌ 仅短期去重（NATS 2分钟窗口） | ✅ **持久化业务幂等** | IdempotencyBehavior + Store |
| **重试策略** | ✅ 传输重试（网络失败） | ✅ **业务重试** | RetryBehavior + 指数退避 |
| **事务Outbox** | ❌ | ✅ **保证最终一致性** | OutboxBehavior + OutboxStore |
| **事务Inbox** | ❌ | ✅ **防止消息丢失** | InboxBehavior + InboxStore |
| **分布式追踪** | ❌ | ✅ **ActivitySource集成** | TracingBehavior + OpenTelemetry |
| **结构化日志** | ❌ | ✅ **LoggerMessage自动生成** | LoggingBehavior |
| **指标监控** | ❌ | ✅ **Meter/Counter/Histogram** | CatgaDiagnostics |
| **请求验证** | ❌ | ✅ **IValidator集成** | ValidationBehavior |
| **结果缓存** | ❌ | ✅ **IDistributedCache集成** | CachingBehavior |

---

## 🚀 NATS 原生能力（Catga直接使用）

### QoS 映射

```csharp
public enum QualityOfService
{
    // NATS Core Pub/Sub: 无ACK，无持久化
    AtMostOnce = 0,

    // NATS JetStream: 持久化 + ACK，可能重复
    AtLeastOnce = 1,

    // NATS JetStream + MsgId去重: NATS去重窗口（2分钟）
    // + Catga IdempotencyBehavior: 持久化业务幂等（24小时+）
    ExactlyOnce = 2
}
```

### NATS Transport 实现

```csharp
switch (qos)
{
    case QualityOfService.AtMostOnce:
        // 直接使用 NATS Core Pub/Sub
        await _connection.PublishAsync(subject, payload, headers, ct);
        break;

    case QualityOfService.AtLeastOnce:
        // 直接使用 JetStream（保证送达，可能重复）
        await _jsContext.PublishAsync(subject, payload,
            opts: new NatsJSPubOpts { MsgId = messageId }, headers, ct);
        break;

    case QualityOfService.ExactlyOnce:
        // 传输层: NATS JetStream MsgId去重（2分钟窗口）
        // 应用层: Catga IdempotencyBehavior（持久化业务幂等）
        await _jsContext.PublishAsync(subject, payload,
            opts: new NatsJSPubOpts { MsgId = messageId }, headers, ct);
        break;
}
```

**关键点**：
- ❌ **不在Transport层自己管理`_processedMessages`字典** - 这是重复实现！
- ✅ **完全依赖NATS JetStream的MsgId去重** - 短期（2分钟）传输层去重
- ✅ **应用层幂等性由IdempotencyBehavior负责** - 长期业务逻辑去重

---

## 📦 Redis 原生能力（Catga直接使用）

### Redis Streams for QoS 1

```csharp
// 使用 Redis Streams + Consumer Groups
// - XADD: 发布消息到Stream
// - XREADGROUP: 消费者组消费（自动负载均衡）
// - XACK: 消费确认
// - Pending List: 未ACK消息自动重试
```

### Redis Pub/Sub for QoS 0

```csharp
// 使用 Redis Pub/Sub
// - PUBLISH: 发布消息
// - SUBSCRIBE: 订阅消息
// - 无持久化，无ACK
```

**关键点**：
- ✅ **使用Redis原生Consumer Groups** - 自动负载均衡和故障转移
- ✅ **使用Redis Pending List** - 自动重试未ACK消息
- ❌ **不在应用层自己管理消费者分配** - 这是重复实现！

---

## 🏗️ Kubernetes 原生能力（Catga直接使用）

### 服务发现

```yaml
# K8s Service 自动提供 DNS
nats-jetstream.default.svc.cluster.local:4222
redis-cluster.default.svc.cluster.local:6379
```

```csharp
// Catga 直接使用 K8s DNS
builder.Services.AddNatsTransport("nats://nats-jetstream:4222");
builder.Services.AddRedisDistributed("redis-cluster:6379");
```

### 负载均衡

```yaml
apiVersion: v1
kind: Service
metadata:
  name: catga-service
spec:
  selector:
    app: catga
  ports:
    - port: 80
  type: ClusterIP  # K8s 自动负载均衡到多个 Pod
```

**关键点**：
- ✅ **使用K8s Service DNS** - 无需应用层服务发现
- ✅ **使用K8s Service负载均衡** - 无需应用层路由策略
- ❌ **不在应用层实现心跳、健康检查** - 这是重复实现！

---

## 🎨 Catga 核心增值功能（保留）

### 1. 持久化业务幂等性

```csharp
// NATS JetStream 只提供 2 分钟去重窗口
// Catga IdempotencyBehavior 提供持久化幂等性（可配置24小时+）

public class IdempotencyBehavior<TRequest, TResponse> : BaseBehavior<TRequest, TResponse>
{
    public override async ValueTask<CatgaResult<TResponse>> HandleAsync(
        TRequest request, PipelineDelegate<TResponse> next, CancellationToken ct)
    {
        var messageId = TryGetMessageId(request);

        // 检查持久化存储（Redis/DB）
        if (await _store.HasBeenProcessedAsync(messageId, ct))
            return await _store.GetCachedResultAsync<TResponse>(messageId, ct);

        var result = await next();

        // 仅缓存成功结果（失败结果允许重试）
        if (result.IsSuccess)
            await _store.MarkAsProcessedAsync(messageId, result.Value, ct);

        return result;
    }
}
```

**价值**：
- ✅ 跨越NATS 2分钟窗口限制
- ✅ 业务逻辑级别的幂等性保证
- ✅ 支持失败重试（不缓存失败结果）

### 2. 智能重试策略

```csharp
public class RetryBehavior<TRequest, TResponse> : BaseBehavior<TRequest, TResponse>
{
    public override async ValueTask<CatgaResult<TResponse>> HandleAsync(
        TRequest request, PipelineDelegate<TResponse> next, CancellationToken ct)
    {
        var maxAttempts = _options.MaxRetryAttempts;
        var delay = _options.RetryDelayMs;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var result = await next();
            if (result.IsSuccess) return result;

            if (attempt < maxAttempts)
                await Task.Delay(delay * attempt, ct); // 指数退避
        }

        return CatgaResult<TResponse>.Failure("Max retries exceeded");
    }
}
```

**价值**：
- ✅ 业务级别的智能重试（非传输层重试）
- ✅ 指数退避策略
- ✅ 可配置重试次数和延迟

### 3. 事务性Outbox模式

```csharp
public class OutboxBehavior<TRequest, TResponse> : BaseBehavior<TRequest, TResponse>
{
    public override async ValueTask<CatgaResult<TResponse>> HandleAsync(
        TRequest request, PipelineDelegate<TResponse> next, CancellationToken ct)
    {
        var result = await next();

        if (result.IsSuccess && request is IOutboxMessage outboxMsg)
        {
            // 保存到Outbox表，后台发布者异步发送
            await _outboxStore.SaveAsync(outboxMsg, ct);
        }

        return result;
    }
}
```

**价值**：
- ✅ 数据库事务 + 消息发送的原子性
- ✅ 保证最终一致性
- ✅ 避免数据更新成功但消息丢失

### 4. 可观测性集成

```csharp
// 分布式追踪
public static class CatgaDiagnostics
{
    public static readonly ActivitySource ActivitySource = new("Catga.CQRS");
    public static readonly Meter Meter = new("Catga.CQRS");

    public static readonly Counter<long> CommandsExecuted =
        Meter.CreateCounter<long>("catga.commands.executed");
    public static readonly Histogram<double> CommandDuration =
        Meter.CreateHistogram<double>("catga.commands.duration");
}

// 结构化日志（LoggerMessage自动生成）
public static partial class CatgaLog
{
    [LoggerMessage(Level = LogLevel.Information,
        Message = "Executing command {RequestType}, MessageId: {MessageId}")]
    public static partial void CommandExecuting(ILogger logger,
        string requestType, string? messageId, string? correlationId);
}
```

**价值**：
- ✅ OpenTelemetry标准集成
- ✅ 自动分布式追踪（ActivitySource）
- ✅ 指标收集（Meter/Counter/Histogram）
- ✅ 零分配结构化日志（LoggerMessage源生成）

---

## 📝 总结

### ✅ Catga 做什么（保留）

1. **CQRS消息调度** - 高性能、AOT兼容的Mediator
2. **Pipeline Behaviors** - 幂等性、重试、Outbox/Inbox、验证、缓存
3. **可观测性** - 追踪、日志、指标（OpenTelemetry集成）
4. **业务增值** - 应用层的CQRS模式抽象

### ❌ Catga 不做什么（委托基础设施）

1. **消息传输** - 使用NATS/Redis原生能力
2. **QoS保证** - 使用NATS JetStream/Redis Streams
3. **服务发现** - 使用K8s DNS和Service
4. **负载均衡** - 使用NATS Consumer Groups/K8s Service
5. **集群管理** - 使用K8s Deployment/ReplicaSet

### 🎯 架构优势

1. **避免重复造轮** - 充分利用成熟基础设施
2. **关注点分离** - 应用层专注CQRS，基础设施专注分布式
3. **简化维护** - 减少Catga框架代码量和复杂度
4. **提升可靠性** - 依赖经过生产验证的组件
5. **易于扩展** - 水平扩展由K8s和消息中间件自动处理

---

## 🔗 相关文档

- [传输与持久化（架构总览）](./overview.md)
- [K8s集成指南](../deployment/kubernetes.md)
- [Pipeline Behaviors](./ARCHITECTURE.md#pipeline-behaviors)
- [幂等性设计](./ARCHITECTURE.md#idempotency-store)



