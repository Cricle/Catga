# Event Sourcing Guide

Catga provides a complete event sourcing solution with support for InMemory, Redis, and NATS backends.

## Core Concepts

### Event Store

The event store persists events as an append-only log:

```csharp
// Append events
await eventStore.AppendAsync("Order-123", new IEvent[]
{
    new OrderCreated { OrderId = "123", CustomerId = "C001" },
    new ItemAdded { OrderId = "123", ProductName = "Laptop", Price = 999.99m }
});

// Read events
var stream = await eventStore.ReadAsync("Order-123");
foreach (var stored in stream.Events)
{
    Console.WriteLine($"v{stored.Version}: {stored.EventType}");
}

// Read from specific version
var fromV5 = await eventStore.ReadAsync("Order-123", fromVersion: 5);

// Read up to specific version (time travel)
var toV10 = await eventStore.ReadToVersionAsync("Order-123", toVersion: 10);
```

### Aggregate Root

```csharp
public class OrderAggregate : AggregateRoot
{
    public override string Id { get; protected set; } = "";
    public decimal TotalAmount { get; private set; }
    public string Status { get; private set; } = "Created";

    public void AddItem(string productName, decimal price)
    {
        RaiseEvent(new ItemAdded { ProductName = productName, Price = price });
    }

    protected override void When(IEvent @event)
    {
        switch (@event)
        {
            case OrderCreated e:
                Id = e.OrderId;
                break;
            case ItemAdded e:
                TotalAmount += e.Price;
                break;
        }
    }
}
```

## Projections

Projections build read models from events:

```csharp
public class OrderSummaryProjection : IProjection
{
    public string Name => "OrderSummary";

    public Dictionary<string, OrderSummary> Orders { get; } = new();

    public ValueTask ApplyAsync(IEvent @event, CancellationToken ct = default)
    {
        switch (@event)
        {
            case OrderCreated e:
                Orders[e.OrderId] = new OrderSummary { OrderId = e.OrderId };
                break;
            case ItemAdded e:
                if (Orders.TryGetValue(e.OrderId, out var order))
                    order.TotalAmount += e.Price;
                break;
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask ResetAsync(CancellationToken ct = default)
    {
        Orders.Clear();
        return ValueTask.CompletedTask;
    }
}
```

### Projection Runners

```csharp
// Catch-up projection (replay from beginning)
var catchUp = new CatchUpProjectionRunner<OrderSummaryProjection>(
    eventStore, checkpointStore, projection, "OrderSummary");
await catchUp.RunAsync();

// Live projection (process new events)
var live = new LiveProjectionRunner<OrderSummaryProjection>(
    eventStore, checkpointStore, projection, "OrderSummary");
await live.StartAsync(cancellationToken);

// Rebuild projection
var rebuilder = new ProjectionRebuilder<OrderSummaryProjection>(
    eventStore, checkpointStore, projection, "OrderSummary");
await rebuilder.RebuildAsync();
```

## Subscriptions

Persistent subscriptions for event processing:

```csharp
// Create subscription
var subscription = new PersistentSubscription("order-processor", "Order-*");
await subscriptionStore.SaveAsync(subscription);

// Event handler
public class OrderEventHandler : IEventHandler
{
    public ValueTask HandleAsync(IEvent @event, CancellationToken ct)
    {
        // Process event
        return ValueTask.CompletedTask;
    }
}

// Run subscription
var runner = new SubscriptionRunner(eventStore, subscriptionStore, handler);
await runner.RunOnceAsync("order-processor");
```

## Snapshots

Snapshots optimize aggregate loading:

```csharp
// Save snapshot
await snapshotStore.SaveAsync("Order-123", aggregate, aggregate.Version);

// Load latest snapshot
var snapshot = await snapshotStore.LoadAsync<OrderAggregate>("Order-123");

// Load snapshot at specific version (time travel)
var atV10 = await snapshotStore.LoadAtVersionAsync<OrderAggregate>("Order-123", 10);

// Get snapshot history
var history = await snapshotStore.GetSnapshotHistoryAsync("Order-123");
```

## Time Travel

Query historical state:

```csharp
// Get state at specific version
var stateAtV5 = await timeTravelService.GetStateAtVersionAsync("order-1", 5);

// Get version history
var history = await timeTravelService.GetVersionHistoryAsync("order-1");

// Compare versions
var comparison = await timeTravelService.CompareVersionsAsync("order-1", 5, 10);
```

