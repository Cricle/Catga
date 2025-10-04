using Catga.Nats;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// NATS CatGa 依赖注入扩展
/// </summary>
public static class NatsCatGaServiceCollectionExtensions
{
    /// <summary>
    /// 添加 NATS CatGa 传输支持
    /// </summary>
    public static IServiceCollection AddNatsCatGaTransport(
        this IServiceCollection services,
        string? natsUrl = null,
        string? serviceId = null)
    {
        // 确保 NATS 连接已注册
        services.TryAddSingleton<INatsConnection>(sp =>
        {
            var url = natsUrl ?? "nats://localhost:4222";
            var logger = sp.GetRequiredService<ILogger<INatsConnection>>();
            logger.LogInformation("Connecting to NATS at {Url}", url);

            var options = new NatsOpts { Url = url };
            return new NatsConnection(options);
        });

        // 注册 NATS CatGa Transport
        services.TryAddSingleton(sp =>
        {
            var connection = sp.GetRequiredService<INatsConnection>();
            var logger = sp.GetRequiredService<ILogger<NatsCatGaTransport>>();
            return new NatsCatGaTransport(connection, logger, serviceId);
        });

        return services;
    }
}

