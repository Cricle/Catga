using Catga.Abstractions;
using Catga.EventSourcing;
using Catga.EventSourcing.Testing;
using Catga.Persistence.Stores;
using Catga.Resilience;
using FluentAssertions;
using Xunit;

namespace Catga.Tests.EventSourcing;

/// <summary>
/// Unit tests for ReplayTester.
/// </summary>
public class ReplayTesterTests
{
    private readonly InMemoryEventStore _eventStore;
    private readonly ReplayTester<TestAggregate> _tester;

    public ReplayTesterTests()
    {
        _eventStore = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());
        _tester = new ReplayTester<TestAggregate>(_eventStore);
    }

    [Fact]
    public async Task ReplayAsync_WithEvents_RebuildsAggregate()
    {
        // Arrange
        await _eventStore.AppendAsync("TestAggregate-test-1", [
            new TestCreated("test-1"),
            new TestUpdated("value-1"),
            new TestUpdated("value-2")
        ]);

        // Act
        var aggregate = await _tester.ReplayAsync("TestAggregate-test-1");

        // Assert
        aggregate.Should().NotBeNull();
        aggregate!.Id.Should().Be("test-1");
        aggregate.Value.Should().Be("value-2");
    }

    [Fact]
    public async Task ReplayAsync_WithNoEvents_ReturnsNull()
    {
        // Act
        var aggregate = await _tester.ReplayAsync("nonexistent-stream");

        // Assert
        aggregate.Should().BeNull();
    }

    [Fact]
    public async Task ReplayToVersionAsync_StopsAtVersion()
    {
        // Arrange
        await _eventStore.AppendAsync("TestAggregate-test-2", [
            new TestCreated("test-2"),
            new TestUpdated("v1"),
            new TestUpdated("v2"),
            new TestUpdated("v3")
        ]);

        // Act
        var aggregate = await _tester.ReplayToVersionAsync("TestAggregate-test-2", 1);

        // Assert
        aggregate.Should().NotBeNull();
        aggregate!.Value.Should().Be("v1");
    }

    [Fact]
    public async Task ReplayToVersionAsync_WithNoEvents_ReturnsNull()
    {
        // Act
        var aggregate = await _tester.ReplayToVersionAsync("nonexistent", 5);

        // Assert
        aggregate.Should().BeNull();
    }

    #region Test helpers

    private class TestAggregate : AggregateRoot
    {
        public override string Id { get; protected set; } = "";
        public string Value { get; private set; } = "";

        protected override void When(IEvent @event)
        {
            switch (@event)
            {
                case TestCreated e:
                    Id = e.Id;
                    break;
                case TestUpdated e:
                    Value = e.Value;
                    break;
            }
        }
    }

    private record TestCreated(string Id) : IEvent
    {
        public long MessageId { get; init; }
    }

    private record TestUpdated(string Value) : IEvent
    {
        public long MessageId { get; init; }
    }

    #endregion
}
