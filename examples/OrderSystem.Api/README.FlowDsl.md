# Flow DSL Complete Feature Showcase

## Overview
This OrderSystem.Api example demonstrates **ALL** features of the Catga Flow DSL, providing a comprehensive reference implementation for enterprise workflow orchestration.

## 🚀 Quick Start

### 1. Simple Registration (Development)
```csharp
// Program.cs
builder.Services.AddFlowDsl();
```

### 2. Redis Registration (Production)
```csharp
builder.Services.AddFlowDslWithRedis("localhost:6379", options =>
{
    options.RedisPrefix = "orderflow:";
    options.AutoRegisterFlows = true;
    options.EnableMetrics = true;
});
```

### 3. NATS Registration (Event-Driven)
```csharp
builder.Services.AddFlowDslWithNats("nats://localhost:4222", options =>
{
    options.NatsBucket = "orderflows";
    options.AutoRegisterFlows = true;
});
```

### 4. Fluent Configuration (Source-Generated)
```csharp
builder.Services.ConfigureFlowDsl(flow => flow
    .UseRedisStorage("localhost:6379", "orderflow:")
    .RegisterGeneratedFlows() // Auto-registers all source-generated flows (no reflection!)
    .RegisterFlow<OrderFlowState, ComprehensiveOrderFlow>() // Optional: manually register specific flows
    .WithMetrics()
    .WithRetryPolicy(maxAttempts: 3)
    .WithStepTimeout(TimeSpan.FromMinutes(10)));
```

### 5. Configuration-Based Setup
```csharp
// Reads from appsettings.json
builder.Services.AddFlowDslFromConfiguration(configuration);
```

## � Source-Generated Registration (Zero Reflection!)

The Catga Flow DSL now uses **source generation** for automatic flow registration, providing:
- **Zero reflection overhead** - All flows discovered at compile time
- **AOT compatibility** - Full Native AOT support
- **Faster startup** - No assembly scanning required
- **Type safety** - Compile-time validation of all flows
- **IntelliSense support** - Generated methods appear in IDE autocomplete

Simply inherit from `FlowConfig<TState>` and the source generator automatically:
1. Discovers all flow configurations
2. Generates registration methods
3. Creates strongly-typed accessors

## �📋 Complete Feature List

### Core Flow Control
✅ **Send** - Execute commands with results
✅ **Publish** - Broadcast events
✅ **WaitFor** - Delay execution
✅ **Into** - Capture command results into state

### Conditional Logic
✅ **If/ElseIf/Else** - Multi-branch conditions
✅ **Nested If** - Complex conditional logic
✅ **Switch/Case/Default** - Pattern matching

### Parallel Processing
✅ **ForEach** - Process collections
✅ **WithParallelism** - Control concurrency
✅ **WithBatchSize** - Batch processing
✅ **Nested ForEach** - Multi-level iteration

### Coordination
✅ **WhenAll** - Wait for all branches
✅ **WhenAny** - Race condition handling
✅ **WaitConditions** - External event waiting

### Error Handling
✅ **Retry** - Automatic retry with backoff
✅ **Compensate** - Saga pattern compensation
✅ **ContinueOnFailure** - Fault tolerance
✅ **StopOnFirstFailure** - Fast failure

### Progress Tracking
✅ **OnItemSuccess** - Item completion callbacks
✅ **OnItemFail** - Error callbacks
✅ **OnComplete** - Completion callbacks
✅ **ForEachProgress** - Detailed progress state

### Recovery & Persistence
✅ **Auto-Recovery** - Automatic flow resumption
✅ **Manual Resume** - Controlled recovery
✅ **State Persistence** - Full state snapshots
✅ **Position Tracking** - Nested execution tracking

### Monitoring & Metrics
✅ **Flow Metrics** - Performance tracking
✅ **Progress Monitoring** - Real-time progress
✅ **Timeout Detection** - Abandoned flow detection
✅ **Visualization** - Flow execution graphs

## 🎯 ComprehensiveOrderFlow Features

The `ComprehensiveOrderFlow` demonstrates every Flow DSL capability:

