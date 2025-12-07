using Catga.Abstractions;
using Catga.EventSourcing;
using FluentAssertions;
using Xunit;

namespace Catga.Tests.EventSourcing;

/// <summary>
/// Unit tests for LiveProjection.
/// </summary>
public class LiveProjectionTests
{
    [Fact]
    public async Task HandleAsync_AppliesEventToProjection()
    {
        // Arrange
        var projection = new TestProjection();
        var liveProjection = new LiveProjection<TestProjection>(projection);
        var @event = new TestEvent("test-data");

        // Act
        await liveProjection.HandleAsync(@event);

        // Assert
        projection.LastEvent.Should().Be("test-data");
        projection.EventCount.Should().Be(1);
    }

    [Fact]
    public async Task HandleAsync_MultipleEvents_AllApplied()
    {
        // Arrange
        var projection = new TestProjection();
        var liveProjection = new LiveProjection<TestProjection>(projection);

        // Act
        await liveProjection.HandleAsync(new TestEvent("event1"));
        await liveProjection.HandleAsync(new TestEvent("event2"));
        await liveProjection.HandleAsync(new TestEvent("event3"));

        // Assert
        projection.EventCount.Should().Be(3);
        projection.LastEvent.Should().Be("event3");
    }

    [Fact]
    public async Task HandleAsync_DifferentEventTypes_AllHandled()
    {
        // Arrange
        var projection = new MultiTypeProjection();
        var liveProjection = new LiveProjection<MultiTypeProjection>(projection);

        // Act
        await liveProjection.HandleAsync(new EventTypeA("a"));
        await liveProjection.HandleAsync(new EventTypeB(42));

        // Assert
        projection.TypeACount.Should().Be(1);
        projection.TypeBCount.Should().Be(1);
    }

    [Fact]
    public async Task HandleAsync_WithCancellation_PassesCancellationToken()
    {
        // Arrange
        var projection = new CancellationAwareProjection();
        var liveProjection = new LiveProjection<CancellationAwareProjection>(projection);
        using var cts = new CancellationTokenSource();

        // Act
        await liveProjection.HandleAsync(new TestEvent("test"), cts.Token);

        // Assert
        projection.ReceivedCancellationToken.Should().BeTrue();
    }

    #region Test helpers

    private record TestEvent(string Data) : IEvent
    {
        public long MessageId { get; init; } = Random.Shared.NextInt64();
    }

    private record EventTypeA(string Value) : IEvent
    {
        public long MessageId { get; init; } = Random.Shared.NextInt64();
    }

    private record EventTypeB(int Value) : IEvent
    {
        public long MessageId { get; init; } = Random.Shared.NextInt64();
    }

    private class TestProjection : IProjection
    {
        public string Name => "test";
        public string? LastEvent { get; private set; }
        public int EventCount { get; private set; }

        public ValueTask ApplyAsync(IEvent @event, CancellationToken ct = default)
        {
            if (@event is TestEvent te)
            {
                LastEvent = te.Data;
                EventCount++;
            }
            return ValueTask.CompletedTask;
        }

        public ValueTask ResetAsync(CancellationToken ct = default)
        {
            LastEvent = null;
            EventCount = 0;
            return ValueTask.CompletedTask;
        }
    }

    private class MultiTypeProjection : IProjection
    {
        public string Name => "multi";
        public int TypeACount { get; private set; }
        public int TypeBCount { get; private set; }

        public ValueTask ApplyAsync(IEvent @event, CancellationToken ct = default)
        {
            switch (@event)
            {
                case EventTypeA: TypeACount++; break;
                case EventTypeB: TypeBCount++; break;
            }
            return ValueTask.CompletedTask;
        }

        public ValueTask ResetAsync(CancellationToken ct = default)
        {
            TypeACount = 0;
            TypeBCount = 0;
            return ValueTask.CompletedTask;
        }
    }

    private class CancellationAwareProjection : IProjection
    {
        public string Name => "cancellation";
        public bool ReceivedCancellationToken { get; private set; }

        public ValueTask ApplyAsync(IEvent @event, CancellationToken ct = default)
        {
            ReceivedCancellationToken = ct != default;
            return ValueTask.CompletedTask;
        }

        public ValueTask ResetAsync(CancellationToken ct = default) => ValueTask.CompletedTask;
    }

    #endregion
}
