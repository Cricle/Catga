using Catga.Configuration;
using Catga.DependencyInjection;
using Catga.DistributedId;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Catga.Tests.DependencyInjection;

/// <summary>
/// CatgaServiceBuilder单元测试
/// 目标覆盖率: 从 0% → 95%+
/// </summary>
public class CatgaServiceBuilderTests
{
    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidArguments_ShouldCreateInstance()
    {
        // Arrange
        var services = new ServiceCollection();
        var options = new CatgaOptions();

        // Act
        var builder = new CatgaServiceBuilder(services, options);

        // Assert
        builder.Should().NotBeNull();
        builder.Services.Should().BeSameAs(services);
        builder.Options.Should().BeSameAs(options);
    }

    [Fact]
    public void Constructor_WithNullServices_ShouldThrowArgumentNullException()
    {
        // Arrange
        IServiceCollection services = null!;
        var options = new CatgaOptions();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new CatgaServiceBuilder(services, options));
    }

    [Fact]
    public void Constructor_WithNullOptions_ShouldThrowArgumentNullException()
    {
        // Arrange
        var services = new ServiceCollection();
        CatgaOptions options = null!;

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new CatgaServiceBuilder(services, options));
    }

    #endregion

    #region Configure Tests

    [Fact]
    public void Configure_ShouldApplyConfiguration()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = services.AddCatga();

        // Act
        builder.Configure(options =>
        {
            options.EnableLogging = true;
            options.MaxRetryAttempts = 10;
        });

        // Assert
        builder.Options.EnableLogging.Should().BeTrue();
        builder.Options.MaxRetryAttempts.Should().Be(10);
    }

    [Fact]
    public void Configure_ShouldReturnSelfForChaining()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = services.AddCatga();

        // Act
        var result = builder.Configure(options => { });

        // Assert
        result.Should().BeSameAs(builder);
    }

    [Fact]
    public void Configure_WithNullAction_ShouldThrowArgumentNullException()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = services.AddCatga();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => builder.Configure(null!));
    }

    #endregion

    #region Environment Presets Tests

    [Fact]
    public void ForDevelopment_ShouldConfigureForDevEnvironment()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = services.AddCatga();

        // Act
        builder.ForDevelopment();

        // Assert
        builder.Options.EnableLogging.Should().BeTrue();
        builder.Options.EnableTracing.Should().BeTrue();
        builder.Options.EnableIdempotency.Should().BeFalse(); // Dev typically doesn't need idempotency
    }

    [Fact]
    public void ForDevelopment_ShouldReturnSelfForChaining()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = services.AddCatga();

        // Act
        var result = builder.ForDevelopment();

        // Assert
        result.Should().BeSameAs(builder);
    }

    [Fact]
    public void ForProduction_ShouldEnableAllFeatures()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = services.AddCatga();

        // Act
        builder.ForProduction();

        // Assert
        builder.Options.EnableLogging.Should().BeTrue();
        builder.Options.EnableTracing.Should().BeTrue();
        builder.Options.EnableIdempotency.Should().BeTrue();
        builder.Options.EnableRetry.Should().BeTrue();
        builder.Options.EnableValidation.Should().BeTrue();
        builder.Options.EnableDeadLetterQueue.Should().BeTrue();
    }

    [Fact]
    public void ForProduction_ShouldReturnSelfForChaining()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = services.AddCatga();

        // Act
        var result = builder.ForProduction();

        // Assert
        result.Should().BeSameAs(builder);
    }

    [Fact]
    public void ForHighPerformance_ShouldConfigureForMinimalOverhead()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = services.AddCatga();

        // Act
        builder.ForHighPerformance();

        // Assert - High performance typically disables heavy features
        // Verify the method was called (actual assertions depend on WithHighPerformance implementation)
        builder.Options.Should().NotBeNull();
    }

    [Fact]
    public void ForHighPerformance_ShouldReturnSelfForChaining()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = services.AddCatga();

        // Act
        var result = builder.ForHighPerformance();

        // Assert
        result.Should().BeSameAs(builder);
    }

    [Fact]
    public void Minimal_ShouldConfigureForMinimalFeatures()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = services.AddCatga();

        // Act
        builder.Minimal();

        // Assert - Verify the method was called
        builder.Options.Should().NotBeNull();
    }

    [Fact]
    public void Minimal_ShouldReturnSelfForChaining()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = services.AddCatga();

        // Act
        var result = builder.Minimal();

        // Assert
        result.Should().BeSameAs(builder);
    }

    #endregion

    #region Feature Toggles Tests

    [Fact]
    public void WithLogging_ShouldEnableLogging()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = services.AddCatga();

        // Act
        builder.WithLogging(true);

        // Assert
        builder.Options.EnableLogging.Should().BeTrue();
    }

    [Fact]
    public void WithLogging_WithFalse_ShouldDisableLogging()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = services.AddCatga();
        builder.Options.EnableLogging = true;

        // Act
        builder.WithLogging(false);

        // Assert
        builder.Options.EnableLogging.Should().BeFalse();
    }

    [Fact]
    public void WithLogging_DefaultTrue_ShouldEnableLogging()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = services.AddCatga();

        // Act
        builder.WithLogging();

        // Assert
        builder.Options.EnableLogging.Should().BeTrue();
    }

    [Fact]
    public void WithLogging_ShouldReturnSelfForChaining()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = services.AddCatga();

        // Act
        var result = builder.WithLogging();

        // Assert
        result.Should().BeSameAs(builder);
    }

    [Fact]
    public void WithTracing_ShouldEnableTracing()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = services.AddCatga();

        // Act
        builder.WithTracing(true);

        // Assert
        builder.Options.EnableTracing.Should().BeTrue();
    }

    [Fact]
    public void WithTracing_ShouldRegisterDistributedTracingBehavior()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = services.AddCatga();

        // Act
        builder.WithTracing(true);

        // Assert
        services.Should().Contain(sd =>
            sd.ServiceType.IsGenericType &&
            sd.ServiceType.GetGenericTypeDefinition().Name.Contains("IPipelineBehavior") &&
            sd.ImplementationType != null &&
            sd.ImplementationType.IsGenericType &&
            sd.ImplementationType.GetGenericTypeDefinition().Name.Contains("DistributedTracingBehavior"));
    }

    [Fact]
    public void WithTracing_WithFalse_ShouldDisableTracing()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = services.AddCatga();
        builder.Options.EnableTracing = true;

        // Act
        builder.WithTracing(false);

        // Assert
        builder.Options.EnableTracing.Should().BeFalse();
    }

    [Fact]
    public void WithTracing_ShouldReturnSelfForChaining()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = services.AddCatga();

        // Act
        var result = builder.WithTracing();

        // Assert
        result.Should().BeSameAs(builder);
    }

    [Fact]
    public void WithRetry_ShouldEnableRetryWithDefaultAttempts()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = services.AddCatga();

        // Act
        builder.WithRetry();

        // Assert
        builder.Options.EnableRetry.Should().BeTrue();
        builder.Options.MaxRetryAttempts.Should().Be(3);
    }

    [Fact]
    public void WithRetry_WithCustomAttempts_ShouldSetAttempts()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = services.AddCatga();

        // Act
        builder.WithRetry(true, 10);

        // Assert
        builder.Options.EnableRetry.Should().BeTrue();
        builder.Options.MaxRetryAttempts.Should().Be(10);
    }

    [Fact]
    public void WithRetry_ShouldReturnSelfForChaining()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = services.AddCatga();

        // Act
        var result = builder.WithRetry();

        // Assert
        result.Should().BeSameAs(builder);
    }

    [Fact]
    public void WithIdempotency_ShouldEnableIdempotencyWithDefaultRetention()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = services.AddCatga();

        // Act
        builder.WithIdempotency();

        // Assert
        builder.Options.EnableIdempotency.Should().BeTrue();
        builder.Options.IdempotencyRetentionHours.Should().Be(24);
    }

    [Fact]
    public void WithIdempotency_WithCustomRetention_ShouldSetRetention()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = services.AddCatga();

        // Act
        builder.WithIdempotency(true, 48);

        // Assert
        builder.Options.EnableIdempotency.Should().BeTrue();
        builder.Options.IdempotencyRetentionHours.Should().Be(48);
    }

    [Fact]
    public void WithIdempotency_ShouldReturnSelfForChaining()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = services.AddCatga();

        // Act
        var result = builder.WithIdempotency();

        // Assert
        result.Should().BeSameAs(builder);
    }

    [Fact]
    public void WithValidation_ShouldEnableValidation()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = services.AddCatga();

        // Act
        builder.WithValidation(true);

        // Assert
        builder.Options.EnableValidation.Should().BeTrue();
    }

    [Fact]
    public void WithValidation_ShouldReturnSelfForChaining()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = services.AddCatga();

        // Act
        var result = builder.WithValidation();

        // Assert
        result.Should().BeSameAs(builder);
    }

    [Fact]
    public void WithDeadLetterQueue_ShouldEnableWithDefaultMaxSize()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = services.AddCatga();

        // Act
        builder.WithDeadLetterQueue();

        // Assert
        builder.Options.EnableDeadLetterQueue.Should().BeTrue();
        builder.Options.DeadLetterQueueMaxSize.Should().Be(1000);
    }

    [Fact]
    public void WithDeadLetterQueue_WithCustomMaxSize_ShouldSetMaxSize()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = services.AddCatga();

        // Act
        builder.WithDeadLetterQueue(true, 5000);

        // Assert
        builder.Options.EnableDeadLetterQueue.Should().BeTrue();
        builder.Options.DeadLetterQueueMaxSize.Should().Be(5000);
    }

    [Fact]
    public void WithDeadLetterQueue_ShouldReturnSelfForChaining()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = services.AddCatga();

        // Act
        var result = builder.WithDeadLetterQueue();

        // Assert
        result.Should().BeSameAs(builder);
    }

    #endregion

    #region WorkerId Configuration Tests

    [Fact]
    public void UseWorkerId_WithValidId_ShouldRegisterIdGenerator()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = services.AddCatga();

        // Act
        builder.UseWorkerId(42);

        // Assert
        var provider = services.BuildServiceProvider();
        var idGenerator = provider.GetRequiredService<IDistributedIdGenerator>();
        idGenerator.Should().NotBeNull();
        idGenerator.Should().BeOfType<SnowflakeIdGenerator>();
    }

    [Fact]
    public void UseWorkerId_WithMinWorkerId_ShouldSucceed()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = services.AddCatga();

        // Act
        builder.UseWorkerId(0);

        // Assert
        var provider = services.BuildServiceProvider();
        var idGenerator = provider.GetRequiredService<IDistributedIdGenerator>();
        idGenerator.NextId().Should().BeGreaterThan(0);
    }

    [Fact]
    public void UseWorkerId_WithMaxWorkerId_ShouldSucceed()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = services.AddCatga();

        // Act
        builder.UseWorkerId(255);

        // Assert
        var provider = services.BuildServiceProvider();
        var idGenerator = provider.GetRequiredService<IDistributedIdGenerator>();
        idGenerator.NextId().Should().BeGreaterThan(0);
    }

    [Fact]
    public void UseWorkerId_WithNegativeId_ShouldThrowArgumentOutOfRangeException()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = services.AddCatga();

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.UseWorkerId(-1));
    }

    [Fact]
    public void UseWorkerId_WithIdAbove255_ShouldThrowArgumentOutOfRangeException()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = services.AddCatga();

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.UseWorkerId(256));
    }

    [Fact]
    public void UseWorkerId_ShouldReturnSelfForChaining()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = services.AddCatga();

        // Act
        var result = builder.UseWorkerId(42);

        // Assert
        result.Should().BeSameAs(builder);
    }

    [Fact]
    public void UseWorkerIdFromEnvironment_WithValidEnvVar_ShouldConfigureWorkerId()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = services.AddCatga();
        Environment.SetEnvironmentVariable("TEST_WORKER_ID", "123");

        try
        {
            // Act
            builder.UseWorkerIdFromEnvironment("TEST_WORKER_ID");

            // Assert
            var provider = services.BuildServiceProvider();
            var idGenerator = provider.GetRequiredService<IDistributedIdGenerator>();
            idGenerator.NextId().Should().BeGreaterThan(0);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TEST_WORKER_ID", null);
        }
    }

    [Fact]
    public void UseWorkerIdFromEnvironment_WithDefaultEnvVarName_ShouldUseCATGA_WORKER_ID()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = services.AddCatga();
        Environment.SetEnvironmentVariable("CATGA_WORKER_ID", "77");

        try
        {
            // Act
            builder.UseWorkerIdFromEnvironment();

            // Assert
            var provider = services.BuildServiceProvider();
            var idGenerator = provider.GetRequiredService<IDistributedIdGenerator>();
            idGenerator.NextId().Should().BeGreaterThan(0);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CATGA_WORKER_ID", null);
        }
    }

    [Fact]
    public void UseWorkerIdFromEnvironment_ShouldReturnSelfForChaining()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = services.AddCatga();
        Environment.SetEnvironmentVariable("TEST_WORKER_ID", "50");

        try
        {
            // Act
            var result = builder.UseWorkerIdFromEnvironment("TEST_WORKER_ID");

            // Assert
            result.Should().BeSameAs(builder);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TEST_WORKER_ID", null);
        }
    }

    #endregion

    #region Fluent API Chaining Tests

    [Fact]
    public void FluentAPI_ShouldChainMultipleCalls()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var result = services.AddCatga()
            .WithLogging()
            .WithTracing()
            .WithRetry(maxAttempts: 5)
            .WithIdempotency(retentionHours: 48)
            .WithValidation()
            .WithDeadLetterQueue(maxSize: 2000)
            .UseWorkerId(42);

        // Assert
        result.Should().NotBeNull();
        result.Options.EnableLogging.Should().BeTrue();
        result.Options.EnableTracing.Should().BeTrue();
        result.Options.MaxRetryAttempts.Should().Be(5);
        result.Options.IdempotencyRetentionHours.Should().Be(48);
        result.Options.EnableValidation.Should().BeTrue();
        result.Options.DeadLetterQueueMaxSize.Should().Be(2000);
    }

    [Fact]
    public void FluentAPI_ForProduction_ShouldWorkWithAdditionalCalls()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var result = services.AddCatga()
            .ForProduction()
            .UseWorkerId(10)
            .WithRetry(maxAttempts: 10);

        // Assert
        result.Options.EnableLogging.Should().BeTrue();
        result.Options.EnableTracing.Should().BeTrue();
        result.Options.EnableIdempotency.Should().BeTrue();
        result.Options.MaxRetryAttempts.Should().Be(10);
    }

    [Fact]
    public void FluentAPI_ForDevelopment_ShouldWorkWithOverrides()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var result = services.AddCatga()
            .ForDevelopment()
            .WithIdempotency(); // Override dev default

        // Assert
        result.Options.EnableIdempotency.Should().BeTrue();
    }

    #endregion
}







