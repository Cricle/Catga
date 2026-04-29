# Benchmark Results

> Framework comparison section below reflects the latest run on 2026-04-29 with BenchmarkDotNet v0.14.0, Debian 12, Intel Xeon Platinum 8457C, .NET SDK 10.0.201, .NET Runtime 10.0.5.

## Framework Comparison (Catga vs MediatR vs MassTransit)

### Command Performance

| Framework | Mean | Allocated | Ratio |
|-----------|------|-----------|-------|
| **Catga** | 149.72 ns | 88 B | 1.00x |
| MediatR | 96.93 ns | 288 B | 0.65x |
| MassTransit | 33,382.25 ns | 12,470 B | 223.01x |

### Event/Notification Performance

| Framework | Mean | Allocated | Ratio |
|-----------|------|-----------|-------|
| **Catga** | 87.23 ns | 64 B | 1.00x |
| MediatR | 111.54 ns | 288 B | 1.28x |

### Batch 100 Commands

| Framework | Mean | Allocated | Ratio |
|-----------|------|-----------|-------|
| **Catga** | 13.24 μs | 8,800 B | 1.00x |
| MediatR | 9.67 μs | 28,800 B | 0.73x |
| MassTransit | 1,250.85 μs | 1,224,240 B | 94.53x |

### Key Insights

- **Catga** remains the lowest-allocation option in all measured framework-comparison scenarios
- **MediatR** is faster for the single in-process command path and for the batch-100 in-process path, but with materially higher allocation than Catga
- **Catga** is faster than MediatR for event publish and keeps allocation much lower
- **MassTransit** is still substantially heavier in this mediator/request-reply benchmark and should not be read as a broker round-trip benchmark

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
| Single Command | 149.72 ns | **6.7M ops/sec** |
| Single Event | 87.23 ns | **11.5M ops/sec** |
| Batch 100 Commands | 13.24 μs | **7.6M ops/sec** |

## Memory Efficiency

| Framework | Command | Event | Batch 100 |
|-----------|---------|-------|-----------|
| **Catga** | 88 B | 64 B | 8,800 B |
| MediatR | 288 B | 288 B | 28,800 B |
| MassTransit | 12,470 B | - | 1,224,240 B |

## Run Benchmarks

```bash
# Framework comparison
dotnet run -c Release --framework net10.0 --project benchmarks/Catga.Benchmarks -- --filter *FrameworkComparison*

# Core CQRS
dotnet run -c Release --project benchmarks/Catga.Benchmarks -- --filter *Core*

# Transport (requires Docker)
dotnet run -c Release --project benchmarks/Catga.Benchmarks -- --filter *Transport*

# All benchmarks
dotnet run -c Release --project benchmarks/Catga.Benchmarks -- --filter *
```