### 1. Multi-Level Approval (If/ElseIf/Else)
```csharp
flow.If(s => s.Order.TotalAmount > 10000)
    .Send(s => new RequireManagerApprovalCommand(s.OrderId))
    .If(s => s.RequiresApproval) // Nested condition
        .Send(s => new NotifyManagerCommand(s.OrderId))
        .WaitFor(TimeSpan.FromMinutes(30))
    .EndIf()
.ElseIf(s => s.Order.TotalAmount > 5000)
    .Send(s => new RequireSeniorStaffReviewCommand(s.OrderId))
.Else()
    .Send(s => new AutoApproveOrderCommand(s.OrderId))
.EndIf();
```

### 2. Customer Type Processing (Switch/Case)
```csharp
flow.Switch(s => s.Order.CustomerType)
    .Case(CustomerType.VIP, vip =>
    {
        vip.Send(s => new ApplyVIPDiscountCommand(s.OrderId, 0.20m));
        vip.Send(s => new AssignPriorityShippingCommand(s.OrderId));
    })
    .Case(CustomerType.Regular, regular =>
    {
        regular.Send(s => new ApplyStandardDiscountCommand(s.OrderId, 0.05m));
    })
    .Default(unknown =>
    {
        unknown.Send(s => new LogUnknownCustomerTypeCommand(s.OrderId));
    })
    .EndSwitch();
```

### 3. Parallel Inventory Check (ForEach)
```csharp
flow.ForEach(s => s.Order.Items)
    .WithParallelism(5)
    .Configure((item, f) =>
    {
        f.Send(s => new CheckInventoryCommand(item.ProductId))
         .Send(s => new ReserveInventoryCommand(item.ProductId));
    })
    .OnItemSuccess((state, item, result) =>
    {
        state.ProcessedItems.Add(item.ProductId);
        state.ProcessingProgress = CalculateProgress(state);
    })
    .ContinueOnFailure()
    .EndForEach();
```

### 4. Payment Provider Racing (WhenAny)
```csharp
flow.WhenAny(
    f => f.Send(s => new ProcessPaymentWithStripeCommand(s.OrderId)),
    f => f.Send(s => new ProcessPaymentWithPayPalCommand(s.OrderId)),
    f => f.Send(s => new ProcessPaymentWithSquareCommand(s.OrderId))
).Into((s, result) =>
{
    s.PaymentProvider = result.Value?.Provider;
    s.TransactionId = result.Value?.TransactionId;
});
```

### 5. Parallel Operations (WhenAll)
```csharp
flow.WhenAll(
    f => f.Send(s => new GenerateInvoiceCommand(s.OrderId)),
    f => f.Send(s => new UpdateLoyaltyPointsCommand(s.CustomerId)),
    f => f.Send(s => new SendConfirmationEmailCommand(s.Email)),
    f => f.Send(s => new CreateShippingLabelCommand(s.OrderId))
);
```

### 6. Nested ForEach (Multi-Warehouse)
```csharp
flow.ForEach(s => s.Warehouses)
    .Configure((warehouse, f) =>
    {
        f.ForEach(s => s.Order.Items.Where(i => warehouse.HasProduct(i.ProductId)))
            .Configure((item, itemFlow) =>
            {
                itemFlow.Send(s => new AllocateItemToWarehouseCommand(
                    warehouse.Id, item.ProductId, item.Quantity));
            })
            .EndForEach();
    })
    .EndForEach();
```

### 7. Fraud Detection with Compensation
```csharp
flow.If(s => s.Order.TotalAmount > 1000 && s.Order.CustomerType == CustomerType.New)
    .Send(s => new PerformFraudCheckCommand(s.OrderId))
    .If(s => s.FraudScore > 0.7)
        .Send(s => new FlagOrderForReviewCommand(s.OrderId))
        .Compensate(s => new ReleaseInventoryReservationsCommand(s.ReservedItems))
    .EndIf()
.EndIf();
```

## 📡 API Endpoints

The example exposes comprehensive management endpoints:

### Flow Management
- `GET /api/flows` - List all registered flows
- `GET /api/flows/{flowId}/status` - Get flow status
- `POST /api/flows/order/start` - Start new order flow
- `POST /api/flows/{flowId}/resume` - Resume suspended flow

### Monitoring
- `GET /api/flows/metrics` - Flow execution metrics
- `GET /api/flows/{flowId}/progress/{stepIndex}` - ForEach progress
- `GET /api/flows/wait-conditions/timed-out` - Timed out conditions

