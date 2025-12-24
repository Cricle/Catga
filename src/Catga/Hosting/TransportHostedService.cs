using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Catga.Transport;

namespace Catga.Hosting;

/// <summary>
/// 传输层托管服务 - 管理消息传输的启动和停止
/// </summary>
public sealed partial class TransportHostedService : IHostedService
{
    private readonly IMessageTransport _transport;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<TransportHostedService> _logger;
    private readonly HostingOptions _options;
    private IDisposable? _stoppingRegistration;

    public TransportHostedService(
        IMessageTransport transport,
        IHostApplicationLifetime lifetime,
        ILogger<TransportHostedService> logger,
        HostingOptions options)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        LogTransportServiceStarting(_transport.Name);

        try
        {
            // 如果传输层需要初始化连接
            if (_transport is IAsyncInitializable initializable)
            {
                LogInitializingTransport(_transport.Name);
                await initializable.InitializeAsync(cancellationToken);
                LogTransportInitialized(_transport.Name);
            }

            // 注册停机事件 - 停止接受新消息
            _stoppingRegistration = _lifetime.ApplicationStopping.Register(() =>
            {
                LogApplicationStoppingDetected();
                
                if (_transport is IStoppable stoppable)
                {
                    LogStoppingMessageAcceptance(_transport.Name);
                    stoppable.StopAcceptingMessages();
                    LogMessageAcceptanceStopped(_transport.Name);
                }
            });

            LogTransportServiceStarted(_transport.Name);
        }
        catch (Exception ex)
        {
            LogTransportStartupFailed(_transport.Name, ex);
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        LogTransportServiceStopping(_transport.Name);

        try
        {
            // 创建一个带超时的取消令牌
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_options.ShutdownTimeout);

            // 等待正在处理的消息完成
            if (_transport is IWaitable waitable)
            {
                var pendingCount = waitable.PendingOperations;
                if (pendingCount > 0)
                {
                    LogWaitingForPendingOperations(_transport.Name, pendingCount);
                    
                    try
                    {
                        await waitable.WaitForCompletionAsync(timeoutCts.Token);
                        LogAllOperationsCompleted(_transport.Name);
                    }
                    catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                    {
                        // 超时了
                        LogShutdownTimeout(_transport.Name, _options.ShutdownTimeout.TotalSeconds, waitable.PendingOperations);
                    }
                }
            }

            // 关闭连接
            if (_transport is IAsyncDisposable asyncDisposable)
            {
                LogDisposingTransport(_transport.Name);
                await asyncDisposable.DisposeAsync();
            }
            else if (_transport is IDisposable disposable)
            {
                LogDisposingTransport(_transport.Name);
                disposable.Dispose();
            }

            _stoppingRegistration?.Dispose();
            LogTransportServiceStopped(_transport.Name);
        }
        catch (Exception ex)
        {
            LogTransportShutdownFailed(_transport.Name, ex);
            throw;
        }
    }

    #region Logging

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting transport service: {TransportName}")]
    partial void LogTransportServiceStarting(string transportName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Transport service started: {TransportName}")]
    partial void LogTransportServiceStarted(string transportName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Initializing transport: {TransportName}")]
    partial void LogInitializingTransport(string transportName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Transport initialized: {TransportName}")]
    partial void LogTransportInitialized(string transportName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Application stopping detected")]
    partial void LogApplicationStoppingDetected();

    [LoggerMessage(Level = LogLevel.Information, Message = "Stopping message acceptance for transport: {TransportName}")]
    partial void LogStoppingMessageAcceptance(string transportName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Message acceptance stopped for transport: {TransportName}")]
    partial void LogMessageAcceptanceStopped(string transportName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Stopping transport service: {TransportName}")]
    partial void LogTransportServiceStopping(string transportName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Waiting for {PendingCount} pending operation(s) to complete for transport: {TransportName}")]
    partial void LogWaitingForPendingOperations(string transportName, int pendingCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "All operations completed for transport: {TransportName}")]
    partial void LogAllOperationsCompleted(string transportName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Shutdown timeout ({TimeoutSeconds}s) exceeded for transport: {TransportName}. {RemainingOperations} operation(s) still pending. Forcing shutdown.")]
    partial void LogShutdownTimeout(string transportName, double timeoutSeconds, int remainingOperations);

    [LoggerMessage(Level = LogLevel.Information, Message = "Disposing transport: {TransportName}")]
    partial void LogDisposingTransport(string transportName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Transport service stopped: {TransportName}")]
    partial void LogTransportServiceStopped(string transportName);

    [LoggerMessage(Level = LogLevel.Error, Message = "Transport startup failed for: {TransportName}")]
    partial void LogTransportStartupFailed(string transportName, Exception ex);

    [LoggerMessage(Level = LogLevel.Error, Message = "Transport shutdown failed for: {TransportName}")]
    partial void LogTransportShutdownFailed(string transportName, Exception ex);

    #endregion
}
