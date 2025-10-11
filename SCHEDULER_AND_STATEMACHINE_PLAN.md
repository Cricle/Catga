# Catga 调度器 & 状态机实现计划

**目标**: 添加比 MassTransit Saga 更简单的状态机和调度器功能，保持 Catga 的极简风格

**日期**: 2025-10-11

---

## 📋 总体目标

### 1. **Catga Scheduler** - 延迟消息调度器
- ✅ 延迟消息（Delay Message）
- ✅ 定时消息（Schedule Message）
- ✅ Cron 表达式支持
- ✅ 取消调度任务
- ✅ 持久化支持（NATS/Redis）
- ✅ 极简 API（1-2 行代码）

### 2. **Catga StateMachine** - 简化状态机
- ✅ 比 Saga 更简单的 API
- ✅ 基于 record 的状态定义
- ✅ Fluent API 配置
- ✅ 自动状态转换
- ✅ 补偿操作支持
- ✅ 持久化支持（Memory/Redis）
- ✅ 极简使用（5-10 行代码）

---

## 🎯 设计原则

### Catga 风格
```
1. 极简 API        - 最少代码实现功能
2. 类型安全        - 使用 record 和泛型
3. AOT 兼容        - 避免反射
4. 无锁设计        - ConcurrentDictionary + Interlocked
5. 高性能          - ValueTask + 零拷贝
6. 易理解          - 清晰的状态转换
```

---

## 📐 Phase 1: Catga Scheduler (调度器)

### 1.1 核心接口设计

```csharp
namespace Catga.Scheduling;

/// <summary>
/// Message scheduler - schedule messages for delayed or recurring execution
/// </summary>
public interface IMessageScheduler
{
    /// <summary>
    /// Schedule a message to be sent after a delay
    /// </summary>
    ValueTask<ScheduleToken> ScheduleAsync<TMessage>(
        TMessage message,
        TimeSpan delay,
        CancellationToken cancellationToken = default)
        where TMessage : class;

    /// <summary>
    /// Schedule a message to be sent at a specific time
    /// </summary>
    ValueTask<ScheduleToken> ScheduleAsync<TMessage>(
        TMessage message,
        DateTimeOffset scheduleTime,
        CancellationToken cancellationToken = default)
        where TMessage : class;

    /// <summary>
    /// Schedule a recurring message using cron expression
    /// </summary>
    ValueTask<ScheduleToken> ScheduleRecurringAsync<TMessage>(
        TMessage message,
        string cronExpression,
        CancellationToken cancellationToken = default)
        where TMessage : class;

    /// <summary>
    /// Cancel a scheduled message
    /// </summary>
    ValueTask<bool> CancelAsync(
        ScheduleToken token,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get scheduled message info
    /// </summary>
    ValueTask<ScheduleInfo?> GetScheduleInfoAsync(
        ScheduleToken token,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Schedule token - unique identifier for scheduled message
/// </summary>
public readonly record struct ScheduleToken(string Value)
{
    public static ScheduleToken New() => new(Guid.NewGuid().ToString("N"));
}

/// <summary>
/// Schedule info
/// </summary>
public record ScheduleInfo(
    ScheduleToken Token,
    string MessageType,
    DateTimeOffset ScheduleTime,
    ScheduleStatus Status,
    string? CronExpression = null);

/// <summary>
/// Schedule status
/// </summary>
public enum ScheduleStatus
{
    Pending = 0,
    Executing = 1,
    Completed = 2,
    Cancelled = 3,
    Failed = 4
}
```

### 1.2 实现方案

#### Option A: NATS JetStream 实现（推荐）

```csharp
namespace Catga.Scheduling.Nats;

/// <summary>
/// NATS JetStream based message scheduler
/// Uses JetStream's native message TTL and delivery delay
/// </summary>
public sealed class NatsMessageScheduler : IMessageScheduler
{
    private readonly INatsConnection _connection;
    private readonly INatsJSContext _jetStream;
    private readonly ConcurrentDictionary<ScheduleToken, ScheduleInfo> _schedules;
    private readonly ILogger<NatsMessageScheduler> _logger;

    public async ValueTask<ScheduleToken> ScheduleAsync<TMessage>(
        TMessage message,
        TimeSpan delay,
        CancellationToken cancellationToken = default)
        where TMessage : class
    {
        var token = ScheduleToken.New();
        var scheduleTime = DateTimeOffset.UtcNow.Add(delay);

        // Use NATS JetStream message with delivery delay
        var subject = $"schedule.{typeof(TMessage).Name}";
        
        var headers = new NatsHeaders
        {
            ["X-Schedule-Token"] = token.Value,
            ["X-Schedule-Time"] = scheduleTime.ToString("O"),
            ["X-Message-Type"] = typeof(TMessage).FullName!
        };

        // Publish with delay using JetStream
        await _jetStream.PublishAsync(
            subject,
            message,
            headers: headers,
            cancellationToken: cancellationToken);

        var info = new ScheduleInfo(
            token,
            typeof(TMessage).Name,
            scheduleTime,
            ScheduleStatus.Pending);

        _schedules[token] = info;

        return token;
    }

    // ... other methods
}
```

