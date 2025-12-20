# Benchmark Results

> BenchmarkDotNet v0.14.0, Windows 10, AMD Ryzen 7 5800H, .NET 9.0.8

## Core CQRS Performance

| Operation | Mean | Error | StdDev | Gen0 | Allocated |
|-----------|------|-------|--------|------|-----------|
| Command | 256.3 ns | 82.72 ns | 4.53 ns | 0.0105 | 88 B |
| Query | 230.3 ns | 10.72 ns | 0.59 ns | 0.0038 | 32 B |
| Event (1 handler) | 146.4 ns | 38.96 ns | 2.14 ns | 0.0038 | 32 B |
| Command x100 | 22.08 μs | 5.48 μs | 300 ns | 1.0376 | 8,800 B |
| Event x100 | 20.16 μs | 10.28 μs | 564 ns | 0.3662 | 3,200 B |

## Business Scenario Benchmarks

| Scenario | Mean | Error | StdDev | Gen0 | Allocated |
|----------|------|-------|--------|------|-----------|
| Create Order | 351.3 ns | 64.70 ns | 3.55 ns | 0.0124 | 104 B |
| Process Payment | 449.2 ns | 90.49 ns | 4.96 ns | 0.0277 | 232 B |
| Get Order | 337.3 ns | 63.60 ns | 3.49 ns | 0.0095 | 80 B |
| Order Event (3 handlers) | 351.8 ns | 25.31 ns | 1.39 ns | 0.0248 | 208 B |
| Full Order Flow | 728.7 ns | 61.72 ns | 3.38 ns | 0.0372 | 312 B |
| E-Commerce Flow | 922.5 ns | 106.13 ns | 5.82 ns | 0.0496 | 416 B |

## Throughput Analysis

| Scenario | Latency | Throughput |
|----------|---------|------------|
| Single Command | 256 ns | **3.9M ops/sec** |
| Single Query | 230 ns | **4.3M ops/sec** |
| Single Event | 146 ns | **6.8M ops/sec** |
| Complete Flow | 729 ns | **1.37M ops/sec** |
| E-Commerce Scenario | 923 ns | **1.08M ops/sec** |

## Memory Efficiency

| Operation | Allocation |
|-----------|------------|
| Query | 32 B |
| Event | 32 B |
| Command | 88 B |
| Business Command | 104-232 B |
| Complete Flow | 312 B |

## Run Benchmarks

```bash
# Run all benchmarks
dotnet run -c Release --project benchmarks/Catga.Benchmarks -- --filter *

# Run specific suite
dotnet run -c Release --project benchmarks/Catga.Benchmarks -- --filter *Core*
dotnet run -c Release --project benchmarks/Catga.Benchmarks -- --filter *Business*
dotnet run -c Release --project benchmarks/Catga.Benchmarks -- --filter *Concurrency*
dotnet run -c Release --project benchmarks/Catga.Benchmarks -- --filter *EventSourcing*
dotnet run -c Release --project benchmarks/Catga.Benchmarks -- --filter *MediatR*
```
