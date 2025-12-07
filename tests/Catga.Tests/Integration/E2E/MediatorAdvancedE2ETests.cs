using Catga.Abstractions;
using Catga.Core;
using Catga.DependencyInjection;
using FluentAssertions;
using MemoryPack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Catga.Tests.Integration.E2E;

/// <summary>
/// Advanced E2E tests for CatgaMediator covering edge cases and complex scenarios
/// </summary>
public sealed partial class MediatorAdvancedE2ETests
{
    #region Handler Resolution Tests

    [Fact]
    public async Task SendAsync_WithScopedHandler_ShouldCreateNewInstancePerScope()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<IRequestHandler<ScopedTestCommand, ScopedTestResult>, ScopedTestHandler>();
        var sp = services.BuildServiceProvider();

        // Act
        string instanceId1, instanceId2;
        using (var scope1 = sp.CreateScope())
        {
            var mediator = scope1.ServiceProvider.GetRequiredService<ICatgaMediator>();
            var result = await mediator.SendAsync<ScopedTestCommand, ScopedTestResult>(
                new ScopedTestCommand { MessageId = MessageExtensions.NewMessageId() });
            instanceId1 = result.Value!.HandlerInstanceId;
        }

        using (var scope2 = sp.CreateScope())
        {
            var mediator = scope2.ServiceProvider.GetRequiredService<ICatgaMediator>();
            var result = await mediator.SendAsync<ScopedTestCommand, ScopedTestResult>(
                new ScopedTestCommand { MessageId = MessageExtensions.NewMessageId() });
            instanceId2 = result.Value!.HandlerInstanceId;
        }

