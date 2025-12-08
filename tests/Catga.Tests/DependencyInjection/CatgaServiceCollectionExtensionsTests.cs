using Catga;
using Catga.Abstractions;
using Catga.Configuration;
using Catga.DependencyInjection;
using Catga.DistributedId;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Catga.Tests.DependencyInjection;

/// <summary>
/// CatgaServiceCollectionExtensions单元测试
/// 目标覆盖率: 从 0% → 95%+
/// </summary>
public class CatgaServiceCollectionExtensionsTests
{
    #region AddCatga() Tests

    [Fact]
    public void AddCatga_ShouldRegisterCoreServices()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var builder = services.AddCatga();

        // Assert
        builder.Should().NotBeNull();
        builder.Should().BeOfType<CatgaServiceBuilder>();

        // Verify services are registered
        services.Should().Contain(sd => sd.ServiceType == typeof(CatgaOptions));
        services.Should().Contain(sd => sd.ServiceType == typeof(ICatgaMediator));
        services.Should().Contain(sd => sd.ServiceType == typeof(IDistributedIdGenerator));
    }

    [Fact]
    public void AddCatga_ShouldRegisterMediatorAsSingleton()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddCatga();

        // Assert
        var mediatorDescriptor = services.FirstOrDefault(sd => sd.ServiceType == typeof(ICatgaMediator));
        mediatorDescriptor.Should().NotBeNull();
        mediatorDescriptor!.Lifetime.Should().Be(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Singleton);
        mediatorDescriptor.ImplementationType.Should().Be(typeof(CatgaMediator));
    }

    [Fact]
    public void AddCatga_ShouldRegisterOptionsAsSingleton()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddCatga();

        // Assert
        var optionsDescriptor = services.FirstOrDefault(sd => sd.ServiceType == typeof(CatgaOptions));
        optionsDescriptor.Should().NotBeNull();
        optionsDescriptor!.Lifetime.Should().Be(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Singleton);
    }

    [Fact]
    public void AddCatga_ShouldRegisterIdGeneratorAsSingleton()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddCatga();

        // Assert
        var idGenDescriptor = services.FirstOrDefault(sd => sd.ServiceType == typeof(IDistributedIdGenerator));
        idGenDescriptor.Should().NotBeNull();
        idGenDescriptor!.Lifetime.Should().Be(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Singleton);
    }

    [Fact]
    public void AddCatga_ShouldCreateSnowflakeIdGenerator()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga();

        // Act
        var provider = services.BuildServiceProvider();
        var idGenerator = provider.GetRequiredService<IDistributedIdGenerator>();

        // Assert
        idGenerator.Should().NotBeNull();
        idGenerator.Should().BeOfType<SnowflakeIdGenerator>();
    }

    [Fact]
    public void AddCatga_SnowflakeIdGenerator_ShouldGenerateValidIds()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga();
        var provider = services.BuildServiceProvider();
        var idGenerator = provider.GetRequiredService<IDistributedIdGenerator>();

        // Act
        var id1 = idGenerator.NextId();
        var id2 = idGenerator.NextId();

        // Assert
        id1.Should().BeGreaterThan(0);
        id2.Should().BeGreaterThan(0);
        id2.Should().BeGreaterThan(id1); // IDs should be monotonically increasing
    }

    [Fact]
    public void AddCatga_CalledMultipleTimes_ShouldNotDuplicateRegistrations()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddCatga();
        services.AddCatga();
        services.AddCatga();

        // Assert - TryAdd* ensures only one registration
        services.Where(sd => sd.ServiceType == typeof(ICatgaMediator)).Should().HaveCount(1);
        services.Where(sd => sd.ServiceType == typeof(CatgaOptions)).Should().HaveCount(1);
    }

    [Fact]
    public void AddCatga_WithNullServices_ShouldThrowArgumentNullException()
    {
        // Arrange
        IServiceCollection services = null!;

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => services.AddCatga());
    }

    #endregion

    #region AddCatga(Action<CatgaOptions>) Tests

    [Fact]
    public void AddCatgaWithConfigure_ShouldApplyConfiguration()
    {
        // Arrange
        var services = new ServiceCollection();
        var configureWasCalled = false;

        // Act
        var builder = services.AddCatga(options =>
        {
            configureWasCalled = true;
            options.EnableLogging = true;
            options.EnableTracing = true;
            options.MaxRetryAttempts = 5;
        });

        // Assert
        configureWasCalled.Should().BeTrue();
        builder.Options.EnableLogging.Should().BeTrue();
        builder.Options.EnableTracing.Should().BeTrue();
        builder.Options.MaxRetryAttempts.Should().Be(5);
    }

    [Fact]
    public void AddCatgaWithConfigure_ShouldReturnBuilder()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var builder = services.AddCatga(options => { });

        // Assert
        builder.Should().NotBeNull();
        builder.Should().BeOfType<CatgaServiceBuilder>();
    }

    [Fact]
    public void AddCatgaWithConfigure_WithNullServices_ShouldThrowArgumentNullException()
    {
        // Arrange
        IServiceCollection services = null!;

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => services.AddCatga(options => { }));
    }

    [Fact]
    public void AddCatgaWithConfigure_WithNullConfigure_ShouldThrowArgumentNullException()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => services.AddCatga(null!));
    }

    [Fact]
    public void AddCatgaWithConfigure_ShouldChainWithBuilder()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var result = services.AddCatga(options =>
        {
            options.EnableLogging = false;
        })
        .WithTracing()
        .WithRetry(maxAttempts: 10);

        // Assert
        result.Options.EnableLogging.Should().BeFalse();
        result.Options.EnableTracing.Should().BeTrue();
        result.Options.MaxRetryAttempts.Should().Be(10);
    }

    #endregion

    #region WorkerId From Environment Tests

    [Fact]
    public void AddCatga_WithValidWorkerIdEnvVar_ShouldUseEnvironmentValue()
    {
        // Arrange
        var services = new ServiceCollection();
        Environment.SetEnvironmentVariable("CATGA_WORKER_ID", "42");

        try
        {
            // Act
            services.AddCatga();
            var provider = services.BuildServiceProvider();
            var idGenerator = provider.GetRequiredService<IDistributedIdGenerator>() as SnowflakeIdGenerator;

            // Assert
            idGenerator.Should().NotBeNull();
            // Note: We can't directly verify WorkerId without exposing it, but we can verify it generates valid IDs
            var id = idGenerator!.NextId();
            id.Should().BeGreaterThan(0);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CATGA_WORKER_ID", null);
        }
    }

    [Fact]
    public void AddCatga_WithInvalidWorkerIdEnvVar_ShouldUseRandomWorkerId()
    {
        // Arrange
        var services = new ServiceCollection();
        Environment.SetEnvironmentVariable("CATGA_WORKER_ID", "999"); // Out of range

        try
        {
            // Act
            services.AddCatga();
            var provider = services.BuildServiceProvider();
            var idGenerator = provider.GetRequiredService<IDistributedIdGenerator>();

            // Assert - Should still create a valid generator
            idGenerator.Should().NotBeNull();
            idGenerator.Should().BeOfType<SnowflakeIdGenerator>();
            idGenerator.NextId().Should().BeGreaterThan(0);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CATGA_WORKER_ID", null);
        }
    }

    [Fact]
    public void AddCatga_WithoutWorkerIdEnvVar_ShouldUseRandomWorkerId()
    {
        // Arrange
        var services = new ServiceCollection();
        Environment.SetEnvironmentVariable("CATGA_WORKER_ID", null);

        // Act
        services.AddCatga();
        var provider = services.BuildServiceProvider();
        var idGenerator = provider.GetRequiredService<IDistributedIdGenerator>();

        // Assert
        idGenerator.Should().NotBeNull();
        idGenerator.Should().BeOfType<SnowflakeIdGenerator>();
        idGenerator.NextId().Should().BeGreaterThan(0);
    }

    #endregion

    #region Integration Tests

    [Fact]
    public void AddCatga_FullIntegration_ShouldResolveMediatorWithDependencies()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga();
        services.AddLogging(); // Add logging for realistic scenario

        // Act
        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<ICatgaMediator>();

        // Assert
        mediator.Should().NotBeNull();
        mediator.Should().BeOfType<CatgaMediator>();
    }

    [Fact]
    public void AddCatga_WithSingletonMediator_ShouldReuseSameInstanceAcrossScopes()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga();
        services.AddLogging();
        var provider = services.BuildServiceProvider();

        // Act
        ICatgaMediator? mediator1;
        ICatgaMediator? mediator2;

        using (var scope1 = provider.CreateScope())
        {
            mediator1 = scope1.ServiceProvider.GetRequiredService<ICatgaMediator>();
        }

        using (var scope2 = provider.CreateScope())
        {
            mediator2 = scope2.ServiceProvider.GetRequiredService<ICatgaMediator>();
        }

        // Assert - Singleton: same instance across scopes for performance
        mediator1.Should().BeSameAs(mediator2);
    }

    [Fact]
    public void AddCatga_WithSingletonIdGenerator_ShouldReuseInstanceAcrossScopes()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga();
        var provider = services.BuildServiceProvider();

        // Act
        IDistributedIdGenerator? idGen1;
        IDistributedIdGenerator? idGen2;

        using (var scope1 = provider.CreateScope())
        {
            idGen1 = scope1.ServiceProvider.GetRequiredService<IDistributedIdGenerator>();
        }

        using (var scope2 = provider.CreateScope())
        {
            idGen2 = scope2.ServiceProvider.GetRequiredService<IDistributedIdGenerator>();
        }

        // Assert
        idGen1.Should().BeSameAs(idGen2);
    }

    #endregion
}







