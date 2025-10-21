using Catga.Abstractions;
using Catga.Core;
using Catga.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Catga.Testing;

/// <summary>
/// Catga 测试固件基类 - 简化测试设置
/// </summary>
public class CatgaTestFixture : IDisposable
{
    private readonly ServiceCollection _services;
    private ServiceProvider? _serviceProvider;

    public CatgaTestFixture()
    {
        _services = new ServiceCollection();
        ConfigureDefaultServices();
    }

    /// <summary>
    /// 服务提供者
    /// </summary>
    public IServiceProvider ServiceProvider
    {
        get
        {
            if (_serviceProvider == null)
            {
                _serviceProvider = _services.BuildServiceProvider();
            }
            return _serviceProvider;
        }
    }

    /// <summary>
    /// Mediator 实例
    /// </summary>
    public ICatgaMediator Mediator => ServiceProvider.GetRequiredService<ICatgaMediator>();

    /// <summary>
    /// 配置默认服务
    /// </summary>
    protected virtual void ConfigureDefaultServices()
    {
        // 添加 Catga 核心服务
        _services.AddCatga();

        // 添加内存传输（测试用）
        _services.AddSingleton<IMessageTransport, InMemoryMessageTransport>();

        // 添加日志（可选）
        _services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Warning); // 测试时减少日志噪音
        });
    }

    /// <summary>
    /// 添加自定义服务
    /// </summary>
    public CatgaTestFixture ConfigureServices(Action<IServiceCollection> configure)
    {
        if (_serviceProvider != null)
        {
            throw new InvalidOperationException("Cannot configure services after ServiceProvider has been built.");
        }

        configure(_services);
        return this;
    }

    /// <summary>
    /// 注册 Handler
    /// </summary>
    public CatgaTestFixture RegisterHandler<THandler>() where THandler : class
    {
        _services.AddScoped(typeof(THandler));
        return this;
    }

    /// <summary>
    /// 注册请求 Handler
    /// </summary>
    public CatgaTestFixture RegisterRequestHandler<TRequest, TResponse, THandler>()
        where TRequest : IRequest<TResponse>
        where THandler : class, IRequestHandler<TRequest, TResponse>
    {
        _services.AddScoped<IRequestHandler<TRequest, TResponse>, THandler>();
        return this;
    }

    /// <summary>
    /// 注册事件 Handler
    /// </summary>
    public CatgaTestFixture RegisterEventHandler<TEvent, THandler>()
        where TEvent : IEvent
        where THandler : class, IEventHandler<TEvent>
    {
        _services.AddScoped<IEventHandler<TEvent>, THandler>();
        return this;
    }

    /// <summary>
    /// 获取服务
    /// </summary>
    public T GetService<T>() where T : notnull
    {
        return ServiceProvider.GetRequiredService<T>();
    }

    /// <summary>
    /// 尝试获取服务
    /// </summary>
    public T? TryGetService<T>() where T : class
    {
        return ServiceProvider.GetService<T>();
    }

    public void Dispose()
    {
        _serviceProvider?.Dispose();
        GC.SuppressFinalize(this);
    }
}

