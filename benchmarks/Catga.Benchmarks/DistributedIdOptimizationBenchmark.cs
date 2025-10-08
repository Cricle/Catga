using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Catga.DistributedId;

namespace Catga.Benchmarks;

/// <summary>
/// Comprehensive benchmark for DistributedId optimizations
/// Tests P1-3 (batch optimization), P2-3 (TryNextId), P3-3 (cache line padding)
/// </summary>
[SimpleJob(RuntimeMoniker.Net90)]
[MemoryDiagnoser]
[ThreadingDiagnoser]
public class DistributedIdOptimizationBenchmark
{
    private SnowflakeIdGenerator _generator = null!;
    private long[] _buffer1000 = null!;
    private long[] _buffer10000 = null!;
    private long[] _buffer50000 = null!;

    [GlobalSetup]
    public void Setup()
    {
        _generator = new SnowflakeIdGenerator(1);
        _buffer1000 = new long[1000];
        _buffer10000 = new long[10000];
        _buffer50000 = new long[50000];
    }

    /// <summary>
    /// Baseline: Single ID generation
    /// </summary>
    [Benchmark(Baseline = true)]
    public long NextId_Single()
    {
        return _generator.NextId();
    }

    /// <summary>
    /// P2-3: TryNextId (exception-free path)
    /// </summary>
    [Benchmark]
    public bool TryNextId_Single()
    {
        return _generator.TryNextId(out _);
    }

    /// <summary>
    /// P1-3: Batch generation - small batch (normal batching)
    /// </summary>
    [Benchmark]
    public int NextIds_Batch_1000()
    {
        return _generator.NextIds(_buffer1000);
    }

    /// <summary>
    /// P1-3: Batch generation - medium batch (adaptive starts here at >10k)
    /// </summary>
    [Benchmark]
    public int NextIds_Batch_10000()
    {
        return _generator.NextIds(_buffer10000);
    }

    /// <summary>
    /// P1-3: Batch generation - large batch (full adaptive optimization)
    /// </summary>
    [Benchmark]
    public int NextIds_Batch_50000()
    {
        return _generator.NextIds(_buffer50000);
    }

    /// <summary>
    /// P3-3: High concurrency test with cache line padding
    /// Tests false sharing prevention
    /// </summary>
    [Benchmark]
    [Arguments(8)]
    public long[] Concurrent_HighContention(int threads)
    {
        var ids = new long[threads * 100];
        Parallel.For(0, threads, threadId =>
        {
            for (int i = 0; i < 100; i++)
            {
                ids[threadId * 100 + i] = _generator.NextId();
            }
        });
        return ids;
    }

    /// <summary>
    /// Throughput test: How many IDs per second?
    /// </summary>
    [Benchmark]
    public long Throughput_1000_Sequential()
    {
        long count = 0;
        for (int i = 0; i < 1000; i++)
        {
            _generator.NextId();
            count++;
        }
        return count;
    }

    /// <summary>
    /// Compare individual NextId() calls vs batch NextIds()
    /// </summary>
    [Benchmark]
    [Arguments(1000)]
    public long[] Individual_vs_Batch_Individual(int count)
    {
        var ids = new long[count];
        for (int i = 0; i < count; i++)
        {
            ids[i] = _generator.NextId();
        }
        return ids;
    }

    [Benchmark]
    [Arguments(1000)]
    public int Individual_vs_Batch_Batched(int count)
    {
        return _generator.NextIds(_buffer1000.AsSpan(0, count));
    }
}

/// <summary>
/// Benchmark comparing batch sizes to find optimal adaptive threshold
/// </summary>
[SimpleJob(RuntimeMoniker.Net90)]
[MemoryDiagnoser]
public class BatchSizeOptimizationBenchmark
{
    private SnowflakeIdGenerator _generator = null!;

    [Params(100, 500, 1000, 5000, 10000, 20000, 50000)]
    public int BatchSize { get; set; }

    private long[] _buffer = null!;

    [GlobalSetup]
    public void Setup()
    {
        _generator = new SnowflakeIdGenerator(1);
        _buffer = new long[BatchSize];
    }

    [Benchmark]
    public int NextIds_Batch()
    {
        return _generator.NextIds(_buffer);
    }
}