## 🔧 Configuration Options

### appsettings.json
```json
{
  "FlowDsl": {
    "Storage": "Redis",
    "RedisConnection": "localhost:6379",
    "AutoRegisterFlows": true,
    "EnableMetrics": true,
    "Features": {
      "EnableParallelProcessing": true,
      "MaxParallelism": 10,
      "EnableCompensation": true,
      "EnableWaitConditions": true,
      "EnableProgressTracking": true
    },
    "Recovery": {
      "EnableAutoRecovery": true,
      "RecoveryCheckInterval": "00:01:00",
      "MaxRecoveryAttempts": 5
    }
  }
}
```

## 🏃‍♂️ Running the Example

### Prerequisites
1. .NET 8.0 SDK
2. Redis (optional, for production mode)
3. NATS (optional, for event-driven mode)

### Development Mode (InMemory)
```bash
dotnet run --environment Development
```

### Production Mode (Redis)
```bash
# Start Redis
docker run -p 6379:6379 redis

# Run application
dotnet run --environment Production
```

### Event-Driven Mode (NATS)
```bash
# Start NATS
docker run -p 4222:4222 nats

# Run application
dotnet run --configuration NatsMode
```

## 📊 Performance Characteristics

| Feature | Performance | Scalability |
|---------|-------------|------------|
| ForEach with Parallelism(10) | 10x throughput | Linear with cores |
| WhenAny (3 branches) | First response time | Reduces latency |
| WhenAll (4 operations) | Parallel execution | Maximizes throughput |
| Nested ForEach | O(n*m) complexity | Configurable batching |
| Switch/Case (10 cases) | O(1) lookup | Hash-based |
| If/ElseIf (5 branches) | Short-circuit eval | Minimal overhead |

## 🧪 Testing Flows

### Unit Testing
```csharp
[Fact]
public async Task OrderFlow_HighValueOrder_RequiresApproval()
{
    var mediator = Substitute.For<ICatgaMediator>();
    var store = new InMemoryDslFlowStore();
    var executor = new DslFlowExecutor<OrderFlowState, ComprehensiveOrderFlow>(
        mediator, store, new ComprehensiveOrderFlow());

    var state = new OrderFlowState
    {
        Order = new Order { TotalAmount = 15000 }
    };

    var result = await executor.RunAsync(state);

    result.IsSuccess.Should().BeTrue();
    state.RequiresApproval.Should().BeTrue();
}
```

### Integration Testing
```csharp
[Fact]
public async Task OrderFlow_CompleteScenario_ProcessesSuccessfully()
{
    var factory = new WebApplicationFactory<Program>();
    var client = factory.CreateClient();

    var response = await client.PostAsJsonAsync("/api/flows/order/start", new
    {
        OrderId = "ORD-001",
        Order = new { TotalAmount = 5000, CustomerType = "VIP" }
    });

    response.StatusCode.Should().Be(HttpStatusCode.OK);
}
```

## 🎓 Best Practices

1. **State Design**: Keep state classes focused and serializable
2. **Error Handling**: Always provide compensation for critical operations
3. **Performance**: Use parallelism for independent operations
4. **Recovery**: Design flows to be resumable at any point
5. **Monitoring**: Track key metrics and progress indicators
6. **Testing**: Test each branch and error scenario

## 📚 Additional Resources

- [Flow DSL Guide](../../docs/guides/flow-dsl.md)
- [Storage Parity Verification](../../docs/flow/STORAGE_PARITY_VERIFICATION.md)
- [Performance Benchmarks](../../docs/BENCHMARK-RESULTS.md)
- [Error Handling Guide](../../docs/guides/error-handling.md)

## 🤝 Contributing

This example is designed to be a living reference implementation. Contributions that demonstrate additional patterns or optimizations are welcome!

## ⚡ Summary

This OrderSystem.Api example provides a **complete, production-ready** implementation showcasing:

- ✅ All Flow DSL features
- ✅ Multiple registration methods
- ✅ Comprehensive error handling
- ✅ Full recovery support
- ✅ Performance optimization
- ✅ Monitoring and metrics
- ✅ Testing strategies

Use this as a reference for implementing complex workflow orchestration in your applications!
