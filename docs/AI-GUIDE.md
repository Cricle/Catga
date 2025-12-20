# Catga AI Guide - Complete Usage Reference

This document is designed for AI assistants to understand how to use Catga framework correctly.

## Overview

Catga is a high-performance .NET mediator framework with:
- CQRS pattern (Commands, Queries, Events)
- Event Sourcing
- Distributed messaging (Redis, NATS)
- Flow/Saga orchestration
- AOT compatible

## Project Structure

```
src/
├── Catga/                      # Core library
├── Catga.Abstractions/         # Interfaces (merged into Catga)
├── Catga.AspNetCore/           # ASP.NET Core integration
├── Catga.Cluster/              # Cluster coordination
├── Catga.Persistence.InMemory/ # InMemory stores
├── Catga.Persistence.Redis/    # Redis stores
├── Catga.Persistence.Nats/     # NATS JetStream stores
├── Catga.Transport.InMemory/   # InMemory transport
├── Catga.Transport.Redis/      # Redis Pub/Sub transport
├── Catga.Transport.Nats/       # NATS transport
├── Catga.Serialization.MemoryPack/ # MemoryPack serializer
├── Catga.Scheduling.Hangfire/  # Hangfire integration
├── Catga.Scheduling.Quartz/    # Quartz integration
└── Catga.SourceGenerator/      # Source generators
```

## Basic Setup

### Minimal Setup (InMemory)
```csharp
var services = new ServiceCollection();
services.AddCatga();
services.AddInMemoryPersistence();
services.AddInMemoryTransport();
services.UseMemoryPackSerializer();
services.AddCatgaResilience();
```

### Redis Setup
```csharp
var redis = await ConnectionMultiplexer.ConnectAsync("localhost:6379");
services.AddCatga();
services.AddSingleton(redis);
services.UseMemoryPackSerializer();
services.AddCatgaResilience();
services.AddRedisTransport(redis);
services.AddRedisEventStore();
services.AddRedisSnapshotStore();
services.AddRedisIdempotencyStore();
services.AddRedisDeadLetterQueue();
services.AddRedisDistributedLock();
```

### NATS Setup
```csharp
var nats = new NatsConnection(new NatsOpts { Url = "nats://localhost:4222" });
await nats.ConnectAsync();
services.AddCatga();
services.AddSingleton(nats);
services.UseMemoryPackSerializer();
services.AddCatgaResilience();
services.AddNatsTransport("nats://localhost:4222");
```

### Mixed Backend (Redis Transport + InMemory Persistence)
```csharp
services.AddCatga();
services.AddSingleton(redis);
services.UseMemoryPackSerializer();
services.AddCatgaResilience();
services.AddRedisTransport(redis);      // Transport: Redis
services.AddInMemoryPersistence();       // Persistence: InMemory
```


## Message Types

### CRITICAL: Message Definition Rules
1. All messages MUST be `record` types
2. Queries inherit from `QueryBase<TResponse>`
3. Commands inherit from `CommandBase`
4. Events inherit from `EventBase`

```csharp
// Query - returns a value
public record GetOrderQuery : QueryBase<OrderDto>
{
    public string OrderId { get; init; } = "";
}

// Command - no return value (returns CatgaResult)
public record CreateOrderCommand : CommandBase
{
    public string CustomerId { get; init; } = "";
    public List<OrderItem> Items { get; init; } = new();
}

// Event - for event sourcing and pub/sub
public record OrderCreatedEvent : EventBase
{
    public string OrderId { get; init; } = "";
    public decimal Total { get; init; }
}
```

### Handler Implementation

