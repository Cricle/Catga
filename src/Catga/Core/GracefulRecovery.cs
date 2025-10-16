using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Catga.Core;

/// <summary>
/// Graceful recovery manager - auto-handles reconnection and state recovery
/// Users don't need to worry about complex recovery logic
/// </summary>
public sealed partial class GracefulRecoveryManager
{
    private readonly ILogger<GracefulRecoveryManager> _logger;
    private readonly ConcurrentBag<IRecoverableComponent> _components = new();
    private readonly SemaphoreSlim _recoveryLock = new(1, 1);
    private volatile bool _isRecovering;

    public GracefulRecoveryManager(ILogger<GracefulRecoveryManager> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Is recovery in progress
    /// </summary>
    public bool IsRecovering => _isRecovering;

    /// <summary>
    /// Register recoverable component
    /// </summary>
    public void RegisterComponent(IRecoverableComponent component)
    {
        _components.Add(component);  // Lock-free with ConcurrentBag
        LogComponentRegistered(component.GetType().Name);
    }

    /// <summary>
    /// Recover all components - auto-reconnect
    /// </summary>
    public async Task<RecoveryResult> RecoverAsync(CancellationToken cancellationToken = default)
    {
        await _recoveryLock.WaitAsync(cancellationToken);
        try
        {
            if (_isRecovering)
            {
                LogRecoveryInProgress();
                return RecoveryResult.AlreadyRecovering;
            }

            _isRecovering = true;
            var components = _components.ToArray();  // Lock-free read
            LogRecoveryStarted(components.Length);

            var sw = Stopwatch.StartNew();
            var succeeded = 0;
            var failed = 0;

            foreach (var component in components)
            {
                try
                {
                    LogRecoveringComponent(component.GetType().Name);
                    await component.RecoverAsync(cancellationToken);
                    succeeded++;
                }
                catch (Exception ex)
                {
                    LogComponentRecoveryFailed(ex, component.GetType().Name);
                    failed++;
                }
            }

            LogRecoveryComplete(succeeded, failed, sw.Elapsed.TotalSeconds);

            return new RecoveryResult(succeeded, failed, sw.Elapsed);
        }
        finally
        {
            _isRecovering = false;
            _recoveryLock.Release();
        }
    }

    /// <summary>
    /// Auto-recovery loop - retries on failure detection
    /// </summary>
    public async Task StartAutoRecoveryAsync(
        TimeSpan checkInterval,
        int maxRetries = 3,
        CancellationToken cancellationToken = default)
    {
        LogAutoRecoveryStarted(checkInterval);

        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(checkInterval, cancellationToken);

            // Check if any component needs recovery
            var needsRecovery = false;
            var components = _components.ToArray();  // Lock-free read

            foreach (var component in components)
            {
                if (!component.IsHealthy)
                {
                    needsRecovery = true;
                    LogUnhealthyComponentDetected(component.GetType().Name);
                    break;
                }
            }

            if (needsRecovery)
            {
                var retries = 0;
                while (retries < maxRetries && !cancellationToken.IsCancellationRequested)
                {
                    var result = await RecoverAsync(cancellationToken);
                    if (result.Failed == 0)
                    {
                        LogAutoRecoverySucceeded();
                        break;
                    }

                    retries++;
                    if (retries < maxRetries)
                    {
                        var delay = TimeSpan.FromSeconds(Math.Pow(2, retries)); // Exponential backoff
                        LogRecoveryIncomplete(delay.TotalSeconds, retries, maxRetries);
                        await Task.Delay(delay, cancellationToken);
                    }
                }
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Component registered: {ComponentType}")]
    partial void LogComponentRegistered(string componentType);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Recovery already in progress")]
    partial void LogRecoveryInProgress();

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting recovery, components: {Count}")]
    partial void LogRecoveryStarted(int count);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Recovering component: {ComponentType}")]
    partial void LogRecoveringComponent(string componentType);

    [LoggerMessage(Level = LogLevel.Error, Message = "Component recovery failed: {ComponentType}")]
    partial void LogComponentRecoveryFailed(Exception ex, string componentType);

    [LoggerMessage(Level = LogLevel.Information, Message = "Recovery complete - succeeded: {Succeeded}, failed: {Failed}, duration: {Elapsed:F1}s")]
    partial void LogRecoveryComplete(int succeeded, int failed, double elapsed);

    [LoggerMessage(Level = LogLevel.Information, Message = "Auto-recovery started, interval: {Interval}")]
    partial void LogAutoRecoveryStarted(TimeSpan interval);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Unhealthy component detected: {ComponentType}")]
    partial void LogUnhealthyComponentDetected(string componentType);

    [LoggerMessage(Level = LogLevel.Information, Message = "Auto-recovery succeeded")]
    partial void LogAutoRecoverySucceeded();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Recovery incomplete, retry in {Delay}s ({Retry}/{MaxRetries})")]
    partial void LogRecoveryIncomplete(double delay, int retry, int maxRetries);
}

/// <summary>
/// 可恢复组件接口 - 任何需要恢复的组件都实现此接口
/// </summary>
public interface IRecoverableComponent
{
    /// <summary>
    /// 组件是否健康
    /// </summary>
    bool IsHealthy { get; }

    /// <summary>
    /// 执行恢复逻辑
    /// </summary>
    Task RecoverAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 恢复结果
/// </summary>
public readonly record struct RecoveryResult(int Succeeded, int Failed, TimeSpan Duration)
{
    public static RecoveryResult AlreadyRecovering => new(-1, -1, TimeSpan.Zero);
    public bool IsSuccess => Failed == 0 && Succeeded > 0;
}

