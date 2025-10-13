using Catga;
using Catga.Idempotency;
using Catga.InMemory;
using Catga.Messages;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Catga.Tests.Idempotency;

/// <summary>
/// 幂等性测试
/// </summary>
public class IdempotencyTests
{
    [Fact]
    public async Task ShardedIdempotencyStore_SameMessageId_ShouldReturnCachedResult()
    {
        // Arrange
        var store = new ShardedIdempotencyStore();
        var messageId = Guid.NewGuid().ToString();
        var expectedResult = new TestIdempotencyResult("cached-data");

        // Act
        await store.MarkAsProcessedAsync(messageId, expectedResult);
        var cachedResult = await store.GetCachedResultAsync<TestIdempotencyResult>(messageId);

        // Assert
        cachedResult.Should().NotBeNull();
        cachedResult!.Data.Should().Be("cached-data");
    }

    [Fact]
    public async Task ShardedIdempotencyStore_DifferentMessageIds_ShouldNotInterfere()
    {
        // Arrange
        var store = new ShardedIdempotencyStore();
        var messageId1 = Guid.NewGuid().ToString();
        var messageId2 = Guid.NewGuid().ToString();
        var result1 = new TestIdempotencyResult("data-1");
        var result2 = new TestIdempotencyResult("data-2");

        // Act
        await store.MarkAsProcessedAsync(messageId1, result1);
        await store.MarkAsProcessedAsync(messageId2, result2);

        var cached1 = await store.GetCachedResultAsync<TestIdempotencyResult>(messageId1);
        var cached2 = await store.GetCachedResultAsync<TestIdempotencyResult>(messageId2);

        // Assert
        cached1!.Data.Should().Be("data-1");
        cached2!.Data.Should().Be("data-2");
    }

    [Fact]
    public async Task ShardedIdempotencyStore_HasBeenProcessed_ShouldReturnTrue()
    {
        // Arrange
        var store = new ShardedIdempotencyStore();
        var messageId = Guid.NewGuid().ToString();

        // Act
        await store.MarkAsProcessedAsync<string>(messageId, "test");
        var hasBeenProcessed = await store.HasBeenProcessedAsync(messageId);

        // Assert
        hasBeenProcessed.Should().BeTrue();
    }

    [Fact]
    public async Task ShardedIdempotencyStore_NotProcessed_ShouldReturnFalse()
    {
        // Arrange
        var store = new ShardedIdempotencyStore();
        var messageId = Guid.NewGuid().ToString();

        // Act
        var hasBeenProcessed = await store.HasBeenProcessedAsync(messageId);

        // Assert
        hasBeenProcessed.Should().BeFalse();
    }

    [Fact]
    public async Task Mediator_WithExactlyOnceQoS_ShouldUseIdempotency()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        services.AddCatga();
        services.AddCatgaInMemoryTransport();
        services.AddCatgaInMemoryPersistence();

        services.AddSingleton<IRequestHandler<IdempotentCommand, string>, IdempotentCommandHandler>();

        var serviceProvider = services.BuildServiceProvider();
        var mediator = serviceProvider.GetRequiredService<ICatgaMediator>();

        var messageId = Guid.NewGuid().ToString();
        var command1 = new IdempotentCommand("test") { MessageId = messageId };
        var command2 = new IdempotentCommand("test") { MessageId = messageId };

        // Reset handler call count
        IdempotentCommandHandler.CallCount = 0;

        // Act
        var result1 = await mediator.SendAsync(command1);
        var result2 = await mediator.SendAsync(command2);

        // Assert
        result1.IsSuccess.Should().BeTrue();
        result2.IsSuccess.Should().BeTrue();
        result1.Value.Should().Be("Processed: test");
        result2.Value.Should().Be("Processed: test");
        
        // Handler should only be called once due to idempotency
        IdempotentCommandHandler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task ShardedIdempotencyStore_ConcurrentAccess_ShouldBeThreadSafe()
    {
        // Arrange
        var store = new ShardedIdempotencyStore();
        var messageId = Guid.NewGuid().ToString();
        var tasks = new List<Task>();

        // Act - Concurrent writes
        for (int i = 0; i < 100; i++)
        {
            var index = i;
            tasks.Add(Task.Run(async () =>
            {
                await store.MarkAsProcessedAsync(messageId, new TestIdempotencyResult($"data-{index}"));
            }));
        }

        await Task.WhenAll(tasks);

        // Assert - Should not throw and should have a result
        var hasBeenProcessed = await store.HasBeenProcessedAsync(messageId);
        hasBeenProcessed.Should().BeTrue();

        var cachedResult = await store.GetCachedResultAsync<TestIdempotencyResult>(messageId);
        cachedResult.Should().NotBeNull();
    }

    [Fact]
    public async Task ShardedIdempotencyStore_WithNullResult_ShouldCacheNull()
    {
        // Arrange
        var store = new ShardedIdempotencyStore();
        var messageId = Guid.NewGuid().ToString();

        // Act
        await store.MarkAsProcessedAsync<TestIdempotencyResult>(messageId, null);
        var hasBeenProcessed = await store.HasBeenProcessedAsync(messageId);
        var cachedResult = await store.GetCachedResultAsync<TestIdempotencyResult>(messageId);

        // Assert
        hasBeenProcessed.Should().BeTrue();
        cachedResult.Should().BeNull();
    }
}

// Test Messages and Handlers
public record TestIdempotencyResult(string Data);

public record IdempotentCommand(string Data) : IRequest<string>, IMessage
{
    public string MessageId { get; init; } = Guid.NewGuid().ToString();
    public string? CorrelationId { get; init; }
    public QualityOfService QoS { get; init; } = QualityOfService.ExactlyOnce;
}

public class IdempotentCommandHandler : IRequestHandler<IdempotentCommand, string>
{
    public static int CallCount = 0;

    public Task<CatgaResult<string>> HandleAsync(IdempotentCommand request, CancellationToken cancellationToken = default)
    {
        CallCount++;
        var result = $"Processed: {request.Data}";
        return Task.FromResult(CatgaResult<string>.Success(result));
    }
}

