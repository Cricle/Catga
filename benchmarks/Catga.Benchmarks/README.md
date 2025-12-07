# Catga Benchmarks

Performance benchmarks for Catga framework with MediatR comparison.

## Quick Start

```bash
# Run all benchmarks
dotnet run -c Release

# Run specific benchmark
dotnet run -c Release --filter *MediatRComparison*
dotnet run -c Release --filter *CqrsPerformance*
dotnet run -c Release --filter *BusinessScenario*
dotnet run -c Release --filter *ConcurrencyPerformance*
```

## Results

**Environment**: AMD Ryzen 7 5800H, .NET 9.0.8, Windows 10

---

## ⭐ Catga vs MediatR (Fair Comparison)

Both frameworks tested with in-memory mediator pattern, same handlers.

| Operation | Catga | MediatR | Catga Memory | MediatR Memory |
|-----------|-------|---------|--------------|----------------|
| Send Command | 342 ns | 164 ns | **88 B** | 424 B |
| Send Query | 313 ns | 153 ns | **32 B** | 368 B |
| Publish Event | 481 ns | 157 ns | 424 B | 288 B |
| Batch 100 Commands | 25.6 μs | 13.9 μs | **8.8 KB** | 35.2 KB |

### Key Insights

| Metric | Catga | MediatR | Winner |
|--------|-------|---------|--------|
| **Single Op Latency** | ~340 ns | ~160 ns | MediatR |
| **Batch Memory** | **8.8 KB** | 35.2 KB | **Catga (4x less)** |
| **Per-Op Memory** | **32-88 B** | 288-424 B | **Catga (3-11x less)** |

> **Conclusion**: MediatR is faster for simple in-memory dispatch. Catga excels in **memory efficiency** and **distributed scenarios** (Redis/NATS transport, Event Sourcing, Outbox/Inbox).

---

## CqrsPerformanceBenchmarks (~35s)

| Method | Mean | Allocated |
|--------|------|-----------|
| Send Command (single) | 265 ns | 32 B |
| Send Query (single) | 274 ns | 32 B |
| Publish Event (single) | 313 ns | 424 B |
| Send Command (batch 100) | 23.7 μs | 3,200 B |
| Publish Event (batch 100) | 39.5 μs | 42.4 KB |

---

## BusinessScenarioBenchmarks (~70s)

| Method | Mean | Allocated |
|--------|------|-----------|
| Create Order (Command) | 487 ns | 104 B |
| Process Payment (Command) | 499 ns | 232 B |
| Get Order (Query) | 486 ns | 80 B |
| Order Created Event (3 handlers) | 903 ns | 1,024 B |
| Complete Order Flow | 1.39 μs | 1,128 B |
| E-Commerce Scenario | 1.48 μs | 416 B |
| E-Commerce Batch (10 flows) | 14.7 μs | 4,160 B |
| High-Throughput Batch (20 Orders) | 9.77 μs | 5,440 B |

---

## ConcurrencyPerformanceBenchmarks (~30s)

| Method | Mean | Allocated |
|--------|------|-----------|
| Concurrent Commands (10) | 4.88 μs | 1.19 KB |
| Concurrent Commands (100) | 48.8 μs | 11.9 KB |
| Concurrent Commands (200) | 97.8 μs | 23.8 KB |
| Concurrent Events (100) | 50.3 μs | 45.4 KB |

---

## Reproduce

```bash
# MediatR comparison (recommended first)
dotnet run -c Release --filter *MediatRComparison*

# Full benchmark suite
dotnet run -c Release

# Quick validation
dotnet run -c Release --filter *CqrsPerformance* --job short
```

## Output

Results saved to `BenchmarkDotNet.Artifacts/results/`