```csharp
// Query handler - returns CatgaResult<T>
public class GetOrderQueryHandler : IRequestHandler<GetOrderQuery, OrderDto>
{
    public ValueTask<CatgaResult<OrderDto>> HandleAsync(GetOrderQuery request, CancellationToken ct = default)
    {
        var order = /* fetch order */;
        return ValueTask.FromResult(CatgaResult<OrderDto>.Success(order));
    }
}

// Command handler - returns CatgaResult
public class CreateOrderCommandHandler : IRequestHandler<CreateOrderCommand>
{
    public ValueTask<CatgaResult> HandleAsync(CreateOrderCommand request, CancellationToken ct = default)
    {
        // process command
        return ValueTask.FromResult(CatgaResult.Success());
    }
}

// Event handler
public class OrderCreatedEventHandler : IEventHandler<OrderCreatedEvent>
{
    public ValueTask HandleAsync(OrderCreatedEvent @event, CancellationToken ct = default)
    {
        // handle event
        return ValueTask.CompletedTask;
    }
}
```

## Using the Mediator

### CRITICAL: SendAsync Type Parameters
```csharp
// For queries - TWO type parameters required
var result = await mediator.SendAsync<GetOrderQuery, OrderDto>(new GetOrderQuery { OrderId = "123" });

// For commands - ONE type parameter
var result = await mediator.SendAsync<CreateOrderCommand>(new CreateOrderCommand { ... });

// Publishing events
await mediator.PublishAsync(new OrderCreatedEvent { OrderId = "123" });
```

## CatgaResult Pattern

### CRITICAL: Correct Method Names
```csharp
// SUCCESS - use Success(), NOT Ok()
CatgaResult.Success()
CatgaResult<T>.Success(value)

// FAILURE - use Failure(), NOT Fail()
CatgaResult.Failure("error message")
CatgaResult<T>.Failure("error message")

// Checking result
if (result.IsSuccess)
{
    var value = result.Value; // for CatgaResult<T>
}
else
{
    var error = result.Error;
}
```


## Event Sourcing

### Event Store
```csharp
var eventStore = sp.GetRequiredService<IEventStore>();

// Append events - use AppendAsync, NOT SaveAsync
await eventStore.AppendAsync(streamId, events, expectedVersion);

// Read events - use ReadAsync, NOT LoadAsync
var stream = await eventStore.ReadAsync(streamId);
foreach (var evt in stream.Events)
{
    // process event
}
```

### Snapshot Store
```csharp
var snapshotStore = sp.GetRequiredService<ISnapshotStore>();

// Save snapshot
await snapshotStore.SaveAsync(aggregateId, state, version);

// Load snapshot - use LoadAsync<T>
var snapshot = await snapshotStore.LoadAsync<MyState>(aggregateId);
if (snapshot != null)
{
    var state = snapshot.State;
    var version = snapshot.Version;
}
```

### Aggregate Repository Pattern
```csharp
public class OrderAggregate : AggregateRoot
{
    public string CustomerId { get; private set; } = "";
    public decimal Total { get; private set; }
    
    public void Create(string customerId)
    {
        Apply(new OrderCreatedEvent { CustomerId = customerId });
    }
    
    protected override void When(IEvent @event)
    {
        switch (@event)
        {
            case OrderCreatedEvent e:
                CustomerId = e.CustomerId;
                break;
        }
    }
}

// Using repository
var repo = sp.GetRequiredService<IAggregateRepository<OrderAggregate>>();
var order = await repo.LoadAsync(orderId);
order.Create("customer-1");
await repo.SaveAsync(order);
```

## Idempotency Store

```csharp
var idempotencyStore = sp.GetRequiredService<IIdempotencyStore>();

// Check if processed - use HasBeenProcessedAsync
var exists = await idempotencyStore.HasBeenProcessedAsync(messageId);

// Mark as processed - use MarkAsProcessedAsync<T>
await idempotencyStore.MarkAsProcessedAsync<string>(messageId, "result");
```

## Distributed Lock

```csharp
var lockProvider = sp.GetRequiredService<IDistributedLockProvider>();

// Create lock instance
var distLock = lockProvider.CreateLock("my-lock-key");

// Acquire lock with timeout
await using var handle = await distLock.TryAcquireAsync(TimeSpan.FromSeconds(30));
if (handle != null)
{
    // Lock acquired, do work
    // Lock automatically released when handle is disposed
}
```


