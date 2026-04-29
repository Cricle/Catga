using System.Diagnostics.CodeAnalysis;
using Catga.Abstractions;
using Catga.Core;

namespace Catga.Testing;

/// <summary>
/// Spy wrapper for request handlers - records calls and delegates to inner handler.
/// </summary>
public sealed class HandlerSpy<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest,
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse> : IRequestHandler<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IRequestHandler<TRequest, TResponse>? _inner;
    private readonly Func<TRequest, CancellationToken, ValueTask<CatgaResult<TResponse>>>? _factory;
    private readonly List<TRequest> _calls = new();

    public HandlerSpy(IRequestHandler<TRequest, TResponse> inner) => _inner = inner;

    public HandlerSpy(Func<TRequest, CancellationToken, ValueTask<CatgaResult<TResponse>>> factory)
        => _factory = factory;

    /// <summary>All requests received by this handler.</summary>
    public IReadOnlyList<TRequest> Calls => _calls;

    /// <summary>Number of times the handler was called.</summary>
    public int CallCount => _calls.Count;

    /// <summary>Last request received.</summary>
    public TRequest? LastCall => _calls.Count > 0 ? _calls[^1] : default;

    public async ValueTask<CatgaResult<TResponse>> HandleAsync(TRequest request, CancellationToken ct = default)
    {
        _calls.Add(request);
        if (_factory != null) return await _factory(request, ct);
        if (_inner != null) return await _inner.HandleAsync(request, ct);
        return CatgaResult<TResponse>.Failure("No handler configured");
    }
}

/// <summary>
/// Spy wrapper for event handlers.
/// </summary>
public sealed class EventHandlerSpy<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEvent> : IEventHandler<TEvent>
    where TEvent : IEvent
{
    private readonly IEventHandler<TEvent>? _inner;
    private readonly Func<TEvent, CancellationToken, ValueTask>? _action;
    private readonly List<TEvent> _calls = new();

    public EventHandlerSpy(IEventHandler<TEvent>? inner = null) => _inner = inner;

    public EventHandlerSpy(Func<TEvent, CancellationToken, ValueTask> action) => _action = action;

    public IReadOnlyList<TEvent> Calls => _calls;
    public int CallCount => _calls.Count;
    public TEvent? LastCall => _calls.Count > 0 ? _calls[^1] : default;

    public async ValueTask HandleAsync(TEvent @event, CancellationToken ct = default)
    {
        _calls.Add(@event);
        if (_action != null) await _action(@event, ct);
        else if (_inner != null) await _inner.HandleAsync(@event, ct);
    }
}
