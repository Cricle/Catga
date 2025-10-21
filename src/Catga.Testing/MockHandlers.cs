using Catga.Abstractions;
using Catga.Core;

namespace Catga.Testing;

/// <summary>
/// 简单的 Mock Handler - 总是返回成功
/// </summary>
public class MockSuccessHandler<TRequest, TResponse> : IRequestHandler<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
    where TResponse : new()
{
    public Task<CatgaResult<TResponse>> HandleAsync(TRequest request, CancellationToken cancellationToken = default)
    {
        var response = new TResponse();
        return Task.FromResult(CatgaResult<TResponse>.Success(response));
    }
}

/// <summary>
/// 简单的 Mock Handler - 总是返回失败
/// </summary>
public class MockFailureHandler<TRequest, TResponse> : IRequestHandler<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly string _errorMessage;

    public MockFailureHandler(string errorMessage = "Mock failure")
    {
        _errorMessage = errorMessage;
    }

    public Task<CatgaResult<TResponse>> HandleAsync(TRequest request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(CatgaResult<TResponse>.Failure(_errorMessage));
    }
}

/// <summary>
/// 可追踪的 Mock Handler - 记录调用次数
/// </summary>
public class TrackableHandler<TRequest, TResponse> : IRequestHandler<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
    where TResponse : new()
{
    public int CallCount { get; private set; }
    public TRequest? LastRequest { get; private set; }
    public List<TRequest> AllRequests { get; } = new();

    public Task<CatgaResult<TResponse>> HandleAsync(TRequest request, CancellationToken cancellationToken = default)
    {
        CallCount++;
        LastRequest = request;
        AllRequests.Add(request);

        var response = new TResponse();
        return Task.FromResult(CatgaResult<TResponse>.Success(response));
    }

    public void Reset()
    {
        CallCount = 0;
        LastRequest = default;
        AllRequests.Clear();
    }
}

/// <summary>
/// 可追踪的事件 Handler
/// </summary>
public class TrackableEventHandler<TEvent> : IEventHandler<TEvent>
    where TEvent : IEvent
{
    public int CallCount { get; private set; }
    public TEvent? LastEvent { get; private set; }
    public List<TEvent> AllEvents { get; } = new();

    public Task HandleAsync(TEvent @event, CancellationToken cancellationToken = default)
    {
        CallCount++;
        LastEvent = @event;
        AllEvents.Add(@event);
        return Task.CompletedTask;
    }

    public void Reset()
    {
        CallCount = 0;
        LastEvent = default;
        AllEvents.Clear();
    }
}

/// <summary>
/// 延迟 Handler - 模拟慢速操作
/// </summary>
public class DelayedHandler<TRequest, TResponse> : IRequestHandler<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
    where TResponse : new()
{
    private readonly TimeSpan _delay;

    public DelayedHandler(TimeSpan delay)
    {
        _delay = delay;
    }

    public async Task<CatgaResult<TResponse>> HandleAsync(TRequest request, CancellationToken cancellationToken = default)
    {
        await Task.Delay(_delay, cancellationToken);
        var response = new TResponse();
        return CatgaResult<TResponse>.Success(response);
    }
}

/// <summary>
/// 异常 Handler - 总是抛出异常
/// </summary>
public class ExceptionHandler<TRequest, TResponse> : IRequestHandler<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly Exception _exception;

    public ExceptionHandler(Exception? exception = null)
    {
        _exception = exception ?? new InvalidOperationException("Mock exception");
    }

    public Task<CatgaResult<TResponse>> HandleAsync(TRequest request, CancellationToken cancellationToken = default)
    {
        throw _exception;
    }
}

