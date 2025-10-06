using Catga.Messages;
using Catga.Results;

namespace Catga;

/// <summary>
/// Mediator for sending requests and publishing events (AOT-compatible)
/// </summary>
public interface ICatgaMediator
{
    /// <summary>
    /// Send a request and wait for response (AOT-compatible with explicit type parameters)
    /// </summary>
    ValueTask<CatgaResult<TResponse>> SendAsync<TRequest, TResponse>(
        TRequest request,
        CancellationToken cancellationToken = default)
        where TRequest : IRequest<TResponse>;

    /// <summary>
    /// Send a request without expecting a response (AOT-compatible)
    /// </summary>
    Task<CatgaResult> SendAsync<TRequest>(
        TRequest request,
        CancellationToken cancellationToken = default)
        where TRequest : IRequest;

    /// <summary>
    /// Publish an event to all subscribers (AOT-compatible)
    /// </summary>
    Task PublishAsync<TEvent>(
        TEvent @event,
        CancellationToken cancellationToken = default) where TEvent : IEvent;

    /// <summary>
    /// 🔥 批量发送请求 - 高性能批处理
    /// </summary>
    ValueTask<IReadOnlyList<CatgaResult<TResponse>>> SendBatchAsync<TRequest, TResponse>(
        IReadOnlyList<TRequest> requests,
        CancellationToken cancellationToken = default)
        where TRequest : IRequest<TResponse>;

    /// <summary>
    /// 🔥 流式发送请求 - 实时处理大量数据
    /// </summary>
    IAsyncEnumerable<CatgaResult<TResponse>> SendStreamAsync<TRequest, TResponse>(
        IAsyncEnumerable<TRequest> requests,
        CancellationToken cancellationToken = default)
        where TRequest : IRequest<TResponse>;

    /// <summary>
    /// 🔥 批量发布事件 - 高性能批处理
    /// </summary>
    Task PublishBatchAsync<TEvent>(
        IReadOnlyList<TEvent> events,
        CancellationToken cancellationToken = default)
        where TEvent : IEvent;
}
