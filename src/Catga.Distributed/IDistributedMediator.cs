using Catga.Messages;

namespace Catga.Distributed;

/// <summary>
/// Distributed Mediator for cross-service communication
/// <para>
/// Implementation Note:
/// - Use Catga.Transport.Nats or Catga.Transport.Redis for message transport
/// - Service discovery: Kubernetes DNS, Consul, or .NET Aspire
/// - Load balancing: K8s Service, NATS Consumer Groups, or Redis Streams
/// </para>
/// <para>
/// Example (Kubernetes + NATS):
/// <code>
/// services.AddSingleton&lt;INatsConnection&gt;(...);
/// services.AddSingleton&lt;IMessageTransport, NatsMessageTransport&gt;();
/// services.AddSingleton&lt;IDistributedMediator&gt;(sp =>
///     new DistributedMediator(
///         sp.GetRequiredService&lt;ICatgaMediator&gt;(),
///         sp.GetRequiredService&lt;IMessageTransport&gt;(),
///         sp.GetRequiredService&lt;ILogger&lt;DistributedMediator&gt;&gt;()
///     )
/// );
/// </code>
/// </para>
/// </summary>
public interface IDistributedMediator : ICatgaMediator
{
    // Inherits all CQRS methods from ICatgaMediator
    // Distribution handled by IMessageTransport implementation
}
