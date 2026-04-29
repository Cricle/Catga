# Benchmark Results

> Framework comparison section below reflects the latest run on 2026-04-29 with BenchmarkDotNet v0.14.0, Debian 12, Intel Xeon Platinum 8457C, .NET SDK 10.0.201, .NET Runtime 10.0.5.

## Framework Comparison (Catga vs MediatR vs MassTransit)

### Command Performance

| Framework | Mean | Allocated | Ratio |
|-----------|------|-----------|-------|
| **Catga** | 232.4 ns | 88 B | 1.00x |
| MediatR | 127.5 ns | 288 B | 0.55x |
| MassTransit | 97,693.8 ns | 12,478 B | 420.64x |

### Event/Notification Performance

| Framework | Mean | Allocated | Ratio |
|-----------|------|-----------|-------|
| **Catga** | 112.3 ns | 64 B | 1.00x |
| MediatR | 167.0 ns | 288 B | 1.49x |

### Batch 100 Commands

| Framework | Mean | Allocated | Ratio |
|-----------|------|-----------|-------|
| **Catga** | 18.64 μs | 8,800 B | 1.00x |
| MediatR | 13.35 μs | 28,800 B | 0.72x |
| MassTransit | 1,862.79 μs | 1,224,220 B | 99.97x |

### Key Insights

- **Catga** remains the lowest-allocation option in all measured framework-comparison scenarios
- **MediatR** is faster for the single in-process command path, but with ~3.3x higher allocation than Catga
- **Catga** is faster than MediatR for event publish and keeps allocation much lower
- **MassTransit** is substantially heavier in this mediator/request-reply benchmark and should not be read as a broker round-trip benchmark

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
| Single Command | 232.4 ns | **4.3M ops/sec** |
| Single Event | 112.3 ns | **8.9M ops/sec** |
| Batch 100 Commands | 18.64 μs | **5.4M ops/sec** |

## Memory Efficiency

| Framework | Command | Event | Batch 100 |
|-----------|---------|-------|-----------|
| **Catga** | 88 B | 64 B | 8,800 B |
| MediatR | 288 B | 288 B | 28,800 B |
| MassTransit | 12,478 B | - | 1,224,220 B |

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
