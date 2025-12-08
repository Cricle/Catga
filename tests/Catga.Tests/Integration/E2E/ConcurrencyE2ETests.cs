using Catga.Abstractions;
using Catga.Core;
using Catga.DependencyInjection;
using FluentAssertions;
using MemoryPack;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Catga.Tests.Integration.E2E;

/// <summary>
/// End-to-end tests for concurrent message processing
/// </summary>
[Trait("Category", "Integration")]
public sealed partial class ConcurrencyE2ETests
{
    [Fact]
    public async Task Mediator_HighConcurrency_AllRequestsSucceed()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<IRequestHandler<ConcurrentRequest, ConcurrentResponse>, ConcurrentHandler>();

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        const int requestCount = 200;

        // Act
        var tasks = Enumerable.Range(0, requestCount).Select(async i =>
        {
            var request = new ConcurrentRequest { MessageId = MessageExtensions.NewMessageId(), Index = i };
            return await mediator.SendAsync<ConcurrentRequest, ConcurrentResponse>(request);
        });

        var results = await Task.WhenAll(tasks);

        // Assert
        results.Should().HaveCount(requestCount);
        results.Should().AllSatisfy(r => r.IsSuccess.Should().BeTrue());
        results.Select(r => r.Value!.ProcessedIndex).Should().BeEquivalentTo(Enumerable.Range(0, requestCount));
    }

    [Fact]
    public async Task Mediator_ParallelEvents_AllHandlersReceive()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<IEventHandler<ParallelEvent>, ParallelEventHandler1>();
        services.AddScoped<IEventHandler<ParallelEvent>, ParallelEventHandler2>();

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        ParallelEventHandler1.ReceivedCount = 0;
        ParallelEventHandler2.ReceivedCount = 0;

        const int eventCount = 50;

        // Act
        var tasks = Enumerable.Range(0, eventCount).Select(async i =>
        {
            var @event = new ParallelEvent { MessageId = MessageExtensions.NewMessageId(), Data = $"event-{i}" };
            await mediator.PublishAsync(@event);
        });

        await Task.WhenAll(tasks);

        // Assert
        ParallelEventHandler1.ReceivedCount.Should().Be(eventCount);
        ParallelEventHandler2.ReceivedCount.Should().Be(eventCount);
    }

    [Fact]
    public async Task Mediator_MixedRequestsAndEvents_ProcessCorrectly()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<IRequestHandler<MixedRequest, MixedResponse>, MixedRequestHandler>();
        services.AddScoped<IEventHandler<MixedEvent>, MixedEventHandler>();

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        MixedEventHandler.ReceivedCount = 0;

        const int count = 30;

        // Act - Interleave requests and events
        var tasks = new List<Task>();
        for (var i = 0; i < count; i++)
        {
            var index = i;
            tasks.Add(Task.Run(async () =>
            {
                var request = new MixedRequest { MessageId = MessageExtensions.NewMessageId(), Value = index };
                await mediator.SendAsync<MixedRequest, MixedResponse>(request);
            }));
            tasks.Add(Task.Run(async () =>
            {
                var @event = new MixedEvent { MessageId = MessageExtensions.NewMessageId(), Value = index };
                await mediator.PublishAsync(@event);
            }));
        }

        await Task.WhenAll(tasks);

        // Assert
        MixedEventHandler.ReceivedCount.Should().Be(count);
    }

    [Fact]
    public async Task Mediator_RapidFireRequests_NoDeadlocks()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<IRequestHandler<RapidRequest, RapidResponse>, RapidHandler>();

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        const int burstSize = 100;
        const int burstCount = 5;

        // Act - Multiple bursts of rapid requests
        for (var burst = 0; burst < burstCount; burst++)
        {
            var tasks = Enumerable.Range(0, burstSize).Select(async i =>
            {
                var request = new RapidRequest { MessageId = MessageExtensions.NewMessageId(), BurstId = burst, Index = i };
                return await mediator.SendAsync<RapidRequest, RapidResponse>(request);
            });

            var results = await Task.WhenAll(tasks);

            // Assert each burst
            results.Should().AllSatisfy(r => r.IsSuccess.Should().BeTrue());
        }
    }

    [Fact]
    public async Task Mediator_SlowHandler_DoesNotBlockOthers()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<IRequestHandler<SlowRequest, SlowResponse>, SlowHandler>();
        services.AddScoped<IRequestHandler<FastRequest, FastResponse>, FastHandler>();

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        // Act - Start slow request, then fire fast requests
        var slowTask = Task.Run(async () =>
        {
            var request = new SlowRequest { MessageId = MessageExtensions.NewMessageId(), DelayMs = 500 };
            return await mediator.SendAsync<SlowRequest, SlowResponse>(request);
        });

        await Task.Delay(50); // Let slow request start

        var fastTasks = Enumerable.Range(0, 10).Select(async i =>
        {
            var request = new FastRequest { MessageId = MessageExtensions.NewMessageId(), Index = i };
            return await mediator.SendAsync<FastRequest, FastResponse>(request);
        }).ToList();

        var fastResults = await Task.WhenAll(fastTasks);
        var slowResult = await slowTask;

        // Assert - Fast requests should complete before slow one
        fastResults.Should().AllSatisfy(r => r.IsSuccess.Should().BeTrue());
        slowResult.IsSuccess.Should().BeTrue();
    }

    #region Commands and Handlers

    [MemoryPackable]
    private partial record ConcurrentRequest : IRequest<ConcurrentResponse>
    {
        public required long MessageId { get; init; }
        public required int Index { get; init; }
    }

    [MemoryPackable]
    private partial record ConcurrentResponse
    {
        public required int ProcessedIndex { get; init; }
    }

    private sealed class ConcurrentHandler : IRequestHandler<ConcurrentRequest, ConcurrentResponse>
    {
        public async ValueTask<CatgaResult<ConcurrentResponse>> HandleAsync(ConcurrentRequest request, CancellationToken ct = default)
        {
            await Task.Delay(Random.Shared.Next(1, 10), ct);
            return CatgaResult<ConcurrentResponse>.Success(new ConcurrentResponse { ProcessedIndex = request.Index });
        }
    }

    [MemoryPackable]
    private partial record ParallelEvent : IEvent
    {
        public required long MessageId { get; init; }
        public required string Data { get; init; }
    }

    private sealed class ParallelEventHandler1 : IEventHandler<ParallelEvent>
    {
        public static int ReceivedCount;
        public ValueTask HandleAsync(ParallelEvent @event, CancellationToken ct = default)
        {
            Interlocked.Increment(ref ReceivedCount);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ParallelEventHandler2 : IEventHandler<ParallelEvent>
    {
        public static int ReceivedCount;
        public ValueTask HandleAsync(ParallelEvent @event, CancellationToken ct = default)
        {
            Interlocked.Increment(ref ReceivedCount);
            return ValueTask.CompletedTask;
        }
    }

    [MemoryPackable]
    private partial record MixedRequest : IRequest<MixedResponse>
    {
        public required long MessageId { get; init; }
        public required int Value { get; init; }
    }

    [MemoryPackable]
    private partial record MixedResponse
    {
        public required int Result { get; init; }
    }

    private sealed class MixedRequestHandler : IRequestHandler<MixedRequest, MixedResponse>
    {
        public ValueTask<CatgaResult<MixedResponse>> HandleAsync(MixedRequest request, CancellationToken ct = default)
        {
            return new ValueTask<CatgaResult<MixedResponse>>(CatgaResult<MixedResponse>.Success(new MixedResponse { Result = request.Value * 2 }));
        }
    }

    [MemoryPackable]
    private partial record MixedEvent : IEvent
    {
        public required long MessageId { get; init; }
        public required int Value { get; init; }
    }

    private sealed class MixedEventHandler : IEventHandler<MixedEvent>
    {
        public static int ReceivedCount;
        public ValueTask HandleAsync(MixedEvent @event, CancellationToken ct = default)
        {
            Interlocked.Increment(ref ReceivedCount);
            return ValueTask.CompletedTask;
        }
    }

    [MemoryPackable]
    private partial record RapidRequest : IRequest<RapidResponse>
    {
        public required long MessageId { get; init; }
        public required int BurstId { get; init; }
        public required int Index { get; init; }
    }

    [MemoryPackable]
    private partial record RapidResponse
    {
        public required int BurstId { get; init; }
        public required int Index { get; init; }
    }

    private sealed class RapidHandler : IRequestHandler<RapidRequest, RapidResponse>
    {
        public ValueTask<CatgaResult<RapidResponse>> HandleAsync(RapidRequest request, CancellationToken ct = default)
        {
            return new ValueTask<CatgaResult<RapidResponse>>(CatgaResult<RapidResponse>.Success(new RapidResponse { BurstId = request.BurstId, Index = request.Index }));
        }
    }

    [MemoryPackable]
    private partial record SlowRequest : IRequest<SlowResponse>
    {
        public required long MessageId { get; init; }
        public required int DelayMs { get; init; }
    }

    [MemoryPackable]
    private partial record SlowResponse
    {
        public required bool Completed { get; init; }
    }

    private sealed class SlowHandler : IRequestHandler<SlowRequest, SlowResponse>
    {
        public async ValueTask<CatgaResult<SlowResponse>> HandleAsync(SlowRequest request, CancellationToken ct = default)
        {
            await Task.Delay(request.DelayMs, ct);
            return CatgaResult<SlowResponse>.Success(new SlowResponse { Completed = true });
        }
    }

    [MemoryPackable]
    private partial record FastRequest : IRequest<FastResponse>
    {
        public required long MessageId { get; init; }
        public required int Index { get; init; }
    }

    [MemoryPackable]
    private partial record FastResponse
    {
        public required int Index { get; init; }
    }

    private sealed class FastHandler : IRequestHandler<FastRequest, FastResponse>
    {
        public ValueTask<CatgaResult<FastResponse>> HandleAsync(FastRequest request, CancellationToken ct = default)
        {
            return new ValueTask<CatgaResult<FastResponse>>(CatgaResult<FastResponse>.Success(new FastResponse { Index = request.Index }));
        }
    }

    #endregion
}



