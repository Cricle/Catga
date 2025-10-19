using BenchmarkDotNet.Attributes;
using Catga.Pooling;
using System.Buffers;

namespace Catga.Benchmarks;

/// <summary>
/// 内存池性能测试
/// 测试 MemoryPoolManager 和 PooledBufferWriter 的性能
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class MemoryPoolBenchmarks
{
    private MemoryPoolManager? _poolManager;
    private byte[]? _testData;

    [GlobalSetup]
    public void Setup()
    {
        _poolManager = new MemoryPoolManager();
        _testData = new byte[1024];
        Random.Shared.NextBytes(_testData);
    }

    #region MemoryPoolManager Benchmarks

    [Benchmark(Description = "MemoryPool: Rent Small (256B)")]
    public void MemoryPool_RentSmall()
    {
        using var buffer = _poolManager!.RentMemory(256);
    }

    [Benchmark(Description = "MemoryPool: Rent Medium (4KB)")]
    public void MemoryPool_RentMedium()
    {
        using var buffer = _poolManager!.RentMemory(4 * 1024);
    }

    [Benchmark(Description = "MemoryPool: Rent Large (64KB)")]
    public void MemoryPool_RentLarge()
    {
        using var buffer = _poolManager!.RentMemory(64 * 1024);
    }

    [Benchmark(Description = "MemoryPool: Rent & Write Small")]
    public void MemoryPool_RentAndWriteSmall()
    {
        using var buffer = _poolManager!.RentMemory(256);
        _testData.AsSpan(0, 256).CopyTo(buffer.Memory.Span);
    }

    [Benchmark(Description = "MemoryPool: Rent & Write Medium")]
    public void MemoryPool_RentAndWriteMedium()
    {
        using var buffer = _poolManager!.RentMemory(_testData!.Length);
        _testData.CopyTo(buffer.Memory.Span);
    }

    [Benchmark(Baseline = true, Description = "Baseline: ArrayPool Rent")]
    public void Baseline_ArrayPool()
    {
        var array = ArrayPool<byte>.Shared.Rent(1024);
        try
        {
            _testData!.CopyTo(array, 0);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(array);
        }
    }

    [Benchmark(Description = "Baseline: New Byte Array")]
    public void Baseline_NewArray()
    {
        var array = new byte[1024];
        _testData!.CopyTo(array, 0);
    }

    #endregion

    #region PooledBufferWriter Benchmarks

    [Benchmark(Description = "PooledWriter: Write 1KB")]
    public void PooledWriter_Write1KB()
    {
        using var writer = new PooledBufferWriter<byte>(1024);
        
        for (int i = 0; i < 1024; i++)
        {
            var span = writer.GetSpan(1);
            span[0] = (byte)i;
            writer.Advance(1);
        }
    }

    [Benchmark(Description = "PooledWriter: Write 10KB")]
    public void PooledWriter_Write10KB()
    {
        using var writer = new PooledBufferWriter<byte>(10 * 1024);
        
        for (int i = 0; i < 10 * 1024; i++)
        {
            var span = writer.GetSpan(1);
            span[0] = (byte)(i % 256);
            writer.Advance(1);
        }
    }

    [Benchmark(Description = "PooledWriter: Write Span 1KB")]
    public void PooledWriter_WriteSpan1KB()
    {
        using var writer = new PooledBufferWriter<byte>(1024);
        var span = writer.GetSpan(_testData!.Length);
        _testData.AsSpan().CopyTo(span);
        writer.Advance(_testData.Length);
    }

    [Benchmark(Description = "PooledWriter: GetSpan & Advance")]
    public void PooledWriter_GetSpanAdvance()
    {
        using var writer = new PooledBufferWriter<byte>(1024);
        
        var span = writer.GetSpan(1024);
        _testData!.AsSpan().CopyTo(span);
        writer.Advance(1024);
    }

    [Benchmark(Description = "Baseline: MemoryStream Write")]
    public void Baseline_MemoryStream()
    {
        using var stream = new MemoryStream(1024);
        stream.Write(_testData!, 0, _testData.Length);
    }

    #endregion

    #region Memory Copy Benchmarks

    [Benchmark(Description = "Span.CopyTo (1KB)")]
    public void SpanCopy_1KB()
    {
        Span<byte> destination = stackalloc byte[1024];
        _testData!.AsSpan().CopyTo(destination);
    }

    [Benchmark(Description = "Array.Copy (1KB)")]
    public void ArrayCopy_1KB()
    {
        var destination = new byte[1024];
        Array.Copy(_testData!, destination, 1024);
    }

    [Benchmark(Description = "Buffer.BlockCopy (1KB)")]
    public void BufferBlockCopy_1KB()
    {
        var destination = new byte[1024];
        Buffer.BlockCopy(_testData!, 0, destination, 0, 1024);
    }

    #endregion

    #region Pooled Buffer Lifecycle

    [Benchmark(Description = "PooledBuffer: Full Lifecycle")]
    public void PooledBuffer_FullLifecycle()
    {
        // Rent
        using var buffer = _poolManager!.RentMemory(1024);
        
        // Write
        _testData!.AsSpan().CopyTo(buffer.Memory.Span);
        
        // Read
        var result = buffer.Memory.Span[0];
        
        // Dispose happens automatically
    }

    [Benchmark(Description = "PooledBuffer: Concurrent Rent/Return")]
    public async Task PooledBuffer_Concurrent()
    {
        var tasks = Enumerable.Range(0, 10).Select(_ => Task.Run(() =>
        {
            using var buffer = _poolManager!.RentMemory(1024);
            _testData!.AsSpan().CopyTo(buffer.Memory.Span);
        }));

        await Task.WhenAll(tasks);
    }

    #endregion
}

