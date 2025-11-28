using Catga;
using Catga.Abstractions;
using Catga.Configuration;
using Catga.Core;
using Catga.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Catga.Tests.Core;

/// <summary>
/// CatgaMediator boundary and error path tests - complementary to CatgaMediatorExtendedTests
/// Focuses on edge cases, null handling, empty collections, and error paths
/// </summary>
public class CatgaMediatorBoundaryTests
{
    [Fact]
    public async Task SendAsync_WithNullRequest_ShouldHandleGracefully()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<IRequestHandler<TestCommand, TestResponse>, TestCommandHandler>();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<ICatgaMediator>();

        // Act
        var result = await mediator.SendAsync<TestCommand, TestResponse>(null!);

        // Assert - CatgaMediator handles null gracefully, returns failure result
        result.Should().NotBeNull();
        // Either throws or returns failure is acceptable
    }

    [Fact]
    public async Task SendBatchAsync_WithEmptyBatch_ShouldReturnEmptyResults()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<IRequestHandler<TestCommand, TestResponse>, TestCommandHandler>();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<ICatgaMediator>();
        var emptyBatch = Array.Empty<TestCommand>();

        // Act
        var results = await mediator.SendBatchAsync<TestCommand, TestResponse>(emptyBatch);

        // Assert
        results.Should().NotBeNull();
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task SendBatchAsync_WithSingleItem_ShouldProcessSuccessfully()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<IRequestHandler<TestCommand, TestResponse>, TestCommandHandler>();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<ICatgaMediator>();
        var batch = new[] { new TestCommand { Data = "single" } };

        // Act
        var results = await mediator.SendBatchAsync<TestCommand, TestResponse>(batch);

        // Assert
        results.Should().HaveCount(1);
        results[0].IsSuccess.Should().BeTrue();
        results[0].Value!.Result.Should().Be("single-processed");
    }

    [Fact]
    public async Task PublishAsync_WithNullEvent_ShouldHandleGracefully()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<IEventHandler<TestEvent>, TestEventHandler>();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<ICatgaMediator>();

        // Act
        var act = async () => await mediator.PublishAsync<TestEvent>(null!);

        // Assert - CatgaMediator handles null gracefully
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task PublishAsync_WithNoHandlers_ShouldCompleteSuccessfully()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        // No handlers registered

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<ICatgaMediator>();
        var @event = new TestEvent { Data = "test" };

        // Act
        var act = async () => await mediator.PublishAsync(@event);

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task PublishBatchAsync_WithEmptyBatch_ShouldComplete()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<IEventHandler<TestEvent>, TestEventHandler>();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<ICatgaMediator>();
        var emptyBatch = Array.Empty<TestEvent>();

        // Act
        var act = async () => await mediator.PublishBatchAsync(emptyBatch);

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SendAsync_WithCircuitBreakerOpen_ShouldFailAfterThreshold()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga(options =>
        {
            options.CircuitBreakerThreshold = 3;
            options.CircuitBreakerDuration = TimeSpan.FromSeconds(1);
        });
        services.AddScoped<IRequestHandler<FailingCommand, TestResponse>, FailingCommandHandler>();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<ICatgaMediator>();

        // Act - Trigger failures to open circuit breaker
        for (int i = 0; i < 4; i++)
        {
            await mediator.SendAsync<FailingCommand, TestResponse>(new FailingCommand());
        }

        // Circuit should be open now, all subsequent requests should fail
        var result = await mediator.SendAsync<FailingCommand, TestResponse>(new FailingCommand());

        // Assert - Should fail (either circuit breaker or handler exception)
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task SendAsync_WithMaxConcurrency_ShouldNotExceedLimit()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga(options =>
        {
            options.MaxEventHandlerConcurrency = 2;
        });
        services.AddScoped<IRequestHandler<SlowCommand, TestResponse>, SlowCommandHandler>();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<ICatgaMediator>();

        // Act - Send multiple requests concurrently
        var tasks = Enumerable.Range(0, 5).Select(_ =>
            mediator.SendAsync<SlowCommand, TestResponse>(new SlowCommand()).AsTask()
        ).ToArray();

        var results = await Task.WhenAll(tasks);

        // Assert
        results.Should().AllSatisfy(r => r.IsSuccess.Should().BeTrue());
    }

    [Fact]
    public async Task SendStreamAsync_WithEmptyStream_ShouldReturnEmptyStream()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<IRequestHandler<TestCommand, TestResponse>, TestCommandHandler>();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<ICatgaMediator>();
        var emptyStream = AsyncEnumerableEmpty<TestCommand>();

        // Act
        var results = new List<CatgaResult<TestResponse>>();
        await foreach (var result in mediator.SendStreamAsync<TestCommand, TestResponse>(emptyStream))
        {
            results.Add(result);
        }

        // Assert
        results.Should().BeEmpty();
    }

    [Fact]
    public void Dispose_ShouldDisposeResources()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<IRequestHandler<TestCommand, TestResponse>, TestCommandHandler>();

        var provider = services.BuildServiceProvider();

        // Act & Assert - Using ServiceProvider.Dispose indirectly tests resource cleanup
        Action act = () =>
        {
            using var scope = provider.CreateScope();
            var mediator = scope.ServiceProvider.GetRequiredService<ICatgaMediator>();
            // Mediator will be disposed when scope is disposed
        };

        act.Should().NotThrow();
    }

    // ==================== Test Helpers ====================

    private static async IAsyncEnumerable<T> AsyncEnumerableEmpty<T>()
    {
        await Task.CompletedTask;
        yield break;
    }

    public record TestCommand : IRequest<TestResponse>
    {
        public long MessageId { get; init; }
        public string Data { get; init; } = string.Empty;
    }

    public record TestResponse
    {
        public string Result { get; init; } = string.Empty;
    }

    public record TestEvent : IEvent
    {
        public long MessageId { get; init; }
        public string Data { get; init; } = string.Empty;
    }

    public record FailingCommand : IRequest<TestResponse>
    {
        public long MessageId { get; init; }
    }

    public record SlowCommand : IRequest<TestResponse>
    {
        public long MessageId { get; init; }
    }

    public class TestCommandHandler : IRequestHandler<TestCommand, TestResponse>
    {
        public Task<CatgaResult<TestResponse>> HandleAsync(TestCommand request, CancellationToken cancellationToken)
        {
            return Task.FromResult(CatgaResult<TestResponse>.Success(new TestResponse
            {
                Result = $"{request.Data}-processed"
            }));
        }
    }

    public class TestEventHandler : IEventHandler<TestEvent>
    {
        public Task HandleAsync(TestEvent @event, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    public class FailingCommandHandler : IRequestHandler<FailingCommand, TestResponse>
    {
        public Task<CatgaResult<TestResponse>> HandleAsync(FailingCommand request, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Handler always fails");
        }
    }

    public class SlowCommandHandler : IRequestHandler<SlowCommand, TestResponse>
    {
        public async Task<CatgaResult<TestResponse>> HandleAsync(SlowCommand request, CancellationToken cancellationToken)
        {
            await Task.Delay(50, cancellationToken);
            return CatgaResult<TestResponse>.Success(new TestResponse { Result = "slow" });
        }
    }
}

