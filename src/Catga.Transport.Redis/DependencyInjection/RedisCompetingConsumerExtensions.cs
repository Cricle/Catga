using System.Diagnostics.CodeAnalysis;
using Catga.Abstractions;
using Catga.DeadLetter;
using Catga.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Catga.Transport.Redis.DependencyInjection;

public static class RedisCompetingConsumerExtensions
{
    /// <summary>
    /// Register a Redis Streams competing consumer.
    /// Multiple app instances with the same GroupName compete for messages.
    /// </summary>
    public static IServiceCollection AddRedisCompetingConsumer<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(
        this IServiceCollection services,
        string streamKey,
        Action<CompetingConsumerOptions>? configure = null)
        where TMessage : class
    {
        var options = new CompetingConsumerOptions();
        configure?.Invoke(options);

        services.TryAddSingleton<ICompetingConsumer<TMessage>>(sp =>
        {
            var transportOptions = sp.GetService<RedisTransportOptions>() ?? new RedisTransportOptions();
            var resolvedStreamKey = ResolveStreamKey(streamKey, transportOptions.ChannelPrefix);

            return new RedisCompetingConsumer<TMessage>(
                sp.GetRequiredService<IConnectionMultiplexer>(),
                sp.GetRequiredService<IMessageSerializer>(),
                resolvedStreamKey,
                options,
                sp.GetService<IDeadLetterQueue>(),
                sp.GetService<ILogger<RedisCompetingConsumer<TMessage>>>());
        });

        return services;
    }

    private static string ResolveStreamKey(string streamKey, string? prefix)
    {
        var normalizedPrefix = NormalizePrefix(prefix);
        var rawDestination = streamKey.StartsWith("stream:", StringComparison.OrdinalIgnoreCase)
            ? streamKey["stream:".Length..]
            : streamKey;
        var trimmedDestination = rawDestination.TrimStart(':', '.');
        var resolvedDestination = trimmedDestination.StartsWith(normalizedPrefix, StringComparison.Ordinal)
            ? trimmedDestination
            : $"{normalizedPrefix}{trimmedDestination}";

        return $"stream:{resolvedDestination}";
    }

    private static string NormalizePrefix(string? prefix)
    {
        var effective = string.IsNullOrWhiteSpace(prefix) ? "catga." : prefix.Trim();
        return effective.EndsWith(".", StringComparison.Ordinal) ? effective : $"{effective}.";
    }
}
