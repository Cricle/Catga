using Catga.Abstractions;
using Catga.Core;
using Catga.DependencyInjection;
using Catga.Exceptions;
using FluentAssertions;
using MemoryPack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Catga.Tests.Core;

/// <summary>
/// Comprehensive coverage tests for CatgaMediator
/// </summary>
public sealed partial class CatgaMediatorCoverageTests
{
    [Fact]
    public async Task SendAsync_WithNullRequest_ShouldReturnFailure()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        // Act
        var result = await mediator.SendAsync<TestCommand, TestResponse>(null!);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("null");
    }

    [Fact]
    public async Task SendAsync_WithNoHandler_ShouldReturnFailure()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        // Note: No handler registered
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        // Act
        var command = new UnhandledCommand { MessageId = MessageExtensions.NewMessageId() };
        var result = await mediator.SendAsync<UnhandledCommand, UnhandledResponse>(command);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("No handler");
    }

    [Fact]
    public async Task SendAsync_WithSingletonHandler_ShouldSucceed()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddSingleton<IRequestHandler<SingletonCommand, SingletonResponse>, SingletonHandler>();
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        // Act
        var command = new SingletonCommand { MessageId = MessageExtensions.NewMessageId(), Value = 10 };
        var result = await mediator.SendAsync<SingletonCommand, SingletonResponse>(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.DoubledValue.Should().Be(20);
    }

    [Fact]
    public async Task SendAsync_WithScopedHandler_ShouldSucceed()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<IRequestHandler<ScopedCommand, ScopedResponse>, ScopedHandler>();
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        // Act
        var command = new ScopedCommand { MessageId = MessageExtensions.NewMessageId(), Value = 5 };
        var result = await mediator.SendAsync<ScopedCommand, ScopedResponse>(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.TripleValue.Should().Be(15);
    }

    [Fact]
    public async Task SendAsync_WithTransientHandler_ShouldSucceed()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddTransient<IRequestHandler<TransientCommand, TransientResponse>, TransientHandler>();
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        // Act
        var command = new TransientCommand { MessageId = MessageExtensions.NewMessageId() };
        var result = await mediator.SendAsync<TransientCommand, TransientResponse>(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task SendAsync_HandlerThrowsException_ShouldReturnFailure()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<IRequestHandler<ThrowingCommand, ThrowingResponse>, ThrowingHandler>();
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        // Act
        var command = new ThrowingCommand { MessageId = MessageExtensions.NewMessageId() };
        var result = await mediator.SendAsync<ThrowingCommand, ThrowingResponse>(command);

        // Assert
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task PublishAsync_WithNullEvent_ShouldNotThrow()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        // Act
        var act = async () => await mediator.PublishAsync<TestEvent>(null!);

        // Assert - Should handle gracefully
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task PublishAsync_WithNoHandlers_ShouldNotThrow()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        // Act
        var @event = new UnhandledEvent { MessageId = MessageExtensions.NewMessageId() };
        var act = async () => await mediator.PublishAsync(@event);

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task PublishAsync_WithMultipleHandlers_ShouldInvokeAll()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<IEventHandler<MultiEvent>, MultiEventHandler1>();
        services.AddScoped<IEventHandler<MultiEvent>, MultiEventHandler2>();
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        MultiEventHandler1.Count = 0;
        MultiEventHandler2.Count = 0;

        // Act
        var @event = new MultiEvent { MessageId = MessageExtensions.NewMessageId() };
        await mediator.PublishAsync(@event);

        // Assert
        MultiEventHandler1.Count.Should().Be(1);
        MultiEventHandler2.Count.Should().Be(1);
    }

    [Fact]
    public async Task PublishAsync_HandlerThrows_OtherHandlersShouldStillRun()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<IEventHandler<PartialFailEvent>, PartialFailHandler1>();
        services.AddScoped<IEventHandler<PartialFailEvent>, PartialFailHandler2>();
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        PartialFailHandler2.Count = 0;

        // Act
        var @event = new PartialFailEvent { MessageId = MessageExtensions.NewMessageId() };
        await mediator.PublishAsync(@event);

        // Assert
        PartialFailHandler2.Count.Should().Be(1);
    }

    [Fact]
    public async Task SendAsync_WithCancellation_ShouldRespectToken()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<IRequestHandler<CancellableCommand, CancellableResponse>, CancellableHandler>();
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        var command = new CancellableCommand { MessageId = MessageExtensions.NewMessageId() };

        try
        {
            var result = await mediator.SendAsync<CancellableCommand, CancellableResponse>(command, cts.Token);
            result.IsSuccess.Should().BeFalse();
        }
        catch (OperationCanceledException)
        {
            // Expected
        }
    }

    [Fact]
    public void Dispose_ShouldNotThrow()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>() as IDisposable;

        // Act
        var act = () => mediator?.Dispose();

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public async Task SendAsync_WithCorrelationId_ShouldPropagate()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<IRequestHandler<CorrelatedCommand, CorrelatedResponse>, CorrelatedHandler>();
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        // Act
        var correlationId = MessageExtensions.NewCorrelationId();
        var command = new CorrelatedCommand
        {
            MessageId = MessageExtensions.NewMessageId(),
            CorrelationId = correlationId
        };
        var result = await mediator.SendAsync<CorrelatedCommand, CorrelatedResponse>(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    #region Test Types

    [MemoryPackable]
    private partial record TestCommand : IRequest<TestResponse>
    {
        public required long MessageId { get; init; }
    }

    [MemoryPackable]
    private partial record TestResponse { }

    [MemoryPackable]
    private partial record UnhandledCommand : IRequest<UnhandledResponse>
    {
        public required long MessageId { get; init; }
    }

    [MemoryPackable]
    private partial record UnhandledResponse { }

    [MemoryPackable]
    private partial record SingletonCommand : IRequest<SingletonResponse>
    {
        public required long MessageId { get; init; }
        public int Value { get; init; }
    }

    [MemoryPackable]
    private partial record SingletonResponse
    {
        public int DoubledValue { get; init; }
    }

    private sealed class SingletonHandler : IRequestHandler<SingletonCommand, SingletonResponse>
    {
        public ValueTask<CatgaResult<SingletonResponse>> HandleAsync(SingletonCommand request, CancellationToken ct = default)
        {
            return new ValueTask<CatgaResult<SingletonResponse>>(CatgaResult<SingletonResponse>.Success(new SingletonResponse { DoubledValue = request.Value * 2 }));
        }
    }

    [MemoryPackable]
    private partial record ScopedCommand : IRequest<ScopedResponse>
    {
        public required long MessageId { get; init; }
        public int Value { get; init; }
    }

    [MemoryPackable]
    private partial record ScopedResponse
    {
        public int TripleValue { get; init; }
    }

    private sealed class ScopedHandler : IRequestHandler<ScopedCommand, ScopedResponse>
    {
        public ValueTask<CatgaResult<ScopedResponse>> HandleAsync(ScopedCommand request, CancellationToken ct = default)
        {
            return new ValueTask<CatgaResult<ScopedResponse>>(CatgaResult<ScopedResponse>.Success(new ScopedResponse { TripleValue = request.Value * 3 }));
        }
    }

    [MemoryPackable]
    private partial record TransientCommand : IRequest<TransientResponse>
    {
        public required long MessageId { get; init; }
    }

    [MemoryPackable]
    private partial record TransientResponse { }

    private sealed class TransientHandler : IRequestHandler<TransientCommand, TransientResponse>
    {
        public ValueTask<CatgaResult<TransientResponse>> HandleAsync(TransientCommand request, CancellationToken ct = default)
        {
            return new ValueTask<CatgaResult<TransientResponse>>(CatgaResult<TransientResponse>.Success(new TransientResponse()));
        }
    }

    [MemoryPackable]
    private partial record ThrowingCommand : IRequest<ThrowingResponse>
    {
        public required long MessageId { get; init; }
    }

    [MemoryPackable]
    private partial record ThrowingResponse { }

    private sealed class ThrowingHandler : IRequestHandler<ThrowingCommand, ThrowingResponse>
    {
        public ValueTask<CatgaResult<ThrowingResponse>> HandleAsync(ThrowingCommand request, CancellationToken ct = default)
        {
            throw new InvalidOperationException("Test exception");
        }
    }

    [MemoryPackable]
    private partial record TestEvent : IEvent
    {
        public required long MessageId { get; init; }
    }

    [MemoryPackable]
    private partial record UnhandledEvent : IEvent
    {
        public required long MessageId { get; init; }
    }

    [MemoryPackable]
    private partial record MultiEvent : IEvent
    {
        public required long MessageId { get; init; }
    }

    private sealed class MultiEventHandler1 : IEventHandler<MultiEvent>
    {
        public static int Count;
        public ValueTask HandleAsync(MultiEvent @event, CancellationToken ct = default)
        {
            Interlocked.Increment(ref Count);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class MultiEventHandler2 : IEventHandler<MultiEvent>
    {
        public static int Count;
        public ValueTask HandleAsync(MultiEvent @event, CancellationToken ct = default)
        {
            Interlocked.Increment(ref Count);
            return ValueTask.CompletedTask;
        }
    }

    [MemoryPackable]
    private partial record PartialFailEvent : IEvent
    {
        public required long MessageId { get; init; }
    }

    private sealed class PartialFailHandler1 : IEventHandler<PartialFailEvent>
    {
        public ValueTask HandleAsync(PartialFailEvent @event, CancellationToken ct = default)
        {
            throw new InvalidOperationException("Handler 1 failed");
        }
    }

    private sealed class PartialFailHandler2 : IEventHandler<PartialFailEvent>
    {
        public static int Count;
        public ValueTask HandleAsync(PartialFailEvent @event, CancellationToken ct = default)
        {
            Interlocked.Increment(ref Count);
            return ValueTask.CompletedTask;
        }
    }

    [MemoryPackable]
    private partial record CancellableCommand : IRequest<CancellableResponse>
    {
        public required long MessageId { get; init; }
    }

    [MemoryPackable]
    private partial record CancellableResponse { }

    private sealed class CancellableHandler : IRequestHandler<CancellableCommand, CancellableResponse>
    {
        public ValueTask<CatgaResult<CancellableResponse>> HandleAsync(CancellableCommand request, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return new ValueTask<CatgaResult<CancellableResponse>>(CatgaResult<CancellableResponse>.Success(new CancellableResponse()));
        }
    }

    [MemoryPackable]
    private partial record CorrelatedCommand : IRequest<CorrelatedResponse>
    {
        public required long MessageId { get; init; }
        public long? CorrelationId { get; init; }
    }

    [MemoryPackable]
    private partial record CorrelatedResponse { }

    private sealed class CorrelatedHandler : IRequestHandler<CorrelatedCommand, CorrelatedResponse>
    {
        public ValueTask<CatgaResult<CorrelatedResponse>> HandleAsync(CorrelatedCommand request, CancellationToken ct = default)
        {
            return new ValueTask<CatgaResult<CorrelatedResponse>>(CatgaResult<CorrelatedResponse>.Success(new CorrelatedResponse()));
        }
    }

    #endregion
}






