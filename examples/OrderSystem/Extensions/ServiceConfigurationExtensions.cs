using System.Text.Json.Serialization;
using Catga;
using Catga.DependencyInjection;
using OrderSystem.Configuration;
using OrderSystem.Models;

namespace OrderSystem.Extensions;

public static class ServiceConfigurationExtensions
{
    public static void ConfigureCatgaServices(
        this IServiceCollection services,
        string transport,
        string persistence,
        string redisConn,
        string natsUrl,
        IHostEnvironment environment)
    {
        // Configure Catga with MemoryPack serialization
        var catga = services.AddCatga().UseMemoryPack();

        // Configure persistence backend
        switch (persistence.ToLower())
        {
            case "redis":
                catga.UseRedis(redisConn);
                Console.WriteLine($"✓ Persistence: Redis ({redisConn})");
                break;
            case "nats":
                services.AddNatsConnection(natsUrl);
                catga.UseNats();
                Console.WriteLine($"✓ Persistence: NATS ({natsUrl})");
                break;
            default:
                catga.UseInMemory();
                Console.WriteLine("✓ Persistence: InMemory");
                break;
        }

        // Configure transport backend
        switch (transport.ToLower())
        {
            case "redis":
                services.AddRedisTransport(redisConn);
                Console.WriteLine($"✓ Transport: Redis ({redisConn})");
                break;
            case "nats":
                services.AddNatsTransport(natsUrl);
                Console.WriteLine($"✓ Transport: NATS ({natsUrl})");
                break;
            default:
                services.AddInMemoryTransport();
                Console.WriteLine("✓ Transport: InMemory");
                break;
        }

        // Enable hosted services for lifecycle management
        catga.AddHostedServices(options =>
        {
            if (environment.IsProduction())
            {
                options.Recovery.CheckInterval = TimeSpan.FromMinutes(2);
                options.OutboxProcessor.ScanInterval = TimeSpan.FromSeconds(5);
                options.ShutdownTimeout = TimeSpan.FromSeconds(60);
            }
            else
            {
                options.Recovery.CheckInterval = TimeSpan.FromSeconds(30);
                options.OutboxProcessor.ScanInterval = TimeSpan.FromSeconds(2);
                options.ShutdownTimeout = TimeSpan.FromSeconds(30);
            }
        });
        Console.WriteLine("✓ Hosted Services: Enabled (Recovery, Transport, Outbox)");

        // Add health checks
        services.AddHealthChecks().AddCatgaHealthChecks();
        Console.WriteLine("✓ Health Checks: Enabled");

        // Register handlers and services
        services.AddCatgaHandlers();
        services.AddSingleton<OrderStore>();
    }

    public static void ConfigureJsonOptions(this IServiceCollection services)
    {
        services.ConfigureHttpJsonOptions(opt =>
        {
            opt.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default);
            opt.SerializerOptions.Converters.Add(new JsonStringEnumConverter<OrderStatus>());
        });
    }
}
