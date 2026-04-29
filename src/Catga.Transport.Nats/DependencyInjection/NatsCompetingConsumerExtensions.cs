using System.Diagnostics.CodeAnalysis;
using Catga.Abstractions;
using Catga.DeadLetter;
using Catga.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;

namespace Catga.Transport.Nats.DependencyInjection;

public static class NatsCompetingConsumerExtensions
{
    /// <summary>
    /// Register a NATS JetStream competing consumer.
    /// Multiple app instances with the same GroupName compete for messages.
    /// </summary>
    public static IServiceCollection AddNatsCompetingConsumer<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(
        this IServiceCollection services,
        string subject,
        string? streamName = null,
        Action<CompetingConsumerOptions>? configure = null)
        where TMessage : class
    {
        var options = new CompetingConsumerOptions();
        configure?.Invoke(options);

        services.TryAddSingleton<ICompetingConsumer<TMessage>>(sp =>
        {
            var transportOptions = sp.GetService<NatsTransportOptions>() ?? new NatsTransportOptions();
            var resolvedSubject = ResolveSubject(subject, transportOptions.SubjectPrefix);

            return new NatsCompetingConsumer<TMessage>(
                sp.GetRequiredService<INatsConnection>(),
                sp.GetRequiredService<IMessageSerializer>(),
                resolvedSubject,
                streamName,
                options,
                sp.GetService<IDeadLetterQueue>(),
                sp.GetService<ILogger<NatsCompetingConsumer<TMessage>>>());
        });

        return services;
    }

    private static string ResolveSubject(string subject, string? prefix)
    {
        var normalizedPrefix = NormalizePrefix(prefix);
        return subject.StartsWith(normalizedPrefix, StringComparison.Ordinal)
            ? subject
            : $"{normalizedPrefix}{subject.TrimStart('.')}";
    }

    private static string NormalizePrefix(string? prefix)
    {
        var effective = string.IsNullOrWhiteSpace(prefix) ? "catga." : prefix.Trim();
        return effective.EndsWith(".", StringComparison.Ordinal)
            ? effective
            : $"{effective}.";
    }
}
