using Catga.Abstractions;
using Catga.DependencyInjection;
using Catga.Flow.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Catga.Testing;

/// <summary>
/// In-process test harness for Catga. Wires up InMemory transport + persistence,
/// captures published messages for assertions.
/// </summary>
public sealed class CatgaTestHarness : IAsyncDisposable
{
    private ServiceProvider? _provider;
    private readonly ServiceCollection _services = new();
    private readonly MessageCapture _capture = new();

    public CatgaTestHarness(Action<IServiceCollection>? configure = null)
    {
        _services.AddLogging();
        _services.AddCatga().UseInMemory().AddFlows();
        _services.AddInMemoryTransport();
        _services.AddSingleton(_capture);
        configure?.Invoke(_services);
    }

    /// <summary>Start the harness (builds the DI container).</summary>
    public CatgaTestHarness Start()
    {
        _provider = _services.BuildServiceProvider();
        return this;
    }

    public ICatgaMediator Mediator => Provider.GetRequiredService<ICatgaMediator>();

    public T GetService<T>() where T : notnull => Provider.GetRequiredService<T>();

    public IReadOnlyList<object> Published => _capture.Published;
    public IReadOnlyList<object> Consumed => _capture.Consumed;

    public IEnumerable<T> PublishedOf<T>() => _capture.Published.OfType<T>();
    public IEnumerable<T> ConsumedOf<T>() => _capture.Consumed.OfType<T>();

    private ServiceProvider Provider => _provider ?? throw new InvalidOperationException("Call Start() first.");

    public async ValueTask DisposeAsync()
    {
        if (_provider != null)
            await _provider.DisposeAsync();
    }
}
