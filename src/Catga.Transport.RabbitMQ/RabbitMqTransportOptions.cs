namespace Catga.Transport.RabbitMQ;

/// <summary>
/// Configuration options for RabbitMQ transport.
/// </summary>
public sealed class RabbitMqTransportOptions
{
    /// <summary>RabbitMQ connection URI. Default: amqp://guest:guest@localhost:5672/</summary>
    public string Uri { get; set; } = "amqp://guest:guest@localhost:5672/";

    /// <summary>Exchange name. Default: catga (topic exchange)</summary>
    public string Exchange { get; set; } = "catga";

    /// <summary>Exchange type. Default: topic</summary>
    public string ExchangeType { get; set; } = "topic";

    /// <summary>
    /// When enabled, Catga declares the exchange as RabbitMQ delayed-message exchange
    /// (`x-delayed-message`) and uses <see cref="ExchangeType"/> as the underlying routing semantics.
    /// Requires the RabbitMQ delayed message exchange plugin.
    /// </summary>
    public bool UseDelayedExchange { get; set; }

    /// <summary>Whether to declare exchange on startup. Default: true</summary>
    public bool DeclareExchange { get; set; } = true;

    /// <summary>Whether exchange is durable. Default: true</summary>
    public bool DurableExchange { get; set; } = true;

    /// <summary>Subject/routing-key prefix. Default: catga.</summary>
    public string Prefix { get; set; } = "catga.";

    /// <summary>Default message TTL in milliseconds. Null = no TTL.</summary>
    public int? MessageTtlMs { get; set; }

    /// <summary>
    /// Optional max queue priority for declared queues. When set, Catga declares RabbitMQ priority queues
    /// and maps transport metadata key <c>x-priority</c> onto the broker-native message priority field.
    /// </summary>
    public byte? MaxPriority { get; set; }

    /// <summary>Consumer prefetch count (QoS). Default: 10</summary>
    public ushort PrefetchCount { get; set; } = 10;

    /// <summary>Whether queues are durable. Default: true</summary>
    public bool DurableQueues { get; set; } = true;

    /// <summary>Whether queues auto-delete when no consumers. Default: false</summary>
    public bool AutoDeleteQueues { get; set; } = false;

    /// <summary>Request/Reply timeout. Default: 30s</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Custom endpoint naming convention. Null = use type name.</summary>
    public Func<Type, string>? EndpointNaming { get; set; }
}
