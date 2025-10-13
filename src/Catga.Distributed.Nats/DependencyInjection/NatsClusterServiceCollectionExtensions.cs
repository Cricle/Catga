using Catga.Transport;
using Catga.Transport.Nats;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;

namespace Catga.Distributed.Nats.DependencyInjection;

/// <summary>
/// NATS distributed messaging extensions
/// <para>
/// Relies on NATS JetStream for:
/// - Clustering and high availability
/// - Consumer groups for load balancing
/// - Message persistence and replay
/// - At-least-once/exactly-once delivery
/// </para>
/// <para>
/// Service discovery: Use K8s DNS, Consul, or Aspire
/// </para>
/// </summary>
public static class NatsClusterServiceCollectionExtensions
{
    public static IServiceCollection AddNatsDistributed(this IServiceCollection services, string natsUrl, bool useJetStream = true)
    {
        services.AddSingleton<INatsConnection>(sp =>
        {
            var opts = NatsOpts.Default with { Url = natsUrl };
            return new NatsConnection(opts);
        });

        services.AddSingleton<IMessageTransport>(sp =>
        {
            var connection = sp.GetRequiredService<INatsConnection>();
            var serializer = sp.GetRequiredService<Catga.Serialization.IMessageSerializer>();
            var logger = sp.GetRequiredService<ILogger<NatsMessageTransport>>();
            // Use default options (JetStream enabled by default)
            return new NatsMessageTransport(connection, serializer, logger);
        });

        services.AddSingleton<IDistributedMediator, DistributedMediator>();

        return services;
    }
}
