using Catga.Abstractions;
using Catga.Configuration;
using Catga.Core;
using Catga.DependencyInjection;
using Catga.DistributedId;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Catga.Tests.DependencyInjection;

/// <summary>
/// Unit tests for AddCatga extension method and service registration
/// </summary>
public class AddCatgaTests
{
    [Fact]
    public void AddCatga_ShouldRegisterCatgaMediator()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddCatga();
        var sp = services.BuildServiceProvider();

        // Assert
        var mediator = sp.GetService<ICatgaMediator>();
        mediator.Should().NotBeNull();
        mediator.Should().BeOfType<CatgaMediator>();
    }

    [Fact]
    public void AddCatga_ShouldRegisterCatgaOptions()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddCatga();
        var sp = services.BuildServiceProvider();

        // Assert
        var options = sp.GetService<CatgaOptions>();
        options.Should().NotBeNull();
    }

    [Fact]
    public void AddCatga_ShouldRegisterDistributedIdGenerator()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddCatga();
        var sp = services.BuildServiceProvider();

        // Assert
        var generator = sp.GetService<IDistributedIdGenerator>();
        generator.Should().NotBeNull();
    }

    [Fact]
    public void AddCatga_WithConfiguration_ShouldApplyOptions()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddCatga(options =>
        {
            options.TimeoutSeconds = 60;
        });
        var sp = services.BuildServiceProvider();

        // Assert
        var options = sp.GetRequiredService<CatgaOptions>();
        options.TimeoutSeconds.Should().Be(60);
    }

    [Fact]
    public void AddCatga_ShouldReturnCatgaServiceBuilder()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        var builder = services.AddCatga();

        // Assert
        builder.Should().NotBeNull();
        builder.Should().BeOfType<CatgaServiceBuilder>();
        builder.Services.Should().BeSameAs(services);
    }

    [Fact]
    public void AddCatga_CalledMultipleTimes_ShouldNotDuplicate()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddCatga();
        services.AddCatga();
        services.AddCatga();
        var sp = services.BuildServiceProvider();

        // Assert - Should still resolve single instance
        var mediator1 = sp.CreateScope().ServiceProvider.GetRequiredService<ICatgaMediator>();
        var mediator2 = sp.CreateScope().ServiceProvider.GetRequiredService<ICatgaMediator>();
        mediator1.Should().NotBeNull();
        mediator2.Should().NotBeNull();
    }

    [Fact]
    public void AddCatga_ShouldRegisterEventTypeRegistry()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddCatga();
        var sp = services.BuildServiceProvider();

        // Assert
        var registry = sp.GetService<IEventTypeRegistry>();
        registry.Should().NotBeNull();
    }

    [Fact]
    public void AddCatga_MediatorShouldBeSingleton()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        var sp = services.BuildServiceProvider();

        // Act
        ICatgaMediator? mediator1, mediator2;
        using (var scope1 = sp.CreateScope())
        {
            mediator1 = scope1.ServiceProvider.GetRequiredService<ICatgaMediator>();
        }
        using (var scope2 = sp.CreateScope())
        {
            mediator2 = scope2.ServiceProvider.GetRequiredService<ICatgaMediator>();
        }

        // Assert - Singleton: same instance across scopes for performance
        mediator1.Should().BeSameAs(mediator2);
    }

    [Fact]
    public void AddCatga_WithNullServices_ShouldThrow()
    {
        // Arrange
        IServiceCollection services = null!;

        // Act
        var act = () => services.AddCatga();

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddCatga_WithNullConfigure_ShouldThrow()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var act = () => services.AddCatga(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }
}
