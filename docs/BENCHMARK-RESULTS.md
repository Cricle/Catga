# Benchmark Results

> BenchmarkDotNet v0.14.0, Windows 10, AMD Ryzen 7 5800H, .NET 9.0.8

## Framework Comparison (Catga vs MediatR vs MassTransit)

### Command Performance

| Framework | Mean | Allocated | Ratio |
|-----------|------|-----------|-------|
| **Catga** | 154 ns | 88 B | 1.00x |
| MediatR | 122 ns | 352 B | 0.79x |
| MassTransit | 28,912 ns | 12,800 B | 187.87x |

### Event/Notification Performance

| Framework | Mean | Allocated | Ratio |
|-----------|------|-----------|-------|
| **Catga** | 111 ns | 64 B | 1.00x |
| MediatR | 137 ns | 288 B | 1.24x |

### Batch 100 Commands

| Framework | Mean | Allocated | Ratio |
|-----------|------|-----------|-------|
| **Catga** | 15.8 μs | 8,800 B | 1.00x |
| MediatR | 12.2 μs | 35,200 B | 0.77x |
| MassTransit | 2,399 μs | 1,255,459 B | 151.78x |

### Key Insights

- **Catga** has the lowest memory allocation (4x less than MediatR)
- **MediatR** is slightly faster for single commands but allocates 4x more memory
- **Catga** is faster for events/notifications
- **MassTransit** is designed for distributed messaging, not in-process mediator (hence higher overhead)

## Core CQRS Performance

| Operation | Mean | Allocated |
|-----------|------|-----------|
| Command | 256 ns | 88 B |
| Query | 230 ns | 32 B |
| Event (1 handler) | 146 ns | 32 B |
| Command x100 | 22.08 μs | 8,800 B |
| Event x100 | 20.16 μs | 3,200 B |

## Throughput Analysis

| Scenario | Latency | Throughput |
|----------|---------|------------|
| Single Command | 154 ns | **6.5M ops/sec** |
| Single Event | 111 ns | **9.0M ops/sec** |
| Batch 100 Commands | 15.8 μs | **6.3M ops/sec** |

## Memory Efficiency

| Framework | Command | Event | Batch 100 |
|-----------|---------|-------|-----------|
| **Catga** | 88 B | 64 B | 8,800 B |
| MediatR | 352 B | 288 B | 35,200 B |
| MassTransit | 12,800 B | - | 1,255,459 B |

## Run Benchmarks

```bash
# Framework comparison
dotnet run -c Release --project benchmarks/Catga.Benchmarks -- --filter *FrameworkComparison*

# Core CQRS
dotnet run -c Release --project benchmarks/Catga.Benchmarks -- --filter *Core*

# Transport (requires Docker)
dotnet run -c Release --project benchmarks/Catga.Benchmarks -- --filter *Transport*

# All benchmarks
dotnet run -c Release --project benchmarks/Catga.Benchmarks -- --filter *
```
