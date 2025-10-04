using Catga.CatGa.Models;

namespace Catga.CatGa.Transport;

/// <summary>
/// CatGa 传输接口 - 单一职责：消息传输
/// </summary>
public interface ICatGaTransport
{
    /// <summary>
    /// 发送事务请求（同步调用）
    /// </summary>
    Task<CatGaResult<TResponse>> SendAsync<TRequest, TResponse>(
        string endpoint,
        TRequest request,
        CatGaContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 发布事务事件（异步，无需响应）
    /// </summary>
    Task PublishAsync<TRequest>(
        string topic,
        TRequest request,
        CatGaContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 订阅事务请求
    /// </summary>
    Task<IDisposable> SubscribeAsync<TRequest, TResponse>(
        string endpoint,
        Func<TRequest, CatGaContext, Task<CatGaResult<TResponse>>> handler,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 订阅事务事件
    /// </summary>
    Task<IDisposable> SubscribeEventAsync<TRequest>(
        string topic,
        Func<TRequest, CatGaContext, Task> handler,
        CancellationToken cancellationToken = default);
}

