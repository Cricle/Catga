using Catga.Abstractions;
using Catga.Transport;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace Catga.Rpc;

/// <summary>High-performance lock-free RPC server</summary>
public sealed partial class RpcServer : IRpcServer, IAsyncDisposable
{
    private readonly IMessageTransport _transport;
    private readonly IMessageSerializer _serializer;
    private readonly ILogger<RpcServer> _logger;
    private readonly RpcOptions _options;
    private readonly ConcurrentDictionary<string, IRpcHandler> _handlers = new();
    private readonly CancellationTokenSource _cts = new();
    private Task? _receiveTask;
    private bool _disposed;

    public RpcServer(IMessageTransport transport, IMessageSerializer serializer, ILogger<RpcServer> logger, RpcOptions? options = null)
    {
        _transport = transport;
        _serializer = serializer;
        _logger = logger;
        _options = options ?? new();
    }

    public void RegisterHandler<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse>(string methodName, Func<TRequest, CancellationToken, Task<TResponse>> handler) where TRequest : class
    {
        _handlers[methodName] = new RpcHandler<TRequest, TResponse>(handler, _serializer);
        LogHandlerRegistered(methodName);
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _receiveTask) != null)
            return Task.CompletedTask;

        lock (_handlers) // Reuse _handlers as lock object
        {
            if (_receiveTask != null)
                return Task.CompletedTask;

            var requestSubject = $"rpc.{_options.ServiceName}.>";
            _receiveTask = _transport.SubscribeAsync<RpcRequest>(async (rpcRequest, context) =>
            {
                await HandleRequestAsync(rpcRequest, cancellationToken);
            }, cancellationToken);

            LogServerStarted(_options.ServiceName);
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _cts.Cancel();
        if (_receiveTask != null) await _receiveTask;
        LogServerStopped(_options.ServiceName);
    }

    private async Task HandleRequestAsync(RpcRequest request, CancellationToken cancellationToken)
    {
        var response = new RpcResponse { RequestId = request.RequestId, Success = false };
        try
        {
            if (!_handlers.TryGetValue(request.MethodName, out var handler))
            {
                response.ErrorMessage = $"Handler not found: {request.MethodName}";
                response.ErrorCode = "HANDLER_NOT_FOUND";
                await SendResponseAsync(request, response, cancellationToken);
                return;
            }
            var result = await handler.HandleAsync(request.Payload, cancellationToken);
            response.Success = true;
            response.Payload = result;
        }
        catch (Exception ex)
        {
            LogHandlerException(ex, request.MethodName);
            response.ErrorMessage = ex.Message;
            response.ErrorCode = "HANDLER_EXCEPTION";
        }
        await SendResponseAsync(request, response, cancellationToken);
    }

    private async Task SendResponseAsync(RpcRequest request, RpcResponse response, CancellationToken cancellationToken)
    {
        try
        {
            var responseSubject = $"rpc.response.{request.ServiceName}";
            await _transport.SendAsync(response, responseSubject, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            LogSendResponseFailed(ex, request.RequestId);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        _cts.Cancel();

        if (_receiveTask != null)
        {
            try
            {
                // Use WaitAsync with timeout (available in .NET 6+)
                await _receiveTask.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (TimeoutException)
            {
                LogReceiveTaskTimeout();
            }
            catch (OperationCanceledException)
            {
                // Expected on cancellation
            }
        }

        _cts.Dispose();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Registered RPC handler: {Method}")]
    partial void LogHandlerRegistered(string method);

    [LoggerMessage(Level = LogLevel.Information, Message = "RPC server started: {ServiceName}")]
    partial void LogServerStarted(string serviceName);

    [LoggerMessage(Level = LogLevel.Information, Message = "RPC server stopped: {ServiceName}")]
    partial void LogServerStopped(string serviceName);

    [LoggerMessage(Level = LogLevel.Error, Message = "RPC handler exception: {Method}")]
    partial void LogHandlerException(Exception ex, string method);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to send RPC response for request {RequestId}")]
    partial void LogSendResponseFailed(Exception ex, string requestId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "RPC server receive task did not complete within timeout")]
    partial void LogReceiveTaskTimeout();
}

internal interface IRpcHandler
{
    Task<byte[]> HandleAsync(byte[] payload, CancellationToken cancellationToken);
}

internal sealed class RpcHandler<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse> : IRpcHandler where TRequest : class
{
    private readonly Func<TRequest, CancellationToken, Task<TResponse>> _handler;
    private readonly IMessageSerializer _serializer;

    public RpcHandler(Func<TRequest, CancellationToken, Task<TResponse>> handler, IMessageSerializer serializer)
    {
        _handler = handler;
        _serializer = serializer;
    }

    public async Task<byte[]> HandleAsync(byte[] payload, CancellationToken cancellationToken)
    {
        var request = _serializer.Deserialize<TRequest>(payload);
        var response = await _handler(request!, cancellationToken);
        return _serializer.Serialize(response);
    }
}