        // Assert - Different scopes should have different handler instances
        instanceId1.Should().NotBe(instanceId2);
    }

    [Fact]
    public async Task SendAsync_WithSingletonHandler_ShouldReuseInstance()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddSingleton<IRequestHandler<SingletonTestCommand, SingletonTestResult>, SingletonTestHandler>();
        var sp = services.BuildServiceProvider();

        // Act
        string instanceId1, instanceId2;
        using (var scope1 = sp.CreateScope())
        {
            var mediator = scope1.ServiceProvider.GetRequiredService<ICatgaMediator>();
            var result = await mediator.SendAsync<SingletonTestCommand, SingletonTestResult>(
                new SingletonTestCommand { MessageId = MessageExtensions.NewMessageId() });
            instanceId1 = result.Value!.HandlerInstanceId;
        }

        using (var scope2 = sp.CreateScope())
        {
            var mediator = scope2.ServiceProvider.GetRequiredService<ICatgaMediator>();
            var result = await mediator.SendAsync<SingletonTestCommand, SingletonTestResult>(
                new SingletonTestCommand { MessageId = MessageExtensions.NewMessageId() });
            instanceId2 = result.Value!.HandlerInstanceId;
        }

        // Assert - Singleton should reuse same instance
        instanceId1.Should().Be(instanceId2);
    }

    #endregion

    #region Event Fan-out Tests

    [Fact]
    public async Task PublishAsync_WithMultipleHandlers_ShouldExecuteAll()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        var tracker = new EventTracker();
        services.AddSingleton(tracker);
        services.AddScoped<IEventHandler<MultiHandlerEvent>, MultiHandlerEventHandler1>();
        services.AddScoped<IEventHandler<MultiHandlerEvent>, MultiHandlerEventHandler2>();
        services.AddScoped<IEventHandler<MultiHandlerEvent>, MultiHandlerEventHandler3>();
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        // Act
        await mediator.PublishAsync(new MultiHandlerEvent
        {
            MessageId = MessageExtensions.NewMessageId(),
            Data = "test-data"
        });

        // Assert
        tracker.HandledBy.Should().HaveCount(3);
        tracker.HandledBy.Should().Contain("Handler1");
        tracker.HandledBy.Should().Contain("Handler2");
        tracker.HandledBy.Should().Contain("Handler3");
    }

    [Fact]
    public async Task PublishAsync_WithOneHandlerFailing_ShouldStillExecuteOthers()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        var tracker = new EventTracker();
        services.AddSingleton(tracker);
        services.AddScoped<IEventHandler<PartialFailEvent>, PartialFailEventHandler1>();
        services.AddScoped<IEventHandler<PartialFailEvent>, PartialFailEventHandlerFailing>();
        services.AddScoped<IEventHandler<PartialFailEvent>, PartialFailEventHandler3>();
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        // Act
        await mediator.PublishAsync(new PartialFailEvent
        {
            MessageId = MessageExtensions.NewMessageId()
        });

        // Assert - All handlers should be attempted
        tracker.HandledBy.Should().Contain("Handler1");
        tracker.HandledBy.Should().Contain("Handler3");
    }

    #endregion

    #region Batch Processing Tests

    [Fact]
    public async Task SendBatchAsync_WithMixedResults_ShouldReturnAllResults()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<IRequestHandler<BatchItemCommand, BatchItemResult>, BatchItemHandler>();
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        var commands = new List<BatchItemCommand>
        {
            new() { MessageId = MessageExtensions.NewMessageId(), ItemId = "ITEM-1", ShouldFail = false },
            new() { MessageId = MessageExtensions.NewMessageId(), ItemId = "ITEM-2", ShouldFail = true },
            new() { MessageId = MessageExtensions.NewMessageId(), ItemId = "ITEM-3", ShouldFail = false },
            new() { MessageId = MessageExtensions.NewMessageId(), ItemId = "ITEM-4", ShouldFail = true },
            new() { MessageId = MessageExtensions.NewMessageId(), ItemId = "ITEM-5", ShouldFail = false },
        };

        // Act
        var results = await mediator.SendBatchAsync<BatchItemCommand, BatchItemResult>(commands);

        // Assert
        results.Should().HaveCount(5);
        results[0].IsSuccess.Should().BeTrue();
        results[1].IsSuccess.Should().BeFalse();
        results[2].IsSuccess.Should().BeTrue();
        results[3].IsSuccess.Should().BeFalse();
        results[4].IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task PublishBatchAsync_ShouldPublishAllEvents()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        var tracker = new EventTracker();
        services.AddSingleton(tracker);
        services.AddScoped<IEventHandler<BatchEvent>, BatchEventHandler>();
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        var events = Enumerable.Range(1, 10)
            .Select(i => new BatchEvent
            {
                MessageId = MessageExtensions.NewMessageId(),
                Index = i
            })
            .ToList();

        // Act
        await mediator.PublishBatchAsync(events);

        // Assert
        tracker.EventIndices.Should().HaveCount(10);
        tracker.EventIndices.Should().BeEquivalentTo(Enumerable.Range(1, 10));
    }

    #endregion

    #region Stream Processing Tests

    [Fact]
    public async Task SendStreamAsync_ShouldProcessAllItems()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<IRequestHandler<StreamItemCommand, StreamItemResult>, StreamItemHandler>();
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        async IAsyncEnumerable<StreamItemCommand> GenerateCommands()
        {
            for (int i = 1; i <= 5; i++)
            {
                yield return new StreamItemCommand
                {
                    MessageId = MessageExtensions.NewMessageId(),
                    Value = i * 10
                };
                await Task.Delay(10); // Simulate async source
            }
        }

        // Act
        var results = new List<CatgaResult<StreamItemResult>>();
        await foreach (var result in mediator.SendStreamAsync<StreamItemCommand, StreamItemResult>(GenerateCommands()))
        {
            results.Add(result);
        }

        // Assert
        results.Should().HaveCount(5);
        results.All(r => r.IsSuccess).Should().BeTrue();
        results.Select(r => r.Value!.ProcessedValue).Should().BeEquivalentTo(new[] { 20, 40, 60, 80, 100 });
    }

    #endregion

    #region Dependency Injection Tests

    [Fact]
    public async Task Handler_WithMultipleDependencies_ShouldResolveAll()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddSingleton<IDependencyA, DependencyA>();
        services.AddSingleton<IDependencyB, DependencyB>();
        services.AddSingleton<IDependencyC, DependencyC>();
        services.AddScoped<IRequestHandler<MultiDependencyCommand, MultiDependencyResult>, MultiDependencyHandler>();
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        // Act
        var result = await mediator.SendAsync<MultiDependencyCommand, MultiDependencyResult>(
            new MultiDependencyCommand { MessageId = MessageExtensions.NewMessageId() });

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.DependencyAValue.Should().Be("A");
        result.Value.DependencyBValue.Should().Be("B");
        result.Value.DependencyCValue.Should().Be("C");
    }

    #endregion

    #region Test Types

    // Scoped/Singleton tests
    [MemoryPackable]
    private partial record ScopedTestCommand : IRequest<ScopedTestResult>
    {
        public required long MessageId { get; init; }
    }

    [MemoryPackable]
    private partial record ScopedTestResult(string HandlerInstanceId);

    private sealed class ScopedTestHandler : IRequestHandler<ScopedTestCommand, ScopedTestResult>
    {
        private readonly string _instanceId = Guid.NewGuid().ToString();

        public ValueTask<CatgaResult<ScopedTestResult>> HandleAsync(ScopedTestCommand request, CancellationToken ct)
        {
            return new ValueTask<CatgaResult<ScopedTestResult>>(CatgaResult<ScopedTestResult>.Success(new ScopedTestResult(_instanceId)));
        }
    }

    [MemoryPackable]
    private partial record SingletonTestCommand : IRequest<SingletonTestResult>
    {
        public required long MessageId { get; init; }
    }

    [MemoryPackable]
    private partial record SingletonTestResult(string HandlerInstanceId);

    private sealed class SingletonTestHandler : IRequestHandler<SingletonTestCommand, SingletonTestResult>
    {
        private readonly string _instanceId = Guid.NewGuid().ToString();

        public ValueTask<CatgaResult<SingletonTestResult>> HandleAsync(SingletonTestCommand request, CancellationToken ct)
        {
            return new ValueTask<CatgaResult<SingletonTestResult>>(CatgaResult<SingletonTestResult>.Success(new SingletonTestResult(_instanceId)));
        }
    }

    // Event fan-out tests
    private sealed class EventTracker
    {
        private readonly object _lock = new();
        public List<string> HandledBy { get; } = new();
        public List<int> EventIndices { get; } = new();

        public void AddHandler(string name)
        {
            lock (_lock) HandledBy.Add(name);
        }

        public void AddEventIndex(int index)
        {
            lock (_lock) EventIndices.Add(index);
        }
    }

    [MemoryPackable]
    private partial record MultiHandlerEvent : IEvent
    {
        public required long MessageId { get; init; }
        public string Data { get; init; } = "";
    }

    private sealed class MultiHandlerEventHandler1 : IEventHandler<MultiHandlerEvent>
    {
        private readonly EventTracker _tracker;
        public MultiHandlerEventHandler1(EventTracker tracker) => _tracker = tracker;
        public ValueTask HandleAsync(MultiHandlerEvent @event, CancellationToken ct)
        {
            _tracker.AddHandler("Handler1");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class MultiHandlerEventHandler2 : IEventHandler<MultiHandlerEvent>
    {
        private readonly EventTracker _tracker;
        public MultiHandlerEventHandler2(EventTracker tracker) => _tracker = tracker;
        public ValueTask HandleAsync(MultiHandlerEvent @event, CancellationToken ct)
        {
            _tracker.AddHandler("Handler2");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class MultiHandlerEventHandler3 : IEventHandler<MultiHandlerEvent>
    {
        private readonly EventTracker _tracker;
        public MultiHandlerEventHandler3(EventTracker tracker) => _tracker = tracker;
        public ValueTask HandleAsync(MultiHandlerEvent @event, CancellationToken ct)
        {
            _tracker.AddHandler("Handler3");
            return ValueTask.CompletedTask;
        }
    }

    [MemoryPackable]
    private partial record PartialFailEvent : IEvent
    {
        public required long MessageId { get; init; }
    }

    private sealed class PartialFailEventHandler1 : IEventHandler<PartialFailEvent>
    {
        private readonly EventTracker _tracker;
        public PartialFailEventHandler1(EventTracker tracker) => _tracker = tracker;
        public ValueTask HandleAsync(PartialFailEvent @event, CancellationToken ct)
        {
            _tracker.AddHandler("Handler1");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class PartialFailEventHandlerFailing : IEventHandler<PartialFailEvent>
    {
        public ValueTask HandleAsync(PartialFailEvent @event, CancellationToken ct)
        {
            throw new InvalidOperationException("Intentional failure");
        }
    }

    private sealed class PartialFailEventHandler3 : IEventHandler<PartialFailEvent>
    {
        private readonly EventTracker _tracker;
        public PartialFailEventHandler3(EventTracker tracker) => _tracker = tracker;
        public ValueTask HandleAsync(PartialFailEvent @event, CancellationToken ct)
        {
            _tracker.AddHandler("Handler3");
            return ValueTask.CompletedTask;
        }
    }

    // Batch tests
    [MemoryPackable]
    private partial record BatchItemCommand : IRequest<BatchItemResult>
    {
        public required long MessageId { get; init; }
        public string ItemId { get; init; } = "";
        public bool ShouldFail { get; init; }
    }

    [MemoryPackable]
    private partial record BatchItemResult(string ProcessedItemId);

    private sealed class BatchItemHandler : IRequestHandler<BatchItemCommand, BatchItemResult>
    {
        public ValueTask<CatgaResult<BatchItemResult>> HandleAsync(BatchItemCommand request, CancellationToken ct)
        {
            if (request.ShouldFail)
                return new ValueTask<CatgaResult<BatchItemResult>>(CatgaResult<BatchItemResult>.Failure($"Failed: {request.ItemId}"));
            return new ValueTask<CatgaResult<BatchItemResult>>(CatgaResult<BatchItemResult>.Success(new BatchItemResult(request.ItemId)));
        }
    }

    [MemoryPackable]
    private partial record BatchEvent : IEvent
    {
        public required long MessageId { get; init; }
        public int Index { get; init; }
    }

    private sealed class BatchEventHandler : IEventHandler<BatchEvent>
    {
        private readonly EventTracker _tracker;
        public BatchEventHandler(EventTracker tracker) => _tracker = tracker;
        public ValueTask HandleAsync(BatchEvent @event, CancellationToken ct)
        {
            _tracker.AddEventIndex(@event.Index);
            return ValueTask.CompletedTask;
        }
    }

    // Stream tests
    [MemoryPackable]
    private partial record StreamItemCommand : IRequest<StreamItemResult>
    {
        public required long MessageId { get; init; }
        public int Value { get; init; }
    }

    [MemoryPackable]
    private partial record StreamItemResult(int ProcessedValue);

    private sealed class StreamItemHandler : IRequestHandler<StreamItemCommand, StreamItemResult>
    {
        public ValueTask<CatgaResult<StreamItemResult>> HandleAsync(StreamItemCommand request, CancellationToken ct)
        {
            return new ValueTask<CatgaResult<StreamItemResult>>(CatgaResult<StreamItemResult>.Success(
                new StreamItemResult(request.Value * 2)));
        }
    }

    // Multi-dependency tests
    private interface IDependencyA { string GetValue(); }
    private interface IDependencyB { string GetValue(); }
    private interface IDependencyC { string GetValue(); }

    private sealed class DependencyA : IDependencyA { public string GetValue() => "A"; }
    private sealed class DependencyB : IDependencyB { public string GetValue() => "B"; }
    private sealed class DependencyC : IDependencyC { public string GetValue() => "C"; }

    [MemoryPackable]
    private partial record MultiDependencyCommand : IRequest<MultiDependencyResult>
    {
        public required long MessageId { get; init; }
    }

    [MemoryPackable]
    private partial record MultiDependencyResult(string DependencyAValue, string DependencyBValue, string DependencyCValue);

    private sealed class MultiDependencyHandler : IRequestHandler<MultiDependencyCommand, MultiDependencyResult>
    {
        private readonly IDependencyA _a;
        private readonly IDependencyB _b;
        private readonly IDependencyC _c;

        public MultiDependencyHandler(IDependencyA a, IDependencyB b, IDependencyC c)
        {
            _a = a;
            _b = b;
            _c = c;
        }

        public ValueTask<CatgaResult<MultiDependencyResult>> HandleAsync(MultiDependencyCommand request, CancellationToken ct)
        {
            return new ValueTask<CatgaResult<MultiDependencyResult>>(CatgaResult<MultiDependencyResult>.Success(
                new MultiDependencyResult(_a.GetValue(), _b.GetValue(), _c.GetValue())));
        }
    }

    #endregion
}
