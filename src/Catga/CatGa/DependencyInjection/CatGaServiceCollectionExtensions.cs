using Catga.CatGa.Core;
using Catga.CatGa.Models;
using Catga.CatGa.Policies;
using Catga.CatGa.Repository;
using Catga.CatGa.Transport;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// CatGa 依赖注入扩展 - 模块化版本
/// </summary>
public static class CatGaServiceCollectionExtensions
{
    /// <summary>
    /// 添加 CatGa 分布式事务支持（模块化架构）
    /// </summary>
    public static IServiceCollection AddCatGa(
        this IServiceCollection services,
        Action<CatGaOptions>? configureOptions = null)
    {
        var options = new CatGaOptions();
        configureOptions?.Invoke(options);

        // ═══════════════════════════════════════════════════════════
        // 1️⃣ 模型层：注册配置
        // ═══════════════════════════════════════════════════════════
        services.TryAddSingleton(options);

        // ═══════════════════════════════════════════════════════════
        // 2️⃣ 仓储层：注册默认内存仓储
        // ═══════════════════════════════════════════════════════════
        services.TryAddSingleton<ICatGaRepository>(sp =>
        {
            var opts = sp.GetRequiredService<CatGaOptions>();
            return new InMemoryCatGaRepository(
                opts.IdempotencyShardCount,
                opts.IdempotencyExpiry);
        });

        // ═══════════════════════════════════════════════════════════
        // 3️⃣ 传输层：注册默认本地传输
        // ═══════════════════════════════════════════════════════════
        services.TryAddSingleton<ICatGaTransport, LocalCatGaTransport>();

        // ═══════════════════════════════════════════════════════════
        // 4️⃣ 策略层：注册默认策略
        // ═══════════════════════════════════════════════════════════
        services.TryAddSingleton<IRetryPolicy>(sp =>
        {
            var opts = sp.GetRequiredService<CatGaOptions>();
            return new ExponentialBackoffRetryPolicy(
                opts.MaxRetryAttempts,
                opts.InitialRetryDelay,
                opts.MaxRetryDelay,
                opts.UseJitter);
        });

        services.TryAddSingleton<ICompensationPolicy>(sp =>
        {
            var opts = sp.GetRequiredService<CatGaOptions>();
            return new DefaultCompensationPolicy(
                opts.CompensationTimeout,
                throwOnCompensationFailure: false);
        });

        // ═══════════════════════════════════════════════════════════
        // 5️⃣ 核心层：注册执行器
        // ═══════════════════════════════════════════════════════════
        services.TryAddSingleton<ICatGaExecutor, CatGaExecutor>();

        return services;
    }

    /// <summary>
    /// 注册 CatGa 事务处理器
    /// </summary>
    public static IServiceCollection AddCatGaTransaction<TRequest, TResponse, TTransaction>(
        this IServiceCollection services)
        where TTransaction : class, ICatGaTransaction<TRequest, TResponse>
    {
        services.TryAddTransient<ICatGaTransaction<TRequest, TResponse>, TTransaction>();
        return services;
    }

    /// <summary>
    /// 注册 CatGa 事务处理器（无返回值）
    /// </summary>
    public static IServiceCollection AddCatGaTransaction<TRequest, TTransaction>(
        this IServiceCollection services)
        where TTransaction : class, ICatGaTransaction<TRequest>
    {
        services.TryAddTransient<ICatGaTransaction<TRequest>, TTransaction>();
        return services;
    }

    /// <summary>
    /// 使用自定义仓储（替换默认内存仓储）
    /// </summary>
    public static IServiceCollection AddCatGaRepository<TRepository>(
        this IServiceCollection services)
        where TRepository : class, ICatGaRepository
    {
        services.Replace(ServiceDescriptor.Singleton<ICatGaRepository, TRepository>());
        return services;
    }

    /// <summary>
    /// 使用自定义传输（替换默认本地传输）
    /// </summary>
    public static IServiceCollection AddCatGaTransport<TTransport>(
        this IServiceCollection services)
        where TTransport : class, ICatGaTransport
    {
        services.Replace(ServiceDescriptor.Singleton<ICatGaTransport, TTransport>());
        return services;
    }

    /// <summary>
    /// 使用自定义重试策略
    /// </summary>
    public static IServiceCollection AddCatGaRetryPolicy<TPolicy>(
        this IServiceCollection services)
        where TPolicy : class, IRetryPolicy
    {
        services.Replace(ServiceDescriptor.Singleton<IRetryPolicy, TPolicy>());
        return services;
    }

    /// <summary>
    /// 使用自定义补偿策略
    /// </summary>
    public static IServiceCollection AddCatGaCompensationPolicy<TPolicy>(
        this IServiceCollection services)
        where TPolicy : class, ICompensationPolicy
    {
        services.Replace(ServiceDescriptor.Singleton<ICompensationPolicy, TPolicy>());
        return services;
    }
}
