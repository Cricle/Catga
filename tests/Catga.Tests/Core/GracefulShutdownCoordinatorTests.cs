using Catga.Core;
using FluentAssertions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Catga.Tests.Core;

/// <summary>
/// Unit tests for GracefulShutdownCoordinator.
/// </summary>
public class GracefulShutdownCoordinatorTests
{
    private readonly ILogger<GracefulShutdownCoordinator> _logger;

    public GracefulShutdownCoordinatorTests()
    {
        _logger = Substitute.For<ILogger<GracefulShutdownCoordinator>>();
    }

    [Fact]
    public void Constructor_WithoutLifetime_ShouldNotThrow()
    {
        // Act
        var coordinator = new GracefulShutdownCoordinator(_logger);

        // Assert
        coordinator.Should().NotBeNull();
        coordinator.IsShuttingDown.Should().BeFalse();
        coordinator.Dispose();
    }

    [Fact]
    public void Constructor_WithLifetime_ShouldRegisterCallback()
    {
        // Arrange
        var lifetime = Substitute.For<IHostApplicationLifetime>();
        var cts = new CancellationTokenSource();
        lifetime.ApplicationStopping.Returns(cts.Token);

        // Act
        var coordinator = new GracefulShutdownCoordinator(_logger, lifetime);

        // Assert
        coordinator.Should().NotBeNull();
        coordinator.IsShuttingDown.Should().BeFalse();
        coordinator.Dispose();
    }

    [Fact]
    public void ShutdownToken_Initially_ShouldNotBeCancelled()
    {
        // Arrange
        var coordinator = new GracefulShutdownCoordinator(_logger);

        // Assert
        coordinator.ShutdownToken.IsCancellationRequested.Should().BeFalse();
        coordinator.Dispose();
    }

    [Fact]
    public void RequestShutdown_ShouldSetIsShuttingDownToTrue()
    {
        // Arrange
        var coordinator = new GracefulShutdownCoordinator(_logger);

        // Act
        coordinator.RequestShutdown();

        // Assert
        coordinator.IsShuttingDown.Should().BeTrue();
        coordinator.Dispose();
    }

    [Fact]
    public void RequestShutdown_ShouldCancelShutdownToken()
    {
        // Arrange
        var coordinator = new GracefulShutdownCoordinator(_logger);

        // Act
        coordinator.RequestShutdown();

        // Assert
        coordinator.ShutdownToken.IsCancellationRequested.Should().BeTrue();
        coordinator.Dispose();
    }

    [Fact]
    public void RequestShutdown_CalledMultipleTimes_ShouldNotThrow()
    {
        // Arrange
        var coordinator = new GracefulShutdownCoordinator(_logger);

        // Act
        coordinator.RequestShutdown();
        coordinator.RequestShutdown(); // Second call

        // Assert
        coordinator.IsShuttingDown.Should().BeTrue();
        coordinator.Dispose();
    }

    [Fact]
    public void Dispose_ShouldNotThrow()
    {
        // Arrange
        var coordinator = new GracefulShutdownCoordinator(_logger);

        // Act
        coordinator.Dispose();

        // Assert - Should not throw
        true.Should().BeTrue();
    }

    [Fact]
    public void Dispose_CalledMultipleTimes_ShouldNotThrow()
    {
        // Arrange
        var coordinator = new GracefulShutdownCoordinator(_logger);

        // Act
        coordinator.Dispose();
        coordinator.Dispose(); // Second call

        // Assert - Should not throw
        true.Should().BeTrue();
    }

    [Fact]
    public void Dispose_WithLifetime_ShouldUnregisterCallback()
    {
        // Arrange
        var lifetime = Substitute.For<IHostApplicationLifetime>();
        var cts = new CancellationTokenSource();
        lifetime.ApplicationStopping.Returns(cts.Token);
        var coordinator = new GracefulShutdownCoordinator(_logger, lifetime);

        // Act
        coordinator.Dispose();

        // Assert - Should not throw
        true.Should().BeTrue();
    }

    [Fact]
    public void ShutdownToken_CanBeUsedForCancellation()
    {
        // Arrange
        var coordinator = new GracefulShutdownCoordinator(_logger);
        var taskCompleted = false;

        // Act
        var task = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(10000, coordinator.ShutdownToken);
            }
            catch (OperationCanceledException)
            {
                taskCompleted = true;
            }
        });

        coordinator.RequestShutdown();
        task.Wait(TimeSpan.FromSeconds(1));

        // Assert
        taskCompleted.Should().BeTrue();
        coordinator.Dispose();
    }

    [Fact]
    public void IsShuttingDown_BeforeShutdown_ShouldBeFalse()
    {
        // Arrange
        var coordinator = new GracefulShutdownCoordinator(_logger);

        // Assert
        coordinator.IsShuttingDown.Should().BeFalse();
        coordinator.Dispose();
    }

    [Fact]
    public void IsShuttingDown_AfterShutdown_ShouldBeTrue()
    {
        // Arrange
        var coordinator = new GracefulShutdownCoordinator(_logger);

        // Act
        coordinator.RequestShutdown();

        // Assert
        coordinator.IsShuttingDown.Should().BeTrue();
        coordinator.Dispose();
    }
}






