using Catga.Abstractions;
using Catga.Configuration;
using Catga.Hosting;
using Catga.Resilience;
using Catga.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Catga.Transport.RabbitMQ.DependencyInjection;

public static class RabbitMqTransportServiceCollectionExtensions
{
    /// <summary>
    /// Add RabbitMQ transport.
    /// </summary>
    public static IServiceCollection AddRabbitMqTransport(
        this IServiceCollection services,
        Action<RabbitMqTransportOptions>? configure = null)
    {
        var options = new RabbitMqTransportOptions();
        configure?.Invoke(options);

        services.TryAddSingleton(options);
        services.TryAddSingleton<IMessageTransport>(sp =>
        {
            var catgaOptions = sp.GetRequiredService<CatgaOptions>();
            if (options.EndpointNaming is null && catgaOptions.EndpointNamingConvention is not null)
                options.EndpointNaming = catgaOptions.EndpointNamingConvention;

            return new RabbitMqMessageTransport(
                sp.GetRequiredService<IMessageSerializer>(),
                sp.GetRequiredService<IResiliencePipelineProvider>(),
                options,
                sp.GetService<ILogger<RabbitMqMessageTransport>>());
        });

        return services;
    }

    /// <summary>
    /// Add RabbitMQ transport with connection string.
    /// </summary>
    public static IServiceCollection AddRabbitMqTransport(
        this IServiceCollection services,
        string connectionString,
        Action<RabbitMqTransportOptions>? configure = null)
        => services.AddRabbitMqTransport(opt =>
        {
            opt.Uri = connectionString;
            configure?.Invoke(opt);
        });
}
