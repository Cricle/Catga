using Catga.Abstractions;
using Catga.EventSourcing;
using Catga.EventSourcing.Testing;
using Catga.Persistence.Stores;
using Catga.Resilience;
using FluentAssertions;
using Xunit;

namespace Catga.Tests.EventSourcing;

/// <summary>
/// Unit tests for ScenarioRunner.
/// </summary>
public class ScenarioRunnerTests
{
    private readonly InMemoryEventStore _eventStore;

    public ScenarioRunnerTests()
    {
        _eventStore = new InMemoryEventStore(new DiagnosticResiliencePipelineProvider());
    }

    [Fact]
    public async Task RunAsync_WithGivenEvents_LoadsHistory()
    {
        // Arrange
        var runner = new ScenarioRunner<TestAggregate>(_eventStore);
        var executed = false;

        // Act
        await runner
            .Given("TestAggregate-1", new TestCreated("1"), new TestUpdated("value"))
            .Then(agg =>
            {
                agg.Id.Should().Be("1");
                agg.Value.Should().Be("value");
                executed = true;
            })
            .RunAsync();

        // Assert
        executed.Should().BeTrue();
    }

    [Fact]
    public async Task RunAsync_WithWhenAction_ExecutesAction()
    {
        // Arrange
        var runner = new ScenarioRunner<TestAggregate>(_eventStore);
        var actionExecuted = false;

        // Act
        await runner
            .Given("TestAggregate-2", new TestCreated("2"))
            .When(agg =>
            {
                agg.Update("new-value");
                actionExecuted = true;
            })
            .Then(agg => agg.Value.Should().Be("new-value"))
            .RunAsync();

        // Assert
        actionExecuted.Should().BeTrue();
    }

    [Fact]
    public async Task RunAsync_WithNoGivenEvents_StartsWithEmptyAggregate()
    {
        // Arrange
        var runner = new ScenarioRunner<TestAggregate>(_eventStore);
        var executed = false;

        // Act
        await runner
            .When(agg => agg.Create("new-id"))
            .Then(agg =>
            {
                agg.Id.Should().Be("new-id");
                executed = true;
            })
            .RunAsync();

        // Assert
        executed.Should().BeTrue();
    }

    [Fact]
    public async Task RunAsync_ChainingMethods_ReturnsRunner()
    {
        // Arrange
        var runner = new ScenarioRunner<TestAggregate>(_eventStore);

        // Act
        var result = runner.Given("TestAggregate-3", new TestCreated("3"));

        // Assert
        result.Should().BeSameAs(runner);
    }

    [Fact]
    public async Task RunAsync_WithMultipleEvents_AppliesAll()
    {
        // Arrange
        var runner = new ScenarioRunner<TestAggregate>(_eventStore);

        // Act & Assert
        await runner
            .Given("TestAggregate-4",
                new TestCreated("4"),
                new TestUpdated("v1"),
                new TestUpdated("v2"),
                new TestUpdated("v3"))
            .Then(agg => agg.Value.Should().Be("v3"))
            .RunAsync();
    }

    #region Test helpers

    private class TestAggregate : AggregateRoot
    {
        public override string Id { get; protected set; } = "";
        public string Value { get; private set; } = "";

        public void Create(string id)
        {
            RaiseEvent(new TestCreated(id));
        }

        public void Update(string value)
        {
            RaiseEvent(new TestUpdated(value));
        }

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