## Audit & Compliance

### Immutability Verification

```csharp
var verifier = new ImmutabilityVerifier(eventStore);
var result = await verifier.VerifyStreamAsync("Order-123");

if (result.IsValid)
    Console.WriteLine($"Stream hash: {result.Hash}");
else
    Console.WriteLine($"Verification failed: {result.Error}");
```

### Audit Logging

```csharp
await auditStore.LogAsync(new AuditLogEntry
{
    StreamId = "Order-123",
    Action = AuditAction.EventAppended,
    UserId = "admin",
    Details = "Order created"
});

var logs = await auditStore.GetLogsAsync("Order-123");
var recentLogs = await auditStore.GetLogsByTimeRangeAsync(from, to);
```

### GDPR Support

```csharp
// Request erasure
await gdprService.RequestErasureAsync("customer-123", "admin");

// Get pending requests
var pending = await gdprService.GetPendingRequestsAsync();

// Crypto erasure (destroy encryption keys)
await cryptoErasureService.EraseAsync("customer-123");
```

## Event Versioning

Handle schema evolution:

```csharp
// V1 event
[EventVersion(1)]
public record OrderItemAddedV1 : IEvent
{
    public string OrderId { get; init; }
    public string ProductName { get; init; }
    public decimal Price { get; init; }
}

// V2 event (added SKU)
[EventVersion(2)]
public record OrderItemAddedV2 : IEvent
{
    public string OrderId { get; init; }
    public string ProductName { get; init; }
    public string Sku { get; init; }
    public decimal Price { get; init; }
}

// Upcaster
public class OrderItemAddedV1ToV2 : EventUpgrader<OrderItemAddedV1, OrderItemAddedV2>
{
    public override int SourceVersion => 1;
    public override int TargetVersion => 2;

    protected override OrderItemAddedV2 UpgradeCore(OrderItemAddedV1 source) => new()
    {
        OrderId = source.OrderId,
        ProductName = source.ProductName,
        Sku = $"SKU-{source.ProductName.ToUpper()}",
        Price = source.Price
    };
}

// Register
var registry = new EventVersionRegistry();
registry.Register(new OrderItemAddedV1ToV2());
```

## Backend Implementations

### InMemory (Development/Testing)

```csharp
services.AddSingleton<IEventStore, InMemoryEventStore>();
services.AddSingleton<InMemoryProjectionCheckpointStore>();
services.AddSingleton<InMemorySubscriptionStore>();
services.AddSingleton<EnhancedInMemorySnapshotStore>();
```

### Redis (Production)

```csharp
services.AddSingleton<IEventStore, RedisEventStore>();
services.AddSingleton<IProjectionCheckpointStore, RedisProjectionCheckpointStore>();
services.AddSingleton<ISubscriptionStore, RedisSubscriptionStore>();
services.AddSingleton<IEnhancedSnapshotStore, RedisEnhancedSnapshotStore>();
```

### NATS (Production)

```csharp
services.AddSingleton<IEventStore, NatsJSEventStore>();
services.AddSingleton<IProjectionCheckpointStore, NatsProjectionCheckpointStore>();
services.AddSingleton<ISubscriptionStore, NatsSubscriptionStore>();
services.AddSingleton<IEnhancedSnapshotStore, NatsEnhancedSnapshotStore>();
```

## Testing

```csharp
// Event store fixture
using var fixture = new EventStoreFixture(eventStore, cleanup);
await fixture.SeedAsync("Order-1", events);
await fixture.AssertEventAppendedAsync<OrderCreated>("Order-1");
await fixture.AssertEventCountAsync("Order-1", 3);

// Aggregate fixture
var fixture = new AggregateFixture<OrderAggregate>();
fixture.Given(new OrderCreated { OrderId = "1" });
fixture.Aggregate.AddItem("Laptop", 999);
fixture.AssertUncommittedEvent<ItemAdded>();

// Replay tester
var tester = new ReplayTester<OrderAggregate>(eventStore);
var aggregate = await tester.ReplayAsync("Order-1");
var atV5 = await tester.ReplayToVersionAsync("Order-1", 5);

// BDD scenario
await new ScenarioRunner<OrderAggregate>(eventStore)
    .Given("Order-1", new OrderCreated { OrderId = "1" })
    .When(agg => agg.AddItem("Laptop", 999))
    .Then(agg => agg.TotalAmount.Should().Be(999))
    .RunAsync();
```


