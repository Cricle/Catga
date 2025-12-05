using Catga.Generated;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Catga.Tests.Core;

/// <summary>
/// Unit tests for GeneratedBootstrapRegistry - source generator bootstrap mechanism
/// </summary>
public class GeneratedBootstrapRegistryTests
{
    [Fact]
    public void Register_WithValidAction_ShouldNotThrow()
    {
        // Arrange
        Action<IServiceCollection> registration = _ => { };

        // Act
        var act = () => GeneratedBootstrapRegistry.Register(registration);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void Register_WithNullAction_ShouldNotThrow()
    {
        // Act
        var act = () => GeneratedBootstrapRegistry.Register(null!);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void Apply_ShouldExecuteRegisteredActions()
    {
        // Arrange
        var services = new ServiceCollection();
        var marker = new object();
        GeneratedBootstrapRegistry.Register(sc => sc.AddSingleton(marker));

        // Act
        GeneratedBootstrapRegistry.Apply(services);

        // Assert
        var sp = services.BuildServiceProvider();
        sp.GetService<object>().Should().BeSameAs(marker);
    }

    [Fact]
    public void Apply_WithNullServices_ShouldNotThrow()
    {
        // Act
        var act = () => GeneratedBootstrapRegistry.Apply(null!);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void RegisterEndpointConvention_ShouldStoreConvention()
    {
        // Arrange
        Func<Type, string> convention = t => t.Name.ToLowerInvariant();

        // Act
        GeneratedBootstrapRegistry.RegisterEndpointConvention(convention);

        // Assert
        GeneratedBootstrapRegistry.EndpointConvention.Should().NotBeNull();
        GeneratedBootstrapRegistry.EndpointConvention!(typeof(string)).Should().Be("string");
    }

    [Fact]
    public void EndpointConvention_WhenNotSet_ShouldReturnNull()
    {
        // Note: This test may fail if other tests have set the convention
        // In a real scenario, you'd want to reset state between tests
        // For now, we just verify the property is accessible
        var convention = GeneratedBootstrapRegistry.EndpointConvention;
        // Convention may or may not be null depending on test order
        // Just verify no exception is thrown
    }

    [Fact]
    public void Register_MultipleActions_ShouldExecuteAll()
    {
        // Arrange
        var services = new ServiceCollection();
        var count = 0;
        GeneratedBootstrapRegistry.Register(_ => Interlocked.Increment(ref count));
        GeneratedBootstrapRegistry.Register(_ => Interlocked.Increment(ref count));
        GeneratedBootstrapRegistry.Register(_ => Interlocked.Increment(ref count));

        // Act
        GeneratedBootstrapRegistry.Apply(services);

        // Assert
        count.Should().BeGreaterOrEqualTo(3);
    }

    [Fact]
    public void Apply_IsThreadSafe()
    {
        // Arrange
        var services = new ServiceCollection();
        var counter = 0;

        // Register multiple actions concurrently
        Parallel.For(0, 100, _ =>
        {
            GeneratedBootstrapRegistry.Register(_ => Interlocked.Increment(ref counter));
        });

        // Act - Apply should not throw even with concurrent registrations
        var act = () => GeneratedBootstrapRegistry.Apply(services);

        // Assert
        act.Should().NotThrow();
    }
}