#### Option B: Redis 实现

```csharp
namespace Catga.Scheduling.Redis;

/// <summary>
/// Redis based message scheduler
/// Uses Redis Sorted Set for time-based scheduling
/// </summary>
public sealed class RedisMessageScheduler : IMessageScheduler
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IMessageSerializer _serializer;
    private readonly Timer _pollingTimer;
    
    private const string ScheduleSetKey = "catga:schedule:pending";
    private const string ScheduleDataKey = "catga:schedule:data:";

    public async ValueTask<ScheduleToken> ScheduleAsync<TMessage>(
        TMessage message,
        TimeSpan delay,
        CancellationToken cancellationToken = default)
        where TMessage : class
    {
        var token = ScheduleToken.New();
        var scheduleTime = DateTimeOffset.UtcNow.Add(delay);
        var db = _redis.GetDatabase();

        // Serialize message
        var data = _serializer.Serialize(message);
        var metadata = new ScheduleMetadata(
            token.Value,
            typeof(TMessage).FullName!,
            scheduleTime);

        // Store in Redis
        // 1. Add to sorted set (score = unix timestamp)
        await db.SortedSetAddAsync(
            ScheduleSetKey,
            token.Value,
            scheduleTime.ToUnixTimeSeconds());

        // 2. Store message data
        await db.StringSetAsync(
            $"{ScheduleDataKey}{token.Value}",
            JsonSerializer.Serialize(new
            {
                Metadata = metadata,
                Data = data
            }));

        return token;
    }

    // Background polling to execute scheduled messages
    private async void ProcessScheduledMessages(object? state)
    {
        var db = _redis.GetDatabase();
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // Get all messages due for execution
        var dueMessages = await db.SortedSetRangeByScoreAsync(
            ScheduleSetKey,
            0,
            now);

        foreach (var tokenValue in dueMessages)
        {
            // Execute message (lock-free using Redis transaction)
            // ...
        }
    }
}
```

### 1.3 使用示例

```csharp
// Example 1: Delay message (5 minutes)
var token = await _scheduler.ScheduleAsync(
    new SendEmailCommand("user@example.com", "Welcome!"),
    TimeSpan.FromMinutes(5));

// Example 2: Schedule at specific time
var token = await _scheduler.ScheduleAsync(
    new SendReminderCommand("meeting-123"),
    DateTimeOffset.UtcNow.AddDays(1).Date.AddHours(9)); // Tomorrow 9 AM

// Example 3: Recurring message (every day at 9 AM)
var token = await _scheduler.ScheduleRecurringAsync(
    new DailyReportCommand(),
    "0 9 * * *"); // Cron: every day at 9:00

// Example 4: Cancel scheduled message
var cancelled = await _scheduler.CancelAsync(token);

// Example 5: Check schedule status
var info = await _scheduler.GetScheduleInfoAsync(token);
Console.WriteLine($"Status: {info?.Status}");
```

### 1.4 DI 注册

```csharp
// Simple registration
builder.Services
    .AddCatga()
    .AddNatsScheduler(opts => opts.Url = "nats://localhost:4222");

// Or Redis
builder.Services
    .AddCatga()
    .AddRedisScheduler(opts => opts.Configuration = "localhost:6379");
```

---

## 📐 Phase 2: Catga StateMachine (状态机)

### 2.1 设计对比

