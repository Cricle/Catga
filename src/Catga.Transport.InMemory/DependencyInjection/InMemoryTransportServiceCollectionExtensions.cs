using Catga.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Catga.Abstractions;
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

        // 注册 Transport (InMemoryIdempotencyStore 是内部实现)
        services.TryAddSingleton<IMessageTransport>(sp =>
        {
            var logger = sp.GetService<ILogger<InMemoryMessageTransport>>();
            var global = sp.GetRequiredService<Catga.Configuration.CatgaOptions>();
            return new InMemoryMessageTransport(new InMemoryTransportOptions(), logger, global);
        });

        return services;
    }
}

