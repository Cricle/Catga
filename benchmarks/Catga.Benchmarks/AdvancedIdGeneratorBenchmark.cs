using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Catga.DistributedId;

namespace Catga.Benchmarks;

/// <summary>
/// Advanced benchmarks for Distributed ID Generator
/// Tests SIMD, Warmup, Adaptive Strategy, and ArrayPool optimizations
/// </summary>
[SimpleJob(RuntimeMoniker.Net90)]
[MemoryDiagnoser]
[ThreadingDiagnoser]
public class AdvancedIdGeneratorBenchmark
{
    private SnowflakeIdGenerator _generator = null!;
    private SnowflakeIdGenerator _warmedUpGenerator = null!;

    [GlobalSetup]
    public void Setup()
    {
        _generator = new SnowflakeIdGenerator(workerId: 1);
        
        // Create a warmed-up generator
        _warmedUpGenerator = new SnowflakeIdGenerator(workerId: 2);
        _warmedUpGenerator.Warmup();
    }

    [Benchmark(Description = "Batch 10K - SIMD")]
    public long[] Batch_10K_SIMD()
    {
        return _generator.NextIds(10_000);
    }

    [Benchmark(Description = "Batch 10K - Warmed Up")]
    public long[] Batch_10K_WarmedUp()
    {
        return _warmedUpGenerator.NextIds(10_000);
    }

    [Benchmark(Description = "Batch 100K - ArrayPool")]
    public long[] Batch_100K_ArrayPool()
    {
        // This will use ArrayPool internally
        return _generator.NextIds(100_000);
    }

    [Benchmark(Description = "Batch 500K - Large ArrayPool")]
    public long[] Batch_500K_LargeArrayPool()
    {
        // This will use ArrayPool internally
        return _generator.NextIds(500_000);
    }

    [Benchmark(Description = "Span 10K - Zero Allocation")]
    public int Span_10K_ZeroAlloc()
    {
        Span<long> buffer = stackalloc long[128];
        var total = 0;
        
        // Generate 10K IDs in chunks of 128
        for (int i = 0; i < 78; i++) // 78 * 128 ≈ 10K
        {
            total += _generator.NextIds(buffer);
        }
        
        return total;
    }

    [Benchmark(Description = "Adaptive - Repeated 1K Batches")]
    public void Adaptive_Repeated1K()
    {
        // This will train the adaptive strategy
        for (int i = 0; i < 10; i++)
        {
            _ = _generator.NextIds(1000);
        }
    }

    [Benchmark(Description = "SIMD vs Scalar - 10K")]
    public long[] SIMD_vs_Scalar_10K()
    {
        // Force SIMD path by requesting batch
        var ids = new long[10_000];
        _generator.NextIds(ids.AsSpan());
        return ids;
    }
}

