using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Catga.Tests.PropertyTests;

/// <summary>
/// 存储测试抽象基类
/// 提供通用的测试基础设施和服务配置
/// </summary>
/// <typeparam name="TStore">被测试的存储类型</typeparam>
public abstract class StoreTestBase<TStore> : IAsyncLifetime where TStore : class
{
    /// <summary>
    /// 被测试的存储实例
    /// </summary>
    protected TStore Store { get; private set; } = null!;

    /// <summary>
    /// 服务提供者
    /// </summary>
    protected IServiceProvider ServiceProvider { get; private set; } = null!;

    /// <summary>
    /// 服务集合（用于配置）
    /// </summary>
    protected IServiceCollection Services { get; private set; } = null!;

    /// <summary>
    /// 创建存储实例
    /// 子类必须实现此方法来创建具体的存储实例
    /// </summary>
    protected abstract TStore CreateStore(IServiceProvider serviceProvider);

    /// <summary>
    /// 配置服务
    /// 子类可以重写此方法来添加额外的服务配置
    /// </summary>
    protected virtual void ConfigureServices(IServiceCollection services)
    {
        // 默认不添加任何服务
    }

    /// <summary>
    /// 初始化测试环境
    /// </summary>
    public virtual async Task InitializeAsync()
    {
        Services = new ServiceCollection();
        ConfigureServices(Services);
        ServiceProvider = Services.BuildServiceProvider();
        Store = CreateStore(ServiceProvider);
        await OnInitializedAsync();
    }

    /// <summary>
    /// 初始化完成后的回调
    /// 子类可以重写此方法来执行额外的初始化逻辑
    /// </summary>
    protected virtual Task OnInitializedAsync() => Task.CompletedTask;

    /// <summary>
    /// 清理测试环境
    /// </summary>
    public virtual async Task DisposeAsync()
    {
        await OnDisposingAsync();

        if (Store is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
        }
        else if (Store is IDisposable disposable)
        {
            disposable.Dispose();
        }

        if (ServiceProvider is IAsyncDisposable asyncSp)
        {
            await asyncSp.DisposeAsync();
        }
        else if (ServiceProvider is IDisposable sp)
        {
            sp.Dispose();
        }
    }

    /// <summary>
    /// 清理前的回调
    /// 子类可以重写此方法来执行额外的清理逻辑
    /// </summary>
    protected virtual Task OnDisposingAsync() => Task.CompletedTask;
}

/// <summary>
/// 带有后端切换支持的存储测试基类
/// </summary>
/// <typeparam name="TStore">被测试的存储类型</typeparam>
public abstract class BackendStoreTestBase<TStore> : StoreTestBase<TStore> where TStore : class
{
    /// <summary>
    /// 当前测试的后端类型
    /// </summary>
    protected BackendType Backend { get; }

    /// <summary>
    /// 后端测试夹具
    /// </summary>
    protected BackendTestFixture? Fixture { get; private set; }

    protected BackendStoreTestBase(BackendType backend)
    {
        Backend = backend;
    }

    public override async Task InitializeAsync()
    {
        // 如果不是 InMemory 后端，需要初始化容器
        if (Backend != BackendType.InMemory)
        {
            Fixture = new BackendTestFixture(Backend);
            await Fixture.InitializeAsync();
        }

        await base.InitializeAsync();
    }

    public override async Task DisposeAsync()
    {
        await base.DisposeAsync();

        if (Fixture != null)
        {
            await Fixture.DisposeAsync();
        }
    }

    /// <summary>
    /// 获取 Redis 连接字符串（仅 Redis 后端可用）
    /// </summary>
    protected string? GetRedisConnectionString() => Fixture?.RedisConnectionString;

    /// <summary>
    /// 获取 NATS 连接字符串（仅 NATS 后端可用）
    /// </summary>
    protected string? GetNatsConnectionString() => Fixture?.NatsConnectionString;
}
