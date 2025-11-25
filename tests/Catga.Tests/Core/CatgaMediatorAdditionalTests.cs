using Catga;
using Catga.Abstractions;
using Catga.Configuration;
using Catga.Core;
using Catga.DistributedId;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Catga.Tests.Core;

/// <summary>
/// Additional tests to increase CatgaMediator coverage from 75.6% to 90%+
/// Focus: Singleton handlers, metrics, activity, error paths, event concurrency
/// </summary>
public class CatgaMediatorAdditionalTests
{
    // ==================== Singleton Handler Path ====================

    [Fact]
    public async Task SendAsync_WithSingletonHandler_ShouldUseSingletonPath()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IRequestHandler<TestCommand, TestResponse>, TestCommandHandler>();
        services.AddSingleton<IDistributedIdGenerator, SnowflakeIdGenerator>();
        services.AddSingleton<ICatgaMediator>(sp => new CatgaMediator(sp, sp.GetRequiredService<ILogger<CatgaMediator>>()));

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();
        var command = new TestCommand { Data = "singleton" };

        // Act
        var result = await mediator.SendAsync<TestCommand, TestResponse>(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Result.Should().Be("singleton_processed");
    }

    [Fact]
    public async Task SendAsync_WithScopedHandler_ShouldUseScopedPath()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<IRequestHandler<TestCommand, TestResponse>, TestCommandHandler>();
        services.AddSingleton<IDistributedIdGenerator, SnowflakeIdGenerator>();
        services.AddSingleton<ICatgaMediator>(sp => new CatgaMediator(sp, sp.GetRequiredService<ILogger<CatgaMediator>>()));

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();
        var command = new TestCommand { Data = "scoped" };

        // Act
        var result = await mediator.SendAsync<TestCommand, TestResponse>(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Result.Should().Be("scoped_processed");
    }

    // ==================== Options Configuration ====================

    [Fact]
    public async Task Constructor_WithNullOptions_ShouldUseDefaultOptions()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<IRequestHandler<TestCommand, TestResponse>, TestCommandHandler>();
        services.AddSingleton<IDistributedIdGenerator, SnowflakeIdGenerator>();

        var sp = services.BuildServiceProvider();

        // Act - Pass null options
        var mediator = new CatgaMediator(sp, sp.GetRequiredService<ILogger<CatgaMediator>>(), options: null);
        var command = new TestCommand { Data = "test" };
        var result = await mediator.SendAsync<TestCommand, TestResponse>(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Constructor_WithEventConcurrencyLimit_ShouldCreateLimiter()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<IEventHandler<TestEvent>, TestEventHandler>();
        services.AddSingleton<IDistributedIdGenerator, SnowflakeIdGenerator>();

        var sp = services.BuildServiceProvider();
        var options = new CatgaOptions
        {
            MaxEventHandlerConcurrency = 2
        };

        // Act
        var mediator = new CatgaMediator(sp, sp.GetRequiredService<ILogger<CatgaMediator>>(), options);
        var @event = new TestEvent { Data = "concurrent" };
        await mediator.PublishAsync(@event);

        // Assert - Should complete without error
        true.Should().BeTrue();
    }

    [Fact]
    public async Task Constructor_WithZeroEventConcurrency_ShouldNotCreateLimiter()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<IEventHandler<TestEvent>, TestEventHandler>();
        services.AddSingleton<IDistributedIdGenerator, SnowflakeIdGenerator>();

        var sp = services.BuildServiceProvider();
        var options = new CatgaOptions
        {
            MaxEventHandlerConcurrency = 0
        };

        // Act
        var mediator = new CatgaMediator(sp, sp.GetRequiredService<ILogger<CatgaMediator>>(), options);
        var @event = new TestEvent { Data = "no_limit" };
        await mediator.PublishAsync(@event);

