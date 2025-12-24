using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Catga.Tests.Framework;

/// <summary>
/// 组合测试基类 - 支持两组件组合测试
/// </summary>
/// <typeparam name="TStore1">第一个组件类型</typeparam>
/// <typeparam name="TStore2">第二个组件类型</typeparam>
public abstract class ComponentCombinationTestBase<TStore1, TStore2> : IAsyncLifetime
    where TStore1 : class
    where TStore2 : class
{
    /// <summary>第一个组件实例</summary>
    protected TStore1 Store1 { get; private set; } = null!;

    /// <summary>第二个组件实例</summary>
    protected TStore2 Store2 { get; private set; } = null!;

    /// <summary>服务提供者</summary>
    protected IServiceProvider ServiceProvider { get; private set; } = null!;

    /// <summary>服务集合</summary>
    protected IServiceCollection Services { get; private set; } = null!;

    /// <summary>
    /// 创建组件实例
    /// 子类必须实现此方法来创建具体的组件实例
    /// </summary>
    protected abstract (TStore1, TStore2) CreateStores(IServiceProvider sp);

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
        (Store1, Store2) = CreateStores(ServiceProvider);
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

        // 清理 Store1
        if (Store1 is IAsyncDisposable asyncDisposable1)
            await asyncDisposable1.DisposeAsync();
        else if (Store1 is IDisposable disposable1)
            disposable1.Dispose();

        // 清理 Store2
        if (Store2 is IAsyncDisposable asyncDisposable2)
            await asyncDisposable2.DisposeAsync();
        else if (Store2 is IDisposable disposable2)
            disposable2.Dispose();

        // 清理 ServiceProvider
        if (ServiceProvider is IAsyncDisposable asyncSp)
            await asyncSp.DisposeAsync();
        else if (ServiceProvider is IDisposable sp)
            sp.Dispose();
    }

    /// <summary>
    /// 清理前的回调
    /// 子类可以重写此方法来执行额外的清理逻辑
    /// </summary>
    protected virtual Task OnDisposingAsync() => Task.CompletedTask;
}

/// <summary>
/// 组合测试基类 - 支持三组件组合测试
/// </summary>
/// <typeparam name="TStore1">第一个组件类型</typeparam>
/// <typeparam name="TStore2">第二个组件类型</typeparam>
/// <typeparam name="TStore3">第三个组件类型</typeparam>
public abstract class ComponentCombinationTestBase<TStore1, TStore2, TStore3> : IAsyncLifetime
    where TStore1 : class
    where TStore2 : class
    where TStore3 : class
{
    /// <summary>第一个组件实例</summary>
    protected TStore1 Store1 { get; private set; } = null!;

    /// <summary>第二个组件实例</summary>
    protected TStore2 Store2 { get; private set; } = null!;

    /// <summary>第三个组件实例</summary>
    protected TStore3 Store3 { get; private set; } = null!;

    /// <summary>服务提供者</summary>
    protected IServiceProvider ServiceProvider { get; private set; } = null!;

    /// <summary>服务集合</summary>
    protected IServiceCollection Services { get; private set; } = null!;

    /// <summary>
    /// 创建组件实例
    /// 子类必须实现此方法来创建具体的组件实例
    /// </summary>
    protected abstract (TStore1, TStore2, TStore3) CreateStores(IServiceProvider sp);

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
        (Store1, Store2, Store3) = CreateStores(ServiceProvider);
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

        // 清理 Store1
        if (Store1 is IAsyncDisposable asyncDisposable1)
            await asyncDisposable1.DisposeAsync();
        else if (Store1 is IDisposable disposable1)
            disposable1.Dispose();

        // 清理 Store2
        if (Store2 is IAsyncDisposable asyncDisposable2)
            await asyncDisposable2.DisposeAsync();
        else if (Store2 is IDisposable disposable2)
            disposable2.Dispose();

        // 清理 Store3
        if (Store3 is IAsyncDisposable asyncDisposable3)
            await asyncDisposable3.DisposeAsync();
        else if (Store3 is IDisposable disposable3)
            disposable3.Dispose();

        // 清理 ServiceProvider
        if (ServiceProvider is IAsyncDisposable asyncSp)
            await asyncSp.DisposeAsync();
        else if (ServiceProvider is IDisposable sp)
            sp.Dispose();
    }

    /// <summary>
    /// 清理前的回调
    /// 子类可以重写此方法来执行额外的清理逻辑
    /// </summary>
    protected virtual Task OnDisposingAsync() => Task.CompletedTask;
}
