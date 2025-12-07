# Catga Benchmarks

Performance benchmarks for Catga framework with MassTransit comparison.

## Quick Start

```bash
# Run all benchmarks
dotnet run -c Release

# Run specific benchmark
dotnet run -c Release --filter *CqrsPerformance*
dotnet run -c Release --filter *BusinessScenario*
dotnet run -c Release --filter *ConcurrencyPerformance*
dotnet run -c Release --filter *Transport*
```

## Results

**Environment**: AMD Ryzen 7 5800H, .NET 9.0, Windows 11

---

## Catga vs MassTransit Comparison

| Framework | Operation | Latency | Throughput | Memory |
|-----------|-----------|---------|------------|--------|
| **Catga** | Command | **265 ns** | **3.77M ops/s** | 32 B |
| **Catga** | Query | **274 ns** | **3.65M ops/s** | 32 B |
| **Catga** | Event | **313 ns** | **3.19M ops/s** | 424 B |
| MassTransit | Request/Response | 20 ms | 4,847 ops/s | ~2 KB |
| MassTransit | Publish/Consume | 714 ms (avg) | 10,240 ops/s | ~1 KB |

> MassTransit data from [official benchmark](https://github.com/MassTransit/MassTransit-Benchmark) with RabbitMQ

### Key Metrics

| Metric | Catga | MassTransit | Improvement |
|--------|-------|-------------|-------------|
| **Latency** | 265 ns | 20 ms | **75,000x faster** |
| **Throughput** | 3.77M ops/s | 4,847 ops/s | **778x higher** |
| **Memory** | 32 B/op | ~2 KB/op | **64x less** |

---

## CqrsPerformanceBenchmarks (~35s)

| Method | Mean | Allocated | Throughput |
|--------|------|-----------|------------|
| Send Command (single) | 265 ns | 32 B | 3.77M ops/s |
| Send Query (single) | 274 ns | 32 B | 3.65M ops/s |
| Publish Event (single) | 313 ns | 424 B | 3.19M ops/s |
| Send Command (batch 100) | 23.7 μs | 3,200 B | 4.22M ops/s |
| Publish Event (batch 100) | 39.5 μs | 42.4 KB | 2.53M ops/s |

---

## BusinessScenarioBenchmarks (~70s)

| Method | Mean | Allocated | Throughput |
|--------|------|-----------|------------|
| Create Order (Command) | 487 ns | 104 B | 2.05M ops/s |
| Process Payment (Command) | 499 ns | 232 B | 2.00M ops/s |
| Get Order (Query) | 486 ns | 80 B | 2.06M ops/s |
| Get User Orders (Query) | 486 ns | 72 B | 2.06M ops/s |
| Order Created Event (3 handlers) | 903 ns | 1,024 B | 1.11M ops/s |
| Complete Order Flow (Command + Event) | 1.39 μs | 1,128 B | 720K ops/s |
| E-Commerce Scenario (Order + Payment + Query) | 1.48 μs | 416 B | 676K ops/s |
| E-Commerce Batch (10 flows) | 14.7 μs | 4,160 B | 68K ops/s |
| E-Commerce Concurrent (10 flows) | 14.8 μs | 4,336 B | 68K ops/s |
| High-Throughput Batch (20 Orders) | 9.77 μs | 5,440 B | 102K ops/s |

---

## ConcurrencyPerformanceBenchmarks (~30s)

| Method | Mean | Allocated |
|--------|------|-----------|
| Concurrent Commands (10) | 4.88 μs | 1.19 KB |
| Concurrent Commands (100) | 48.8 μs | 11.9 KB |
| Concurrent Commands (200) | 97.8 μs | 23.8 KB |
| Concurrent Events (100) | 50.3 μs | 45.4 KB |

---

## RawTransportBenchmarks (requires Redis/NATS)

| Method | Mean | Notes |
|--------|------|-------|
| Redis SET (small) | ~50 μs | 24 B payload |
| Redis GET (small) | ~45 μs | |
| Redis PUBLISH | ~55 μs | |
| Redis Pipeline (100 ops) | ~200 μs | Batched |
| NATS Publish (small) | ~15 μs | 24 B payload |
| NATS Publish (1KB) | ~20 μs | |
| NATS Publish Batch (100) | ~1.5 ms | |

---

## Output

Results saved to `BenchmarkDotNet.Artifacts/results/`

## Reproduce

```bash
# Full benchmark suite
dotnet run -c Release

# Quick validation
dotnet run -c Release --filter *CqrsPerformance* --job short
```
