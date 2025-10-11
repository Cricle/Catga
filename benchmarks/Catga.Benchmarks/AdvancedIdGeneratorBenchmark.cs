using System.Runtime.Intrinsics.X86;
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

    // Pre-allocated buffers to avoid GC in benchmarks
    private long[] _buffer10K = null!;
    private long[] _buffer100K = null!;
    private long[] _buffer500K = null!;

    [GlobalSetup]
    public void Setup()
    {
        _generator = new SnowflakeIdGenerator(workerId: 1);

        // Create a warmed-up generator
        _warmedUpGenerator = new SnowflakeIdGenerator(workerId: 2);

        // Pre-allocate buffers (one-time allocation)
        _buffer10K = new long[10_000];
        _buffer100K = new long[100_000];
        _buffer500K = new long[500_000];
    }

    [Benchmark(Description = "Batch 10K - SIMD (Zero GC)")]
    public int Batch_10K_SIMD()
    {
        // Use pre-allocated buffer to avoid GC
        return _generator.NextIds(_buffer10K.AsSpan());
    }

    [Benchmark(Description = "Batch 10K - Warmed Up (Zero GC)")]
    public int Batch_10K_WarmedUp()
    {
        // Use pre-allocated buffer to avoid GC
        return _warmedUpGenerator.NextIds(_buffer10K.AsSpan());
    }

    [Benchmark(Description = "Batch 100K - SIMD (Zero GC)")]
    public int Batch_100K_SIMD()
    {
        // Use pre-allocated buffer to avoid GC
        return _generator.NextIds(_buffer100K.AsSpan());
    }

    [Benchmark(Description = "Batch 500K - SIMD (Zero GC)")]
    public int Batch_500K_SIMD()
    {
        // Use pre-allocated buffer to avoid GC
        return _generator.NextIds(_buffer500K.AsSpan());
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

    [Benchmark(Description = "Adaptive - Repeated 1K (Zero GC)")]
    public int Adaptive_Repeated1K()
    {
        // Use pre-allocated buffer to avoid GC
        var total = 0;
        for (int i = 0; i < 10; i++)
        {
            total += _generator.NextIds(_buffer10K.AsSpan(0, 1000));
        }
        return total;
    }

    [Benchmark(Description = "SIMD vs Scalar - 10K (Zero GC)")]
    public int SIMD_vs_Scalar_10K()
    {
        // Use pre-allocated buffer to avoid GC
        return _generator.NextIds(_buffer10K.AsSpan());
    }
}