#### MassTransit Saga (复杂)
```csharp
// MassTransit: 需要继承类、定义状态、配置事件
public class OrderStateMachine : MassTransitStateMachine<OrderState>
{
    public State Submitted { get; private set; }
    public State Accepted { get; private set; }
    public State Completed { get; private set; }

    public Event<OrderSubmitted> OrderSubmitted { get; private set; }
    public Event<OrderAccepted> OrderAccepted { get; private set; }

    public OrderStateMachine()
    {
        InstanceState(x => x.CurrentState);

        Event(() => OrderSubmitted, x => x.CorrelateById(m => m.Message.OrderId));
        Event(() => OrderAccepted, x => x.CorrelateById(m => m.Message.OrderId));

        Initially(
            When(OrderSubmitted)
                .Then(context => { /* logic */ })
                .TransitionTo(Submitted));

        During(Submitted,
            When(OrderAccepted)
                .Then(context => { /* logic */ })
                .TransitionTo(Accepted));

        // ... more configurations
    }
}

// 50+ lines for simple state machine
```

#### Catga StateMachine (简单)
```csharp
// Catga: 使用 record + Fluent API，极简
public record OrderState(
    string OrderId,
    string Status,
    decimal Amount);

public enum OrderStatus
{
    Submitted,
    Accepted,
    Completed,
    Cancelled
}

// Define state machine (10 lines)
var stateMachine = new CatgaStateMachine<OrderState, OrderStatus>()
    .InitialState(OrderStatus.Submitted)
    .When(OrderStatus.Submitted)
        .On<OrderAcceptedEvent>()
        .TransitionTo(OrderStatus.Accepted)
        .Do(async (state, @event) =>
        {
            return state with { Status = "Accepted" };
        })
    .When(OrderStatus.Accepted)
        .On<OrderCompletedEvent>()
        .TransitionTo(OrderStatus.Completed)
        .Do(async (state, @event) =>
        {
            return state with { Status = "Completed" };
        })
    .Build();

// Use (1 line)
var newState = await stateMachine.ProcessAsync(
    currentState,
    new OrderAcceptedEvent(orderId));
```

### 2.2 核心接口设计

