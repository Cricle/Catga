using BenchmarkDotNet.Attributes;
using Catga.Core;

namespace Catga.Benchmarks;

/// <summary>Benchmark to verify reflection optimization improvements</summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 5)]
public class ReflectionOptimizationBenchmark
{
    private const int Iterations = 10000;

    #region Type Name Access Benchmarks

    [Benchmark(Baseline = true, Description = "typeof().Name (reflection)")]
    public string TypeName_Reflection()
    {
        string? result = null;
        for (int i = 0; i < Iterations; i++)
            result = typeof(TestMessage).Name;
        return result!;
    }

    [Benchmark(Description = "TypeNameCache<T>.Name (cached)")]
    public string TypeName_Cached()
    {
        string? result = null;
        for (int i = 0; i < Iterations; i++)
            result = TypeNameCache<TestMessage>.Name;
        return result!;
    }

    [Benchmark(Description = "typeof().FullName (reflection)")]
    public string TypeFullName_Reflection()
    {
        string? result = null;
        for (int i = 0; i < Iterations; i++)
            result = typeof(TestMessage).FullName ?? typeof(TestMessage).Name;
        return result!;
    }

    [Benchmark(Description = "TypeNameCache<T>.FullName (cached)")]
    public string TypeFullName_Cached()
    {
        string? result = null;
        for (int i = 0; i < Iterations; i++)
            result = TypeNameCache<TestMessage>.FullName;
        return result!;
    }

    #endregion

    #region Type Comparison Benchmarks

    private static readonly Type TestType = typeof(TestMessage);
    private static readonly Dictionary<Type, string> TypeDict = new() { [typeof(TestMessage)] = "Test" };
    private static readonly Dictionary<string, string> NameDict = new() { [TypeNameCache<TestMessage>.FullName] = "Test" };

    [Benchmark(Description = "Type comparison + dict lookup")]
    public string TypeComparison_Dictionary()
    {
        string? result = null;
        for (int i = 0; i < Iterations; i++)
        {
            if (TypeDict.TryGetValue(TestType, out var value))
                result = value;
        }
        return result!;
    }

    [Benchmark(Description = "Static generic (no comparison)")]
    public string TypeComparison_StaticGeneric()
    {
        string? result = null;
        for (int i = 0; i < Iterations; i++)
            result = StaticCache<TestMessage>.Value;
        return result!;
    }

    [Benchmark(Description = "String key dict lookup")]
    public string TypeComparison_StringKey()
    {
        string? result = null;
        for (int i = 0; i < Iterations; i++)
        {
            if (NameDict.TryGetValue(TypeNameCache<TestMessage>.FullName, out var value))
                result = value;
        }
        return result!;
    }

    #endregion

    #region Helper Classes

    private class TestMessage
    {
        public string Id { get; set; } = "test";
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    private static class StaticCache<T>
    {
        public static readonly string Value = "Test";
    }

    #endregion
}

/// <summary>Benchmark comparing reflection vs AOT-friendly approaches</summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 5)]
public class AotCompatibilityBenchmark
{
    private readonly TestCommand _command = new() { Id = "test-123", Data = "Sample data for benchmarking" };

    [Benchmark(Baseline = true, Description = "Reflection-based serialization")]
    public string ReflectionSerialization()
    {
        // Simulates reflection-based approach
        var type = _command.GetType();
        var props = type.GetProperties();
        return $"{type.Name}:{props.Length}";
    }

    [Benchmark(Description = "AOT-friendly (static cache)")]
    public string AotFriendlySerialization()
    {
        // Simulates AOT-friendly approach with cached metadata
        return $"{TypeNameCache<TestCommand>.Name}:2";
    }

    private class TestCommand
    {
        public string Id { get; set; } = string.Empty;
        public string Data { get; set; } = string.Empty;
    }
}

/// <summary>Real-world scenario: Message routing performance</summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 5)]
public class MessageRoutingBenchmark
{
    private const int MessageCount = 1000;
    private readonly List<object> _messages = new();

    public MessageRoutingBenchmark()
    {
        for (int i = 0; i < MessageCount; i++)
            _messages.Add(new CreateOrderCommand { OrderId = $"order-{i}" });
    }

    [Benchmark(Baseline = true, Description = "Reflection: typeof().Name per message")]
    public int Routing_Reflection()
    {
        int count = 0;
        foreach (var msg in _messages)
        {
            var typeName = msg.GetType().Name;
            if (typeName == "CreateOrderCommand")
                count++;
        }
        return count;
    }

    [Benchmark(Description = "Optimized: Cached type name")]
    public int Routing_Cached()
    {
        int count = 0;
        foreach (var msg in _messages)
        {
            if (msg is CreateOrderCommand)
            {
                var typeName = TypeNameCache<CreateOrderCommand>.Name;
                if (typeName == "CreateOrderCommand")
                    count++;
            }
        }
        return count;
    }

    [Benchmark(Description = "Best: Pattern matching only")]
    public int Routing_PatternMatching()
    {
        int count = 0;
        foreach (var msg in _messages)
        {
            if (msg is CreateOrderCommand)
                count++;
        }
        return count;
    }

    private class CreateOrderCommand
    {
        public string OrderId { get; set; } = string.Empty;
    }
}

