using Catga.Core;
using Catga.DependencyInjection;
using Catga.InMemory;
using Catga.Serialization.MemoryPack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Catga.Tests.Integration;

/// <summary>
/// Base fixture for integration tests, providing DI container and common services
/// </summary>
public class IntegrationTestFixture : IDisposable
{
    public IServiceProvider ServiceProvider { get; private set; }
    public ICatgaMediator Mediator { get; private set; }
    
    private ServiceCollection? _services;
    private bool _disposed;

    public IntegrationTestFixture()
    {
        _services = new ServiceCollection();
        ConfigureServices(_services);
        ServiceProvider = _services.BuildServiceProvider();
        Mediator = ServiceProvider.GetRequiredService<ICatgaMediator>();
    }

    protected virtual void ConfigureServices(IServiceCollection services)
    {
        // Add logging
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Debug);
        });

        // Add Catga with InMemory transport
        services.AddCatga()
            .UseMemoryPack()
            .ForDevelopment();

        services.AddInMemoryTransport();
    }

    public T GetService<T>() where T : notnull
    {
        return ServiceProvider.GetRequiredService<T>();
    }

    public T? GetOptionalService<T>() where T : class
    {
        return ServiceProvider.GetService<T>();
    }

    public IServiceScope CreateScope()
    {
        return ServiceProvider.CreateScope();
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
        {
            (ServiceProvider as IDisposable)?.Dispose();
        }

        _disposed = true;
    }
}

/// <summary>
/// Test fixture with custom service configuration
/// </summary>
public class CustomIntegrationTestFixture : IntegrationTestFixture
{
    private readonly Action<IServiceCollection>? _configureServices;

    public CustomIntegrationTestFixture(Action<IServiceCollection>? configureServices = null)
    {
        _configureServices = configureServices;
    }

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        _configureServices?.Invoke(services);
    }
}

