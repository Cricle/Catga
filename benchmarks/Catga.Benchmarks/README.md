# Catga Benchmarks

Performance benchmarks for the Catga framework.

## Quick Start

```bash
# Run all benchmarks
dotnet run -c Release -- --filter *

# Run specific benchmark suite
dotnet run -c Release -- --filter *Core*
dotnet run -c Release -- --filter *Business*
dotnet run -c Release -- --filter *Concurrency*
dotnet run -c Release -- --filter *EventSourcing*
dotnet run -c Release -- --filter *MediatR*
```

## Benchmark Suites

| Suite | Description |
|-------|-------------|
| `CoreBenchmarks` | Core CQRS operations (Command/Query/Event) |
| `BusinessBenchmarks` | Real-world e-commerce scenarios |
| `ConcurrencyBenchmarks` | High-concurrency stress tests |
| `EventSourcingBenchmarks` | Event store and time travel operations |
| `MediatRComparisonBenchmarks` | Fair comparison with MediatR |

## Results

Results are saved to `BenchmarkDotNet.Artifacts/` directory.

See [BENCHMARK-RESULTS.md](../../docs/BENCHMARK-RESULTS.md) for latest results.
