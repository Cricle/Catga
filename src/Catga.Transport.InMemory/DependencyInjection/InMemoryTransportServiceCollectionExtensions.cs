using Catga.Configuration;
using Catga.Resilience;
using Catga.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Catga;

/// <summary>DI extensions for InMemory Transport</summary>
public static class InMemoryTransportServiceCollectionExtensions
{
    /// <summary>
    /// 注册 InMemory Transport (用于开发/测试)
    /// </summary>
    public static IServiceCollection AddInMemoryTransport(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IMessageTransport>(sp =>
        {
            var logger = sp.GetService<ILogger<InMemoryMessageTransport>>();
            var global = sp.GetRequiredService<CatgaOptions>();
            var provider = sp.GetRequiredService<IResiliencePipelineProvider>();
            return new InMemoryMessageTransport(logger, provider, global);
        });
        return services;
    }
}
