using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Catga.Core;

/// <summary>
/// Graceful recovery manager - auto-handles reconnection and state recovery
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

    public bool IsRecovering => _isRecovering;

    public void RegisterComponent(IRecoverableComponent component)
    {
        _components.Add(component);
        LogComponentRegistered(component.GetType().Name);
    }

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
            var componentCount = _components.Count;
            LogRecoveryStarted(componentCount);

            var sw = Stopwatch.StartNew();
            var succeeded = 0;
            var failed = 0;

            foreach (var component in _components)
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

    public async Task StartAutoRecoveryAsync(
        TimeSpan checkInterval,
        int maxRetries = 3,
        CancellationToken cancellationToken = default)
    {
        LogAutoRecoveryStarted(checkInterval);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(checkInterval, cancellationToken);

                var needsRecovery = false;
                foreach (var component in _components)
                {
                    try
                    {
                        if (!component.IsHealthy)
                        {
                            needsRecovery = true;
                            LogUnhealthyComponentDetected(component.GetType().Name);
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        LogComponentHealthCheckFailed(component.GetType().Name, ex.Message);
                        needsRecovery = true;
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
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                LogAutoRecoveryLoopException(ex.Message, ex);
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

    [LoggerMessage(Level = LogLevel.Error, Message = "Component health check failed: {ComponentType}, Error: {ErrorMessage}")]
    partial void LogComponentHealthCheckFailed(string componentType, string errorMessage);

    [LoggerMessage(Level = LogLevel.Error, Message = "Auto-recovery loop exception: {ErrorMessage}")]
    partial void LogAutoRecoveryLoopException(string errorMessage, Exception ex);
}

/// <summary>Recoverable component interface</summary>
public interface IRecoverableComponent
{
    bool IsHealthy { get; }
    Task RecoverAsync(CancellationToken cancellationToken = default);
}

/// <summary>Recovery result</summary>
public readonly record struct RecoveryResult(int Succeeded, int Failed, TimeSpan Duration)
{
    public static RecoveryResult AlreadyRecovering => new(-1, -1, TimeSpan.Zero);
    public bool IsSuccess => Failed == 0 && Succeeded > 0;
}