## Attributes for Behaviors

### Retry
```csharp
[Retry]  // Default: 3 attempts, 100ms delay, exponential backoff
public record MyCommand : CommandBase { }

// Custom settings
[Retry]
public record MyCommand : CommandBase { }
// Then configure via attribute properties:
// MaxAttempts = 5, DelayMs = 200, Exponential = true
```

### Timeout
```csharp
[Timeout(30)]  // 30 seconds timeout
public record MyCommand : CommandBase { }
```

### Circuit Breaker
```csharp
[CircuitBreaker]  // Default: 5 failures, 30s break duration
public record MyCommand : CommandBase { }
```

### Idempotent
```csharp
[Idempotent]  // Enables idempotency checking
public record MyCommand : CommandBase { }

// With custom TTL
[Idempotent]  // TtlSeconds = 3600
public record MyCommand : CommandBase { }
```

### Distributed Lock
```csharp
[DistributedLock("order-{OrderId}")]  // Lock key with placeholder
public record ProcessOrderCommand : CommandBase
{
    public string OrderId { get; init; } = "";
}
```

### Sharding
```csharp
[Sharded("CustomerId")]  // Route to shard based on CustomerId
public record CustomerCommand : CommandBase
{
    public string CustomerId { get; init; } = "";
}
```

### Cluster Attributes
```csharp
[LeaderOnly]  // Only execute on leader node
public record LeaderCommand : CommandBase { }

[Broadcast]  // Execute on all nodes
public record BroadcastCommand : CommandBase { }

[ClusterSingleton]  // Single instance across cluster
public record SingletonTask : CommandBase { }
```

## Message Transport

```csharp
var transport = sp.GetRequiredService<IMessageTransport>();

// Publish event
await transport.PublishAsync(new MyEvent { Data = "test" });

// Subscribe to events (typically done at startup)
await transport.SubscribeAsync<MyEvent>(async (evt, ct) =>
{
    // Handle event
});
```


## Flow/Saga DSL

### Basic Flow Definition
```csharp
public class OrderFlow : IFlow<OrderFlowState>
{
    public void Configure(IFlowBuilder<OrderFlowState> builder)
    {
        builder
            .Step("ValidateOrder", async (state, ct) =>
            {
                // Validation logic
                return new StepResult(true, null);
            })
            .Step("ProcessPayment", async (state, ct) =>
            {
                // Payment logic
                state.PaymentId = "pay-123";
                return new StepResult(true, null);
            })
            .Step("ShipOrder", async (state, ct) =>
            {
                // Shipping logic
                return new StepResult(true, null);
            });
    }
}

public class OrderFlowState : BaseFlowState
{
    public string OrderId { get; set; } = "";
    public string PaymentId { get; set; } = "";
}
```

### Flow State
```csharp
var state = new FlowState
{
    Id = "flow-123",
    Type = "OrderFlow",
    Status = FlowStatus.Running
};
```

### FlowResult
```csharp
var result = new FlowResult(
    IsSuccess: true,
    CompletedSteps: 3,
    Duration: TimeSpan.FromSeconds(5)
);
```

## Distributed ID Generation

```csharp
// Snowflake ID Generator
var generator = new SnowflakeIdGenerator(nodeId: 1);
var id = generator.NextId();  // Returns long

// MessageId
var messageId = MessageId.NewId(generator);

// CorrelationId
var correlationId = CorrelationId.NewId(generator);

// Simple ID generation
var base64Id = IdGenerator.NewBase64Id();
```

## Serialization

### MemoryPack (Required - AOT Compatible)

Catga uses MemoryPack for all internal serialization. It's binary, fast, and AOT compatible.

```csharp
// Register serializer (REQUIRED)
services.UseMemoryPackSerializer();
```

