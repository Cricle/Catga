using System.Diagnostics.CodeAnalysis;
using Catga.Core;
using Catga.Messages;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Swagger/OpenAPI extensions for Catga</summary>
public static class CatgaSwaggerExtensions
{
    public static RouteHandlerBuilder WithCatgaCommandMetadata<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TCommand, TResponse>(this RouteHandlerBuilder builder) where TCommand : IRequest<TResponse>
    {
        var commandName = TypeNameCache<TCommand>.Name.Replace("Command", "");
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

    public static RouteHandlerBuilder WithCatgaQueryMetadata<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TQuery, TResponse>(this RouteHandlerBuilder builder) where TQuery : IRequest<TResponse>
    {
        var queryName = TypeNameCache<TQuery>.Name.Replace("Query", "");
        return builder
            .WithName(queryName)
            .WithSummary($"Query {queryName}")
            .WithDescription($"Queries {queryName} via Catga CQRS mediator")
            .WithTags("Queries", "Catga")
            .Produces<TResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);
    }

    public static RouteHandlerBuilder WithCatgaEventMetadata<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEvent>(this RouteHandlerBuilder builder) where TEvent : IEvent
    {
        var eventName = TypeNameCache<TEvent>.Name.Replace("Event", "");
        return builder
            .WithName($"Publish{eventName}")
            .WithSummary($"Publish {eventName} event")
            .WithDescription($"Publishes {eventName} event via Catga event bus")
            .WithTags("Events", "Catga")
            .Produces(StatusCodes.Status202Accepted);
    }
}

/// <summary>Standard error response for Catga endpoints</summary>
public class ErrorResponse
{
    public required string Error { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
}

