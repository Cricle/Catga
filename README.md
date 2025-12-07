<div align="center">

<img src="docs/web/favicon.svg" width="120" height="120" alt="Catga Logo"/>

# Catga

**Ultra High-Performance .NET CQRS/Event Sourcing Framework**

[![.NET 9](https://img.shields.io/badge/.NET-9.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Native AOT](https://img.shields.io/badge/Native-AOT-success?logo=dotnet)](https://learn.microsoft.com/dotnet/core/deploying/native-aot/)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![Build](https://github.com/Cricle/Catga/actions/workflows/coverage.yml/badge.svg)](https://github.com/Cricle/Catga/actions/workflows/coverage.yml)

**Low Memory · Zero Reflection · Source Generated · Native AOT · Distributed Ready**

[Quick Start](#-quick-start) · [Performance](#-performance-benchmarks) · [Features](#-features) · [Documentation](https://cricle.github.io/Catga/)

</div>

---

## 🚀 Performance Benchmarks

> **Real benchmark data** - Run `dotnet run -c Release --filter *MediatRComparison*` in `benchmarks/Catga.Benchmarks`

### Catga vs MediatR (Fair Comparison - Both In-Memory)

| Operation | Catga | MediatR | Catga Memory | MediatR Memory |
|-----------|-------|---------|--------------|----------------|
| Send Command | 342 ns | 164 ns | **88 B** | 424 B |
| Send Query | 313 ns | 153 ns | **32 B** | 368 B |
| Publish Event | 481 ns | 157 ns | 424 B | **288 B** |
| Batch 100 Commands | 25.6 μs | 13.9 μs | **8.8 KB** | 35.2 KB |

### Key Insights

| Metric | Catga | MediatR | Notes |
|--------|-------|---------|-------|
| **Single Op Latency** | ~340 ns | ~160 ns | MediatR faster for simple ops |
| **Batch Memory** | **8.8 KB** | 35.2 KB | **4x less memory** in batch |
| **Per-Op Memory** | **32-88 B** | 288-424 B | **3-11x less allocation** |

> **Note**: MediatR is faster for simple in-memory dispatch. Catga's value is in **distributed scenarios** with Redis/NATS transport, Event Sourcing, Outbox/Inbox patterns, and Native AOT support.

### Business Scenario Benchmarks

| Scenario | Latency | Memory | Throughput |
|----------|---------|--------|------------|
| Create Order (Command) | 487 ns | 104 B | 2.05M ops/s |
| Process Payment | 499 ns | 232 B | 2.00M ops/s |
| Get Order (Query) | 486 ns | 80 B | 2.06M ops/s |
| Event (3 handlers) | 903 ns | 1,024 B | 1.11M ops/s |
| Complete Order Flow | 1.39 μs | 1,128 B | 720K ops/s |
| E-Commerce Scenario | 1.48 μs | 416 B | 676K ops/s |
| Batch (10 flows) | 14.7 μs | 4,160 B | 68K ops/s |
| High-Throughput (20 orders) | 9.77 μs | 5,440 B | 102K ops/s |

### Concurrency Benchmarks

| Scenario | Latency | Memory |
|----------|---------|--------|
| 10 Concurrent Commands | 4.88 μs | 1.19 KB |
| 100 Concurrent Commands | 48.8 μs | 11.9 KB |
| 200 Concurrent Commands | 97.8 μs | 23.8 KB |
| 100 Concurrent Events | 50.3 μs | 45.4 KB |

---

## ✨ Features

| Feature | Description |
|---------|-------------|
| **Low Memory** | 32-88 B/op single, 8.8 KB/100 batch (4x less than MediatR) |
| **Native AOT** | Full support, zero reflection, trimming safe |
| **Source Generator** | Compile-time handler registration, zero runtime overhead |
| **Distributed** | Lock, Rate Limiting, Leader Election, Event Sourcing |
| **Multi-Transport** | Redis Streams, NATS JetStream, In-Memory |
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

---

## 🎯 When to Use Catga

| Aspect | Catga | MediatR |
|--------|-------|---------|
| **Memory Efficiency** | **32-88 B/op** | 288-424 B/op |
| **Batch Memory** | **8.8 KB/100 ops** | 35.2 KB/100 ops |
| **Native AOT** | ✅ Full support | ⚠️ Limited |
| **Reflection** | Zero (source generated) | Uses reflection |
| **Distributed** | ✅ Redis, NATS, Event Sourcing | ❌ In-memory only |
| **Reliability** | ✅ Outbox/Inbox, DLQ, Idempotency | ❌ Not included |

### Choose Catga When

- ✅ Need **distributed messaging** (Redis Streams, NATS JetStream)
- ✅ Building **event-sourced** systems
- ✅ Require **exactly-once delivery** (Outbox/Inbox pattern)
- ✅ **Native AOT** deployment (containers, serverless)
- ✅ **Memory-constrained** environments (4x less batch memory)
- ✅ Need **observability** (OpenTelemetry tracing, metrics)

### Choose MediatR When

- ✅ Simple **in-memory** mediator pattern
- ✅ Need **fastest single-operation latency**
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