### CRITICAL: All Serializable Types Must Have [MemoryPackable]

```csharp
// DTOs, Snapshots, and any persisted state
[MemoryPackable]
public partial class OrderDto
{
    public string Id { get; set; } = "";
    public string CustomerId { get; set; } = "";
    public decimal Total { get; set; }
}

[MemoryPackable]
public partial class OrderSnapshot
{
    public string OrderId { get; set; } = "";
    public OrderStatus Status { get; set; }
    public List<OrderItem> Items { get; set; } = new();
}

// Complex nested types
[MemoryPackable]
public partial class OrderItem
{
    public string ProductId { get; set; } = "";
    public int Quantity { get; set; }
    public decimal Price { get; set; }
}

// Collections and dictionaries work automatically
[MemoryPackable]
public partial class ComplexState
{
    public List<string>? Items { get; set; }
    public Dictionary<string, int>? Metadata { get; set; }
}
```

### Message Types (record) - No [MemoryPackable] Needed

Message types (Query, Command, Event) that inherit from base classes don't need `[MemoryPackable]`:

```csharp
// These work without [MemoryPackable]
public record CreateOrderCommand : CommandBase
{
    public string CustomerId { get; init; } = "";
}

public record OrderCreatedEvent : EventBase
{
    public string OrderId { get; init; } = "";
}
```

### Manual Serialization (if needed)

```csharp
// Serialize
var bytes = MemoryPackSerializer.Serialize(myObject);

// Deserialize
var obj = MemoryPackSerializer.Deserialize<MyType>(bytes);
```


## ASP.NET Core Integration

### Setup
```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddCatga();
builder.Services.AddInMemoryPersistence();
builder.Services.AddInMemoryTransport();
builder.Services.UseMemoryPackSerializer();
builder.Services.AddCatgaResilience();

var app = builder.Build();
app.UseCatga();  // Add middleware
```

### Minimal API Endpoints
```csharp
app.MapPost("/orders", async (CreateOrderCommand cmd, ICatgaMediator mediator) =>
{
    var result = await mediator.SendAsync<CreateOrderCommand>(cmd);
    return result.IsSuccess ? Results.Ok() : Results.BadRequest(result.Error);
});

app.MapGet("/orders/{id}", async (string id, ICatgaMediator mediator) =>
{
    var result = await mediator.SendAsync<GetOrderQuery, OrderDto>(new GetOrderQuery { OrderId = id });
    return result.IsSuccess ? Results.Ok(result.Value) : Results.NotFound();
});
```

## Dead Letter Queue

```csharp
var dlq = sp.GetRequiredService<IDeadLetterQueue>();

// Messages are automatically sent to DLQ on failure
// Manual inspection:
var messages = await dlq.GetMessagesAsync(limit: 100);
```

## Resilience Pipeline

```csharp
var resilienceProvider = sp.GetRequiredService<IResiliencePipelineProvider>();

// Get pipeline for specific operation
var pipeline = resilienceProvider.GetPipeline("my-operation");

// Execute with resilience
var result = await pipeline.ExecuteAsync(async ct =>
{
    // Your operation
    return "result";
});
```

## Common Mistakes to Avoid

### 1. Wrong Result Methods
```csharp
// WRONG
CatgaResult.Ok()
CatgaResult.Fail("error")

// CORRECT
CatgaResult.Success()
CatgaResult.Failure("error")
```

### 2. Wrong Event Store Methods
```csharp
// WRONG
await eventStore.SaveAsync(...)
await eventStore.LoadAsync(...)

// CORRECT
await eventStore.AppendAsync(streamId, events, expectedVersion)
var stream = await eventStore.ReadAsync(streamId)
```

### 3. Missing Type Parameters
```csharp
// WRONG - for queries
await mediator.SendAsync(new GetOrderQuery { ... });

// CORRECT - queries need TWO type parameters
await mediator.SendAsync<GetOrderQuery, OrderDto>(new GetOrderQuery { ... });
```

