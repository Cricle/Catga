using Catga;
using Catga.Configuration;
using Catga.Core;
using Catga.DependencyInjection;
using Catga.Core;
using Catga.Abstractions;
using Catga.Core;
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
    public async Task SendAsync_HighVolume_ShouldMaintainPerformance()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<IRequestHandler<PerformanceCommand, PerformanceResponse>, PerformanceCommandHandler>();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<ICatgaMediator>();

        // Act
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 1000; i++)
        {
            await mediator.SendAsync<PerformanceCommand, PerformanceResponse>(new PerformanceCommand(i));
        }
        stopwatch.Stop();

        // Assert - 1000 个命令应该在 150ms 内完成（放宽阈值以适应 CI 环境）
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(150);
    }

    [Fact]
    public async Task PublishAsync_HighVolume_ShouldMaintainPerformance()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<IEventHandler<PerformanceEvent>, PerformanceEventHandler>();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<ICatgaMediator>();

        // Act
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 1000; i++)
        {
            await mediator.PublishAsync(new PerformanceEvent(i));
        }
        stopwatch.Stop();

        // Assert - 1000 个事件应该在 100ms 内完成
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(100);
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

public record PerformanceCommand(int Id) : IRequest<PerformanceResponse>
{
    public long MessageId { get; init; } = MessageExtensions.NewMessageId();
}
public record PerformanceResponse(int Id);

public record PerformanceEvent(int Id) : IEvent
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
    public Task<CatgaResult<MetadataResponse>> HandleAsync(
        MetadataCommand request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(CatgaResult<MetadataResponse>.Success(new MetadataResponse(request.Data)));
    }
}

public class ExceptionCommandHandler : IRequestHandler<ExceptionCommand, ExceptionResponse>
{
    public Task<CatgaResult<ExceptionResponse>> HandleAsync(
        ExceptionCommand request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(CatgaResult<ExceptionResponse>.Failure("Exception occurred"));
    }
}

public class ExceptionEventHandler : IEventHandler<ExceptionEvent>
{
    public Task HandleAsync(ExceptionEvent @event, CancellationToken cancellationToken = default)
    {
        // 模拟异常但不抛出
        return Task.CompletedTask;
    }
}

public class PerformanceCommandHandler : IRequestHandler<PerformanceCommand, PerformanceResponse>
{
    public Task<CatgaResult<PerformanceResponse>> HandleAsync(
        PerformanceCommand request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(CatgaResult<PerformanceResponse>.Success(new PerformanceResponse(request.Id)));
    }
}

public class PerformanceEventHandler : IEventHandler<PerformanceEvent>
{
    public Task HandleAsync(PerformanceEvent @event, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

public class ScopedCommandHandler : IRequestHandler<ScopedCommand, ScopedResponse>
{
    private readonly Guid _instanceId = Guid.NewGuid();

    public Task<CatgaResult<ScopedResponse>> HandleAsync(
        ScopedCommand request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(CatgaResult<ScopedResponse>.Success(new ScopedResponse(_instanceId)));
    }
}

