using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Catga.Core;
using Catga.Messages;
using Catga.Results;
using Catga.Serialization;
using Catga.Transport;
using Microsoft.Extensions.Logging;

namespace Catga.Rpc;

/// <summary>High-performance lock-free RPC client</summary>
public sealed class RpcClient : IRpcClient, IDisposable
{
    private readonly IMessageTransport _transport;
    private readonly IMessageSerializer _serializer;
    private readonly ILogger<RpcClient> _logger;
    private readonly RpcOptions _options;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<RpcResponse>> _pendingCalls = new();
    private readonly CancellationTokenSource _cts = new();
    private Task? _receiveTask;

    public RpcClient(IMessageTransport transport, IMessageSerializer serializer, ILogger<RpcClient> logger, RpcOptions? options = null)
    {
        _transport = transport;
        _serializer = serializer;
        _logger = logger;
        _options = options ?? new();
    }

    public async Task<CatgaResult<TResponse>> CallAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse>(string serviceName, TRequest request, CancellationToken cancellationToken = default) where TRequest : class, IRequest<TResponse>
        => await CallAsync<TRequest, TResponse>(serviceName, TypeNameCache<TRequest>.Name, request, null, cancellationToken);

    public async Task<CatgaResult> CallAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest>(string serviceName, TRequest request, CancellationToken cancellationToken = default) where TRequest : class, IRequest
    {
        var result = await CallAsync<TRequest, object>(serviceName, TypeNameCache<TRequest>.Name, request, null, cancellationToken);
        if (result.IsSuccess) return CatgaResult.Success();
        var errorMsg = "RPC call failed";
        if (result.Metadata?.TryGetValue("ErrorMessage", out var msg) == true && !string.IsNullOrEmpty(msg)) errorMsg = msg;
        return CatgaResult.Failure(errorMsg);
    }

    public async Task<CatgaResult<TResponse>> CallAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse>(string serviceName, string methodName, TRequest request, TimeSpan? timeout = null, CancellationToken cancellationToken = default) where TRequest : class
    {
        if (_receiveTask == null) _receiveTask = StartReceiving(_cts.Token);
        var requestId = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<RpcResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingCalls[requestId] = tcs;
        try
        {
            var rpcRequest = new RpcRequest
            {
                ServiceName = serviceName,
                MethodName = methodName,
                RequestId = requestId,
                Payload = _serializer.Serialize(request),
                RequestType = TypeNameCache<TRequest>.FullName,
                Timeout = timeout ?? _options.DefaultTimeout
            };
            var subject = $"rpc.{serviceName}.{methodName}";
            await _transport.SendAsync(rpcRequest, subject, cancellationToken: cancellationToken);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout ?? _options.DefaultTimeout);
            var response = await tcs.Task.WaitAsync(cts.Token);
            if (response.Success && response.Payload != null)
            {
                var result = _serializer.Deserialize<TResponse>(response.Payload);
                return CatgaResult<TResponse>.Success(result!);
            }
            return CatgaResult<TResponse>.Failure(response.ErrorMessage ?? "RPC call failed");
        }
        catch (OperationCanceledException)
        {
            return CatgaResult<TResponse>.Failure("RPC call timeout");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RPC call failed: {Service}.{Method}", serviceName, methodName);
            return CatgaResult<TResponse>.Failure($"RPC call exception: {ex.Message}");
        }
        finally
        {
            _pendingCalls.TryRemove(requestId, out _);
        }
    }

    private async Task StartReceiving(CancellationToken cancellationToken)
    {
        var responseSubject = $"rpc.response.{_options.ServiceName}";
        await _transport.SubscribeAsync<RpcResponse>(async (response, context) =>
        {
            if (_pendingCalls.TryRemove(response.RequestId, out var tcs)) tcs.TrySetResult(response);
            await Task.CompletedTask;
        }, cancellationToken);
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        _receiveTask?.Wait(TimeSpan.FromSeconds(5));
    }
}

