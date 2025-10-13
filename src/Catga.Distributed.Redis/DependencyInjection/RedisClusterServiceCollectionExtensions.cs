using Catga.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Catga.Distributed.Redis.DependencyInjection;

/// <summary>
/// Redis distributed messaging extensions
/// <para>
/// Relies on Redis for:
/// - Redis Cluster: Automatic sharding and high availability
/// - Redis Sentinel: Master-slave replication and failover
/// - Streams/Pub-Sub: Message delivery and consumer groups
/// </para>
/// <para>
/// Service discovery: Use K8s DNS, Consul, or Aspire
/// </para>
/// </summary>
public static class RedisClusterServiceCollectionExtensions
{
    public static IServiceCollection AddRedisDistributed(this IServiceCollection services, string connectionString)
    {
        services.AddSingleton<IConnectionMultiplexer>(ConnectionMultiplexer.Connect(connectionString));

        services.AddSingleton<IMessageTransport>(sp =>
        {
            var connection = sp.GetRequiredService<IConnectionMultiplexer>();
            var logger = sp.GetRequiredService<ILogger<RedisStreamTransport>>();
            var options = new RedisStreamOptions
            {
                StreamKey = "catga:messages",
                ConsumerGroup = "catga-group",
                ConsumerId = Guid.NewGuid().ToString()
            };
            return new RedisStreamTransport(connection, logger, options);
        });

        services.AddSingleton<IDistributedMediator, DistributedMediator>();

        return services;
    }
}
