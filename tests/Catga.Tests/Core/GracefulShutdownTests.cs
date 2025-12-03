using Catga.Core;
using FluentAssertions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Catga.Tests.Core;

/// <summary>
/// Unit tests for GracefulShutdownCoordinator
/// </summary>
public class GracefulShutdownTests : IDisposable
{
    private readonly ILogger<GracefulShutdownCoordinator> _logger;
    private GracefulShutdownCoordinator? _coordinator;

    public GracefulShutdownTests()
    {
        _logger = Substitute.For<ILogger<GracefulShutdownCoordinator>>();
    }

    public void Dispose()
    {
        _coordinator?.Dispose();
    }

    [Fact]
    public void Constructor_WithoutLifetime_ShouldSucceed()
    {
        // Act
        _coordinator = new GracefulShutdownCoordinator(_logger);

        // Assert
        _coordinator.Should().NotBeNull();
        _coordinator.IsShuttingDown.Should().BeFalse();
    }

    [Fact]
    public void ShutdownToken_Initially_ShouldNotBeCancelled()
    {
        // Arrange
        _coordinator = new GracefulShutdownCoordinator(_logger);

        // Assert
        _coordinator.ShutdownToken.IsCancellationRequested.Should().BeFalse();
    }

    [Fact]
    public void RequestShutdown_ShouldSetIsShuttingDown()
    {
        // Arrange
        _coordinator = new GracefulShutdownCoordinator(_logger);

        // Act
        _coordinator.RequestShutdown();

        // Assert
        _coordinator.IsShuttingDown.Should().BeTrue();
    }

    [Fact]
    public void RequestShutdown_ShouldCancelShutdownToken()
    {
        // Arrange
        _coordinator = new GracefulShutdownCoordinator(_logger);

        // Act
        _coordinator.RequestShutdown();

        // Assert
        _coordinator.ShutdownToken.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public void RequestShutdown_CalledMultipleTimes_ShouldBeIdempotent()
    {
        // Arrange
        _coordinator = new GracefulShutdownCoordinator(_logger);

        // Act
        _coordinator.RequestShutdown();
        _coordinator.RequestShutdown();
        _coordinator.RequestShutdown();

        // Assert
        _coordinator.IsShuttingDown.Should().BeTrue();
        _coordinator.ShutdownToken.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public void Dispose_ShouldNotThrow()
    {
        // Arrange
        _coordinator = new GracefulShutdownCoordinator(_logger);

        // Act
        var act = () => _coordinator.Dispose();

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void Dispose_CalledMultipleTimes_ShouldNotThrow()
    {
        // Arrange
        _coordinator = new GracefulShutdownCoordinator(_logger);

        // Act
        var act = () =>
        {
            _coordinator.Dispose();
            _coordinator.Dispose();
        };

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void ShutdownToken_CanBeUsedWithCancellationTokenSource()
    {
        // Arrange
        _coordinator = new GracefulShutdownCoordinator(_logger);
        var taskCancelled = false;

        // Act
        var token = _coordinator.ShutdownToken;
        token.Register(() => taskCancelled = true);
        _coordinator.RequestShutdown();

        // Assert
        taskCancelled.Should().BeTrue();
    }

    [Fact]
    public async Task ShutdownToken_ShouldCancelRunningTasks()
    {
        // Arrange
        _coordinator = new GracefulShutdownCoordinator(_logger);
        var taskCompleted = false;
        var taskCancelled = false;

        var task = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(10000, _coordinator.ShutdownToken);
                taskCompleted = true;
            }
            catch (OperationCanceledException)
            {
                taskCancelled = true;
            }
        });

        // Act
        await Task.Delay(50); // Let task start
        _coordinator.RequestShutdown();
        await task;

        // Assert
        taskCompleted.Should().BeFalse();
        taskCancelled.Should().BeTrue();
    }
}
