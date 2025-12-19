using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Catga.Core;

/// <summary>
/// Graceful recovery manager - auto-handles reconnection and state recovery.
/// Lock-free design using ConcurrentBag and atomic operations.
/// </summary>
public sealed partial class GracefulRecoveryManager
{
    private readonly ILogger<GracefulRecoveryManager> _logger;
    private readonly ConcurrentBag<IRecoverableComponent> _components = new();
    private volatile int _isRecovering;

    public GracefulRecoveryManager(ILogger<GracefulRecoveryManager> logger) => _logger = logger;

    public bool IsRecovering => _isRecovering == 1;

    public void RegisterComponent(IRecoverableComponent component)
    {
        _components.Add(component);
        LogComponentRegistered(component.GetType().Name);
    }

    public async Task<RecoveryResult> RecoverAsync(CancellationToken cancellationToken = default)
    {
        // Atomic check-and-set using Interlocked
        if (Interlocked.CompareExchange(ref _isRecovering, 1, 0) != 0)
        {
            LogRecoveryInProgress();
            return RecoveryResult.AlreadyRecovering;
        }

        try
        {
            var componentCount = _components.Count;
            LogRecoveryStarted(componentCount);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var succeeded = 0;
            var failed = 0;

            foreach (var component in _components)
            {
                try
                {
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
            Interlocked.Exchange(ref _isRecovering, 0);
        }
    }

    public async Task StartAutoRecoveryAsync(
        TimeSpan checkInterval,
        int maxRetries = 3,
        CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(checkInterval, cancellationToken);

                var needsRecovery = false;
                foreach (var component in _components)
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
                    for (var retries = 0; retries < maxRetries && !cancellationToken.IsCancellationRequested; retries++)
                    {
                        var result = await RecoverAsync(cancellationToken);
                        if (result.Failed == 0) break;

                        if (retries < maxRetries - 1)
                        {
                            var delay = TimeSpan.FromSeconds(Math.Pow(2, retries + 1));
                            await Task.Delay(delay, cancellationToken);
                        }
                    }
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { LogAutoRecoveryLoopException(ex.Message, ex); }
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Component registered: {ComponentType}")]
    partial void LogComponentRegistered(string componentType);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Recovery already in progress")]
    partial void LogRecoveryInProgress();

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting recovery, components: {Count}")]
    partial void LogRecoveryStarted(int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Component recovery failed: {ComponentType}")]
    partial void LogComponentRecoveryFailed(Exception ex, string componentType);

    [LoggerMessage(Level = LogLevel.Information, Message = "Recovery complete - succeeded: {Succeeded}, failed: {Failed}, duration: {Elapsed:F1}s")]
    partial void LogRecoveryComplete(int succeeded, int failed, double elapsed);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Unhealthy component detected: {ComponentType}")]
    partial void LogUnhealthyComponentDetected(string componentType);

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
