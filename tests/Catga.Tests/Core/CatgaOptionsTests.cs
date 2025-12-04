using Catga.Configuration;
using FluentAssertions;
using Xunit;

namespace Catga.Tests.Core;

/// <summary>
/// CatgaOptions单元测试
/// 目标覆盖率: 从 0% → 100%
/// </summary>
public class CatgaOptionsTests
{
    #region Default Values Tests

    [Fact]
    public void Constructor_ShouldSetDefaultValues()
    {
        // Act
        var options = new CatgaOptions();

        // Assert
        options.EnableLogging.Should().BeTrue();
        options.EnableTracing.Should().BeTrue();
        options.EnableRetry.Should().BeTrue();
        options.EnableValidation.Should().BeTrue();
        options.EnableIdempotency.Should().BeTrue();
        options.EnableDeadLetterQueue.Should().BeTrue();
    }

    [Fact]
    public void Constructor_ShouldSetDefaultRetryValues()
    {
        // Act
        var options = new CatgaOptions();

        // Assert
        options.MaxRetryAttempts.Should().Be(3);
        options.RetryDelayMs.Should().Be(100);
    }

    [Fact]
    public void Constructor_ShouldSetDefaultIdempotencyValues()
    {
        // Act
        var options = new CatgaOptions();

        // Assert
        options.IdempotencyRetentionHours.Should().Be(24);
        options.IdempotencyShardCount.Should().Be(32);
    }

    [Fact]
    public void Constructor_ShouldSetDefaultDeadLetterQueueValues()
    {
        // Act
        var options = new CatgaOptions();

        // Assert
        options.DeadLetterQueueMaxSize.Should().Be(1000);
    }

    [Fact]
    public void Constructor_ShouldSetDefaultQoS()
    {
        // Act
        var options = new CatgaOptions();

        // Assert
        options.DefaultQoS.Should().Be(QualityOfService.AtLeastOnce);
    }

    [Fact]
    public void Constructor_ShouldSetDefaultTimeout()
    {
        // Act
        var options = new CatgaOptions();

        // Assert
        options.TimeoutSeconds.Should().Be(30);
    }

    [Fact]
    public void Constructor_ShouldLeaveCircuitBreakerNull()
    {
        // Act
        var options = new CatgaOptions();

        // Assert
        options.CircuitBreakerThreshold.Should().BeNull();
        options.CircuitBreakerDuration.Should().BeNull();
        options.MaxEventHandlerConcurrency.Should().BeNull();
    }

    #endregion

    #region WithHighPerformance Tests

    [Fact]
    public void WithHighPerformance_ShouldDisableHeavyFeatures()
    {
        // Arrange
        var options = new CatgaOptions();

        // Act
        options.WithHighPerformance();

        // Assert
        options.EnableRetry.Should().BeFalse();
        options.EnableValidation.Should().BeFalse();
    }

    [Fact]
    public void WithHighPerformance_ShouldIncreaseShardCount()
    {
        // Arrange
        var options = new CatgaOptions();

        // Act
        options.WithHighPerformance();

        // Assert
        options.IdempotencyShardCount.Should().Be(64);
    }

    [Fact]
    public void WithHighPerformance_ShouldReturnSelf()
    {
        // Arrange
        var options = new CatgaOptions();

        // Act
        var result = options.WithHighPerformance();

        // Assert
        result.Should().BeSameAs(options);
    }

    [Fact]
    public void WithHighPerformance_ShouldAllowChaining()
    {
        // Arrange
        var options = new CatgaOptions();

        // Act
        var result = options.WithHighPerformance().Minimal();

        // Assert
        result.Should().BeSameAs(options);
    }

    #endregion

    #region Minimal Tests

