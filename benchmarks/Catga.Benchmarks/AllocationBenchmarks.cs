using System.Buffers;
using BenchmarkDotNet.Attributes;
using Catga.Messages;
using Catga.Results;

namespace Catga.Benchmarks;

/// <summary>
/// 内存分配优化基准测试
/// </summary>
[MemoryDiagnoser]
[RankColumn]
public class AllocationBenchmarks
{
    private const int Iterations = 1000;

    [Benchmark(Baseline = true)]
    public void StringMessageId_Allocation()
    {
        for (int i = 0; i < Iterations; i++)
        {
            var id = Guid.NewGuid().ToString();
            _ = id.Length;
        }
    }

    [Benchmark]
    public void StructMessageId_Allocation()
    {
        for (int i = 0; i < Iterations; i++)
        {
            var id = MessageId.NewId();
            _ = id.GetHashCode();
        }
    }

    [Benchmark]
    public void ClassResult_Allocation()
    {
        for (int i = 0; i < Iterations; i++)
        {
            var result = CatgaResult<int>.Success(42);
            _ = result.Value;
        }
    }

    [Benchmark]
    public void TaskFromResult_Allocation()
    {
        for (int i = 0; i < Iterations; i++)
        {
            var task = Task.FromResult(42);
            _ = task.Result;
        }
    }

    [Benchmark]
    public void ValueTask_Allocation()
    {
        for (int i = 0; i < Iterations; i++)
        {
            var task = new ValueTask<int>(42);
            _ = task.Result;
        }
    }

    [Benchmark]
    public void ListWithCapacity_Allocation()
    {
        for (int i = 0; i < Iterations; i++)
        {
            var list = new List<int>(100);
            list.Add(1);
            _ = list.Count;
        }
    }

    [Benchmark]
    public void ListWithoutCapacity_Allocation()
    {
        for (int i = 0; i < Iterations; i++)
        {
            var list = new List<int>();
            list.Add(1);
            _ = list.Count;
        }
    }

    [Benchmark]
    public void ArrayPool_Usage()
    {
        for (int i = 0; i < Iterations; i++)
        {
            var array = ArrayPool<byte>.Shared.Rent(1024);
            array[0] = 1;
            ArrayPool<byte>.Shared.Return(array);
        }
    }

    [Benchmark]
    public void DirectArray_Allocation()
    {
        for (int i = 0; i < Iterations; i++)
        {
            var array = new byte[1024];
            array[0] = 1;
        }
    }

    [Benchmark]
    public void Dictionary_WithCapacity()
    {
        for (int i = 0; i < Iterations; i++)
        {
            var dict = new Dictionary<string, string>(4);
            dict["key"] = "value";
            _ = dict.Count;
        }
    }

    [Benchmark]
    public void Dictionary_WithoutCapacity()
    {
        for (int i = 0; i < Iterations; i++)
        {
            var dict = new Dictionary<string, string>();
            dict["key"] = "value";
            _ = dict.Count;
        }
    }
}

