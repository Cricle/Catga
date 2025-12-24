namespace Catga.Tests.Framework.Generators;

/// <summary>
/// 性能测试指标
/// </summary>
public record PerformanceMetrics(
    double ThroughputOpsPerSec,
    LatencyPercentiles Latency,
    ResourceUsage Resources,
    DateTime MeasuredAt);

/// <summary>
/// 延迟百分位数
/// </summary>
public record LatencyPercentiles(
    double P50Ms,
    double P95Ms,
    double P99Ms,
    double P999Ms);

/// <summary>
/// 资源使用情况
/// </summary>
public record ResourceUsage(
    long MemoryBytes,
    double CpuPercent,
    int ThreadCount,
    int ConnectionCount);

/// <summary>
/// 时间旅行查询
/// </summary>
public record TimeTravelQuery(
    string AggregateId,
    DateTime Timestamp,
    int? Version);

/// <summary>
/// 性能数据生成器
/// </summary>
public static class PerformanceGenerators
{
    private static readonly Random Random = new();

    /// <summary>
    /// 生成性能指标
    /// </summary>
    public static PerformanceMetrics GenerateMetrics()
    {
        return new PerformanceMetrics(
            ThroughputOpsPerSec: Random.Next(1000, 100000),
            Latency: GenerateLatencyPercentiles(),
            Resources: GenerateResourceUsage(),
            MeasuredAt: DateTime.UtcNow);
    }

    /// <summary>
    /// 生成延迟百分位数
    /// </summary>
    public static LatencyPercentiles GenerateLatencyPercentiles()
    {
        var p50 = Random.NextDouble() * 10; // 0-10ms
        var p95 = p50 + Random.NextDouble() * 20; // +0-20ms
        var p99 = p95 + Random.NextDouble() * 30; // +0-30ms
        var p999 = p99 + Random.NextDouble() * 50; // +0-50ms

        return new LatencyPercentiles(
            P50Ms: p50,
            P95Ms: p95,
            P99Ms: p99,
            P999Ms: p999);
    }

    /// <summary>
    /// 生成资源使用情况
    /// </summary>
    public static ResourceUsage GenerateResourceUsage()
    {
        return new ResourceUsage(
            MemoryBytes: Random.Next(100, 1000) * 1024 * 1024, // 100-1000 MB
            CpuPercent: Random.NextDouble() * 100,
            ThreadCount: Random.Next(10, 100),
            ConnectionCount: Random.Next(5, 50));
    }

    /// <summary>
    /// 生成时间旅行查询
    /// </summary>
    public static TimeTravelQuery GenerateTimeTravelQuery(string? aggregateId = null)
    {
        return new TimeTravelQuery(
            AggregateId: aggregateId ?? Guid.NewGuid().ToString(),
            Timestamp: DateTime.UtcNow.AddHours(-Random.Next(1, 24)),
            Version: Random.Next(0, 2) == 0 ? Random.Next(1, 100) : null);
    }

    /// <summary>
    /// 生成大量事件数据
    /// </summary>
    public static List<byte[]> GenerateLargeEventData(int count, int sizeKb = 10)
    {
        var events = new List<byte[]>();
        var data = new byte[sizeKb * 1024];

        for (int i = 0; i < count; i++)
        {
            Random.NextBytes(data);
            events.Add((byte[])data.Clone());
        }

        return events;
    }

    /// <summary>
    /// 生成并发操作
    /// </summary>
    public static List<Func<Task>> GenerateConcurrentOperations(int count)
    {
        var operations = new List<Func<Task>>();

        for (int i = 0; i < count; i++)
        {
            var operationId = i;
            operations.Add(async () =>
            {
                await Task.Delay(Random.Next(10, 100));
                // Simulate operation
            });
        }

        return operations;
    }

    /// <summary>
    /// 生成负载测试配置
    /// </summary>
    public static LoadTestConfiguration GenerateLoadTestConfig()
    {
        return new LoadTestConfiguration(
            ConcurrentUsers: Random.Next(10, 100),
            Duration: TimeSpan.FromMinutes(Random.Next(1, 10)),
            RampUpTime: TimeSpan.FromSeconds(Random.Next(10, 60)),
            OperationsPerSecond: Random.Next(100, 10000));
    }

    /// <summary>
    /// 生成性能基准
    /// </summary>
    public static PerformanceBenchmark GenerateBenchmark(string name)
    {
        return new PerformanceBenchmark(
            Name: name,
            Metrics: GenerateMetrics(),
            Configuration: new Dictionary<string, object>
            {
                ["Backend"] = "InMemory",
                ["Serializer"] = "MemoryPack",
                ["CacheEnabled"] = true
            });
    }

    /// <summary>
    /// 生成性能回归阈值
    /// </summary>
    public static RegressionThresholds GenerateRegressionThresholds()
    {
        return new RegressionThresholds(
            ThroughputTolerancePercent: 10.0,
            LatencyTolerancePercent: 10.0,
            MemoryTolerancePercent: 20.0);
    }

    /// <summary>
    /// 生成批处理配置
    /// </summary>
    public static BatchConfiguration GenerateBatchConfig()
    {
        return new BatchConfiguration(
            BatchSize: Random.Next(10, 1000),
            MaxWaitTimeMs: Random.Next(100, 5000),
            MaxConcurrentBatches: Random.Next(1, 10));
    }
}

/// <summary>
/// 负载测试配置
/// </summary>
public record LoadTestConfiguration(
    int ConcurrentUsers,
    TimeSpan Duration,
    TimeSpan RampUpTime,
    int OperationsPerSecond);

/// <summary>
/// 性能基准
/// </summary>
public record PerformanceBenchmark(
    string Name,
    PerformanceMetrics Metrics,
    Dictionary<string, object> Configuration);

/// <summary>
/// 回归阈值
/// </summary>
public record RegressionThresholds(
    double ThroughputTolerancePercent,
    double LatencyTolerancePercent,
    double MemoryTolerancePercent);

/// <summary>
/// 批处理配置
/// </summary>
public record BatchConfiguration(
    int BatchSize,
    int MaxWaitTimeMs,
    int MaxConcurrentBatches);
