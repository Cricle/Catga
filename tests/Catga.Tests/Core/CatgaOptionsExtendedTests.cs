using Catga.Configuration;
using Catga.Core;
using FluentAssertions;
using Xunit;

namespace Catga.Tests.Core;

/// <summary>
/// Extended tests for CatgaOptions
/// </summary>
public class CatgaOptionsExtendedTests
{
    [Fact]
    public void DefaultOptions_ShouldHaveExpectedDefaults()
    {
        // Act
        var options = new CatgaOptions();

        // Assert
        options.TimeoutSeconds.Should().Be(30);
        options.MaxRetryAttempts.Should().Be(3);
        options.RetryDelayMs.Should().BeGreaterThan(0);
        options.EnableLogging.Should().BeTrue();
        options.EnableTracing.Should().BeTrue();
    }

    [Fact]
    public void Minimal_ShouldDisableAllFeatures()
    {
        // Act
        var options = new CatgaOptions();
        options.Minimal();

        // Assert
        options.EnableLogging.Should().BeFalse();
        options.EnableTracing.Should().BeFalse();
        options.EnableIdempotency.Should().BeFalse();
        options.EnableRetry.Should().BeFalse();
        options.EnableValidation.Should().BeFalse();
        options.EnableDeadLetterQueue.Should().BeFalse();
    }

    [Fact]
    public void ForDevelopment_ShouldEnableLoggingAndTracing()
    {
        // Act
        var options = new CatgaOptions();
        options.ForDevelopment();

        // Assert
        options.EnableLogging.Should().BeTrue();
        options.EnableTracing.Should().BeTrue();
        options.EnableIdempotency.Should().BeFalse();
    }

    [Fact]
    public void WithHighPerformance_ShouldOptimizeSettings()
    {
        // Act
        var options = new CatgaOptions();
        options.WithHighPerformance();

        // Assert
        options.IdempotencyShardCount.Should().Be(64);
        options.EnableRetry.Should().BeFalse();
        options.EnableValidation.Should().BeFalse();
    }

    [Fact]
    public void TimeoutSeconds_CanBeModified()
    {
        // Arrange
        var options = new CatgaOptions();

        // Act
        options.TimeoutSeconds = 60;

        // Assert
        options.TimeoutSeconds.Should().Be(60);
    }

    [Fact]
    public void MaxRetryAttempts_CanBeModified()
    {
        // Arrange
        var options = new CatgaOptions();

        // Act
        options.MaxRetryAttempts = 5;

        // Assert
        options.MaxRetryAttempts.Should().Be(5);
    }

    [Fact]
    public void RetryDelayMs_CanBeModified()
    {
        // Arrange
        var options = new CatgaOptions();

        // Act
        options.RetryDelayMs = 2000;

        // Assert
        options.RetryDelayMs.Should().Be(2000);
    }

    [Fact]
    public void EnableLogging_CanBeToggled()
    {
        // Arrange
        var options = new CatgaOptions();

        // Act
        options.EnableLogging = false;

        // Assert
        options.EnableLogging.Should().BeFalse();
    }

    [Fact]
    public void EnableTracing_CanBeToggled()
    {
        // Arrange
        var options = new CatgaOptions();

        // Act
        options.EnableTracing = false;

        // Assert
        options.EnableTracing.Should().BeFalse();
    }

    [Fact]
    public void EnableIdempotency_CanBeToggled()
    {
        // Arrange
        var options = new CatgaOptions();

        // Act
        options.EnableIdempotency = true;

        // Assert
        options.EnableIdempotency.Should().BeTrue();
    }

    [Fact]
    public void EnableRetry_CanBeToggled()
    {
        // Arrange
        var options = new CatgaOptions();

        // Act
        options.EnableRetry = true;

        // Assert
        options.EnableRetry.Should().BeTrue();
    }

    [Fact]
    public void EnableValidation_CanBeToggled()
    {
        // Arrange
        var options = new CatgaOptions();

        // Act
        options.EnableValidation = true;

        // Assert
        options.EnableValidation.Should().BeTrue();
    }

    [Fact]
    public void EnableDeadLetterQueue_CanBeToggled()
    {
        // Arrange
        var options = new CatgaOptions();

        // Act
        options.EnableDeadLetterQueue = true;

        // Assert
        options.EnableDeadLetterQueue.Should().BeTrue();
    }

    [Fact]
    public void IdempotencyShardCount_CanBeModified()
    {
        // Arrange
        var options = new CatgaOptions();

        // Act
        options.IdempotencyShardCount = 32;

        // Assert
        options.IdempotencyShardCount.Should().Be(32);
    }

    [Fact]
    public void ChainedConfiguration_ShouldWork()
    {
        // Act
        var options = new CatgaOptions();
        options.Minimal();
        options.EnableLogging = true;
        options.TimeoutSeconds = 120;

        // Assert
        options.EnableLogging.Should().BeTrue();
        options.EnableTracing.Should().BeFalse();
        options.TimeoutSeconds.Should().Be(120);
    }
}
