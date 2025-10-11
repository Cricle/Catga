using System.Diagnostics.CodeAnalysis;
using Catga;
using Catga.Messages;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Catga endpoint extensions for ASP.NET Core Minimal APIs
/// Similar to CAP's ICapPublisher pattern
/// Note: Uses ASP.NET Core's built-in parameter binding (reflection-based)
/// </summary>
[UnconditionalSuppressMessage("Trimming", "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code", Justification = "<Pending>")]
[UnconditionalSuppressMessage("AOT", "IL3050:Calling members annotated with 'RequiresDynamicCodeAttribute' may break functionality when AOT compiling.", Justification = "<Pending>")]
public static class CatgaEndpointExtensions
{
    /// <summary>
    /// Map Catga command/query to HTTP endpoint
    /// Usage: app.MapCatgaRequest&lt;CreateOrderCommand, CreateOrderResult&gt;("/api/orders")
    /// </summary>
    public static RouteHandlerBuilder MapCatgaRequest<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]TResponse>(
        this IEndpointRouteBuilder endpoints,
        string pattern)
        where TRequest : IRequest<TResponse>
    {
        return endpoints.MapPost(pattern, async (
            TRequest request,
            ICatgaMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var result = await mediator.SendAsync<TRequest, TResponse>(request, cancellationToken);
            return result.ToHttpResult(); // Use extension method for smart status code mapping
        })
        .WithCatgaCommandMetadata<TRequest, TResponse>();
    }

    /// <summary>
    /// Map Catga query to HTTP GET endpoint
    /// Usage: app.MapCatgaQuery&lt;GetOrderQuery, OrderDto&gt;("/api/orders/{orderId}")
    /// </summary>
    public static RouteHandlerBuilder MapCatgaQuery<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TQuery, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse>(
        this IEndpointRouteBuilder endpoints,
        string pattern)
        where TQuery : IRequest<TResponse>
    {
        return endpoints.MapGet(pattern, async (
            [AsParameters] TQuery query,
            ICatgaMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var result = await mediator.SendAsync<TQuery, TResponse>(query, cancellationToken);
            return result.ToHttpResult(); // Use extension method for smart status code mapping
        })
        .WithCatgaQueryMetadata<TQuery, TResponse>();
    }

    /// <summary>
    /// Map Catga event publish to HTTP endpoint
    /// Usage: app.MapCatgaEvent&lt;OrderCreatedEvent&gt;("/api/events/order-created")
    /// </summary>
    public static RouteHandlerBuilder MapCatgaEvent<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEvent>(
        this IEndpointRouteBuilder endpoints,
        string pattern)
        where TEvent : IEvent
    {
        return endpoints.MapPost(pattern, async (
            TEvent @event,
            ICatgaMediator mediator,
            CancellationToken cancellationToken) =>
        {
            await mediator.PublishAsync(@event, cancellationToken: cancellationToken);
            return Results.Accepted();
        })
        .WithCatgaEventMetadata<TEvent>();
    }
}

