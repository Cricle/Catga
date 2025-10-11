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
public static class CatgaEndpointExtensions
{
    /// <summary>
    /// Map Catga command/query to HTTP endpoint
    /// Usage: app.MapCatgaRequest&lt;CreateOrderCommand, CreateOrderResult&gt;("/api/orders")
    /// </summary>
    public static RouteHandlerBuilder MapCatgaRequest<TRequest, TResponse>(
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
    public static RouteHandlerBuilder MapCatgaQuery<TQuery, TResponse>(
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
    public static RouteHandlerBuilder MapCatgaEvent<TEvent>(
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

    /// <summary>
    /// Map Catga health and diagnostics endpoints
    /// Similar to CAP Dashboard
    /// </summary>
    public static IEndpointRouteBuilder MapCatgaDiagnostics(
        this IEndpointRouteBuilder endpoints,
        string prefix = "/catga")
    {
        // Health endpoint
        endpoints.MapGet($"{prefix}/health", () => Results.Ok(new
        {
            status = "healthy",
            framework = "Catga",
            timestamp = DateTime.UtcNow
        }))
        .WithName("CatgaHealth")
        .WithTags("Catga", "Diagnostics")
        .ExcludeFromDescription();

        // Node info endpoint
        endpoints.MapGet($"{prefix}/node", () => Results.Ok(new
        {
            nodeId = Environment.GetEnvironmentVariable("NodeId") ?? Environment.MachineName,
            machineName = Environment.MachineName,
            processId = Environment.ProcessId,
            uptime = DateTime.UtcNow - System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime(),
            runtime = Environment.Version.ToString(),
            isAot = !System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported
        }))
        .WithName("CatgaNodeInfo")
        .WithTags("Catga", "Diagnostics")
        .ExcludeFromDescription();

        return endpoints;
    }
}

