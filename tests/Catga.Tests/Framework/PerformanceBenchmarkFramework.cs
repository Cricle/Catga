using System.Diagnostics;
using System.Text.Json;

namespace Catga.Tests.Framework;

/// <summary>
/// 性能基准测试框架
/// 用于测量和比较性能指标
/// </summary>
public class PerformanceBenchmarkFramework
{
    private const string BaselineFileName = "performance-baseline.json";
    private readonly string _baselineFilePath;

    public PerformanceBenchmarkFramework(string? baselineDirectory = null)
    {
        _baselineFilePath = Path.Combine(
            baselineDirectory ?? Path.GetTempPath(),
            BaselineFileName);
    }

    /// <summary>
    /// 性能基准数据
    /// </summary>
    public class Baseline
    {
        /// <summary>吞吐量（操作/秒）</summary>
        public double ThroughputOpsPerSec { get; set; }

        /// <summary>延迟 P99（毫秒）</summary>
        public double LatencyP99Ms { get; set; }

        /// <summary>延迟 P95（毫秒）</summary>
        public double LatencyP95Ms { get; set; }

        /// <summary>延迟 P50（毫秒）</summary>
        public double LatencyP50Ms { get; set; }

        /// <summary>内存使用（字节）</summary>
        public long MemoryUsageBytes { get; set; }

        /// <summary>启动时间</summary>
        public TimeSpan StartupTime { get; set; }

        /// <summary>测量时间</summary>
        public DateTime MeasuredAt { get; set; }

        /// <summary>测试名称</summary>
        public string TestName { get; set; } = string.Empty;

        /// <summary>后端类型</summary>
        public string BackendType { get; set; } = string.Empty;
    }

    /// <summary>
    /// 性能测量结果
    /// </summary>
    public class PerformanceMeasurement
    {
        /// <summary>操作总数</summary>
        public int TotalOperations { get; set; }

        /// <summary>总耗时</summary>
        public TimeSpan TotalDuration { get; set; }

        /// <summary>吞吐量（操作/秒）</summary>
        public double ThroughputOpsPerSec => TotalOperations / TotalDuration.TotalSeconds;

        /// <summary>所有操作的延迟（毫秒）</summary>
        public List<double> LatenciesMs { get; set; } = new();

        /// <summary>延迟 P50（毫秒）</summary>
        public double LatencyP50Ms => GetPercentile(0.50);

        /// <summary>延迟 P95（毫秒）</summary>
        public double LatencyP95Ms => GetPercentile(0.95);

        /// <summary>延迟 P99（毫秒）</summary>
        public double LatencyP99Ms => GetPercentile(0.99);

        /// <summary>延迟 P999（毫秒）</summary>
        public double LatencyP999Ms => GetPercentile(0.999);

        /// <summary>内存使用（字节）</summary>
        public long MemoryUsageBytes { get; set; }

        /// <summary>启动时间</summary>
        public TimeSpan StartupTime { get; set; }

        private double GetPercentile(double percentile)
        {
            if (LatenciesMs.Count == 0) return 0;

            var sorted = LatenciesMs.OrderBy(x => x).ToList();
            var index = (int)Math.Ceiling(percentile * sorted.Count) - 1;
            index = Math.Max(0, Math.Min(index, sorted.Count - 1));
            return sorted[index];
        }
    }

    /// <summary>
    /// 测量操作性能
    /// </summary>
    public async Task<PerformanceMeasurement> MeasureAsync(
        Func<Task> operation,
        int iterations = 1000,
        int warmupIterations = 100)
    {
        // Warmup
        for (int i = 0; i < warmupIterations; i++)
        {
            await operation();
        }

        // Force GC before measurement
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var memoryBefore = GC.GetTotalMemory(false);
        var latencies = new List<double>();
        var stopwatch = Stopwatch.StartNew();

        for (int i = 0; i < iterations; i++)
        {
            var opStopwatch = Stopwatch.StartNew();
            await operation();
            opStopwatch.Stop();
            latencies.Add(opStopwatch.Elapsed.TotalMilliseconds);
        }

        stopwatch.Stop();

        var memoryAfter = GC.GetTotalMemory(false);

        return new PerformanceMeasurement
        {
            TotalOperations = iterations,
            TotalDuration = stopwatch.Elapsed,
            LatenciesMs = latencies,
            MemoryUsageBytes = memoryAfter - memoryBefore
        };
    }

    /// <summary>
    /// 测量启动时间
    /// </summary>
    public async Task<TimeSpan> MeasureStartupTimeAsync(Func<Task> startupOperation)
    {
        var stopwatch = Stopwatch.StartNew();
        await startupOperation();
        stopwatch.Stop();
        return stopwatch.Elapsed;
    }

    /// <summary>
    /// 测量吞吐量
    /// </summary>
    public async Task<double> MeasureThroughputAsync(
        Func<Task> operation,
        TimeSpan duration)
    {
        var count = 0;
        var stopwatch = Stopwatch.StartNew();

        while (stopwatch.Elapsed < duration)
        {
            await operation();
            count++;
        }

        stopwatch.Stop();
        return count / stopwatch.Elapsed.TotalSeconds;
    }

