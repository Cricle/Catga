<div align="center">

<img src="docs/web/favicon.svg" width="120" height="120" alt="Catga Logo"/>

# Catga

**High-Performance .NET CQRS/Event Sourcing Framework**

[![.NET 9](https://img.shields.io/badge/.NET-9.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Native AOT](https://img.shields.io/badge/Native-AOT-success?logo=dotnet)](https://learn.microsoft.com/dotnet/core/deploying/native-aot/)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

**Zero Reflection · Source Generated · Native AOT · Distributed Ready**

[Quick Start](#-quick-start) · [Performance](#-performance) · [Examples](#-ordersystem-example) · [Documentation](https://cricle.github.io/Catga/)

</div>

---

## ⚡ Performance

> Latest benchmark snapshot: 2026-04-29, BenchmarkDotNet v0.14.0, Debian 12, Intel Xeon Platinum 8457C, .NET SDK 10.0.201, .NET Runtime 10.0.5.

| Scenario | Result |
|----------|--------|
| Command | `149.72 ns / 88 B` |
| Event | `87.23 ns / 64 B` |
| Batch 100 Commands | `13.24 μs / 8,800 B` |
| MediatR comparison | Faster on single-command and batch-100 path, but allocates more |
| MassTransit comparison | Much heavier in this mediator/request-reply benchmark |

Authoritative benchmark docs:
- [Benchmark Results](./docs/BENCHMARK-RESULTS.md)
- [Performance Index](./docs/performance/README.md)

---

## ✨ Features

- **Zero Reflection** - Source Generator, compile-time handler discovery
- **Native AOT** - Full support, trimming safe
- **Distributed** - Redis Streams, NATS JetStream
- **Event Sourcing** - Event Store, Snapshots, Projections, Time Travel
- **Flow DSL** - Distributed workflows, Sagas, ForEach parallel processing
- **Reliability** - Outbox/Inbox, Idempotency, Dead Letter Queue
- **Observability** - OpenTelemetry tracing, Metrics

---

## 🚀 Quick Start

```bash
dotnet add package Catga
dotnet add package Catga.Transport.InMemory
dotnet add package Catga.Persistence.InMemory
dotnet add package Catga.Serialization.MemoryPack
```

```csharp
// Define command
[MemoryPackable]
public partial record CreateOrder(string ProductId, int Quantity) : IRequest<Order>;

// Define handler
public class CreateOrderHandler : IRequestHandler<CreateOrder, Order>
{
    public ValueTask<CatgaResult<Order>> HandleAsync(CreateOrder cmd, CancellationToken ct = default)
        => new(CatgaResult<Order>.Success(new Order(cmd.ProductId, cmd.Quantity)));
}

// Configure
builder.Services.AddCatga().UseMemoryPack();
builder.Services.AddInMemoryTransport();
builder.Services.AddInMemoryPersistence();

// Use
var result = await mediator.SendAsync<CreateOrder, Order>(new("PROD-001", 5));
```

---

## 📦 Packages

| Package | Description |
|---------|-------------|
| `Catga` | Core framework |
| `Catga.Transport.InMemory` | In-memory transport |
| `Catga.Transport.Redis` | Redis Streams |
| `Catga.Transport.Nats` | NATS JetStream |
| `Catga.Transport.RabbitMQ` | RabbitMQ AMQP |
| `Catga.Persistence.InMemory` | In-memory persistence |
| `Catga.Persistence.Redis` | Redis persistence |
| `Catga.Persistence.Nats` | NATS persistence |
| `Catga.Serialization.MemoryPack` | Binary serialization |
| `Catga.AspNetCore` | ASP.NET Core integration |

---

## 🛒 OrderSystem Example

A complete e-commerce system demonstrating best practices. Focus on your business logic, not framework boilerplate.

```
examples/OrderSystem/
├── Commands/         # Command definitions
├── Queries/          # Query definitions
├── Events/           # Event definitions
├── Handlers/         # Business logic
├── Flows/            # Distributed workflows
├── Models/           # Domain models
└── Program.cs        # Minimal setup
```

### Run

```bash
cd examples/OrderSystem
dotnet run

# Run tests
.\test.ps1
```

### Key Patterns

**1. Commands & Queries** - Clean separation of write/read operations
```csharp
// Command - changes state
public record CreateOrder(string CustomerId, List<OrderItem> Items) : IRequest<Order>;

// Query - reads state
public record GetOrder(string OrderId) : IRequest<Order>;
```

**2. Event Sourcing** - Full audit trail
```csharp
public record OrderCreated(string OrderId, string CustomerId) : IEvent;
public record OrderShipped(string OrderId, string TrackingNumber) : IEvent;
```

**3. Flow DSL** - Distributed workflows
```csharp
public class OrderFlow : FlowConfig<OrderState>
{
    protected override void Configure(IFlowBuilder<OrderState> flow)
    {
        flow.Send(s => new ReserveInventory(s.Items))
            .IfFail(s => new ReleaseInventory(s.ReservationId));
        
        flow.Send(s => new ProcessPayment(s.OrderId, s.Total))
            .IfFail(s => new RefundPayment(s.PaymentId));
        
        flow.Publish(s => new OrderCompleted(s.OrderId));
    }
}
```

---

## 🗄️ Event Sourcing

```csharp
// Append events
await eventStore.AppendAsync("Order-123", new[] { orderCreated, itemAdded });

// Read stream
var stream = await eventStore.ReadAsync("Order-123");

// Snapshots
await snapshotStore.SaveAsync("Order-123", aggregate, version);

// Time Travel
var stateAtV5 = await timeTravelService.GetStateAtVersionAsync("order-1", 5);
```

---

## 🔄 Flow DSL

```csharp
public class ProcessOrderFlow : FlowConfig<OrderState>
{
    protected override void Configure(IFlowBuilder<OrderState> flow)
    {
        // Sequential steps with compensation
        flow.Send(s => new ReserveInventory(s.OrderId))
            .Into(s => s.ReservationId)
            .IfFail(s => new ReleaseInventory(s.ReservationId));

        // Parallel processing
        flow.ForEach<OrderItem>(s => s.Items)
            .Configure((item, f) => f.Send(s => new ProcessItem(item.Id)))
            .WithParallelism(4)
            .ContinueOnFailure()
        .EndForEach();

        // Conditional logic
        flow.If(s => s.AllItemsProcessed)
            .Send(s => new CompleteOrder(s.OrderId))
        .EndIf();
    }
}
```

---

## 🔧 Configuration

```csharp
// OpenTelemetry
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t.AddSource(CatgaOpenTelemetryExtensions.ActivitySourceName))
    .WithMetrics(m => m.AddMeter(CatgaOpenTelemetryExtensions.MeterName));

// Resilience
builder.Services.AddCatga()
    .UseResilience(o => o.TransportRetryCount = 3);

// Reliability
builder.Services.AddCatga()
    .UseInbox()
    .UseOutbox();
```

---

## 📚 Documentation

- [Documentation Home](./docs/README.md)
- [Getting Started](./docs/articles/getting-started.md)
- [Architecture Index](./docs/architecture/README.md)
- [Guides Index](./docs/guides/README.md)
- [Performance Index](./docs/performance/README.md)
- [MassTransit Migration / Comparison](./docs/guides/masstransit-migration.md)
- [Benchmark Results](./docs/BENCHMARK-RESULTS.md)
- [API Reference](https://cricle.github.io/Catga/api/)

---

## 📄 License

[MIT](LICENSE)
