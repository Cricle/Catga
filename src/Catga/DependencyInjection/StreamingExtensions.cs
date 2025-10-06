using Catga.Streaming;
using Microsoft.Extensions.DependencyInjection;

namespace Catga.DependencyInjection;

/// <summary>
/// 流处理扩展方法
/// </summary>
public static class StreamingExtensions
{
    /// <summary>
    /// 添加流处理支持
    /// </summary>
    public static IServiceCollection AddStreamProcessing(this IServiceCollection services)
    {
        // 流处理是无状态的，不需要注册服务
        // 用户可以直接使用 StreamProcessor.From() 静态方法
        return services;
    }

    /// <summary>
    /// 添加流处理器（泛型）
    /// </summary>
    public static IServiceCollection AddStreamProcessor<TInput, TOutput, TProcessor>(this IServiceCollection services)
        where TProcessor : class, IStreamProcessor<TInput, TOutput>
    {
        services.AddSingleton<IStreamProcessor<TInput, TOutput>, TProcessor>();
        return services;
    }

    /// <summary>
    /// 添加流源
    /// </summary>
    public static IServiceCollection AddStreamSource<T, TSource>(this IServiceCollection services)
        where TSource : class, IStreamSource<T>
    {
        services.AddSingleton<IStreamSource<T>, TSource>();
        return services;
    }

    /// <summary>
    /// 添加流汇
    /// </summary>
    public static IServiceCollection AddStreamSink<T, TSink>(this IServiceCollection services)
        where TSink : class, IStreamSink<T>
    {
        services.AddSingleton<IStreamSink<T>, TSink>();
        return services;
    }
}

