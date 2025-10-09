using System.Diagnostics.CodeAnalysis;
using Catga.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Catga.DependencyInjection;

/// <summary>
/// Transport layer service registration extensions
/// </summary>
public static class TransportServiceCollectionExtensions
{
    /// <summary>
    /// Add message transport service
    /// </summary>
    public static IServiceCollection AddMessageTransport<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TTransport>(
        this IServiceCollection services)
        where TTransport : class, IMessageTransport
    {
        services.TryAddSingleton<IMessageTransport, TTransport>();
        return services;
    }

    /// <summary>
    /// Add in-memory transport (for testing and local development)
    /// </summary>
    public static IServiceCollection AddInMemoryTransport(
        this IServiceCollection services)
    {
        return services.AddMessageTransport<InMemoryMessageTransport>();
    }
}

