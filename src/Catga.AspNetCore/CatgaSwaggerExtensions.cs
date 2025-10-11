using System.Diagnostics.CodeAnalysis;
using Catga.Messages;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Swagger/OpenAPI extensions for Catga endpoints
/// </summary>
public static class CatgaSwaggerExtensions
{
    /// <summary>
    /// Configure Catga-specific OpenAPI metadata for a command endpoint
    /// </summary>
    [RequiresUnreferencedCode("OpenAPI metadata generation may require types that cannot be statically analyzed.")]
    [RequiresDynamicCode("OpenAPI metadata generation uses reflection.")]
    public static RouteHandlerBuilder WithCatgaCommandMetadata<TCommand, TResponse>(
        this RouteHandlerBuilder builder)
        where TCommand : IRequest<TResponse>
    {
        var commandName = typeof(TCommand).Name.Replace("Command", "");

        return builder
            .WithName(commandName)
            .WithSummary($"Execute {commandName} command")
            .WithDescription($"Executes the {commandName} command via Catga CQRS mediator")
            .WithTags("Commands", "Catga")
            .Produces<TResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict)
            .Produces<ErrorResponse>(StatusCodes.Status422UnprocessableEntity);
    }

    /// <summary>
    /// Configure Catga-specific OpenAPI metadata for a query endpoint
    /// </summary>
    [RequiresUnreferencedCode("OpenAPI metadata generation may require types that cannot be statically analyzed.")]
    [RequiresDynamicCode("OpenAPI metadata generation uses reflection.")]
    public static RouteHandlerBuilder WithCatgaQueryMetadata<TQuery, TResponse>(
        this RouteHandlerBuilder builder)
        where TQuery : IRequest<TResponse>
    {
        var queryName = typeof(TQuery).Name.Replace("Query", "");

        return builder
            .WithName(queryName)
            .WithSummary($"Query {queryName}")
            .WithDescription($"Queries {queryName} via Catga CQRS mediator")
            .WithTags("Queries", "Catga")
            .Produces<TResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);
    }

    /// <summary>
    /// Configure Catga-specific OpenAPI metadata for an event endpoint
    /// </summary>
    [RequiresUnreferencedCode("OpenAPI metadata generation may require types that cannot be statically analyzed.")]
    [RequiresDynamicCode("OpenAPI metadata generation uses reflection.")]
    public static RouteHandlerBuilder WithCatgaEventMetadata<TEvent>(
        this RouteHandlerBuilder builder)
        where TEvent : IEvent
    {
        var eventName = typeof(TEvent).Name.Replace("Event", "");

        return builder
            .WithName($"Publish{eventName}")
            .WithSummary($"Publish {eventName} event")
            .WithDescription($"Publishes {eventName} event via Catga event bus")
            .WithTags("Events", "Catga")
            .Produces(StatusCodes.Status202Accepted);
    }
}

/// <summary>
/// Standard error response for Catga endpoints
/// </summary>
public class ErrorResponse
{
    public required string Error { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
}

