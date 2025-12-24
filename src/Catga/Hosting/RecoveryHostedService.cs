using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Catga.Hosting;

/// <summary>
/// 后台恢复服务 - 定期检查组件健康状态并自动恢复
/// </summary>
public sealed partial class RecoveryHostedService : BackgroundService
{
    private readonly ILogger<RecoveryHostedService> _logger;
    private readonly IEnumerable<IRecoverableComponent> _components;
    private readonly RecoveryOptions _options;
    private volatile int _isRecovering;

    public RecoveryHostedService(
        ILogger<RecoveryHostedService> logger,
        IEnumerable<IRecoverableComponent> components,
        RecoveryOptions options)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _components = components ?? throw new ArgumentNullException(nameof(components));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        
        _options.Validate();
    }

    /// <summary>
    /// 指示是否正在进行恢复
    /// </summary>
    public bool IsRecovering => _isRecovering == 1;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.EnableAutoRecovery)
        {
            LogAutoRecoveryDisabled();
            return;
        }

        LogRecoveryServiceStarted(_options.CheckInterval.TotalSeconds);

        try
        {
            // Perform initial health check immediately on startup
            try
            {
                await CheckAndRecoverAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                LogRecoveryLoopException(ex);
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_options.CheckInterval, stoppingToken);
                    await CheckAndRecoverAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    LogRecoveryLoopException(ex);
                    // 继续运行，不要让单次错误停止整个恢复服务
                }
            }
        }
        finally
        {
            LogRecoveryServiceStopped();
        }
    }

    private async Task CheckAndRecoverAsync(CancellationToken cancellationToken)
    {
        // Check health status of all components (this triggers the IsHealthy property getter)
        var unhealthyComponents = new List<IRecoverableComponent>();
        foreach (var component in _components)
        {
            if (!component.IsHealthy)
            {
                unhealthyComponents.Add(component);
            }
        }
        
        if (unhealthyComponents.Count == 0)
        {
            return;
        }

        LogUnhealthyComponentsDetected(unhealthyComponents.Count);

        foreach (var component in unhealthyComponents)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            await RecoverComponentAsync(component, cancellationToken);
        }
    }

    private async Task RecoverComponentAsync(IRecoverableComponent component, CancellationToken cancellationToken)
    {
        // 使用 Interlocked 确保同一时间只有一个恢复操作在进行
        if (Interlocked.CompareExchange(ref _isRecovering, 1, 0) != 0)
        {
            LogRecoveryAlreadyInProgress(component.ComponentName);
            return;
        }

        try
        {
            LogRecoveryAttemptStarted(component.ComponentName);
            var sw = System.Diagnostics.Stopwatch.StartNew();

            for (var attempt = 0; attempt < _options.MaxRetries; attempt++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                try
                {
                    await component.RecoverAsync(cancellationToken);
                    
                    // 恢复成功
                    sw.Stop();
                    LogRecoverySucceeded(component.ComponentName, attempt + 1, sw.Elapsed.TotalSeconds);
                    return;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    LogRecoveryAttemptFailed(component.ComponentName, attempt + 1, _options.MaxRetries, ex);

                    // 如果还有重试机会，等待后重试
                    if (attempt < _options.MaxRetries - 1)
                    {
                        var delay = CalculateRetryDelay(attempt);
                        LogRetryDelayApplied(component.ComponentName, delay.TotalSeconds);
                        
                        try
                        {
                            await Task.Delay(delay, cancellationToken);
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            throw;
                        }
                    }
                }
            }

            // 所有重试都失败了
            sw.Stop();
            LogRecoveryFailed(component.ComponentName, _options.MaxRetries, sw.Elapsed.TotalSeconds);
        }
        finally
        {
            Interlocked.Exchange(ref _isRecovering, 0);
        }
    }

    private TimeSpan CalculateRetryDelay(int attemptNumber)
    {
        if (!_options.UseExponentialBackoff)
        {
            return _options.RetryDelay;
        }

        // 指数退避：RetryDelay * 2^attemptNumber
        var multiplier = Math.Pow(2, attemptNumber);
        var delaySeconds = _options.RetryDelay.TotalSeconds * multiplier;
        
        // 限制最大延迟为 5 分钟
        var maxDelaySeconds = Math.Min(delaySeconds, 300);
        
        return TimeSpan.FromSeconds(maxDelaySeconds);
    }

    #region Logging

    [LoggerMessage(Level = LogLevel.Information, Message = "Recovery service started with check interval: {CheckIntervalSeconds}s")]
    partial void LogRecoveryServiceStarted(double checkIntervalSeconds);

    [LoggerMessage(Level = LogLevel.Information, Message = "Recovery service stopped")]
    partial void LogRecoveryServiceStopped();

    [LoggerMessage(Level = LogLevel.Information, Message = "Auto-recovery is disabled")]
    partial void LogAutoRecoveryDisabled();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Detected {Count} unhealthy component(s)")]
    partial void LogUnhealthyComponentsDetected(int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting recovery attempt for component: {ComponentName}")]
    partial void LogRecoveryAttemptStarted(string componentName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Component {ComponentName} recovered successfully after {Attempts} attempt(s) in {DurationSeconds:F2}s")]
    partial void LogRecoverySucceeded(string componentName, int attempts, double durationSeconds);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Recovery attempt {Attempt}/{MaxRetries} failed for component {ComponentName}")]
    partial void LogRecoveryAttemptFailed(string componentName, int attempt, int maxRetries, Exception ex);

    [LoggerMessage(Level = LogLevel.Error, Message = "Component {ComponentName} recovery failed after {MaxRetries} attempts in {DurationSeconds:F2}s")]
    partial void LogRecoveryFailed(string componentName, int maxRetries, double durationSeconds);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Recovery already in progress for component: {ComponentName}")]
    partial void LogRecoveryAlreadyInProgress(string componentName);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Applying retry delay of {DelaySeconds:F1}s for component: {ComponentName}")]
    partial void LogRetryDelayApplied(string componentName, double delaySeconds);

    [LoggerMessage(Level = LogLevel.Error, Message = "Exception in recovery loop")]
    partial void LogRecoveryLoopException(Exception ex);

    #endregion
}
