using System.Diagnostics.CodeAnalysis;
using Catga.Abstractions;
using Catga.Core;
using Catga.Flow;
using Catga.Flow.Dsl;
using Catga.Persistence.InMemory.Flow;
using Microsoft.Extensions.DependencyInjection;

namespace Catga.Testing;

/// <summary>
/// Lightweight context for testing a single Flow DSL workflow in isolation.
/// </summary>
public sealed class FlowTestContext<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TState,
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TConfig> : IAsyncDisposable
    where TState : class, IFlowState, new()
    where TConfig : FlowConfig<TState>, new()
{
    private readonly ServiceProvider _provider;
    private readonly MockMediator _mockMediator;

    public FlowTestContext(Action<IServiceCollection>? configure = null)
    {
        _mockMediator = new MockMediator();
        var services = new ServiceCollection();

        services.AddSingleton<ICatgaMediator>(_mockMediator);
        services.AddSingleton<IDslFlowStore, InMemoryDslFlowStore>();
        services.AddSingleton<TConfig>();
        services.AddTransient<DslFlowExecutor<TState, TConfig>>();

        configure?.Invoke(services);
        _provider = services.BuildServiceProvider();
    }

    public MockMediator Mediator => _mockMediator;

    public async Task<DslFlowResult<TState>> RunAsync(TState initialState, CancellationToken ct = default)
    {
        var executor = _provider.GetRequiredService<DslFlowExecutor<TState, TConfig>>();
        return await executor.RunAsync(initialState, ct);
    }

    public async Task<DslFlowResult<TState>> ResumeAsync(string flowId, CancellationToken ct = default)
    {
        var executor = _provider.GetRequiredService<DslFlowExecutor<TState, TConfig>>();
        return await executor.ResumeAsync(flowId, ct);
    }

    public async Task<FlowSnapshot<TState>?> GetStateAsync(string flowId, CancellationToken ct = default)
    {
        var executor = _provider.GetRequiredService<DslFlowExecutor<TState, TConfig>>();
        return await executor.GetAsync(flowId, ct);
    }

    public ValueTask DisposeAsync() => _provider.DisposeAsync();
}

/// <summary>
/// Mock mediator for Flow testing - configure per-type responses.
/// </summary>
public sealed class MockMediator : ICatgaMediator
{
    private readonly Dictionary<Type, Func<object, CancellationToken, ValueTask<object?>>> _handlers = new();
    private readonly List<object> _sent = new();
    private readonly List<object> _published = new();

    public IReadOnlyList<object> Sent => _sent;
    public IReadOnlyList<object> Published => _published;

    public MockMediator OnSend<TRequest, TResponse>(Func<TRequest, TResponse> handler)
        where TRequest : IRequest<TResponse>
    {
        _handlers[typeof(TRequest)] = (req, _) =>
            ValueTask.FromResult<object?>(CatgaResult<TResponse>.Success(handler((TRequest)req)));
        return this;
    }

    public MockMediator OnSendFail<TRequest, TResponse>(string error)
        where TRequest : IRequest<TResponse>
    {
        _handlers[typeof(TRequest)] = (_, _) =>
            ValueTask.FromResult<object?>(CatgaResult<TResponse>.Failure(error));
        return this;
    }

    public async ValueTask<CatgaResult<TResponse>> SendAsync<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse>(
        TRequest request, CancellationToken ct = default)
        where TRequest : IRequest<TResponse>
    {
        _sent.Add(request!);
        if (_handlers.TryGetValue(typeof(TRequest), out var handler))
        {
            var result = await handler(request!, ct);
            return result is CatgaResult<TResponse> typed ? typed : CatgaResult<TResponse>.Failure("Invalid mock response");
        }
        return CatgaResult<TResponse>.Failure($"No mock configured for {typeof(TRequest).Name}");
    }

    public ValueTask<CatgaResult> SendAsync<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest>(
        TRequest request, CancellationToken ct = default)
        where TRequest : IRequest
    {
        _sent.Add(request!);
        return ValueTask.FromResult(CatgaResult.Success());
    }

    public Task PublishAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEvent>(
        TEvent @event, CancellationToken ct = default)
        where TEvent : IEvent
    {
        _published.Add(@event!);
        return Task.CompletedTask;
    }

    public ValueTask<IReadOnlyList<CatgaResult<TResponse>>> SendBatchAsync<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse>(
        IReadOnlyList<TRequest> requests, CancellationToken ct = default)
        where TRequest : IRequest<TResponse>
        => ValueTask.FromResult<IReadOnlyList<CatgaResult<TResponse>>>(Array.Empty<CatgaResult<TResponse>>());

    public IAsyncEnumerable<CatgaResult<TResponse>> SendStreamAsync<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse>(
        IAsyncEnumerable<TRequest> requests, CancellationToken ct = default)
        where TRequest : IRequest<TResponse>
        => AsyncEnumerableEmpty<CatgaResult<TResponse>>();

    private static async IAsyncEnumerable<T> AsyncEnumerableEmpty<T>()
    {
        await Task.CompletedTask;
        yield break;
    }

    public Task PublishBatchAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEvent>(
        IReadOnlyList<TEvent> events, CancellationToken ct = default)
        where TEvent : IEvent
    {
        foreach (var e in events) _published.Add(e!);
        return Task.CompletedTask;
    }

    public void Dispose() { }
}
