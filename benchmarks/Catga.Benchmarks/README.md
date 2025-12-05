# Catga Benchmarks

Performance benchmarks for Catga framework.

## Quick Start

```bash
# Run all benchmarks
dotnet run -c Release --project benchmarks/Catga.Benchmarks

# Run specific benchmark
dotnet run -c Release --filter *CqrsPerformance*
dotnet run -c Release --filter *BusinessScenario*
dotnet run -c Release --filter *ConcurrencyPerformance*
```

## Results

**Environment**: AMD Ryzen 7 5800H, .NET 9.0.8, Windows 10

### CqrsPerformanceBenchmarks (~35s)

| Method | Mean | Allocated |
|--------|------|-----------|
| Send Command (single) | 265 ns | 240 B |
| Send Query (single) | 274 ns | 240 B |
| Publish Event (single) | 429 ns | 424 B |
| Send Command (batch 100) | 22.4 μs | 13.6 KB |
| Publish Event (batch 100) | 45.6 μs | 42.4 KB |

### BusinessScenarioBenchmarks (~70s)

| Method | Mean | Allocated |
|--------|------|-----------|
| Create Order | 488 ns | 312 B |
| Process Payment | 497 ns | 440 B |
| Get Order (Query) | 488 ns | 288 B |
| Order Created Event (3 handlers) | 950 ns | 1024 B |
| Complete Order Flow | 1.37 μs | 1232 B |
| E-Commerce Scenario | 1.47 μs | 728 B |
| E-Commerce Batch (10 flows) | 14.7 μs | 7.3 KB |
| High-Throughput Batch (20) | 9.77 μs | 7.5 KB |

### ConcurrencyPerformanceBenchmarks (~30s)

| Method | Mean | Allocated |
|--------|------|-----------|
| Concurrent Commands (10) | 4.88 μs | 2.21 KB |
| Concurrent Commands (100) | 48.8 μs | 21.9 KB |
| Concurrent Commands (200) | 97.8 μs | 43.8 KB |
| Concurrent Events (100) | 55.3 μs | 45.4 KB |

## Output

Results saved to `BenchmarkDotNet.Artifacts/results/`
