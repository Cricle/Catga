using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting;

/// <summary>
/// Extension methods for adding Catga to .NET Aspire applications
/// </summary>
public static class CatgaResourceExtensions
{
    /// <summary>
    /// Adds a Catga message broker to the application with InMemory transport
    /// </summary>
    /// <param name="builder">The distributed application builder</param>
    /// <param name="name">The name of the resource</param>
    /// <returns>A reference to the <see cref="IResourceBuilder{T}"/></returns>
    public static IResourceBuilder<CatgaResource> AddCatga(
        this IDistributedApplicationBuilder builder,
        string name)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var resource = new CatgaResource(name);

        return builder.AddResource(resource)
            .WithAnnotation(new ManifestPublishingCallbackAnnotation(context =>
            {
                context.Writer.WriteString("type", "catga.v0");
            }));
    }

    /// <summary>
    /// Configures Catga to use Redis transport
    /// </summary>
    public static IResourceBuilder<CatgaResource> WithRedisTransport(
        this IResourceBuilder<CatgaResource> builder,
        IResourceBuilder<IResourceWithConnectionString> redis)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(redis);

        builder.Resource.TransportType = "Redis";
        builder.WithReference(redis);

        return builder;
    }

    /// <summary>
    /// Configures Catga to use NATS transport
    /// </summary>
    public static IResourceBuilder<CatgaResource> WithNatsTransport(
        this IResourceBuilder<CatgaResource> builder,
        IResourceBuilder<IResourceWithConnectionString> nats)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(nats);

        builder.Resource.TransportType = "NATS";
        builder.WithReference(nats);

        return builder;
    }

    /// <summary>
    /// Configures Catga to use InMemory transport (default)
    /// </summary>
    public static IResourceBuilder<CatgaResource> WithInMemoryTransport(
        this IResourceBuilder<CatgaResource> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Resource.TransportType = "InMemory";

        return builder;
    }

    /// <summary>
    /// Configures Catga persistence
    /// </summary>
    public static IResourceBuilder<CatgaResource> WithPersistence(
        this IResourceBuilder<CatgaResource> builder,
        string persistenceType)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(persistenceType);

        builder.Resource.PersistenceType = persistenceType;

        return builder;
    }

    /// <summary>
    /// Adds health check endpoint for Catga
    /// </summary>
    public static IResourceBuilder<CatgaResource> WithHealthCheck(
        this IResourceBuilder<CatgaResource> builder,
        string? path = "/health")
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.WithAnnotation(new HealthCheckAnnotation(path ?? "/health"));
    }
}

/// <summary>
/// Represents a Catga message broker resource in Aspire
/// </summary>
public sealed class CatgaResource : Resource, IResourceWithEnvironment
{
    /// <summary>
    /// Initializes a new instance of <see cref="CatgaResource"/>
    /// </summary>
    public CatgaResource(string name) : base(name)
    {
        TransportType = "InMemory"; // Default
        PersistenceType = "InMemory"; // Default
    }

    /// <summary>
    /// Gets or sets the transport type (InMemory, Redis, NATS)
    /// </summary>
    public string TransportType { get; set; }

    /// <summary>
    /// Gets or sets the persistence type (InMemory, Redis, NATS)
    /// </summary>
    public string PersistenceType { get; set; }
}

