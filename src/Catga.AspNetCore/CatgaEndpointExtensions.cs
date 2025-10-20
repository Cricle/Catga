using System.Diagnostics.CodeAnalysis;
using Catga;
using Catga.Messages;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Catga.Abstractions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Catga endpoint extensions for Minimal APIs.
/// These methods use ASP.NET Core Minimal APIs which require reflection for route parameter binding.
/// For full AOT compatibility, consider using controller-based endpoints instead.
/// </summary>
public static class CatgaEndpointExtensions
{
    /// <summary>
    /// Maps a POST endpoint for a Catga command/request.
    /// </summary>
    [RequiresUnreferencedCode("ASP.NET Core Minimal APIs use reflection for parameter binding. Use controllers for AOT.")]
    [RequiresDynamicCode("ASP.NET Core Minimal APIs may generate code at runtime. Use controllers for AOT.")]
    public static RouteHandlerBuilder MapCatgaRequest<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse>(this IEndpointRouteBuilder endpoints, string pattern) where TRequest : IRequest<TResponse>
    {
        return endpoints.MapPost(pattern, async (TRequest request, ICatgaMediator mediator, CancellationToken cancellationToken) =>
        {
            var result = await mediator.SendAsync<TRequest, TResponse>(request, cancellationToken);
            return result.ToHttpResult();
        })
        .WithCatgaCommandMetadata<TRequest, TResponse>();
    }

    /// <summary>
    /// Maps a GET endpoint for a Catga query.
    /// </summary>
    [RequiresUnreferencedCode("ASP.NET Core Minimal APIs use reflection for parameter binding. Use controllers for AOT.")]
    [RequiresDynamicCode("ASP.NET Core Minimal APIs may generate code at runtime. Use controllers for AOT.")]
    public static RouteHandlerBuilder MapCatgaQuery<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TQuery, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse>(this IEndpointRouteBuilder endpoints, string pattern) where TQuery : IRequest<TResponse>
    {
        return endpoints.MapGet(pattern, async ([AsParameters] TQuery query, ICatgaMediator mediator, CancellationToken cancellationToken) =>
        {
            var result = await mediator.SendAsync<TQuery, TResponse>(query, cancellationToken);
            return result.ToHttpResult();
        })
        .WithCatgaQueryMetadata<TQuery, TResponse>();
    }

    /// <summary>
    /// Maps a POST endpoint for publishing a Catga event.
    /// </summary>
    [RequiresUnreferencedCode("ASP.NET Core Minimal APIs use reflection for parameter binding. Use controllers for AOT.")]
    [RequiresDynamicCode("ASP.NET Core Minimal APIs may generate code at runtime. Use controllers for AOT.")]
    public static RouteHandlerBuilder MapCatgaEvent<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEvent>(this IEndpointRouteBuilder endpoints, string pattern) where TEvent : IEvent
    {
        return endpoints.MapPost(pattern, async (TEvent @event, ICatgaMediator mediator, CancellationToken cancellationToken) =>
        {
            await mediator.PublishAsync(@event, cancellationToken: cancellationToken);
            return Results.Accepted();
        })
        .WithCatgaEventMetadata<TEvent>();
    }
}