```csharp
namespace Catga.StateMachine;

/// <summary>
/// Catga state machine - simple and type-safe state transitions
/// </summary>
public interface ICatgaStateMachine<TState, TStatus>
    where TState : class
    where TStatus : struct, Enum
{
    /// <summary>
    /// Process an event and return new state
    /// </summary>
    ValueTask<StateTransitionResult<TState>> ProcessAsync<TEvent>(
        TState currentState,
        TEvent @event,
        CancellationToken cancellationToken = default)
        where TEvent : class;

    /// <summary>
    /// Check if transition is allowed
    /// </summary>
    bool CanTransition<TEvent>(TStatus fromStatus)
        where TEvent : class;

    /// <summary>
    /// Get current state status
    /// </summary>
    TStatus GetStatus(TState state);
}

/// <summary>
/// State transition result
/// </summary>
public record StateTransitionResult<TState>(
    TState State,
    bool Success,
    string? ErrorMessage = null)
    where TState : class
{
    public static StateTransitionResult<TState> Ok(TState state) =>
        new(state, true);

    public static StateTransitionResult<TState> Fail(TState state, string error) =>
        new(state, false, error);
}

/// <summary>
/// State machine builder - fluent API
/// </summary>
public sealed class CatgaStateMachine<TState, TStatus>
    where TState : class
    where TStatus : struct, Enum
{
    private TStatus _initialState;
    private readonly Dictionary<(TStatus, Type), StateTransition<TState, TStatus>> _transitions = new();
    private Func<TState, TStatus>? _statusSelector;

    public CatgaStateMachine<TState, TStatus> InitialState(TStatus status)
    {
        _initialState = status;
        return this;
    }

    public CatgaStateMachine<TState, TStatus> UseStatusSelector(Func<TState, TStatus> selector)
    {
        _statusSelector = selector;
        return this;
    }

    public StateBuilder<TState, TStatus> When(TStatus fromStatus) =>
        new(this, fromStatus);

    public ICatgaStateMachine<TState, TStatus> Build() =>
        new StateMachineInstance<TState, TStatus>(
            _initialState,
            _statusSelector ?? throw new InvalidOperationException("Status selector not set"),
            _transitions);

    // Internal: add transition
    internal void AddTransition<TEvent>(
        TStatus fromStatus,
        TStatus toStatus,
        Func<TState, TEvent, ValueTask<TState>>? action,
        Func<TState, TEvent, ValueTask<bool>>? guard,
        Func<TState, Exception, ValueTask>? compensate)
        where TEvent : class
    {
        _transitions[(fromStatus, typeof(TEvent))] = new StateTransition<TState, TStatus>(
            fromStatus,
            toStatus,
            async (state, @event) => action != null
                ? await action(state, (TEvent)@event)
                : state,
            async (state, @event) => guard != null
                ? await guard(state, (TEvent)@event)
                : true,
            compensate != null
                ? async (state, ex) => await compensate(state, ex)
                : null);
    }
}

/// <summary>
/// State builder - fluent API for defining transitions
/// </summary>
public sealed class StateBuilder<TState, TStatus>
    where TState : class
    where TStatus : struct, Enum
{
    private readonly CatgaStateMachine<TState, TStatus> _machine;
    private readonly TStatus _fromStatus;

    internal StateBuilder(CatgaStateMachine<TState, TStatus> machine, TStatus fromStatus)
    {
        _machine = machine;
        _fromStatus = fromStatus;
    }

    public EventBuilder<TState, TStatus, TEvent> On<TEvent>() where TEvent : class =>
        new(_machine, _fromStatus);
}

/// <summary>
/// Event builder - fluent API for defining event handlers
/// </summary>
public sealed class EventBuilder<TState, TStatus, TEvent>
    where TState : class
    where TStatus : struct, Enum
    where TEvent : class
{
    private readonly CatgaStateMachine<TState, TStatus> _machine;
    private readonly TStatus _fromStatus;
    private TStatus _toStatus;
    private Func<TState, TEvent, ValueTask<TState>>? _action;
    private Func<TState, TEvent, ValueTask<bool>>? _guard;
    private Func<TState, Exception, ValueTask>? _compensate;

    internal EventBuilder(CatgaStateMachine<TState, TStatus> machine, TStatus fromStatus)
    {
        _machine = machine;
        _fromStatus = fromStatus;
    }

    public EventBuilder<TState, TStatus, TEvent> TransitionTo(TStatus toStatus)
    {
        _toStatus = toStatus;
        return this;
    }

    public EventBuilder<TState, TStatus, TEvent> Do(
        Func<TState, TEvent, ValueTask<TState>> action)
    {
        _action = action;
        return this;
    }

    public EventBuilder<TState, TStatus, TEvent> When(
        Func<TState, TEvent, ValueTask<bool>> guard)
    {
        _guard = guard;
        return this;
    }

    public EventBuilder<TState, TStatus, TEvent> OnError(
        Func<TState, Exception, ValueTask> compensate)
    {
        _compensate = compensate;
        return this;
    }

    public CatgaStateMachine<TState, TStatus> And()
    {
        _machine.AddTransition(_fromStatus, _toStatus, _action, _guard, _compensate);
        return _machine;
    }
}
```

### 2.3 持久化支持

```csharp
namespace Catga.StateMachine.Persistence;

/// <summary>
/// State store - persist state machine states
/// </summary>
public interface IStateStore<TState> where TState : class
{
    ValueTask SaveAsync(
        string key,
        TState state,
        CancellationToken cancellationToken = default);

    ValueTask<TState?> LoadAsync(
        string key,
        CancellationToken cancellationToken = default);

    ValueTask<bool> DeleteAsync(
        string key,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Memory state store
/// </summary>
public sealed class MemoryStateStore<TState> : IStateStore<TState>
    where TState : class
{
    private readonly ConcurrentDictionary<string, TState> _states = new();

    public ValueTask SaveAsync(string key, TState state, CancellationToken ct = default)
    {
        _states[key] = state;
        return ValueTask.CompletedTask;
    }

    public ValueTask<TState?> LoadAsync(string key, CancellationToken ct = default)
    {
        _states.TryGetValue(key, out var state);
        return ValueTask.FromResult(state);
    }

    public ValueTask<bool> DeleteAsync(string key, CancellationToken ct = default)
    {
        return ValueTask.FromResult(_states.TryRemove(key, out _));
    }
}

/// <summary>
/// Redis state store
/// </summary>
public sealed class RedisStateStore<TState> : IStateStore<TState>
    where TState : class
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IMessageSerializer _serializer;
    private const string KeyPrefix = "catga:state:";

    public async ValueTask SaveAsync(string key, TState state, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var data = _serializer.Serialize(state);
        await db.StringSetAsync($"{KeyPrefix}{key}", data);
    }

    public async ValueTask<TState?> LoadAsync(string key, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var data = await db.StringGetAsync($"{KeyPrefix}{key}");
        return data.HasValue
            ? _serializer.Deserialize<TState>(data!)
            : null;
    }

    public async ValueTask<bool> DeleteAsync(string key, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        return await db.KeyDeleteAsync($"{KeyPrefix}{key}");
    }
}
```

