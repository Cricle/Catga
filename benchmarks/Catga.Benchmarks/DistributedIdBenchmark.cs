using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Catga.DistributedId;

namespace Catga.Benchmarks;

/// <summary>
/// Benchmark for distributed ID generator
/// Demonstrates zero-allocation and lock-free performance
/// </summary>
[SimpleJob(RuntimeMoniker.Net90)]
[MemoryDiagnoser]
[ThreadingDiagnoser]
public class DistributedIdBenchmark
{
    private SnowflakeIdGenerator _generator = null!;
    private char[] _buffer = null!;

    [GlobalSetup]
    public void Setup()
    {
        _generator = new SnowflakeIdGenerator(1);
        _buffer = new char[20];
    }

    /// <summary>
    /// Baseline: Generate ID as long (0 allocations expected)
    /// </summary>
    [Benchmark(Baseline = true)]
    public long NextId()
    {
        return _generator.NextId();
    }

    /// <summary>
    /// Generate ID as string (allocates string)
    /// </summary>
    [Benchmark]
    public string NextIdString()
    {
        return _generator.NextIdString();
    }

    /// <summary>
    /// Zero-allocation string generation using TryWriteNextId
    /// </summary>
    [Benchmark]
    public bool TryWriteNextId()
    {
        return _generator.TryWriteNextId(_buffer, out _);
    }

    /// <summary>
    /// Parse ID metadata (allocating version)
    /// </summary>
    [Benchmark]
    public IdMetadata ParseId_Allocating()
    {
        var id = _generator.NextId();
        return _generator.ParseId(id);
    }

    /// <summary>
    /// Parse ID metadata (zero-allocation version)
    /// </summary>
    [Benchmark]
    public void ParseId_ZeroAlloc()
    {
        var id = _generator.NextId();
        _generator.ParseId(id, out _);
    }

    /// <summary>
    /// High concurrency test: 4 threads generating IDs simultaneously
    /// </summary>
    [Benchmark]
    [Arguments(4)]
    public long[] Concurrent_Generate(int threads)
    {
        var ids = new long[threads];
        Parallel.For(0, threads, i =>
        {
            ids[i] = _generator.NextId();
        });
        return ids;
    }
}

/// <summary>
/// Benchmark comparing different bit layouts
/// </summary>
[SimpleJob(RuntimeMoniker.Net90)]
[MemoryDiagnoser]
public class DistributedIdLayoutBenchmark
{
    private SnowflakeIdGenerator _defaultGenerator = null!;
    private SnowflakeIdGenerator _longLifespanGenerator = null!;
    private SnowflakeIdGenerator _highConcurrencyGenerator = null!;
    private SnowflakeIdGenerator _customEpochGenerator = null!;

    [GlobalSetup]
    public void Setup()
    {
        _defaultGenerator = new SnowflakeIdGenerator(1, SnowflakeBitLayout.Default);
        // Default layout now provides 500+ years, no need for separate LongLifespan
        _longLifespanGenerator = new SnowflakeIdGenerator(1, SnowflakeBitLayout.Default);
        _highConcurrencyGenerator = new SnowflakeIdGenerator(1, SnowflakeBitLayout.HighConcurrency);
        
        var customEpoch = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        _customEpochGenerator = new SnowflakeIdGenerator(1, SnowflakeBitLayout.WithEpoch(customEpoch));
    }

    [Benchmark(Baseline = true)]
    public long Default_Layout()
    {
        return _defaultGenerator.NextId();
    }

    [Benchmark]
    public long LongLifespan_Layout()
    {
        return _longLifespanGenerator.NextId();
    }

    [Benchmark]
    public long HighConcurrency_Layout()
    {
        return _highConcurrencyGenerator.NextId();
    }

    [Benchmark]
    public long CustomEpoch_Layout() => _customEpochGenerator.NextId();
}
