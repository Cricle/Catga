using Catga.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Catga.DependencyInjection;

/// <summary>
/// 配置中心扩展方法
/// </summary>
public static class ConfigurationCenterExtensions
{
    /// <summary>
    /// 添加内存配置中心（用于测试）
    /// </summary>
    public static IServiceCollection AddMemoryConfigurationCenter(this IServiceCollection services)
    {
        services.TryAddSingleton<IConfigurationCenter, MemoryConfigurationCenter>();
        return services;
    }
}

