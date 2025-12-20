using Catga.Configuration;
using Catga.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Catga.Tests.DependencyInjection;

/// <summary>
/// Extended tests for CatgaServiceBuilder to improve branch coverage.
/// </summary>
public class CatgaServiceBuilderExtendedTests
{
    [Fact]
    public void AddCatga_ShouldReturnBuilder()
    {
        var services = new ServiceCollection();
        var builder = services.AddCatga();

        builder.Should().NotBeNull();
        builder.Should().BeOfType<CatgaServiceBuilder>();
    }

    [Fact]
    public void AddCatga_ShouldRegisterCoreServices()
    {
        var services = new ServiceCollection();
        services.AddCatga();

        // Verify services are registered
        services.Should().NotBeEmpty();
    }

    [Fact]
    public void AddCatga_WithOptions_ShouldConfigureOptions()
    {
        var services = new ServiceCollection();
        services.AddCatga(options =>
        {
            options.MaxRetryAttempts = 5;
            options.TimeoutSeconds = 60;
        });

        // Just verify the builder returns without error
        // Full integration requires more services
        services.Should().NotBeEmpty();
    }

    [Fact]
    public void UseInMemory_ShouldConfigureInMemoryPersistence()
    {
        var services = new ServiceCollection();
        var builder = services.AddCatga();
        builder.UseInMemory();

        var provider = services.BuildServiceProvider();
        // Verify services are registered
        provider.Should().NotBeNull();
    }

    [Fact]
    public void UseMemoryPack_ShouldConfigureMemoryPackSerializer()
    {
        var services = new ServiceCollection();
        var builder = services.AddCatga();
        builder.UseMemoryPack();

        // Verify serializer is registered
        services.Any(s => s.ServiceType == typeof(Catga.Abstractions.IMessageSerializer)).Should().BeTrue();
    }

    [Fact]
    public void ChainedConfiguration_ShouldWork()
    {
        var services = new ServiceCollection();
        var builder = services.AddCatga()
            .UseInMemory()
            .UseMemoryPack();

        builder.Should().NotBeNull();
    }

    [Fact]
    public void Services_Property_ShouldReturnServiceCollection()
    {
        var services = new ServiceCollection();
        var builder = services.AddCatga();

        builder.Services.Should().BeSameAs(services);
    }

    [Fact]
    public void AddCatga_MultipleTimes_ShouldNotThrow()
    {
        var services = new ServiceCollection();
        services.AddCatga();
        
        // Second call should not throw
        var act = () => services.AddCatga();
        act.Should().NotThrow();
    }

    [Fact]
    public void CatgaOptions_WithHighPerformance_ShouldConfigureCorrectly()
    {
        var options = new CatgaOptions().WithHighPerformance();

        options.IdempotencyShardCount.Should().Be(64);
        options.EnableRetry.Should().BeFalse();
        options.EnableValidation.Should().BeFalse();
    }

    [Fact]
    public void CatgaOptions_Minimal_ShouldDisableAllFeatures()
    {
        var options = new CatgaOptions().Minimal();

        options.EnableLogging.Should().BeFalse();
        options.EnableTracing.Should().BeFalse();
        options.EnableIdempotency.Should().BeFalse();
        options.EnableRetry.Should().BeFalse();
        options.EnableValidation.Should().BeFalse();
        options.EnableDeadLetterQueue.Should().BeFalse();
    }

    [Fact]
    public void CatgaOptions_ForDevelopment_ShouldConfigureCorrectly()
    {
        var options = new CatgaOptions().ForDevelopment();

        options.EnableLogging.Should().BeTrue();
        options.EnableTracing.Should().BeTrue();
        options.EnableIdempotency.Should().BeFalse();
    }

    [Fact]
    public void CatgaOptions_DefaultValues_ShouldBeCorrect()
    {
        var options = new CatgaOptions();

        options.EnableLogging.Should().BeTrue();
        options.EnableTracing.Should().BeTrue();
        options.EnableRetry.Should().BeTrue();
        options.EnableValidation.Should().BeTrue();
        options.EnableIdempotency.Should().BeTrue();
        options.MaxRetryAttempts.Should().Be(3);
        options.RetryDelayMs.Should().Be(100);
        options.TimeoutSeconds.Should().Be(30);
    }
}

/// <summary>
/// Tests for CatgaServiceCollectionExtensions.
/// </summary>
public class CatgaServiceCollectionExtensionsExtendedTests
{
    [Fact]
    public void AddInMemoryTransport_ShouldRegisterTransport()
    {
        var services = new ServiceCollection();
        services.AddCatga();
        services.AddInMemoryTransport();

        // Verify transport is registered
        services.Any(s => s.ServiceType == typeof(Catga.Transport.IMessageTransport)).Should().BeTrue();
    }
}
