using System.Diagnostics;

namespace Catga.Cluster.Metrics;

/// <summary>
/// 系统负载上报（基于 CPU 使用率）
/// </summary>
public sealed class SystemLoadReporter : ILoadReporter
{
    private readonly Process _currentProcess = Process.GetCurrentProcess();
    private DateTime _lastCheckTime = DateTime.UtcNow;
    private TimeSpan _lastTotalProcessorTime;
    private int _cachedLoad;

    public Task<int> GetCurrentLoadAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var elapsed = (now - _lastCheckTime).TotalMilliseconds;

        // 每秒更新一次（避免频繁计算）
        if (elapsed < 1000 && _cachedLoad > 0)
        {
            return Task.FromResult(_cachedLoad);
        }

        try
        {
            var currentTotalProcessorTime = _currentProcess.TotalProcessorTime;
            var processorTimeDelta = (currentTotalProcessorTime - _lastTotalProcessorTime).TotalMilliseconds;

            // CPU 使用率 = (处理器时间增量 / 实际时间增量) / CPU 核心数 * 100
            var cpuUsage = processorTimeDelta / elapsed / Environment.ProcessorCount * 100;

            // 限制在 0-100 范围内
            _cachedLoad = Math.Clamp((int)cpuUsage, 0, 100);

            _lastCheckTime = now;
            _lastTotalProcessorTime = currentTotalProcessorTime;

            return Task.FromResult(_cachedLoad);
        }
        catch
        {
            // 如果获取失败，返回默认值 0
            return Task.FromResult(0);
        }
    }
}