### 2.4 完整使用示例

```csharp
// Example: Order state machine

// 1. Define state and status
public record OrderState(
    string OrderId,
    string Status,
    decimal Amount,
    DateTime CreatedAt,
    DateTime? CompletedAt = null);

public enum OrderStatus
{
    Created,
    PaymentPending,
    PaymentCompleted,
    Shipping,
    Completed,
    Cancelled
}

// 2. Define events
public record OrderCreatedEvent(string OrderId, decimal Amount);
public record PaymentCompletedEvent(string OrderId, string PaymentId);
public record ShippingStartedEvent(string OrderId, string TrackingNumber);
public record OrderCompletedEvent(string OrderId);
public record OrderCancelledEvent(string OrderId, string Reason);

// 3. Build state machine (simple!)
var stateMachine = new CatgaStateMachine<OrderState, OrderStatus>()
    .InitialState(OrderStatus.Created)
    .UseStatusSelector(state => Enum.Parse<OrderStatus>(state.Status))
    
    // Created → PaymentPending
    .When(OrderStatus.Created)
        .On<OrderCreatedEvent>()
        .TransitionTo(OrderStatus.PaymentPending)
        .Do(async (state, @event) =>
        {
            _logger.LogInformation("Order created: {OrderId}", @event.OrderId);
            return state with { Status = OrderStatus.PaymentPending.ToString() };
        })
        .And()
    
    // PaymentPending → PaymentCompleted
    .When(OrderStatus.PaymentPending)
        .On<PaymentCompletedEvent>()
        .TransitionTo(OrderStatus.PaymentCompleted)
        .When(async (state, @event) =>
        {
            // Guard: check payment amount
            return state.Amount > 0;
        })
        .Do(async (state, @event) =>
        {
            _logger.LogInformation("Payment completed: {PaymentId}", @event.PaymentId);
            return state with { Status = OrderStatus.PaymentCompleted.ToString() };
        })
        .OnError(async (state, ex) =>
        {
            // Compensate: refund payment
            await _paymentService.RefundAsync(state.OrderId);
        })
        .And()
    
    // PaymentCompleted → Shipping
    .When(OrderStatus.PaymentCompleted)
        .On<ShippingStartedEvent>()
        .TransitionTo(OrderStatus.Shipping)
        .Do(async (state, @event) =>
        {
            return state with { Status = OrderStatus.Shipping.ToString() };
        })
        .And()
    
    // Shipping → Completed
    .When(OrderStatus.Shipping)
        .On<OrderCompletedEvent>()
        .TransitionTo(OrderStatus.Completed)
        .Do(async (state, @event) =>
        {
            return state with
            {
                Status = OrderStatus.Completed.ToString(),
                CompletedAt = DateTime.UtcNow
            };
        })
        .And()
    
    // Any state → Cancelled (global transition)
    .When(OrderStatus.PaymentPending)
        .On<OrderCancelledEvent>()
        .TransitionTo(OrderStatus.Cancelled)
        .Do(async (state, @event) =>
        {
            _logger.LogWarning("Order cancelled: {Reason}", @event.Reason);
            return state with { Status = OrderStatus.Cancelled.ToString() };
        })
        .And()
    
    .Build();

// 4. Use with persistence
var stateStore = new RedisStateStore<OrderState>(redis, serializer);

// Load current state
var currentState = await stateStore.LoadAsync(orderId)
    ?? new OrderState(orderId, OrderStatus.Created.ToString(), 100m, DateTime.UtcNow);

// Process event
var result = await stateMachine.ProcessAsync(
    currentState,
    new PaymentCompletedEvent(orderId, "payment-123"));

if (result.Success)
{
    // Save new state
    await stateStore.SaveAsync(orderId, result.State);
    Console.WriteLine($"New state: {result.State.Status}");
}
else
{
    Console.WriteLine($"Transition failed: {result.ErrorMessage}");
}
```

### 2.5 与 Catga Mediator 集成

