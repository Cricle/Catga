using System.Diagnostics.CodeAnalysis;
using Catga;
using Catga.Messages;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Catga endpoint extensions for Minimal APIs (CAP-like)</summary>
[UnconditionalSuppressMessage("Trimming", "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code", Justification = "<Pending>")]
[UnconditionalSuppressMessage("AOT", "IL3050:Calling members annotated with 'RequiresDynamicCodeAttribute' may break functionality when AOT compiling.", Justification = "<Pending>")]
public static class CatgaEndpointExtensions
{
    public static RouteHandlerBuilder MapCatgaRequest<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]TResponse>(this IEndpointRouteBuilder endpoints, string pattern) where TRequest : IRequest<TResponse>
    {
        return endpoints.MapPost(pattern, async (TRequest request, ICatgaMediator mediator, CancellationToken cancellationToken) =>
        {
            var result = await mediator.SendAsync<TRequest, TResponse>(request, cancellationToken);
            return result.ToHttpResult();
        })
        .WithCatgaCommandMetadata<TRequest, TResponse>();
    }

    public static RouteHandlerBuilder MapCatgaQuery<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TQuery, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse>(this IEndpointRouteBuilder endpoints, string pattern) where TQuery : IRequest<TResponse>
    {
        return endpoints.MapGet(pattern, async ([AsParameters] TQuery query, ICatgaMediator mediator, CancellationToken cancellationToken) =>
        {
            var result = await mediator.SendAsync<TQuery, TResponse>(query, cancellationToken);
            return result.ToHttpResult();
        })
        .WithCatgaQueryMetadata<TQuery, TResponse>();
    }

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

