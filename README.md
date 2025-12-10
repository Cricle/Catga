<div align="center">

<img src="docs/web/favicon.svg" width="120" height="120" alt="Catga Logo"/>

# Catga

**High-Performance .NET CQRS/Event Sourcing Framework**

[![.NET 9](https://img.shields.io/badge/.NET-9.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Native AOT](https://img.shields.io/badge/Native-AOT-success?logo=dotnet)](https://learn.microsoft.com/dotnet/core/deploying/native-aot/)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![Build](https://github.com/Cricle/Catga/actions/workflows/coverage.yml/badge.svg)](https://github.com/Cricle/Catga/actions/workflows/coverage.yml)

**Low Memory · Zero Reflection · Source Generated · Native AOT · Distributed Ready**

[Quick Start](#-quick-start) · [Performance](#-performance-benchmarks) · [Features](#-features) · [Documentation](https://cricle.github.io/Catga/)

</div>

---

## 🚀 Performance Benchmarks

> **Real benchmark data** from BenchmarkDotNet on AMD Ryzen 7 5800H, .NET 9.0.8
> Run: `dotnet run -c Release --filter *MediatRComparison*` in `benchmarks/Catga.Benchmarks`

### Catga vs MediatR Comparison

| Operation | Catga (minimal) | MediatR | Winner | Memory Savings |
|-----------|-----------------|---------|--------|----------------|
| **Command** | 206 ns | 185 ns | MediatR +11% | **88 B vs 424 B (4.8x less)** |
| **Query** | 205 ns | 208 ns | **Catga +1%** | **32 B vs 368 B (11.5x less)** |
| **Event** | **119 ns** | 147 ns | **Catga +19%** | **64 B vs 288 B (4.5x less)** |
| **Batch 100** | 13.9 μs | 13.4 μs | MediatR +4% | **8.8 KB vs 35.2 KB (4x less)** |

### Performance Modes

| Mode | Command | Query | Event | Use Case |
|------|---------|-------|-------|----------|
| **Minimal** | 206 ns | 205 ns | 119 ns | Production (max performance) |
| **Default** | 314 ns | 313 ns | 182 ns | Development (with logging/tracing) |

### Key Insights

- ✅ **Event publishing 19% faster** than MediatR (119 ns vs 147 ns)
- ✅ **Query performance on par** with MediatR (205 ns vs 208 ns)
- ✅ **4-11x less memory allocation** across all operations
- ✅ **Batch operations use 4x less memory** (8.8 KB vs 35.2 KB)

> **Note**: Catga's value extends beyond raw speed - it provides **distributed messaging** (Redis/NATS), **Event Sourcing**, **Outbox/Inbox patterns**, and **Native AOT** support that MediatR doesn't offer.

---

## ✨ Features

| Feature | Description |
|---------|-------------|
| **Low Memory** | 32-88 B/op single, 8.8 KB/100 batch (4x less than MediatR) |
| **Native AOT** | Full support, zero reflection, trimming safe |
| **Source Generator** | Compile-time handler discovery, zero runtime overhead |
| **Distributed** | Lock, Rate Limiting, Leader Election, Event Sourcing |
| **Multi-Transport** | Redis Streams, NATS JetStream, In-Memory |
| **Flow DSL** | Distributed workflows, ForEach parallel processing, Sagas |
| **Resilience** | Polly integration (Retry, Circuit Breaker, Timeout) |
| **Observability** | OpenTelemetry tracing, Metrics, Structured logging |
| **Reliability** | Outbox/Inbox pattern, Idempotency, Dead Letter Queue |

---

## 🚀 Quick Start

### Installation

```bash
dotnet add package Catga
dotnet add package Catga.Transport.InMemory
dotnet add package Catga.Persistence.InMemory
dotnet add package Catga.Serialization.MemoryPack
```

### Usage

```csharp
// 1. Define message
[MemoryPackable]
public partial record CreateOrderCommand(string ProductId, int Quantity) : IRequest<Order>;

// 2. Define handler
public class CreateOrderHandler : IRequestHandler<CreateOrderCommand, Order>
{
    public ValueTask<CatgaResult<Order>> HandleAsync(
        CreateOrderCommand request, CancellationToken ct = default)
    {
        var order = new Order { ProductId = request.ProductId, Quantity = request.Quantity };
        return new ValueTask<CatgaResult<Order>>(CatgaResult<Order>.Success(order));
    }
}

// 3. Configure services
builder.Services.AddCatga()
    .UseMemoryPack()
    .WithTracing()
    .UseResilience();

builder.Services.AddInMemoryTransport();
builder.Services.AddInMemoryPersistence();

// 4. Use mediator
var result = await mediator.SendAsync<CreateOrderCommand, Order>(
    new CreateOrderCommand("PROD-001", 5));
```

---

## 📦 Packages

| Package | Description |
|---------|-------------|
| `Catga` | Core framework |
| `Catga.Transport.InMemory` | In-memory transport (dev/test) |
| `Catga.Transport.Redis` | Redis Streams transport |
| `Catga.Transport.Nats` | NATS JetStream transport |
| `Catga.Persistence.InMemory` | In-memory persistence |
| `Catga.Persistence.Redis` | Redis persistence (Event Store, Snapshot, Lock, Rate Limiter) |
| `Catga.Persistence.Nats` | NATS JetStream persistence |
| `Catga.Serialization.MemoryPack` | High-performance binary serialization |
| `Catga.AspNetCore` | ASP.NET Core integration |
| `Catga.Testing` | Testing utilities |
| `Catga.Cli` | CLI tool for event sourcing management |

---

## 🗄️ Event Sourcing

Catga provides a complete event sourcing solution:

```csharp
// Event Store
await eventStore.AppendAsync("Order-123", new[] { orderCreated, itemAdded });
var stream = await eventStore.ReadAsync("Order-123");

// Projections
public class OrderSummaryProjection : IProjection
{
    public string Name => "OrderSummary";
    public ValueTask ApplyAsync(IEvent @event, CancellationToken ct) { /* ... */ }
    public ValueTask ResetAsync(CancellationToken ct) { /* ... */ }
}

// Subscriptions
var subscription = new PersistentSubscription("order-processor", "Order-*");
var runner = new SubscriptionRunner(eventStore, subscriptionStore, handler);
await runner.RunOnceAsync("order-processor");

// Snapshots
await snapshotStore.SaveAsync("Order-123", aggregate, version);
var snapshot = await snapshotStore.LoadAtVersionAsync<OrderAggregate>("Order-123", version);

// Time Travel
var stateAtV5 = await timeTravelService.GetStateAtVersionAsync("order-1", 5);
var history = await timeTravelService.GetVersionHistoryAsync("order-1");

// Audit & Compliance
await auditStore.LogAsync(new AuditLogEntry { StreamId, Action, UserId });
var result = await verifier.VerifyStreamAsync("Order-123"); // Immutability check
```

---

## 🔄 Flow DSL (Distributed Workflows)

Catga includes a powerful Flow DSL for building distributed workflows and sagas:

```csharp
// Define workflow state
public class OrderFlowState : IFlowState
{
    public string? FlowId { get; set; }
    public List<OrderItem> Items { get; set; } = [];
    public ConcurrentDictionary<string, string> ProcessedItems { get; set; } = new();
    public bool AllItemsProcessed { get; set; }
}

// Define workflow
public class ProcessOrderFlow : FlowConfig<OrderFlowState>
{
    protected override void Configure(IFlowBuilder<OrderFlowState> flow)
    {
        flow.Name("process-order")
            .DefaultTimeout(TimeSpan.FromMinutes(5));

        // Sequential steps
        flow.Send(s => new ReserveInventoryCommand(s.OrderId))
            .Into(s => s.ReservationId)
            .IfFail(s => new ReleaseInventoryCommand(s.ReservationId));

        // Parallel processing with ForEach
        flow.ForEach<OrderItem>(s => s.Items)
            .Configure((item, f) =>
            {
                f.Send(s => new ProcessItemCommand(item.Id, item.Quantity))
                 .Into(s => s.ProcessedItems[item.Id]);
            })
            .WithParallelism(4)           // Process 4 items concurrently
            .ContinueOnFailure()          // Don't stop on individual failures
            .OnItemSuccess((state, item, result) =>
            {
                // Track successful processing
                state.ProcessedCount++;
            })
            .OnComplete(s => s.AllItemsProcessed = true)
        .EndForEach();

        // Conditional logic
        flow.If(s => s.AllItemsProcessed)
            .Send(s => new CompleteOrderCommand(s.OrderId))
        .EndIf();
    }
}

// Execute workflow
var executor = new DslFlowExecutor<OrderFlowState, ProcessOrderFlow>(mediator, store, config);
var result = await executor.RunAsync(state);
```

### Flow DSL Features

- **🔄 ForEach Processing**: Parallel collection processing with configurable concurrency
- **🎯 Conditional Logic**: If/ElseIf/Else and Switch/Case constructs
- **⚡ Parallel Execution**: WhenAll/WhenAny for concurrent operations
- **🛡️ Error Handling**: Automatic compensation and retry strategies
- **💾 State Management**: Automatic persistence and recovery
- **📊 Progress Tracking**: Built-in progress monitoring and resumption

---

## 🛠️ CLI Tool

```bash
# Install
dotnet tool install -g Catga.Cli

# Commands
catga-cli events list                    # List event streams
catga-cli events read Order-123          # Read events from stream
catga-cli projections list               # List projections
catga-cli projections rebuild OrderSummary  # Rebuild projection
catga-cli flows list                     # List flows
catga-cli streams verify Order-123       # Verify stream integrity
```

---

## 🎯 When to Use Catga

| Aspect | Catga | MediatR |
|--------|-------|---------|
| **Event Performance** | **119 ns** ✅ | 147 ns |
| **Query Performance** | **205 ns** ✅ | 208 ns |
| **Command Performance** | 206 ns | **185 ns** ✅ |
| **Memory Efficiency** | **32-88 B/op** | 288-424 B/op |
| **Batch Memory** | **8.8 KB/100 ops** | 35.2 KB/100 ops |
| **Native AOT** | ✅ Full support | ⚠️ Limited |
| **Reflection** | Zero (source generated) | Uses reflection |
| **Distributed** | ✅ Redis, NATS, Event Sourcing | ❌ In-memory only |
| **Reliability** | ✅ Outbox/Inbox, DLQ, Idempotency | ❌ Not included |

### Choose Catga When

- ✅ **Event-heavy** workloads (19% faster event publishing)
- ✅ Need **distributed messaging** (Redis Streams, NATS JetStream)
- ✅ Building **event-sourced** systems
- ✅ Require **exactly-once delivery** (Outbox/Inbox pattern)
- ✅ **Native AOT** deployment (containers, serverless)
- ✅ **Memory-constrained** environments (4-11x less allocation)
- ✅ Need **observability** (OpenTelemetry tracing, metrics)

### Choose MediatR When

- ✅ Simple **in-memory** mediator pattern
- ✅ **Command-heavy** workloads (11% faster commands)
- ✅ No distributed requirements
- ✅ Existing MediatR codebase

---

## 🔧 Configuration

### OpenTelemetry Integration

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource(CatgaOpenTelemetryExtensions.ActivitySourceName))
    .WithMetrics(metrics => metrics
        .AddMeter(CatgaOpenTelemetryExtensions.MeterName));
```

### Resilience (Polly)

```csharp
builder.Services.AddCatga()
    .UseResilience(o =>
    {
        o.TransportRetryCount = 3;
        o.TransportRetryDelay = TimeSpan.FromMilliseconds(200);
    });
```

### Reliability Patterns

```csharp
builder.Services.AddCatga()
    .UseInbox()      // Exactly-once delivery
    .UseOutbox()     // Reliable publishing
    .UseAutoCompensation();
```

---

## 📚 Documentation

| Topic | Description |
|-------|-------------|
| [Getting Started](./docs/articles/getting-started.md) | First steps with Catga |
| [Flow DSL Guide](./docs/guides/flow-dsl.md) | Distributed workflows and ForEach processing |
| [Architecture](./docs/architecture/ARCHITECTURE.md) | Deep dive into internals |
| [Configuration](./docs/articles/configuration.md) | All configuration options |
| [OpenTelemetry](./docs/articles/opentelemetry-integration.md) | Tracing and metrics |
| [Distributed Tracing](./docs/observability/DISTRIBUTED-TRACING-GUIDE.md) | End-to-end tracing |
| [E2E Scenarios](./docs/examples/e2e-scenarios.md) | Distributed validation |

---

## 🎯 Examples

Complete e-commerce order system with distributed features:

```powershell
cd examples

# Single instance
.\run-demo.ps1 -Mode Single

# Cluster mode (3 replicas + Redis + NATS)
.\run-demo.ps1 -Mode Cluster

# Run benchmarks
dotnet run -c Release --project ../benchmarks/Catga.Benchmarks
```

---

## 📄 License

[MIT License](LICENSE)

---

<div align="center">

**⭐ Star this repo if you find it useful!**

**Built for speed. Designed for scale.**

</div>
