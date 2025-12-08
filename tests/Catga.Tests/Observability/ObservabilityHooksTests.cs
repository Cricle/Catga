using Catga.Observability;
using FluentAssertions;
using System.Diagnostics;
using Xunit;

namespace Catga.Tests.Observability;

/// <summary>
/// Coverage tests for ObservabilityHooks
/// </summary>
public class ObservabilityHooksTests
{
    [Fact]
    public void Enable_ShouldNotThrow()
    {
        // Act
        var act = () => ObservabilityHooks.Enable();

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void IsEnabled_AfterEnable_ShouldBeTrue()
    {
        // Act
        ObservabilityHooks.Enable();

        // Assert
        ObservabilityHooks.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void StartCommand_WithNullMessage_ShouldNotThrow()
    {
        // Arrange
        ObservabilityHooks.Enable();

        // Act
        var act = () => ObservabilityHooks.StartCommand("TestCommand", null);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void StartEventPublish_WithNullMessage_ShouldNotThrow()
    {
        // Arrange
        ObservabilityHooks.Enable();

        // Act
        var act = () => ObservabilityHooks.StartEventPublish("TestEvent", null);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void RecordCommandError_WithNullActivity_ShouldNotThrow()
    {
        // Act
        var act = () => ObservabilityHooks.RecordCommandError("TestCommand", new Exception("test"), null);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void RecordCommandResult_ShouldNotThrow()
    {
        // Arrange
        ObservabilityHooks.Enable();

        // Act
        var act = () => ObservabilityHooks.RecordCommandResult("TestCommand", true, 100.0, null);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void RecordEventPublished_ShouldNotThrow()
    {
        // Arrange
        ObservabilityHooks.Enable();

        // Act
        var act = () => ObservabilityHooks.RecordEventPublished("TestEvent", 3);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void RecordPipelineBehaviorCount_ShouldNotThrow()
    {
        // Arrange
        ObservabilityHooks.Enable();

        // Act
        var act = () => ObservabilityHooks.RecordPipelineBehaviorCount("TestRequest", 5);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void RecordPipelineDuration_ShouldNotThrow()
    {
        // Arrange
        ObservabilityHooks.Enable();

        // Act
        var act = () => ObservabilityHooks.RecordPipelineDuration("TestRequest", 50.0);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void RecordMediatorBatchMetrics_ShouldNotThrow()
    {
        // Arrange
        ObservabilityHooks.Enable();

        // Act
        var act = () => ObservabilityHooks.RecordMediatorBatchMetrics(10, 5, 25.0);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void RecordMediatorBatchOverflow_ShouldNotThrow()
    {
        // Arrange
        ObservabilityHooks.Enable();

        // Act
        var act = () => ObservabilityHooks.RecordMediatorBatchOverflow();

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void Enable_CalledMultipleTimes_ShouldBeIdempotent()
    {
        // Act
        var act = () =>
        {
            ObservabilityHooks.Enable();
            ObservabilityHooks.Enable();
            ObservabilityHooks.Enable();
        };

        // Assert
        act.Should().NotThrow();
        ObservabilityHooks.IsEnabled.Should().BeTrue();
    }
}






