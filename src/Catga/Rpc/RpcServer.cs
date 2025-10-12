using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Catga.Messages;
using Catga.Serialization;
using Catga.Transport;
using Microsoft.Extensions.Logging;

namespace Catga.Rpc;

/// <summary>High-performance lock-free RPC server</summary>
public sealed class RpcServer : IRpcServer, IDisposable
{
    private readonly IMessageTransport _transport;
    private readonly IMessageSerializer _serializer;
    private readonly ILogger<RpcServer> _logger;
    private readonly RpcOptions _options;
    private readonly ConcurrentDictionary<string, IRpcHandler> _handlers = new();
    private readonly CancellationTokenSource _cts = new();
    private Task? _receiveTask;

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
        _logger.LogInformation("Registered RPC handler: {Method}", methodName);
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_receiveTask != null) return Task.CompletedTask;
        var requestSubject = $"rpc.{_options.ServiceName}.>";
        _receiveTask = _transport.SubscribeAsync<RpcRequest>(async (rpcRequest, context) =>
        {
            await HandleRequestAsync(rpcRequest, cancellationToken);
        }, cancellationToken);
        _logger.LogInformation("RPC server started: {ServiceName}", _options.ServiceName);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _cts.Cancel();
        if (_receiveTask != null) await _receiveTask;
        _logger.LogInformation("RPC server stopped: {ServiceName}", _options.ServiceName);
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
            _logger.LogError(ex, "RPC handler exception: {Method}", request.MethodName);
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
            _logger.LogError(ex, "Failed to send RPC response for request {RequestId}", request.RequestId);
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        _receiveTask?.Wait(TimeSpan.FromSeconds(5));
    }
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

