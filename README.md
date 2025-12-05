<div align="center">

<img src="docs/web/favicon.svg" width="120" height="120" alt="Catga Logo"/>

# Catga

**High-performance .NET CQRS/Event Sourcing Framework**

[![.NET 9](https://img.shields.io/badge/.NET-9.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Native AOT](https://img.shields.io/badge/Native-AOT-success?logo=dotnet)](https://learn.microsoft.com/dotnet/core/deploying/native-aot/)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![Build](https://github.com/Cricle/Catga/actions/workflows/coverage.yml/badge.svg)](https://github.com/Cricle/Catga/actions/workflows/coverage.yml)

**Nanosecond Latency · Million QPS · Zero Reflection · Source Generated · Production Ready**

[Quick Start](#-quick-start) · [Features](#-features) · [Performance](#-performance) · [Documentation](https://cricle.github.io/Catga/)

</div>

---

## ✨ Features

| Feature | Description |
|---------|-------------|
| **High Performance** | 462 ns/op, 2.2M+ QPS, 432 B allocation |
| **Native AOT** | Full support, zero reflection |
| **Source Generator** | Auto handler registration, compile-time validation |
| **Distributed** | Lock, Rate Limiting, Leader Election, Event Sourcing |
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
public record CreateOrderCommand(string ProductId, int Quantity) : IRequest<Order>;

// 2. Define handler (auto-registered)
public class CreateOrderHandler : IRequestHandler<CreateOrderCommand, Order>
{
    public Task<CatgaResult<Order>> HandleAsync(
        CreateOrderCommand request, CancellationToken ct = default)
    {
        var order = new Order { ProductId = request.ProductId, Quantity = request.Quantity };
        return Task.FromResult(CatgaResult<Order>.Success(order));
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
var result = await mediator.SendAsync(new CreateOrderCommand("PROD-001", 5));
```

---

## 📦 Packages

| Package | Description |
|---------|-------------|
| `Catga` | Core framework |
| `Catga.Transport.InMemory` | In-memory transport |
| `Catga.Transport.Redis` | Redis Pub/Sub transport |
| `Catga.Transport.Nats` | NATS transport |
| `Catga.Persistence.InMemory` | In-memory persistence |
| `Catga.Persistence.Redis` | Redis persistence |
| `Catga.Persistence.Nats` | NATS JetStream persistence |
| `Catga.Serialization.MemoryPack` | High-performance serialization |
| `Catga.AspNetCore` | ASP.NET Core integration |
| `Catga.Testing` | Testing utilities |

---

## 📊 Performance

### Micro-Benchmarks (BenchmarkDotNet, .NET 9.0)

| Operation | Latency | Allocation | Throughput |
|-----------|---------|------------|------------|
| Command | 462 ns | 432 B | 2.2M ops/s |
| Query | 446 ns | 368 B | 2.2M ops/s |
| Event | 438 ns | 432 B | 2.3M ops/s |
| Batch (100) | 45.1 μs | 32.8 KB | 2.2M ops/s |

### E2E Stress Tests (OrderSystem Example)

| Mode | Infrastructure | Sequential RPS | Parallel RPS | Order RPS | Avg Latency |
|------|----------------|----------------|--------------|-----------|-------------|
| **Single** | In-Memory | 862 req/s | 635 req/s | 147 req/s | 1.11 ms |
| **Cluster (3x)** | Redis + NATS | 551 req/s | 491 req/s | 127 req/s | 1.76 ms |

> Run `cd examples && .\cross-test.ps1` to reproduce benchmarks.

---

## 🎯 Examples

Complete e-commerce order system with distributed features:

```powershell
cd examples

# Single instance (simplest)
.\run-demo.ps1 -Mode Single

# Cluster mode (3 replicas + Redis + NATS)
.\run-demo.ps1 -Mode Cluster

# Run stress tests
.\test-demo.ps1 -StressTest

# Cross-mode benchmark
.\cross-test.ps1
```

See [examples/README.md](./examples/README.md) for detailed benchmarks and architecture.

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

### Reliability

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

## 📄 License

[MIT License](LICENSE)

---

<div align="center">

**⭐ Star this repo if you find it useful!**

</div>