```csharp
// Integrate state machine with mediator

public class OrderStateMachineHandler
    : IEventHandler<PaymentCompletedEvent>
{
    private readonly ICatgaStateMachine<OrderState, OrderStatus> _stateMachine;
    private readonly IStateStore<OrderState> _stateStore;

    public async Task HandleAsync(
        PaymentCompletedEvent @event,
        CancellationToken ct)
    {
        // Load state
        var state = await _stateStore.LoadAsync(@event.OrderId, ct);
        if (state == null) return;

        // Process event
        var result = await _stateMachine.ProcessAsync(state, @event, ct);

        if (result.Success)
        {
            // Save state
            await _stateStore.SaveAsync(@event.OrderId, result.State, ct);
        }
    }
}

// DI registration
builder.Services
    .AddCatga()
    .AddStateMachine<OrderState, OrderStatus>(stateMachine)
    .AddRedisStateStore<OrderState>();
```

---

## 📊 实现优先级

### Phase 1: Scheduler (Week 1-2)
- [x] Day 1-2: 核心接口设计
- [x] Day 3-4: NATS 实现
- [ ] Day 5-6: Redis 实现
- [ ] Day 7: Cron 表达式支持
- [ ] Day 8-9: 测试
- [ ] Day 10: 文档和示例

### Phase 2: StateMachine (Week 3-4)
- [x] Day 1-2: 核心接口设计
- [x] Day 3-4: Fluent API 实现
- [ ] Day 5-6: 持久化实现
- [ ] Day 7-8: Mediator 集成
- [ ] Day 9: 测试
- [ ] Day 10: 文档和示例

---

## 🎯 成功标准

### Scheduler
1. ✅ 1 行代码调度延迟消息
2. ✅ 支持 Cron 表达式
3. ✅ 支持取消调度
4. ✅ NATS/Redis 双实现
5. ✅ 性能：10万+ 调度/秒
6. ✅ AOT 兼容

### StateMachine
1. ✅ 10 行代码定义状态机
2. ✅ 类型安全（record + Enum）
3. ✅ Fluent API
4. ✅ 自动持久化
5. ✅ 补偿操作支持
6. ✅ 比 MassTransit Saga 简单 **5x**

---

## 📝 文件结构

```
src/
├── Catga.Scheduling/
│   ├── IMessageScheduler.cs
│   ├── ScheduleToken.cs
│   ├── ScheduleInfo.cs
│   ├── ScheduleStatus.cs
│   └── DependencyInjection/
│       └── SchedulerServiceCollectionExtensions.cs
│
├── Catga.Scheduling.Nats/
│   ├── NatsMessageScheduler.cs
│   └── DependencyInjection/
│       └── NatsSchedulerServiceCollectionExtensions.cs
│
├── Catga.Scheduling.Redis/
│   ├── RedisMessageScheduler.cs
│   └── DependencyInjection/
│       └── RedisSchedulerServiceCollectionExtensions.cs
│
├── Catga.StateMachine/
│   ├── ICatgaStateMachine.cs
│   ├── CatgaStateMachine.cs
│   ├── StateBuilder.cs
│   ├── EventBuilder.cs
│   ├── StateTransitionResult.cs
│   ├── Persistence/
│   │   ├── IStateStore.cs
│   │   ├── MemoryStateStore.cs
│   │   └── RedisStateStore.cs
│   └── DependencyInjection/
│       └── StateMachineServiceCollectionExtensions.cs
│
tests/
├── Catga.Scheduling.Tests/
│   ├── NatsSchedulerTests.cs
│   └── RedisSchedulerTests.cs
│
└── Catga.StateMachine.Tests/
    ├── StateMachineBuilderTests.cs
    └── StatePersistenceTests.cs

examples/
├── SchedulerExample/
│   ├── DelayedMessageExample.cs
│   ├── CronScheduleExample.cs
│   └── CancelScheduleExample.cs
│
└── StateMachineExample/
    ├── OrderStateMachineExample.cs
    └── CompensationExample.cs
```

---

## 🚀 下一步行动

1. **创建项目结构** ✅
2. **实现 Scheduler 核心接口**
3. **实现 NATS Scheduler**
4. **实现 StateMachine Fluent API**
5. **编写测试**
6. **编写文档和示例**
7. **更新 README**

---

**预计完成时间**: 2-3 周
**代码行数**: ~2000 行（包括测试和示例）
**学习曲线**: 10 分钟（vs MassTransit 2-3 天）

🎉 让 Catga 保持简单和强大！

