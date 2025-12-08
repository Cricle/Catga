using Catga.Core;
using FluentAssertions;
using Xunit;

namespace Catga.Tests.Core;

/// <summary>
/// Unit tests for RecoveryResult.
/// </summary>
public class RecoveryResultTests
{
    [Fact]
    public void Constructor_WithSuccessfulRecovery_IsSuccess()
    {
        // Act
        var result = new RecoveryResult(5, 0, TimeSpan.FromSeconds(1));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Succeeded.Should().Be(5);
        result.Failed.Should().Be(0);
    }

    [Fact]
    public void Constructor_WithFailedRecovery_IsNotSuccess()
    {
        // Act
        var result = new RecoveryResult(3, 2, TimeSpan.FromSeconds(2));

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Succeeded.Should().Be(3);
        result.Failed.Should().Be(2);
    }

    [Fact]
    public void AlreadyRecovering_HasNegativeValues()
    {
        // Act
        var result = RecoveryResult.AlreadyRecovering;

        // Assert
        result.Succeeded.Should().Be(-1);
        result.Failed.Should().Be(-1);
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Duration_IsStoredCorrectly()
    {
        // Arrange
        var duration = TimeSpan.FromMilliseconds(500);

        // Act
        var result = new RecoveryResult(1, 0, duration);

        // Assert
        result.Duration.Should().Be(duration);
    }

    [Fact]
    public void ZeroSucceeded_IsNotSuccess()
    {
        // Act
        var result = new RecoveryResult(0, 0, TimeSpan.Zero);

        // Assert
        result.IsSuccess.Should().BeFalse();
    }
}






