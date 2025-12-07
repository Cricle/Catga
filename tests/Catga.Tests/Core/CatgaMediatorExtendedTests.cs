using Catga;
using Catga.Abstractions;
using Catga.Configuration;
using Catga.Core;
using Catga.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Catga.Tests.Core;

/// <summary>
/// Extended CatgaMediator tests - comprehensive coverage
/// </summary>
public class CatgaMediatorExtendedTests
{
    [Fact]
    public async Task SendAsync_WithMetadata_ShouldPreserveMetadata()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<IRequestHandler<MetadataCommand, MetadataResponse>, MetadataCommandHandler>();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<ICatgaMediator>();
        var command = new MetadataCommand("test-data");

        // Act
        var result = await mediator.SendAsync<MetadataCommand, MetadataResponse>(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.Data.Should().Be("test-data");
    }

    [Fact]
    public async Task SendAsync_WithException_ShouldReturnFailure()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<IRequestHandler<ExceptionCommand, ExceptionResponse>, ExceptionCommandHandler>();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<ICatgaMediator>();
        var command = new ExceptionCommand();

        // Act
        var result = await mediator.SendAsync<ExceptionCommand, ExceptionResponse>(command);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Exception");
    }

    [Fact]
    public async Task PublishAsync_WithException_ShouldNotThrow()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<IEventHandler<ExceptionEvent>, ExceptionEventHandler>();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<ICatgaMediator>();
        var @event = new ExceptionEvent();

        // Act & Assert - 不应该抛出异常
        await mediator.PublishAsync(@event);
    }

    [Fact]
    public async Task SendAsync_WithScope_ShouldIsolateHandlers()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<IRequestHandler<ScopedCommand, ScopedResponse>, ScopedCommandHandler>();

        var provider = services.BuildServiceProvider();

        // Act & Assert
        using (var scope1 = provider.CreateScope())
        {
            var mediator1 = scope1.ServiceProvider.GetRequiredService<ICatgaMediator>();
            var result1 = await mediator1.SendAsync<ScopedCommand, ScopedResponse>(new ScopedCommand());
            result1.IsSuccess.Should().BeTrue();
        }

        using (var scope2 = provider.CreateScope())
        {
            var mediator2 = scope2.ServiceProvider.GetRequiredService<ICatgaMediator>();
            var result2 = await mediator2.SendAsync<ScopedCommand, ScopedResponse>(new ScopedCommand());
            result2.IsSuccess.Should().BeTrue();
        }
    }
}

// Test message types
public record MetadataCommand(string Data) : IRequest<MetadataResponse>
{
    public long MessageId { get; init; } = MessageExtensions.NewMessageId();
}
public record MetadataResponse(string Data);

public record ExceptionCommand : IRequest<ExceptionResponse>
{
    public long MessageId { get; init; } = MessageExtensions.NewMessageId();
}
public record ExceptionResponse(string Message);

public record ExceptionEvent : IEvent
{
    public long MessageId { get; init; } = MessageExtensions.NewMessageId();
}

public record ScopedCommand : IRequest<ScopedResponse>
{
    public long MessageId { get; init; } = MessageExtensions.NewMessageId();
}
public record ScopedResponse(Guid InstanceId);

// Test handlers
public class MetadataCommandHandler : IRequestHandler<MetadataCommand, MetadataResponse>
{
    public ValueTask<CatgaResult<MetadataResponse>> HandleAsync(
        MetadataCommand request,
        CancellationToken cancellationToken = default)
    {
        return new ValueTask<CatgaResult<MetadataResponse>>(CatgaResult<MetadataResponse>.Success(new MetadataResponse(request.Data)));
    }
}

public class ExceptionCommandHandler : IRequestHandler<ExceptionCommand, ExceptionResponse>
{
    public ValueTask<CatgaResult<ExceptionResponse>> HandleAsync(
        ExceptionCommand request,
        CancellationToken cancellationToken = default)
    {
        return new ValueTask<CatgaResult<ExceptionResponse>>(CatgaResult<ExceptionResponse>.Failure("Exception occurred"));
    }
}

public class ExceptionEventHandler : IEventHandler<ExceptionEvent>
{
    public ValueTask HandleAsync(ExceptionEvent @event, CancellationToken cancellationToken = default)
    {
        // 模拟异常但不抛出
        return ValueTask.CompletedTask;
    }
}

public class ScopedCommandHandler : IRequestHandler<ScopedCommand, ScopedResponse>
{
    private readonly Guid _instanceId = Guid.NewGuid();

    public ValueTask<CatgaResult<ScopedResponse>> HandleAsync(
        ScopedCommand request,
        CancellationToken cancellationToken = default)
    {
        return new ValueTask<CatgaResult<ScopedResponse>>(CatgaResult<ScopedResponse>.Success(new ScopedResponse(_instanceId)));
    }
}

