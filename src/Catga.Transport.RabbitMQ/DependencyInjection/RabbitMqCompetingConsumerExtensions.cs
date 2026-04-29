using System.Diagnostics.CodeAnalysis;
using Catga.Abstractions;
using Catga.DeadLetter;
using Catga.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Catga.Transport.RabbitMQ.DependencyInjection;

public static class RabbitMqCompetingConsumerExtensions
{
    /// <summary>
    /// Register a RabbitMQ competing consumer backed by a shared queue.
    /// Multiple app instances using the same queue name compete for messages.
    /// </summary>
    public static IServiceCollection AddRabbitMqCompetingConsumer<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(
        this IServiceCollection services,
        string queueName,
        string? routingKey = null,
        Action<CompetingConsumerOptions>? configure = null)
        where TMessage : class
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);

        var options = new CompetingConsumerOptions();
        configure?.Invoke(options);

        services.TryAddSingleton<ICompetingConsumer<TMessage>>(sp =>
        {
            var transportOptions = sp.GetService<RabbitMqTransportOptions>() ?? new RabbitMqTransportOptions();
            var resolvedQueueName = ResolveName(queueName, transportOptions.Prefix);
            var resolvedRoutingKey = ResolveName(routingKey ?? queueName, transportOptions.Prefix);

            if (options.GroupName == "default")
                options.GroupName = resolvedQueueName;

            return new RabbitMqCompetingConsumer<TMessage>(
                sp.GetRequiredService<IMessageSerializer>(),
                resolvedQueueName,
                resolvedRoutingKey,
                transportOptions,
                options,
                sp.GetService<IDeadLetterQueue>(),
                sp.GetService<ILogger<RabbitMqCompetingConsumer<TMessage>>>());
        });

        return services;
    }

    private static string ResolveName(string name, string prefix)
    {
        var normalizedPrefix = NormalizePrefix(prefix);
        return name.StartsWith(normalizedPrefix, StringComparison.Ordinal)
            ? name
            : $"{normalizedPrefix}{name.TrimStart('.')}";
    }

    private static string NormalizePrefix(string? prefix)
    {
        var effective = string.IsNullOrWhiteSpace(prefix) ? "catga." : prefix.Trim();
        return effective.EndsWith(".", StringComparison.Ordinal) ? effective : $"{effective}.";
    }
}
