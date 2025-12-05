# Flow DSL Guide

Catga Flow DSL provides a fluent API for defining distributed transactions (sagas) with automatic compensation, parallel execution, and state management.

## Quick Start

### 1. Define Flow State

```csharp
public class OrderFlowState : IFlowState
{
    // Required by IFlowState
    public string? FlowId { get; set; }

    // Your state properties
    public string? OrderId { get; set; }
    public string? CustomerId { get; set; }
    public decimal TotalAmount { get; set; }
    public string? ReservationId { get; set; }
    public string? PaymentId { get; set; }

    // Change tracking (can be generated)
    private int _changedMask;
    public bool HasChanges => _changedMask != 0;
    public int GetChangedMask() => _changedMask;
    public bool IsFieldChanged(int fieldIndex) => (_changedMask & (1 << fieldIndex)) != 0;
    public void ClearChanges() => _changedMask = 0;
    public void MarkChanged(int fieldIndex) => _changedMask |= (1 << fieldIndex);
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}
```

### 2. Define Flow Configuration

```csharp
public class CreateOrderFlowConfig : FlowConfig<OrderFlowState>
{
    protected override void Configure(IFlowBuilder<OrderFlowState> flow)
    {
        flow.Name("create-order")
            .DefaultTimeout(TimeSpan.FromMinutes(5));

        // Step 1: Reserve inventory with compensation
        flow.Send(s => new ReserveInventoryCommand(s.OrderId!))
            .Into(s => s.ReservationId)
            .IfFail(s => new ReleaseInventoryCommand(s.ReservationId!));

        // Step 2: Process payment with compensation
        flow.Send(s => new ProcessPaymentCommand(s.OrderId!, s.TotalAmount))
            .Into(s => s.PaymentId)
            .IfFail(s => new RefundPaymentCommand(s.PaymentId!));

        // Step 3: Confirm order (no compensation needed)
        flow.Send(s => new ConfirmOrderCommand(s.OrderId!));
    }
}
```

### 3. Register Services

```csharp
services.AddDslFlow();  // Core services
services.AddFlow<OrderFlowState, CreateOrderFlowConfig>();  // Your flow
```

### 4. Execute Flow

```csharp
public class OrderService
{
    private readonly IFlow<OrderFlowState> _flow;

    public OrderService(IFlow<OrderFlowState> flow) => _flow = flow;

    public async Task<string> CreateOrderAsync(CreateOrderRequest request)
    {
        var state = new OrderFlowState
        {
            OrderId = Guid.NewGuid().ToString(),
            CustomerId = request.CustomerId,
            TotalAmount = request.TotalAmount
        };

        var result = await _flow.RunAsync(state);

        if (!result.IsSuccess)
            throw new Exception($"Order creation failed: {result.Error}");

        return state.OrderId;
    }
}
```

## DSL Reference

### Step Types

#### Send (Command)
Execute a command that returns `CatgaResult`:

```csharp
flow.Send(s => new SaveOrderCommand(s.OrderId!));
```

With result capture:
```csharp
flow.Send(s => new ProcessPaymentCommand(s.OrderId!, s.Amount))
    .Into(s => s.PaymentId);  // Capture result
```

#### Query
Execute a query that returns a value:

```csharp
flow.Query(s => new GetCustomerQuery(s.CustomerId!))
    .Into(s => s.CustomerName);
```

#### Publish (Event)
Publish an event:

```csharp
flow.Publish(s => new OrderCreatedEvent(s.OrderId!));
```

### Compensation

#### IfFail
Define compensation for a single step:

```csharp
flow.Send(s => new ReserveInventoryCommand(s.OrderId!))
    .IfFail(s => new ReleaseInventoryCommand(s.ReservationId!));
```

When a step fails, compensations are executed in reverse order for all previously successful steps.

### Conditional Execution

#### OnlyWhen
Execute step only if condition is true:

```csharp
flow.Send(s => new ApplyDiscountCommand(s.OrderId!))
    .OnlyWhen(s => s.HasDiscount);
```

### Optional Steps

#### Optional
Mark step as optional (failure won't stop flow):

```csharp
flow.Send(s => new SendNotificationCommand(s.OrderId!))
    .Optional();
```

### Parallel Execution

#### WhenAll
Wait for all parallel operations to complete:

```csharp
flow.WhenAll(
    s => new NotifyCustomerCommand(s.OrderId!),
    s => new UpdateAnalyticsCommand(s.OrderId!),
    s => new SendEmailCommand(s.OrderId!))
    .Timeout(TimeSpan.FromSeconds(30))
    .IfAnyFail(s => new RollbackNotificationsCommand(s.OrderId!));
```

#### WhenAny
Wait for first successful operation:

```csharp
flow.WhenAny(
    s => new TryPaymentGateway1(s.OrderId!),
    s => new TryPaymentGateway2(s.OrderId!))
    .Into(s => s.PaymentId)
    .Timeout(TimeSpan.FromSeconds(10));
```

### Timeouts

#### Default Timeout
```csharp
flow.DefaultTimeout(TimeSpan.FromMinutes(5));
```

#### Per-Step Timeout
```csharp
flow.Send(s => new SlowOperationCommand())
    .Timeout(TimeSpan.FromMinutes(10));
```

#### Tagged Timeout
```csharp
flow.TimeoutForTag("payment", TimeSpan.FromSeconds(30));

flow.Send(s => new ProcessPaymentCommand(s.OrderId!, s.Amount))
    .Tag("payment");  // Uses 30s timeout
```

### Events

#### OnStepCompleted
```csharp
flow.OnStepCompleted((state, stepIndex) =>
    new StepCompletedEvent(state.FlowId!, stepIndex));
```

#### OnFlowCompleted
```csharp
flow.OnFlowCompleted(state =>
    new OrderFlowCompletedEvent(state.OrderId!));
```

#### OnFlowFailed
```csharp
flow.OnFlowFailed((state, error) =>
    new OrderFlowFailedEvent(state.OrderId!, error));
```

## Persistence

### InMemory (Default)
```csharp
services.AddDslFlow();  // Uses InMemoryDslFlowStore
```

### Redis
```csharp
services.AddRedisDslFlowStore();
```

## Telemetry

Flow DSL automatically emits:

### Metrics
- `catga.flow.started` - Flows started
- `catga.flow.completed` - Flows completed successfully
- `catga.flow.failed` - Flows failed
- `catga.flow.step.executed` - Steps executed
- `catga.flow.compensation.executed` - Compensations executed
- `catga.flow.duration` - Flow duration (ms)
- `catga.flow.step.duration` - Step duration (ms)

### Tracing
- Activity: `Flow.{flowName}`
- Tags: `flow.id`, `flow.name`, `flow.type`

## Flow Lifecycle

```
┌─────────────┐
│   Running   │ ← Initial state
└──────┬──────┘
       │
       ▼
┌─────────────┐     ┌─────────────┐
│  Suspended  │ ←── │ WhenAll/Any │
└──────┬──────┘     └─────────────┘
       │
       ▼
┌─────────────┐     ┌─────────────┐
│  Completed  │ or  │   Failed    │
└─────────────┘     └─────────────┘
```

## Best Practices

1. **Keep state minimal** - Only store what's needed for compensation
2. **Idempotent operations** - Commands should be safe to retry
3. **Compensation order** - Define compensations in logical reverse order
4. **Timeouts** - Always set reasonable timeouts
5. **Optional notifications** - Mark non-critical steps as optional
