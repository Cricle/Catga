using Catga.Abstractions;
using Catga.EventSourcing;
using Catga.EventSourcing.Testing;
using FluentAssertions;
using Xunit;

namespace Catga.Tests.EventSourcing;

/// <summary>
/// Unit tests for AggregateFixture.
/// </summary>
public class AggregateFixtureTests
{
    [Fact]
    public void Constructor_CreatesNewAggregate()
    {
        // Act
        var fixture = new AggregateFixture<TestAggregate>();

        // Assert
        fixture.Aggregate.Should().NotBeNull();
    }

    [Fact]
    public void Given_LoadsHistoricalEvents()
    {
        // Arrange
        var fixture = new AggregateFixture<TestAggregate>();

        // Act
        fixture.Given(new TestCreated("test-1"), new TestUpdated("new-value"));

        // Assert
        fixture.Aggregate.Id.Should().Be("test-1");
        fixture.Aggregate.Value.Should().Be("new-value");
    }

    [Fact]
    public void Given_ReturnsFixtureForChaining()
    {
        // Arrange
        var fixture = new AggregateFixture<TestAggregate>();

        // Act
        var result = fixture.Given(new TestCreated("test-1"));

        // Assert
        result.Should().BeSameAs(fixture);
    }

    [Fact]
    public void AssertUncommittedEvent_WithMatchingEvent_DoesNotThrow()
    {
        // Arrange
        var fixture = new AggregateFixture<TestAggregate>();
        fixture.Given(new TestCreated("test-1"));
        fixture.Aggregate.Update("new-value");

        // Act & Assert
        var act = () => fixture.AssertUncommittedEvent<TestUpdated>();
        act.Should().NotThrow();
    }

    [Fact]
    public void AssertUncommittedEvent_WithoutMatchingEvent_Throws()
    {
        // Arrange
        var fixture = new AggregateFixture<TestAggregate>();
        fixture.Given(new TestCreated("test-1"));

        // Act & Assert
        var act = () => fixture.AssertUncommittedEvent<TestUpdated>();
        act.Should().Throw<AssertionException>();
    }

    [Fact]
    public void AssertUncommittedEventCount_WithCorrectCount_DoesNotThrow()
    {
        // Arrange
        var fixture = new AggregateFixture<TestAggregate>();
        fixture.Given(new TestCreated("test-1"));
        fixture.Aggregate.Update("v1");
        fixture.Aggregate.Update("v2");

        // Act & Assert
        var act = () => fixture.AssertUncommittedEventCount(2);
        act.Should().NotThrow();
    }

    [Fact]
    public void AssertUncommittedEventCount_WithWrongCount_Throws()
    {
        // Arrange
        var fixture = new AggregateFixture<TestAggregate>();
        fixture.Given(new TestCreated("test-1"));
        fixture.Aggregate.Update("v1");

        // Act & Assert
        var act = () => fixture.AssertUncommittedEventCount(5);
        act.Should().Throw<AssertionException>();
    }

    #region Test helpers

    private class TestAggregate : AggregateRoot
    {
        public override string Id { get; protected set; } = "";
        public string Value { get; private set; } = "";

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