    [Fact]
    public void Minimal_ShouldDisableAllFeatures()
    {
        // Arrange
        var options = new CatgaOptions();

        // Act
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
    public void Minimal_ShouldReturnSelf()
    {
        // Arrange
        var options = new CatgaOptions();

        // Act
        var result = options.Minimal();

        // Assert
        result.Should().BeSameAs(options);
    }

    #endregion

    #region ForDevelopment Tests

    [Fact]
    public void ForDevelopment_ShouldEnableLoggingAndTracing()
    {
        // Arrange
        var options = new CatgaOptions();

        // Act
        options.ForDevelopment();

        // Assert
        options.EnableLogging.Should().BeTrue();
        options.EnableTracing.Should().BeTrue();
    }

    [Fact]
    public void ForDevelopment_ShouldDisableIdempotency()
    {
        // Arrange
        var options = new CatgaOptions();

        // Act
        options.ForDevelopment();

        // Assert
        options.EnableIdempotency.Should().BeFalse();
    }

    [Fact]
    public void ForDevelopment_ShouldReturnSelf()
    {
        // Arrange
        var options = new CatgaOptions();

        // Act
        var result = options.ForDevelopment();

        // Assert
        result.Should().BeSameAs(options);
    }

    #endregion

    #region Property Mutation Tests

    [Fact]
    public void Properties_ShouldBeSettable()
    {
        // Arrange
        var options = new CatgaOptions();

        // Act
        options.EnableLogging = false;
        options.MaxRetryAttempts = 10;
        options.TimeoutSeconds = 60;
        options.CircuitBreakerThreshold = 5;
        options.CircuitBreakerDuration = TimeSpan.FromSeconds(30);
        options.MaxEventHandlerConcurrency = 100;

        // Assert
        options.EnableLogging.Should().BeFalse();
        options.MaxRetryAttempts.Should().Be(10);
        options.TimeoutSeconds.Should().Be(60);
        options.CircuitBreakerThreshold.Should().Be(5);
        options.CircuitBreakerDuration.Should().Be(TimeSpan.FromSeconds(30));
        options.MaxEventHandlerConcurrency.Should().Be(100);
    }

    [Fact]
    public void RetryDelayMs_ShouldBeSettable()
    {
        // Arrange
        var options = new CatgaOptions();

        // Act
        options.RetryDelayMs = 500;

        // Assert
        options.RetryDelayMs.Should().Be(500);
    }

    [Fact]
    public void IdempotencyRetentionHours_ShouldBeSettable()
    {
        // Arrange
        var options = new CatgaOptions();

        // Act
        options.IdempotencyRetentionHours = 48;

        // Assert
        options.IdempotencyRetentionHours.Should().Be(48);
    }

    [Fact]
    public void DeadLetterQueueMaxSize_ShouldBeSettable()
    {
        // Arrange
        var options = new CatgaOptions();

        // Act
        options.DeadLetterQueueMaxSize = 5000;

        // Assert
        options.DeadLetterQueueMaxSize.Should().Be(5000);
    }

    [Fact]
    public void DefaultQoS_ShouldBeSettable()
    {
        // Arrange
        var options = new CatgaOptions();

        // Act
        options.DefaultQoS = QualityOfService.ExactlyOnce;

        // Assert
        options.DefaultQoS.Should().Be(QualityOfService.ExactlyOnce);
    }

    #endregion

    #region Preset Combinations Tests

    [Fact]
    public void MinimalThenForDevelopment_ShouldApplyBoth()
    {
        // Arrange
        var options = new CatgaOptions();

        // Act
        options.Minimal().ForDevelopment();

        // Assert
        options.EnableLogging.Should().BeTrue(); // From ForDevelopment
        options.EnableTracing.Should().BeTrue(); // From ForDevelopment
        options.EnableRetry.Should().BeFalse(); // From Minimal
        options.EnableValidation.Should().BeFalse(); // From Minimal
    }

    [Fact]
    public void WithHighPerformanceThenMinimal_ShouldOverride()
    {
        // Arrange
        var options = new CatgaOptions();

        // Act
        options.WithHighPerformance().Minimal();

        // Assert
        options.IdempotencyShardCount.Should().Be(64); // From WithHighPerformance
        options.EnableLogging.Should().BeFalse(); // From Minimal
    }

    #endregion
}