### 4. Non-Record Message Types
```csharp
// WRONG
public class MyCommand : CommandBase { }

// CORRECT
public record MyCommand : CommandBase { }
```

### 5. Missing MemoryPackable Attribute
```csharp
// WRONG - DTOs/Snapshots won't serialize
public partial class MySnapshot { }

// CORRECT - Add [MemoryPackable] for any persisted type
[MemoryPackable]
public partial class MySnapshot { }
```

### 6. Using JSON Instead of MemoryPack
```csharp
// WRONG - Catga doesn't use JSON for internal serialization
services.AddJsonSerializer();

// CORRECT - Use MemoryPack
services.UseMemoryPackSerializer();
```


## Service Registration Order

The order of service registration matters:

```csharp
// 1. Core Catga
services.AddCatga();

// 2. External dependencies (Redis, NATS connections)
services.AddSingleton(redisConnection);
services.AddSingleton(natsConnection);

// 3. Serializer
services.UseMemoryPackSerializer();

// 4. Resilience (adds behaviors)
services.AddCatgaResilience();

// 5. Transport (choose one)
services.AddInMemoryTransport();
// OR
services.AddRedisTransport(redis);
// OR
services.AddNatsTransport(url);

// 6. Persistence (choose one or mix)
services.AddInMemoryPersistence();
// OR
services.AddRedisEventStore();
services.AddRedisSnapshotStore();
// etc.
```

## Async Disposal

When using NATS or services with IAsyncDisposable:

```csharp
// WRONG
using var sp = services.BuildServiceProvider();

// CORRECT
await using var sp = services.BuildServiceProvider();
```

## Testing

### Build Command
```bash
dotnet build Catga.sln --no-restore
```

### Test Command
```bash
dotnet test tests/Catga.Tests/Catga.Tests.csproj --no-build --verbosity minimal
```

### AOT Validation
```bash
# InMemory only
dotnet run --project tests/Catga.AotValidation -- inmemory

# Redis (requires Redis server)
dotnet run --project tests/Catga.AotValidation -- redis

# NATS (requires NATS server)
dotnet run --project tests/Catga.AotValidation -- nats

# All backends
dotnet run --project tests/Catga.AotValidation -- all

# Mixed backend test
dotnet run --project tests/Catga.AotValidation -- mixed
```

## Key Interfaces Reference

| Interface | Purpose | InMemory | Redis | NATS |
|-----------|---------|----------|-------|------|
| `ICatgaMediator` | Send commands/queries | ✓ | ✓ | ✓ |
| `IEventStore` | Event sourcing | ✓ | ✓ | ✓ |
| `ISnapshotStore` | Aggregate snapshots | ✓ | ✓ | - |
| `IIdempotencyStore` | Deduplication | ✓ | ✓ | ✓ |
| `IMessageTransport` | Pub/Sub messaging | ✓ | ✓ | ✓ |
| `IDeadLetterQueue` | Failed messages | ✓ | ✓ | ✓ |
| `IDistributedLockProvider` | Distributed locks | ✓ | ✓ | ✓ |
| `IProjectionCheckpointStore` | Projection state | ✓ | ✓ | ✓ |
| `IFlowStore` | Flow/Saga state | ✓ | ✓ | ✓ |
| `IResiliencePipelineProvider` | Polly pipelines | ✓ | ✓ | ✓ |

## Environment Variables

```bash
REDIS_CONNECTION=localhost:6379
NATS_URL=nats://localhost:4222
```

## Docker Commands for Testing

```bash
# Start Redis
docker run -d --name catga-redis -p 6379:6379 redis:latest

# Start NATS
docker run -d --name catga-nats -p 4222:4222 nats:latest

# Stop and remove
docker stop catga-redis catga-nats
docker rm catga-redis catga-nats
```


