using Catga.Flow;
using FluentAssertions;
using Xunit;

namespace Catga.Tests.Flow;

/// <summary>
/// Unit tests for FlowResult.
/// </summary>
public class FlowResultTests
{
    [Fact]
    public void FlowResult_Success_HasCorrectProperties()
    {
        // Act
        var result = new FlowResult(true, 5, TimeSpan.FromSeconds(1));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.CompletedSteps.Should().Be(5);
        result.Duration.Should().Be(TimeSpan.FromSeconds(1));
        result.Error.Should().BeNull();
    }

    [Fact]
    public void FlowResult_Failure_HasError()
    {
        // Act
        var result = new FlowResult(false, 2, TimeSpan.FromMilliseconds(500), "Step failed");

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.CompletedSteps.Should().Be(2);
        result.Error.Should().Be("Step failed");
    }

    [Fact]
    public void FlowResult_Cancelled_HasFlag()
    {
        // Act
        var result = new FlowResult(false, 3, TimeSpan.FromSeconds(2)) { IsCancelled = true };

        // Assert
        result.IsCancelled.Should().BeTrue();
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void FlowResult_WithFlowId_StoresId()
    {
        // Act
        var result = new FlowResult(true, 1, TimeSpan.Zero) { FlowId = "flow-123" };

        // Assert
        result.FlowId.Should().Be("flow-123");
    }

    [Fact]
    public void FlowResultGeneric_Success_HasValue()
    {
        // Act
        var result = new FlowResult<int>(true, 42, 3, TimeSpan.FromSeconds(1));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(42);
        result.CompletedSteps.Should().Be(3);
    }

    [Fact]
    public void FlowResultGeneric_Failure_HasError()
    {
        // Act
        var result = new FlowResult<string>(false, null, 1, TimeSpan.Zero, "Failed");

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Value.Should().BeNull();
        result.Error.Should().Be("Failed");
    }

    [Fact]
    public void FlowResultGeneric_Cancelled_HasFlag()
    {
        // Act
        var result = new FlowResult<int>(false, 0, 2, TimeSpan.Zero) { IsCancelled = true };

        // Assert
        result.IsCancelled.Should().BeTrue();
    }

    [Fact]
    public void FlowResultGeneric_WithFlowId_StoresId()
    {
        // Act
        var result = new FlowResult<string>(true, "test", 1, TimeSpan.Zero) { FlowId = "flow-456" };

        // Assert
        result.FlowId.Should().Be("flow-456");
    }

    [Fact]
    public void FlowResult_DefaultValues()
    {
        // Act
        var result = new FlowResult(true, 0, TimeSpan.Zero);

        // Assert
        result.IsCancelled.Should().BeFalse();
        result.FlowId.Should().BeNull();
        result.Error.Should().BeNull();
    }

    [Fact]
    public void FlowResultGeneric_DefaultValues()
    {
        // Act
        var result = new FlowResult<int>(true, 42, 1, TimeSpan.Zero);

        // Assert
        result.IsCancelled.Should().BeFalse();
        result.FlowId.Should().BeNull();
        result.Error.Should().BeNull();
    }

    [Fact]
    public void FlowResult_AllPropertiesSet()
    {
        // Act
        var result = new FlowResult(false, 3, TimeSpan.FromSeconds(5), "Error message")
        {
            IsCancelled = true,
            FlowId = "flow-789"
        };

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.CompletedSteps.Should().Be(3);
        result.Duration.Should().Be(TimeSpan.FromSeconds(5));
        result.Error.Should().Be("Error message");
        result.IsCancelled.Should().BeTrue();
        result.FlowId.Should().Be("flow-789");
    }
}
