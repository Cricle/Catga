using System.Diagnostics.CodeAnalysis;
using Catga.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Catga.DependencyInjection;

/// <summary>Transport layer service registration extensions</summary>
public static class TransportServiceCollectionExtensions
{
    public static IServiceCollection AddMessageTransport<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TTransport>(this IServiceCollection services) where TTransport : class, IMessageTransport
    {
        services.TryAddSingleton<IMessageTransport, TTransport>();
        return services;
    }

    public static IServiceCollection AddInMemoryTransport(this IServiceCollection services)
        => services.AddMessageTransport<InMemoryMessageTransport>();
}