## Complete Example: Order System

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

// Setup Catga
builder.Services.AddCatga();
builder.Services.UseMemoryPackSerializer();
builder.Services.AddCatgaResilience();
builder.Services.AddInMemoryPersistence();
builder.Services.AddInMemoryTransport();

var app = builder.Build();
app.UseCatga();

// Endpoints
app.MapPost("/orders", async (CreateOrderCommand cmd, ICatgaMediator mediator) =>
{
    var result = await mediator.SendAsync<CreateOrderCommand>(cmd);
    return result.IsSuccess ? Results.Created($"/orders/{cmd.OrderId}", null) : Results.BadRequest(result.Error);
});

app.MapGet("/orders/{id}", async (string id, ICatgaMediator mediator) =>
{
    var result = await mediator.SendAsync<GetOrderQuery, OrderDto>(new GetOrderQuery { OrderId = id });
    return result.IsSuccess ? Results.Ok(result.Value) : Results.NotFound();
});

app.Run();

// Messages
public record CreateOrderCommand : CommandBase
{
    public string OrderId { get; init; } = IdGenerator.NewBase64Id();
    public string CustomerId { get; init; } = "";
    public List<OrderItem> Items { get; init; } = new();
}

public record GetOrderQuery : QueryBase<OrderDto>
{
    public string OrderId { get; init; } = "";
}

public record OrderCreatedEvent : EventBase
{
    public string OrderId { get; init; } = "";
    public string CustomerId { get; init; } = "";
    public decimal Total { get; init; }
}

// DTOs - MUST have [MemoryPackable] for persistence
[MemoryPackable]
public partial class OrderDto
{
    public string Id { get; set; } = "";
    public string CustomerId { get; set; } = "";
    public decimal Total { get; set; }
}

[MemoryPackable]
public partial class OrderItem
{
    public string ProductId { get; set; } = "";
    public int Quantity { get; set; }
    public decimal Price { get; set; }
}

// Handlers
public class CreateOrderCommandHandler : IRequestHandler<CreateOrderCommand>
{
    private readonly IEventStore _eventStore;
    private readonly IMessageTransport _transport;
    
    public CreateOrderCommandHandler(IEventStore eventStore, IMessageTransport transport)
    {
        _eventStore = eventStore;
        _transport = transport;
    }
    
    public async ValueTask<CatgaResult> HandleAsync(CreateOrderCommand request, CancellationToken ct = default)
    {
        var total = request.Items.Sum(i => i.Price * i.Quantity);
        var evt = new OrderCreatedEvent
        {
            OrderId = request.OrderId,
            CustomerId = request.CustomerId,
            Total = total
        };
        
        await _eventStore.AppendAsync($"order-{request.OrderId}", new List<IEvent> { evt }, -1, ct);
        await _transport.PublishAsync(evt, ct);
        
        return CatgaResult.Success();
    }
}

public class GetOrderQueryHandler : IRequestHandler<GetOrderQuery, OrderDto>
{
    private readonly IEventStore _eventStore;
    
    public GetOrderQueryHandler(IEventStore eventStore)
    {
        _eventStore = eventStore;
    }
    
    public async ValueTask<CatgaResult<OrderDto>> HandleAsync(GetOrderQuery request, CancellationToken ct = default)
    {
        var stream = await _eventStore.ReadAsync($"order-{request.OrderId}", ct: ct);
        if (stream.Events.Count == 0)
            return CatgaResult<OrderDto>.Failure("Order not found");
        
        var created = stream.Events.OfType<OrderCreatedEvent>().FirstOrDefault();
        if (created == null)
            return CatgaResult<OrderDto>.Failure("Order not found");
        
        return CatgaResult<OrderDto>.Success(new OrderDto
        {
            Id = created.OrderId,
            CustomerId = created.CustomerId,
            Total = created.Total
        });
    }
}
```

---

*Last updated: December 2024*
*Catga Version: Latest*
