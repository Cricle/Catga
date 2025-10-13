using Catga.Messages;

namespace Catga.Distributed;

/// <summary>
/// Distributed Mediator using message transport (NATS/Redis)
/// <para>
/// Simplified design philosophy:
/// - Application layer: CQRS message dispatch
/// - Infrastructure layer: Distribution, load balancing, high availability
/// - Service discovery: K8s/Consul/Aspire (not application concern)
/// </para>
/// <para>
/// Benefits:
/// - NATS JetStream: Built-in clustering, consumer groups, load balancing
/// - Redis Cluster/Sentinel: Built-in sharding, replication, high availability
/// - K8s Service Discovery: DNS-based service resolution
/// </para>
/// </summary>
public interface IDistributedMediator : ICatgaMediator
{
    // All distribution happens through message transport automatically
    // No need for explicit node management or routing strategies
}