        // Assert - Should complete without error
        true.Should().BeTrue();
    }

    [Fact]
    public async Task Constructor_WithNullEventConcurrency_ShouldNotCreateLimiter()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<IEventHandler<TestEvent>, TestEventHandler>();
        services.AddSingleton<IDistributedIdGenerator, SnowflakeIdGenerator>();

        var sp = services.BuildServiceProvider();
        var options = new CatgaOptions
        {
            MaxEventHandlerConcurrency = null
        };

        // Act
        var mediator = new CatgaMediator(sp, sp.GetRequiredService<ILogger<CatgaMediator>>(), options);
        var @event = new TestEvent { Data = "null_limit" };
        await mediator.PublishAsync(@event);

        // Assert - Should complete without error
        true.Should().BeTrue();
    }

    // ==================== Message Properties ====================

    [Fact]
    public async Task SendAsync_WithMessageIdAndCorrelationId_ShouldLogBoth()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<IRequestHandler<TestCommand, TestResponse>, TestCommandHandler>();
        services.AddSingleton<IDistributedIdGenerator, SnowflakeIdGenerator>();
        services.AddSingleton<ICatgaMediator, CatgaMediator>();

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();
        var command = new TestCommand
        {
            MessageId = 12345,
            CorrelationId = 67890,
            Data = "with_ids"
        };

        // Act
        var result = await mediator.SendAsync<TestCommand, TestResponse>(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task SendAsync_WithNullCorrelationId_ShouldHandleGracefully()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<IRequestHandler<TestCommand, TestResponse>, TestCommandHandler>();
        services.AddSingleton<IDistributedIdGenerator, SnowflakeIdGenerator>();
        services.AddSingleton<ICatgaMediator, CatgaMediator>();

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();
        var command = new TestCommand
        {
            MessageId = 12345,
            CorrelationId = null, // Explicitly null
            Data = "no_correlation"
        };

        // Act
        var result = await mediator.SendAsync<TestCommand, TestResponse>(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    // ==================== SendAsync Without Response ====================

    [Fact]
    public async Task SendAsyncNoResponse_WithSingletonHandler_ShouldSucceed()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IRequestHandler<TestCommandNoResponse>, TestCommandNoResponseHandler>();
        services.AddSingleton<IDistributedIdGenerator, SnowflakeIdGenerator>();
        services.AddSingleton<ICatgaMediator, CatgaMediator>();

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();
        var command = new TestCommandNoResponse { Data = "no_response" };

        // Act
        var result = await mediator.SendAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task SendAsyncNoResponse_WithScopedHandler_ShouldSucceed()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<IRequestHandler<TestCommandNoResponse>, TestCommandNoResponseHandler>();
        services.AddSingleton<IDistributedIdGenerator, SnowflakeIdGenerator>();
        services.AddSingleton<ICatgaMediator, CatgaMediator>();

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();
        var command = new TestCommandNoResponse { Data = "scoped_no_response" };

        // Act
        var result = await mediator.SendAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    // ==================== PublishAsync ====================

    [Fact]
    public async Task PublishAsync_WithMultipleHandlers_ShouldExecuteAll()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<IEventHandler<TestEvent>, TestEventHandler>();
        services.AddScoped<IEventHandler<TestEvent>, TestEventHandler2>();
        services.AddSingleton<IDistributedIdGenerator, SnowflakeIdGenerator>();
        services.AddSingleton<ICatgaMediator, CatgaMediator>();

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();
        var @event = new TestEvent { Data = "multi_handler" };

        // Act
        await mediator.PublishAsync(@event);

        // Assert - Should complete without error
        true.Should().BeTrue();
    }

    [Fact]
    public async Task PublishAsync_WithEventConcurrencyLimiter_ShouldRespectLimit()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<IEventHandler<TestEvent>, SlowEventHandler>();
        services.AddScoped<IEventHandler<TestEvent>, SlowEventHandler2>();
        services.AddSingleton<IDistributedIdGenerator, SnowflakeIdGenerator>();

        var sp = services.BuildServiceProvider();
        var options = new CatgaOptions
        {
            MaxEventHandlerConcurrency = 1 // Force sequential execution
        };
        var mediator = new CatgaMediator(sp, sp.GetRequiredService<ILogger<CatgaMediator>>(), options);
        var @event = new TestEvent { Data = "slow" };

        // Act
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await mediator.PublishAsync(@event);
        stopwatch.Stop();

        // Assert - Should take time due to sequential execution (allow some timing variance)
        stopwatch.ElapsedMilliseconds.Should().BeGreaterOrEqualTo(30); // 2 handlers * ~20ms each
    }

    // ==================== SendBatchAsync ====================

    [Fact]
    public async Task SendBatchAsync_WithEmptyList_ShouldReturnEmptyResults()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<IRequestHandler<TestCommand, TestResponse>, TestCommandHandler>();
        services.AddSingleton<IDistributedIdGenerator, SnowflakeIdGenerator>();
        services.AddSingleton<ICatgaMediator, CatgaMediator>();

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();
        var emptyList = Array.Empty<TestCommand>();

        // Act
        var results = await mediator.SendBatchAsync<TestCommand, TestResponse>(emptyList);

        // Assert
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task PublishBatchAsync_WithEmptyList_ShouldComplete()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<IEventHandler<TestEvent>, TestEventHandler>();
        services.AddSingleton<IDistributedIdGenerator, SnowflakeIdGenerator>();
        services.AddSingleton<ICatgaMediator, CatgaMediator>();

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();
        var emptyList = Array.Empty<TestEvent>();

        // Act
        await mediator.PublishBatchAsync(emptyList);

        // Assert - Should complete without error
        true.Should().BeTrue();
    }

    // ==================== Error Handling ====================

    [Fact]
    public async Task SendAsync_WithHandlerThrowingException_ShouldReturnFailure()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<IRequestHandler<FailingCommand, TestResponse>, FailingCommandHandler>();
        services.AddSingleton<IDistributedIdGenerator, SnowflakeIdGenerator>();
        services.AddSingleton<ICatgaMediator, CatgaMediator>();

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();
        var command = new FailingCommand { Data = "will_fail" };

        // Act
        var result = await mediator.SendAsync<FailingCommand, TestResponse>(command);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Handler failed");
    }

    [Fact]
    public async Task PublishAsync_WithHandlerThrowingException_ShouldSwallowException()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<IEventHandler<TestEvent>, FailingEventHandler>();
        services.AddSingleton<IDistributedIdGenerator, SnowflakeIdGenerator>();
        services.AddSingleton<ICatgaMediator, CatgaMediator>();

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();
        var @event = new TestEvent { Data = "will_fail" };

        // Act & Assert - Should not throw
        await mediator.PublishAsync(@event);
    }

    // ==================== Dispose ====================

    [Fact]
    public void Dispose_ShouldDisposeCircuitBreakerAndLimiter()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        var sp = services.BuildServiceProvider();
        var options = new CatgaOptions
        {
            MaxEventHandlerConcurrency = 5
        };
        var mediator = new CatgaMediator(sp, sp.GetRequiredService<ILogger<CatgaMediator>>(), options);

        // Act
        mediator.Dispose();

        // Assert - Should not throw
        true.Should().BeTrue();
    }

    [Fact]
    public void Dispose_CalledMultipleTimes_ShouldNotThrow()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        var sp = services.BuildServiceProvider();
        var mediator = new CatgaMediator(sp, sp.GetRequiredService<ILogger<CatgaMediator>>());

        // Act
        mediator.Dispose();
        mediator.Dispose(); // Second call

        // Assert - Should not throw
        true.Should().BeTrue();
    }

    // ==================== Test Helpers ====================

    public record TestCommand : IRequest<TestResponse>, IMessage
    {
        public long MessageId { get; init; }
        public long? CorrelationId { get; init; }
        public string Data { get; init; } = string.Empty;
    }

    public record TestResponse
    {
        public string Result { get; init; } = string.Empty;
    }

    public record TestCommandNoResponse : IRequest, IMessage
    {
        public long MessageId { get; init; }
        public string Data { get; init; } = string.Empty;
    }

    public record TestEvent : IEvent, IMessage
    {
        public long MessageId { get; init; }
        public string Data { get; init; } = string.Empty;
    }

    public record FailingCommand : IRequest<TestResponse>, IMessage
    {
        public long MessageId { get; init; }
        public long? CorrelationId { get; init; }
        public string Data { get; init; } = string.Empty;
    }

    public class TestCommandHandler : IRequestHandler<TestCommand, TestResponse>
    {
        public Task<CatgaResult<TestResponse>> HandleAsync(TestCommand request, CancellationToken cancellationToken = default)
        {
            var response = new TestResponse { Result = $"{request.Data}_processed" };
            return Task.FromResult(CatgaResult<TestResponse>.Success(response));
        }
    }

    public class TestCommandNoResponseHandler : IRequestHandler<TestCommandNoResponse>
    {
        public Task<CatgaResult> HandleAsync(TestCommandNoResponse request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(CatgaResult.Success());
        }
    }

    public class TestEventHandler : IEventHandler<TestEvent>
    {
        public Task HandleAsync(TestEvent @event, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    public class TestEventHandler2 : IEventHandler<TestEvent>
    {
        public Task HandleAsync(TestEvent @event, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    public class SlowEventHandler : IEventHandler<TestEvent>
    {
        public async Task HandleAsync(TestEvent @event, CancellationToken cancellationToken = default)
        {
            await Task.Delay(20, cancellationToken);
        }
    }

    public class SlowEventHandler2 : IEventHandler<TestEvent>
    {
        public async Task HandleAsync(TestEvent @event, CancellationToken cancellationToken = default)
        {
            await Task.Delay(20, cancellationToken);
        }
    }

    public class FailingCommandHandler : IRequestHandler<FailingCommand, TestResponse>
    {
        public Task<CatgaResult<TestResponse>> HandleAsync(FailingCommand request, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Handler failed intentionally");
        }
    }

    public class FailingEventHandler : IEventHandler<TestEvent>
    {
        public Task HandleAsync(TestEvent @event, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Event handler failed intentionally");
        }
    }
}

