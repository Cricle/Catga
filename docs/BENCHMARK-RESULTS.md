# Benchmark Results

> BenchmarkDotNet v0.14.0, Windows 10, AMD Ryzen 7 5800H, .NET 9.0.8

## Business Scenario Benchmarks

| Scenario | Mean | Error | StdDev | Gen0 | Allocated |
|----------|------|-------|--------|------|-----------|
| Create Order (Command) | 351.3 ns | 64.70 ns | 3.55 ns | 0.0124 | 104 B |
| Process Payment (Command) | 449.2 ns | 90.49 ns | 4.96 ns | 0.0277 | 232 B |
| Get Order (Query) | 337.3 ns | 63.60 ns | 3.49 ns | 0.0095 | 80 B |
| Get User Orders (Query) | 325.3 ns | 42.69 ns | 2.34 ns | 0.0086 | 72 B |
| Order Created Event (3 handlers) | 351.8 ns | 25.31 ns | 1.39 ns | 0.0248 | 208 B |
| Complete Order Flow (Command + Event) | 728.7 ns | 61.72 ns | 3.38 ns | 0.0372 | 312 B |
| E-Commerce Scenario (Order + Payment + Query) | 922.5 ns | 106.13 ns | 5.82 ns | 0.0496 | 416 B |
| E-Commerce Scenario Batch (10 flows) | 10.18 μs | 1.99 μs | 109.30 ns | 0.4883 | 4,160 B |
| E-Commerce Scenario Concurrent (10 flows) | 9.25 μs | 769.38 ns | 42.17 ns | 0.5035 | 4,336 B |
| High-Throughput Batch (20 Orders) | 5.84 μs | 832.64 ns | 45.64 ns | 0.6485 | 5,440 B |

## Throughput Analysis

| Scenario | Latency | Throughput |
|----------|---------|------------|
| Single Command | 351 ns | **2.85M ops/sec** |
| Single Query | 337 ns | **2.97M ops/sec** |
| Event (3 handlers) | 352 ns | **2.84M ops/sec** |
| Complete Flow | 729 ns | **1.37M ops/sec** |
| E-Commerce Scenario | 923 ns | **1.08M ops/sec** |
| Batch 10 Flows | 10.2 μs | **98K flows/sec** |
| Concurrent 10 Flows | 9.3 μs | **108K flows/sec** |
| High-Throughput 20 Orders | 5.8 μs | **172K ops/sec** |

## Memory Efficiency

| Operation | Allocation |
|-----------|------------|
| Query | 72-80 B |
| Command | 104-232 B |
| Event (3 handlers) | 208 B |
| Complete Flow | 312 B |
| E-Commerce Scenario | 416 B |

## Run Benchmarks

```bash
cd benchmarks/Catga.Benchmarks
dotnet run -c Release --filter *BusinessScenario*
```
