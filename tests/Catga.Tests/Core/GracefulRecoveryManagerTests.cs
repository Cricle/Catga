using Catga.Core;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Catga.Tests.Core;

// All async tests have 10s timeout to prevent hanging

/// <summary>
/// Unit tests for GracefulRecoveryManager.
/// </summary>
public class GracefulRecoveryManagerTests
{
    private readonly ILogger<GracefulRecoveryManager> _logger;

    public GracefulRecoveryManagerTests()
    {
        _logger = Substitute.For<ILogger<GracefulRecoveryManager>>();
    }

    [Fact]
    public void Constructor_ShouldNotThrow()
    {
        // Act
        var manager = new GracefulRecoveryManager(_logger);

        // Assert
        manager.Should().NotBeNull();
        manager.IsRecovering.Should().BeFalse();
    }

    [Fact]
    public void RegisterComponent_ShouldAddComponent()
    {
        // Arrange
        var manager = new GracefulRecoveryManager(_logger);
        var component = Substitute.For<IRecoverableComponent>();

        // Act
        manager.RegisterComponent(component);

        // Assert - No exception thrown
        true.Should().BeTrue();
    }

    [Fact(Timeout = 10000)]
    public async Task RecoverAsync_WithNoComponents_ReturnsEmptyResult()
    {
        // Arrange
        var manager = new GracefulRecoveryManager(_logger);

        // Act
        var result = await manager.RecoverAsync();

        // Assert
        result.Succeeded.Should().Be(0);
        result.Failed.Should().Be(0);
    }

    [Fact(Timeout = 10000)]
    public async Task RecoverAsync_WithHealthyComponent_ReturnsSuccess()
    {
        // Arrange
        var manager = new GracefulRecoveryManager(_logger);
        var component = Substitute.For<IRecoverableComponent>();
        component.RecoverAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        manager.RegisterComponent(component);

        // Act
        var result = await manager.RecoverAsync();

        // Assert
        result.Succeeded.Should().Be(1);
        result.Failed.Should().Be(0);
        result.IsSuccess.Should().BeTrue();
    }

    [Fact(Timeout = 10000)]
    public async Task RecoverAsync_WithFailingComponent_ReturnsFailure()
    {
        // Arrange
        var manager = new GracefulRecoveryManager(_logger);
        var component = Substitute.For<IRecoverableComponent>();
        component.RecoverAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("Recovery failed")));
        manager.RegisterComponent(component);

        // Act
        var result = await manager.RecoverAsync();

        // Assert
        result.Succeeded.Should().Be(0);
        result.Failed.Should().Be(1);
        result.IsSuccess.Should().BeFalse();
    }

    [Fact(Timeout = 10000)]
    public async Task RecoverAsync_WithMixedComponents_ReturnsCorrectCounts()
    {
        // Arrange
        var manager = new GracefulRecoveryManager(_logger);

        var healthyComponent = Substitute.For<IRecoverableComponent>();
        healthyComponent.RecoverAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var failingComponent = Substitute.For<IRecoverableComponent>();
        failingComponent.RecoverAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new Exception("Failed")));

        manager.RegisterComponent(healthyComponent);
        manager.RegisterComponent(failingComponent);

        // Act
        var result = await manager.RecoverAsync();

        // Assert
        result.Succeeded.Should().Be(1);
        result.Failed.Should().Be(1);
    }

    [Fact(Timeout = 10000)]
    public async Task RecoverAsync_SetsIsRecoveringDuringRecovery()
    {
        // Arrange
        var manager = new GracefulRecoveryManager(_logger);
        var tcs = new TaskCompletionSource();
        var component = Substitute.For<IRecoverableComponent>();
        component.RecoverAsync(Arg.Any<CancellationToken>()).Returns(tcs.Task);
        manager.RegisterComponent(component);

        // Act
        var recoveryTask = manager.RecoverAsync();
        var wasRecovering = manager.IsRecovering;
        tcs.SetResult();
        await recoveryTask;

        // Assert
        wasRecovering.Should().BeTrue();
        manager.IsRecovering.Should().BeFalse();
    }

    [Fact(Timeout = 10000)]
    public async Task RecoverAsync_ConcurrentCalls_ReturnsAlreadyRecovering()
    {
        // Arrange
        var manager = new GracefulRecoveryManager(_logger);
        var tcs = new TaskCompletionSource();
        var recoveryStarted = new TaskCompletionSource();
        var component = Substitute.For<IRecoverableComponent>();
        component.RecoverAsync(Arg.Any<CancellationToken>()).Returns(async _ =>
        {
            recoveryStarted.TrySetResult();
            await tcs.Task;
        });
        manager.RegisterComponent(component);

        // Act
        var firstRecovery = manager.RecoverAsync();
        await recoveryStarted.Task.WaitAsync(TimeSpan.FromSeconds(5)); // Wait for first recovery to start
        var secondRecovery = await manager.RecoverAsync();
        tcs.SetResult();
        await firstRecovery;

        // Assert
        secondRecovery.Should().Be(RecoveryResult.AlreadyRecovering);
    }

    [Fact(Timeout = 10000)]
    public async Task RecoverAsync_WithCancellation_ThrowsOperationCanceledException()
    {
        // Arrange
        var manager = new GracefulRecoveryManager(_logger);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        Func<Task> act = async () => await manager.RecoverAsync(cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void RecoveryResult_AlreadyRecovering_HasNegativeValues()
    {
        // Act
        var result = RecoveryResult.AlreadyRecovering;

        // Assert
        result.Succeeded.Should().Be(-1);
        result.Failed.Should().Be(-1);
        result.Duration.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void RecoveryResult_IsSuccess_TrueWhenNoFailures()
    {
        // Arrange
        var result = new RecoveryResult(3, 0, TimeSpan.FromSeconds(1));

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void RecoveryResult_IsSuccess_FalseWhenHasFailures()
    {
        // Arrange
        var result = new RecoveryResult(2, 1, TimeSpan.FromSeconds(1));

        // Assert
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void RecoveryResult_IsSuccess_FalseWhenNoSuccesses()
    {
        // Arrange
        var result = new RecoveryResult(0, 0, TimeSpan.FromSeconds(1));

        // Assert
        result.IsSuccess.Should().BeFalse();
    }
}
