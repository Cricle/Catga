using Catga.Abstractions;
using Catga.DependencyInjection;
using Catga.Messaging;
using Catga.Pipeline;
using Catga.Pipeline.Behaviors;
using Catga.Security;
using Catga.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Fluent feature extensions for optional Catga capabilities.
/// </summary>
public static class CatgaServiceBuilderFeatureExtensions
{
    /// <summary>
    /// Enables ambient correlation propagation for mediator request handling.
    /// </summary>
    public static CatgaServiceBuilder WithCorrelationPropagation(this CatgaServiceBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.TryAddSingleton<ICorrelationContext, CorrelationContext>();
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Scoped(typeof(IPipelineBehavior<,>), typeof(CorrelationPropagationBehavior<,>)));

        builder.Services.ValidateCatgaLifetimes();

        return builder;
    }

    /// <summary>
    /// Enables request/response client support on top of the configured transport.
    /// </summary>
    public static CatgaServiceBuilder UseRequestClient(this CatgaServiceBuilder builder, TimeSpan? defaultTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.RemoveAll<IRequestClientFactory>();
        builder.Services.AddSingleton<IRequestClientFactory>(sp =>
            new RequestClientFactory(sp.GetRequiredService<IMessageTransport>(), defaultTimeout));

        builder.Services.ValidateCatgaLifetimes();

        return builder;
    }

    /// <summary>
    /// Registers message version mappings for rolling upgrades.
    /// </summary>
    public static CatgaServiceBuilder WithMessageVersioning(
        this CatgaServiceBuilder builder,
        Action<MessageVersionMapperBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var mapperBuilder = new MessageVersionMapperBuilder();
        configure?.Invoke(mapperBuilder);
        var mapper = mapperBuilder.Build();

        builder.Services.RemoveAll<IMessageVersionMapper>();
        builder.Services.AddSingleton(mapper);

        builder.Services.ValidateCatgaLifetimes();

        return builder;
    }

    /// <summary>
    /// Enables authorization checks based on Catga's host-agnostic security abstractions.
    /// </summary>
    public static CatgaServiceBuilder WithAuthorization(
        this CatgaServiceBuilder builder,
        Action<AuthorizationPolicyRegistry>? configurePolicies = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.TryAddSingleton<ISecurityContext, SecurityContext>();

        if (configurePolicies is null)
        {
            builder.Services.TryAddSingleton<IAuthorizationPolicyRegistry, AuthorizationPolicyRegistry>();
        }
        else
        {
            var registry = new AuthorizationPolicyRegistry();
            configurePolicies(registry);

            builder.Services.RemoveAll<IAuthorizationPolicyRegistry>();
            builder.Services.AddSingleton<IAuthorizationPolicyRegistry>(registry);
        }

        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Scoped(typeof(IPipelineBehavior<,>), typeof(AuthorizationBehavior<,>)));

        builder.Services.ValidateCatgaLifetimes();

        return builder;
    }

    /// <summary>
    /// Enables HMAC message signing with a shared secret.
    /// </summary>
    public static CatgaServiceBuilder WithMessageSigning(
        this CatgaServiceBuilder builder,
        string secretKey,
        Action<MessageSigningOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(secretKey);

        var options = new MessageSigningOptions
        {
            SecretKey = secretKey
        };
        configure?.Invoke(options);

        builder.Services.RemoveAll<MessageSigningOptions>();
        builder.Services.RemoveAll<IMessageSigner>();
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<IMessageSigner>(_ => new HmacMessageSigner(options.SecretKey));

        builder.Services.ValidateCatgaLifetimes();

        return builder;
    }
}
