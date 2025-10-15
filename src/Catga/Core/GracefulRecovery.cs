using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Catga.Core;

/// <summary>
/// Graceful recovery manager - auto-handles reconnection and state recovery
/// Users don't need to worry about complex recovery logic
/// </summary>
public sealed class GracefulRecoveryManager
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
        _logger.LogDebug("Component registered: {ComponentType}", component.GetType().Name);
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
                _logger.LogWarning("Recovery already in progress");
                return RecoveryResult.AlreadyRecovering;
            }

            _isRecovering = true;
            var components = _components.ToArray();  // Lock-free read
            _logger.LogInformation("Starting recovery, components: {Count}", components.Length);

            var sw = Stopwatch.StartNew();
            var succeeded = 0;
            var failed = 0;

            foreach (var component in components)
            {
                try
                {
                    _logger.LogDebug("Recovering component: {ComponentType}", component.GetType().Name);
                    await component.RecoverAsync(cancellationToken);
                    succeeded++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Component recovery failed: {ComponentType}", component.GetType().Name);
                    failed++;
                }
            }

            _logger.LogInformation("Recovery complete - succeeded: {Succeeded}, failed: {Failed}, duration: {Elapsed:F1}s",
                succeeded, failed, sw.Elapsed.TotalSeconds);

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
        _logger.LogInformation("Auto-recovery started, interval: {Interval}", checkInterval);

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
                    _logger.LogWarning("Unhealthy component detected: {ComponentType}", component.GetType().Name);
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
                        _logger.LogInformation("Auto-recovery succeeded");
                        break;
                    }

                    retries++;
                    if (retries < maxRetries)
                    {
                        var delay = TimeSpan.FromSeconds(Math.Pow(2, retries)); // Exponential backoff
                        _logger.LogWarning("Recovery incomplete, retry in {Delay}s ({Retry}/{MaxRetries})",
                            delay.TotalSeconds, retries, maxRetries);
                        await Task.Delay(delay, cancellationToken);
                    }
                }
            }
        }
    }
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

