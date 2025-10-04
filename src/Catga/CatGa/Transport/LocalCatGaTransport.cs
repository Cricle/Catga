using Catga.CatGa.Models;
using Microsoft.Extensions.Logging;

namespace Catga.CatGa.Transport;

/// <summary>
/// 本地（进程内）CatGa 传输 - 用于单实例场景
/// </summary>
public sealed class LocalCatGaTransport : ICatGaTransport
{
    private readonly ILogger<LocalCatGaTransport> _logger;

    public LocalCatGaTransport(ILogger<LocalCatGaTransport> logger)
    {
        _logger = logger;
    }

    public Task<CatGaResult<TResponse>> SendAsync<TRequest, TResponse>(
        string endpoint,
        TRequest request,
        CatGaContext context,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(
            "Local transport: SendAsync to {Endpoint}, TransactionId: {TransactionId}",
            endpoint, context.TransactionId);

        // 本地传输直接返回失败，因为本地没有端点概念
        // 实际的执行由 CatGaExecutor 完成
        return Task.FromResult(CatGaResult<TResponse>.Failure(
            "Local transport does not support remote calls", context));
    }

    public Task PublishAsync<TRequest>(
        string topic,
        TRequest request,
        CatGaContext context,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(
            "Local transport: PublishAsync to {Topic}, TransactionId: {TransactionId}",
            topic, context.TransactionId);

        // 本地传输中的发布是空操作
        return Task.CompletedTask;
    }

    public Task<IDisposable> SubscribeAsync<TRequest, TResponse>(
        string endpoint,
        Func<TRequest, CatGaContext, Task<CatGaResult<TResponse>>> handler,
        CancellationToken cancellationToken = default)
    {
        _logger.LogWarning(
            "Local transport does not support subscriptions: {Endpoint}",
            endpoint);

        // 返回空的 IDisposable
        return Task.FromResult<IDisposable>(new EmptyDisposable());
    }

    public Task<IDisposable> SubscribeEventAsync<TRequest>(
        string topic,
        Func<TRequest, CatGaContext, Task> handler,
        CancellationToken cancellationToken = default)
    {
        _logger.LogWarning(
            "Local transport does not support event subscriptions: {Topic}",
            topic);

        return Task.FromResult<IDisposable>(new EmptyDisposable());
    }

    private class EmptyDisposable : IDisposable
    {
        public void Dispose() { }
    }
}