    /// <summary>
    /// 保存基准数据
    /// </summary>
    public async Task SaveBaselineAsync(Baseline baseline)
    {
        var json = JsonSerializer.Serialize(baseline, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        await File.WriteAllTextAsync(_baselineFilePath, json);
    }

    /// <summary>
    /// 加载基准数据
    /// </summary>
    public async Task<Baseline?> LoadBaselineAsync()
    {
        if (!File.Exists(_baselineFilePath))
            return null;

        var json = await File.ReadAllTextAsync(_baselineFilePath);
        return JsonSerializer.Deserialize<Baseline>(json);
    }

    /// <summary>
    /// 断言性能无回归
    /// </summary>
    public void AssertNoRegression(
        PerformanceMeasurement current,
        Baseline baseline,
        double tolerance = 0.1)
    {
        var regressions = new List<string>();

        // 检查吞吐量
        var throughputRatio = current.ThroughputOpsPerSec / baseline.ThroughputOpsPerSec;
        if (throughputRatio < (1.0 - tolerance))
        {
            regressions.Add(
                $"Throughput regressed: {current.ThroughputOpsPerSec:F2} ops/s < {baseline.ThroughputOpsPerSec * (1.0 - tolerance):F2} ops/s " +
                $"(baseline: {baseline.ThroughputOpsPerSec:F2} ops/s, tolerance: {tolerance:P})");
        }

        // 检查延迟
        var latencyRatio = current.LatencyP99Ms / baseline.LatencyP99Ms;
        if (latencyRatio > (1.0 + tolerance))
        {
            regressions.Add(
                $"Latency P99 regressed: {current.LatencyP99Ms:F2} ms > {baseline.LatencyP99Ms * (1.0 + tolerance):F2} ms " +
                $"(baseline: {baseline.LatencyP99Ms:F2} ms, tolerance: {tolerance:P})");
        }

        // 检查内存
        if (current.MemoryUsageBytes > 0 && baseline.MemoryUsageBytes > 0)
        {
            var memoryRatio = (double)current.MemoryUsageBytes / baseline.MemoryUsageBytes;
            if (memoryRatio > (1.0 + tolerance))
            {
                regressions.Add(
                    $"Memory usage regressed: {current.MemoryUsageBytes:N0} bytes > {baseline.MemoryUsageBytes * (1.0 + tolerance):N0} bytes " +
                    $"(baseline: {baseline.MemoryUsageBytes:N0} bytes, tolerance: {tolerance:P})");
            }
        }

        if (regressions.Any())
        {
            throw new PerformanceRegressionException(
                $"Performance regression detected:\n{string.Join("\n", regressions)}");
        }
    }

    /// <summary>
    /// 将测量结果转换为基准数据
    /// </summary>
    public Baseline ToBaseline(
        PerformanceMeasurement measurement,
        string testName,
        string backendType)
    {
        return new Baseline
        {
            ThroughputOpsPerSec = measurement.ThroughputOpsPerSec,
            LatencyP99Ms = measurement.LatencyP99Ms,
            LatencyP95Ms = measurement.LatencyP95Ms,
            LatencyP50Ms = measurement.LatencyP50Ms,
            MemoryUsageBytes = measurement.MemoryUsageBytes,
            StartupTime = measurement.StartupTime,
            MeasuredAt = DateTime.UtcNow,
            TestName = testName,
            BackendType = backendType
        };
    }

    /// <summary>
    /// 生成性能报告
    /// </summary>
    public string GenerateReport(PerformanceMeasurement measurement, Baseline? baseline = null)
    {
        var report = new System.Text.StringBuilder();
        report.AppendLine("=== Performance Report ===");
        report.AppendLine($"Total Operations: {measurement.TotalOperations:N0}");
        report.AppendLine($"Total Duration: {measurement.TotalDuration.TotalSeconds:F2}s");
        report.AppendLine($"Throughput: {measurement.ThroughputOpsPerSec:F2} ops/s");
        report.AppendLine($"Latency P50: {measurement.LatencyP50Ms:F2} ms");
        report.AppendLine($"Latency P95: {measurement.LatencyP95Ms:F2} ms");
        report.AppendLine($"Latency P99: {measurement.LatencyP99Ms:F2} ms");
        report.AppendLine($"Latency P999: {measurement.LatencyP999Ms:F2} ms");
        report.AppendLine($"Memory Usage: {measurement.MemoryUsageBytes:N0} bytes");

        if (baseline != null)
        {
            report.AppendLine();
            report.AppendLine("=== Comparison with Baseline ===");
            
            var throughputChange = (measurement.ThroughputOpsPerSec / baseline.ThroughputOpsPerSec - 1.0) * 100;
            report.AppendLine($"Throughput: {throughputChange:+0.00;-0.00}% (baseline: {baseline.ThroughputOpsPerSec:F2} ops/s)");
            
            var latencyChange = (measurement.LatencyP99Ms / baseline.LatencyP99Ms - 1.0) * 100;
            report.AppendLine($"Latency P99: {latencyChange:+0.00;-0.00}% (baseline: {baseline.LatencyP99Ms:F2} ms)");
            
            if (baseline.MemoryUsageBytes > 0)
            {
                var memoryChange = ((double)measurement.MemoryUsageBytes / baseline.MemoryUsageBytes - 1.0) * 100;
                report.AppendLine($"Memory: {memoryChange:+0.00;-0.00}% (baseline: {baseline.MemoryUsageBytes:N0} bytes)");
            }
        }

        return report.ToString();
    }
}

/// <summary>
/// 性能回归异常
/// </summary>
public class PerformanceRegressionException : Exception
{
    public PerformanceRegressionException(string message) : base(message)
    {
    }
}
