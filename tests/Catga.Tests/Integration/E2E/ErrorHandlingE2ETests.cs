using Catga.Abstractions;
using Catga.Core;
using Catga.DependencyInjection;
using Catga.Exceptions;
using FluentAssertions;
using MemoryPack;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Catga.Tests.Integration.E2E;

/// <summary>
/// End-to-end tests for error handling scenarios
/// </summary>
[Trait("Category", "Integration")]
public sealed partial class ErrorHandlingE2ETests
{
    [Fact]
    public async Task Handler_ThrowsException_ShouldReturnFailure()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<IRequestHandler<ThrowingCommand, ThrowingResult>, ThrowingHandler>();

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        // Act
        var command = new ThrowingCommand { MessageId = MessageExtensions.NewMessageId() };
        var result = await mediator.SendAsync<ThrowingCommand, ThrowingResult>(command);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Handler_ThrowsCatgaException_ShouldPreserveErrorCode()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<IRequestHandler<CatgaExceptionCommand, CatgaExceptionResult>, CatgaExceptionHandler>();

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        // Act
        var command = new CatgaExceptionCommand { MessageId = MessageExtensions.NewMessageId() };
        var result = await mediator.SendAsync<CatgaExceptionCommand, CatgaExceptionResult>(command);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("CUSTOM_ERROR");
    }

    [Fact]
    public async Task Handler_ReturnsFailureResult_ShouldPropagateError()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<IRequestHandler<FailureResultCommand, FailureResultResponse>, FailureResultHandler>();

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        // Act
        var command = new FailureResultCommand { MessageId = MessageExtensions.NewMessageId() };
        var result = await mediator.SendAsync<FailureResultCommand, FailureResultResponse>(command);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("Business logic failure");
    }

    [Fact]
    public async Task EventHandler_ThrowsException_ShouldNotAffectOtherHandlers()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<IEventHandler<MultiHandlerEvent>, ThrowingEventHandler>();
        services.AddScoped<IEventHandler<MultiHandlerEvent>, SuccessfulEventHandler>();

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        SuccessfulEventHandler.ReceivedCount = 0;

        // Act
        var @event = new MultiHandlerEvent { MessageId = MessageExtensions.NewMessageId() };
        await mediator.PublishAsync(@event);

        // Assert - The successful handler should still receive the event
        SuccessfulEventHandler.ReceivedCount.Should().Be(1);
    }

    [Fact]
    public async Task Handler_WithNullResponse_ShouldHandleGracefully()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<IRequestHandler<NullResponseCommand, NullResponseResult>, NullResponseHandler>();

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        // Act
        var command = new NullResponseCommand { MessageId = MessageExtensions.NewMessageId() };
        var result = await mediator.SendAsync<NullResponseCommand, NullResponseResult>(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeNull();
    }

    [Fact]
    public async Task Handler_Timeout_ShouldReturnFailure()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<IRequestHandler<SlowCommand, SlowResult>, SlowHandler>();

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        // Act
        var command = new SlowCommand { MessageId = MessageExtensions.NewMessageId(), DelayMs = 5000 };

        try
        {
            var result = await mediator.SendAsync<SlowCommand, SlowResult>(command, cts.Token);
            // If we get here, the result should indicate failure or cancellation
            result.IsSuccess.Should().BeFalse();
        }
        catch (OperationCanceledException)
        {
            // This is also acceptable
        }
    }

    [Fact]
    public async Task Handler_ArgumentException_ShouldReturnFailure()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<IRequestHandler<ArgumentExceptionCommand, ArgumentExceptionResult>, ArgumentExceptionHandler>();

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        // Act
        var command = new ArgumentExceptionCommand { MessageId = MessageExtensions.NewMessageId() };
        var result = await mediator.SendAsync<ArgumentExceptionCommand, ArgumentExceptionResult>(command);

        // Assert
        result.IsSuccess.Should().BeFalse();
    }

    #region Commands and Handlers

    [MemoryPackable]
    private partial record ThrowingCommand : IRequest<ThrowingResult>
    {
        public required long MessageId { get; init; }
    }

    [MemoryPackable]
    private partial record ThrowingResult { }

    private sealed class ThrowingHandler : IRequestHandler<ThrowingCommand, ThrowingResult>
    {
        public ValueTask<CatgaResult<ThrowingResult>> HandleAsync(ThrowingCommand request, CancellationToken ct = default)
        {
            throw new InvalidOperationException("Handler threw an exception");
        }
    }

    [MemoryPackable]
    private partial record CatgaExceptionCommand : IRequest<CatgaExceptionResult>
    {
        public required long MessageId { get; init; }
    }

    [MemoryPackable]
    private partial record CatgaExceptionResult { }

    private sealed class CatgaExceptionHandler : IRequestHandler<CatgaExceptionCommand, CatgaExceptionResult>
    {
        public ValueTask<CatgaResult<CatgaExceptionResult>> HandleAsync(CatgaExceptionCommand request, CancellationToken ct = default)
        {
            throw new CatgaException("Custom error occurred", "CUSTOM_ERROR");
        }
    }

    [MemoryPackable]
    private partial record FailureResultCommand : IRequest<FailureResultResponse>
    {
        public required long MessageId { get; init; }
    }

    [MemoryPackable]
    private partial record FailureResultResponse { }

    private sealed class FailureResultHandler : IRequestHandler<FailureResultCommand, FailureResultResponse>
    {
        public ValueTask<CatgaResult<FailureResultResponse>> HandleAsync(FailureResultCommand request, CancellationToken ct = default)
        {
            return new ValueTask<CatgaResult<FailureResultResponse>>(CatgaResult<FailureResultResponse>.Failure("Business logic failure"));
        }
    }

    [MemoryPackable]
    private partial record MultiHandlerEvent : IEvent
    {
        public required long MessageId { get; init; }
    }

    private sealed class ThrowingEventHandler : IEventHandler<MultiHandlerEvent>
    {
        public ValueTask HandleAsync(MultiHandlerEvent @event, CancellationToken ct = default)
        {
            throw new InvalidOperationException("Event handler threw");
        }
    }

    private sealed class SuccessfulEventHandler : IEventHandler<MultiHandlerEvent>
    {
        public static int ReceivedCount;

        public ValueTask HandleAsync(MultiHandlerEvent @event, CancellationToken ct = default)
        {
            Interlocked.Increment(ref ReceivedCount);
            return ValueTask.CompletedTask;
        }
    }

    [MemoryPackable]
    private partial record NullResponseCommand : IRequest<NullResponseResult>
    {
        public required long MessageId { get; init; }
    }

    [MemoryPackable]
    private partial record NullResponseResult { }

    private sealed class NullResponseHandler : IRequestHandler<NullResponseCommand, NullResponseResult>
    {
        public ValueTask<CatgaResult<NullResponseResult>> HandleAsync(NullResponseCommand request, CancellationToken ct = default)
        {
            return new ValueTask<CatgaResult<NullResponseResult>>(CatgaResult<NullResponseResult>.Success(null!));
        }
    }

    [MemoryPackable]
    private partial record SlowCommand : IRequest<SlowResult>
    {
        public required long MessageId { get; init; }
        public int DelayMs { get; init; }
    }

    [MemoryPackable]
    private partial record SlowResult { }

    private sealed class SlowHandler : IRequestHandler<SlowCommand, SlowResult>
    {
        public async ValueTask<CatgaResult<SlowResult>> HandleAsync(SlowCommand request, CancellationToken ct = default)
        {
            await Task.Delay(request.DelayMs, ct);
            return CatgaResult<SlowResult>.Success(new SlowResult());
        }
    }

    [MemoryPackable]
    private partial record ArgumentExceptionCommand : IRequest<ArgumentExceptionResult>
    {
        public required long MessageId { get; init; }
    }

    [MemoryPackable]
    private partial record ArgumentExceptionResult { }

    private sealed class ArgumentExceptionHandler : IRequestHandler<ArgumentExceptionCommand, ArgumentExceptionResult>
    {
        public ValueTask<CatgaResult<ArgumentExceptionResult>> HandleAsync(ArgumentExceptionCommand request, CancellationToken ct = default)
        {
            throw new ArgumentException("Invalid argument");
        }
    }

    #endregion
}



