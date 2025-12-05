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

## Benchmarks

| Benchmark | Description | Est. Time |
|-----------|-------------|-----------|
| `CqrsPerformanceBenchmarks` | Core CQRS operations | ~2 min |
| `BusinessScenarioBenchmarks` | E-commerce scenarios | ~3 min |
| `ConcurrencyPerformanceBenchmarks` | Concurrency tests | ~2 min |

## Output

Results saved to `BenchmarkDotNet.Artifacts/results/`
