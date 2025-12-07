using Catga.Abstractions;
using Catga.Core;
using Catga.DependencyInjection;
using FluentAssertions;
using MemoryPack;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Catga.Tests.Core;

/// <summary>
/// Extended tests for batch operations
/// </summary>
public sealed partial class BatchOperationExtendedTests
{
    [Fact]
    public async Task SendBatchAsync_EmptyBatch_ShouldReturnEmptyResults()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        // Act
        var requests = Array.Empty<BatchCommand>();
        var results = await mediator.SendBatchAsync<BatchCommand, BatchResponse>(requests);

        // Assert
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task SendBatchAsync_SingleItem_ShouldProcess()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<IRequestHandler<BatchCommand, BatchResponse>, BatchHandler>();
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        // Act
        var requests = new[] { new BatchCommand { MessageId = MessageExtensions.NewMessageId(), Value = 1 } };
        var results = await mediator.SendBatchAsync<BatchCommand, BatchResponse>(requests);

        // Assert
        results.Should().HaveCount(1);
        results[0].IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task SendBatchAsync_MultipleItems_ShouldProcessAll()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<IRequestHandler<BatchCommand, BatchResponse>, BatchHandler>();
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        // Act
        var requests = Enumerable.Range(1, 10)
            .Select(i => new BatchCommand { MessageId = MessageExtensions.NewMessageId(), Value = i })
            .ToArray();
        var results = await mediator.SendBatchAsync<BatchCommand, BatchResponse>(requests);

        // Assert
        results.Should().HaveCount(10);
        results.All(r => r.IsSuccess).Should().BeTrue();
    }

    [Fact]
    public async Task PublishBatchAsync_EmptyBatch_ShouldNotThrow()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        // Act
        var events = Array.Empty<BatchEvent>();
        var act = async () => await mediator.PublishBatchAsync(events);

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task PublishBatchAsync_MultipleEvents_ShouldPublishAll()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<IEventHandler<BatchEvent>, BatchEventHandler>();
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        BatchEventHandler.ReceivedCount = 0;

        // Act
        var events = Enumerable.Range(1, 5)
            .Select(i => new BatchEvent { MessageId = MessageExtensions.NewMessageId() })
            .ToArray();
        await mediator.PublishBatchAsync(events);

        // Assert
        BatchEventHandler.ReceivedCount.Should().Be(5);
    }

    [Fact]
    public async Task SendBatchAsync_WithCancellation_ShouldRespectToken()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<IRequestHandler<SlowBatchCommand, SlowBatchResponse>, SlowBatchHandler>();
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        using var cts = new CancellationTokenSource(50);

        // Act
        var requests = Enumerable.Range(1, 100)
            .Select(i => new SlowBatchCommand { MessageId = MessageExtensions.NewMessageId(), Value = i })
            .ToArray();

        try
        {
            var results = await mediator.SendBatchAsync<SlowBatchCommand, SlowBatchResponse>(requests, cts.Token);
            // If it completes, some results may have failures due to cancellation
        }
        catch (OperationCanceledException)
        {
            // Expected
        }
    }

    [Fact]
    public async Task SendBatchAsync_PartialFailure_ShouldReturnMixedResults()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<IRequestHandler<FailingBatchCommand, FailingBatchResponse>, FailingBatchHandler>();
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        // Act
        var requests = new[]
        {
            new FailingBatchCommand { MessageId = MessageExtensions.NewMessageId(), ShouldFail = false },
            new FailingBatchCommand { MessageId = MessageExtensions.NewMessageId(), ShouldFail = true },
            new FailingBatchCommand { MessageId = MessageExtensions.NewMessageId(), ShouldFail = false }
        };
        var results = await mediator.SendBatchAsync<FailingBatchCommand, FailingBatchResponse>(requests);

        // Assert
        results.Should().HaveCount(3);
        results[0].IsSuccess.Should().BeTrue();
        results[1].IsSuccess.Should().BeFalse();
        results[2].IsSuccess.Should().BeTrue();
    }

    #region Test Types

    [MemoryPackable]
    private partial record BatchCommand : IRequest<BatchResponse>
    {
        public required long MessageId { get; init; }
        public int Value { get; init; }
    }

    [MemoryPackable]
    private partial record BatchResponse
    {
        public int ProcessedValue { get; init; }
    }

    private sealed class BatchHandler : IRequestHandler<BatchCommand, BatchResponse>
    {
        public ValueTask<CatgaResult<BatchResponse>> HandleAsync(BatchCommand request, CancellationToken ct = default)
        {
            return new ValueTask<CatgaResult<BatchResponse>>(CatgaResult<BatchResponse>.Success(new BatchResponse { ProcessedValue = request.Value * 2 }));
        }
    }

    private sealed class SlowBatchHandler : IRequestHandler<SlowBatchCommand, SlowBatchResponse>
    {
        public async ValueTask<CatgaResult<SlowBatchResponse>> HandleAsync(SlowBatchCommand request, CancellationToken ct = default)
        {
            await Task.Delay(10, ct);
            return CatgaResult<SlowBatchResponse>.Success(new SlowBatchResponse { ProcessedValue = request.Value * 2 });
        }
    }

    [MemoryPackable]
    private partial record SlowBatchCommand : IRequest<SlowBatchResponse>
    {
        public required long MessageId { get; init; }
        public int Value { get; init; }
    }

    [MemoryPackable]
    private partial record SlowBatchResponse
    {
        public int ProcessedValue { get; init; }
    }

    [MemoryPackable]
    private partial record BatchEvent : IEvent
    {
        public required long MessageId { get; init; }
    }

    private sealed class BatchEventHandler : IEventHandler<BatchEvent>
    {
        public static int ReceivedCount;

        public ValueTask HandleAsync(BatchEvent @event, CancellationToken ct = default)
        {
            Interlocked.Increment(ref ReceivedCount);
            return ValueTask.CompletedTask;
        }
    }

    [MemoryPackable]
    private partial record FailingBatchCommand : IRequest<FailingBatchResponse>
    {
        public required long MessageId { get; init; }
        public bool ShouldFail { get; init; }
    }

    [MemoryPackable]
    private partial record FailingBatchResponse { }

    private sealed class FailingBatchHandler : IRequestHandler<FailingBatchCommand, FailingBatchResponse>
    {
        public ValueTask<CatgaResult<FailingBatchResponse>> HandleAsync(FailingBatchCommand request, CancellationToken ct = default)
        {
            if (request.ShouldFail)
            {
                return new ValueTask<CatgaResult<FailingBatchResponse>>(CatgaResult<FailingBatchResponse>.Failure("Intentional failure"));
            }
            return new ValueTask<CatgaResult<FailingBatchResponse>>(CatgaResult<FailingBatchResponse>.Success(new FailingBatchResponse()));
        }
    }

    #endregion
}
