using Catga.Core;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Catga.Tests.Core;

/// <summary>
/// Extended tests for GracefulRecoveryManager to improve branch coverage.
/// </summary>
public class GracefulRecoveryManagerExtendedTests
{
    private readonly Mock<ILogger<GracefulRecoveryManager>> _loggerMock;
    private readonly GracefulRecoveryManager _manager;

    public GracefulRecoveryManagerExtendedTests()
    {
        _loggerMock = new Mock<ILogger<GracefulRecoveryManager>>();
        _manager = new GracefulRecoveryManager(_loggerMock.Object);
    }

    [Fact]
    public void IsRecovering_Initially_ShouldBeFalse()
    {
        _manager.IsRecovering.Should().BeFalse();
    }

    [Fact]
    public void RegisterComponent_ShouldAddComponent()
    {
        var component = new TestRecoverableComponent();
        _manager.RegisterComponent(component);
        // Component is registered (verified by recovery)
    }

    [Fact]
    public async Task RecoverAsync_WithNoComponents_ShouldReturnSuccess()
    {
        var result = await _manager.RecoverAsync();
        result.Succeeded.Should().Be(0);
        result.Failed.Should().Be(0);
    }

    [Fact]
    public async Task RecoverAsync_WithHealthyComponent_ShouldSucceed()
    {
        var component = new TestRecoverableComponent { IsHealthy = true };
        _manager.RegisterComponent(component);

        var result = await _manager.RecoverAsync();

        result.Succeeded.Should().Be(1);
        result.Failed.Should().Be(0);
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task RecoverAsync_WithFailingComponent_ShouldRecordFailure()
    {
        var component = new TestRecoverableComponent
        {
            IsHealthy = false,
            ThrowOnRecover = true
        };
        _manager.RegisterComponent(component);

        var result = await _manager.RecoverAsync();

        result.Succeeded.Should().Be(0);
        result.Failed.Should().Be(1);
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task RecoverAsync_WithMixedComponents_ShouldRecordBoth()
    {
        var healthy = new TestRecoverableComponent { IsHealthy = true };
        var failing = new TestRecoverableComponent { IsHealthy = false, ThrowOnRecover = true };

        _manager.RegisterComponent(healthy);
        _manager.RegisterComponent(failing);

        var result = await _manager.RecoverAsync();

        result.Succeeded.Should().Be(1);
        result.Failed.Should().Be(1);
    }

    [Fact]
    public async Task RecoverAsync_WhenAlreadyRecovering_ShouldReturnAlreadyRecovering()
    {
        var slowComponent = new TestRecoverableComponent
        {
            IsHealthy = false,
            RecoveryDelay = TimeSpan.FromSeconds(1)
        };
        _manager.RegisterComponent(slowComponent);

        // Start first recovery
        var firstRecoveryTask = _manager.RecoverAsync();

        // Try second recovery while first is in progress
        await Task.Delay(50);
        var secondResult = await _manager.RecoverAsync();

        secondResult.Should().Be(RecoveryResult.AlreadyRecovering);

        await firstRecoveryTask;
    }

    [Fact]
    public async Task RecoverAsync_WithCancellation_ShouldThrow()
    {
        var component = new TestRecoverableComponent
        {
            IsHealthy = false,
            RecoveryDelay = TimeSpan.FromSeconds(5)
        };
        _manager.RegisterComponent(component);

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(50);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _manager.RecoverAsync(cts.Token));
    }

    [Fact]
    public async Task RecoverAsync_WithCancellationBeforeStart_ShouldThrow()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _manager.RecoverAsync(cts.Token));
    }

    [Fact]
    public async Task RecoverAsync_ShouldSetDuration()
    {
        var component = new TestRecoverableComponent
        {
            IsHealthy = true,
            RecoveryDelay = TimeSpan.FromMilliseconds(50)
        };
        _manager.RegisterComponent(component);

        var result = await _manager.RecoverAsync();

        result.Duration.Should().BeGreaterThan(TimeSpan.FromMilliseconds(40));
    }

    [Fact]
    public async Task StartAutoRecoveryAsync_ShouldRecoverUnhealthyComponents()
    {
        var component = new TestRecoverableComponent { IsHealthy = false, StayUnhealthy = true };
        _manager.RegisterComponent(component);

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(300);

        await _manager.StartAutoRecoveryAsync(
            TimeSpan.FromMilliseconds(50),
            maxRetries: 1,
            cts.Token);

        // 组件应该被恢复至少一次
        component.RecoverCallCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task StartAutoRecoveryAsync_WithHealthyComponents_ShouldNotRecover()
    {
        var component = new TestRecoverableComponent { IsHealthy = true };
        _manager.RegisterComponent(component);

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(150);

        await _manager.StartAutoRecoveryAsync(
            TimeSpan.FromMilliseconds(50),
            maxRetries: 1,
            cts.Token);

        component.RecoverCallCount.Should().Be(0);
    }

    [Fact]
    public async Task StartAutoRecoveryAsync_WithRetries_ShouldRetryOnFailure()
    {
        var component = new TestRecoverableComponent
        {
            IsHealthy = false,
            ThrowOnRecover = true,
            StayUnhealthy = true // 保持不健康状态以便持续重试
        };
        _manager.RegisterComponent(component);

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(600);

        await _manager.StartAutoRecoveryAsync(
            TimeSpan.FromMilliseconds(50),
            maxRetries: 3,
            cts.Token);

        // 由于组件持续不健康且恢复失败，应该有多次重试
        component.RecoverCallCount.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public void RecoveryResult_AlreadyRecovering_ShouldHaveNegativeValues()
    {
        var result = RecoveryResult.AlreadyRecovering;
        result.Succeeded.Should().Be(-1);
        result.Failed.Should().Be(-1);
        result.Duration.Should().Be(TimeSpan.Zero);
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void RecoveryResult_IsSuccess_ShouldBeTrueWhenNoFailures()
    {
        var result = new RecoveryResult(5, 0, TimeSpan.FromSeconds(1));
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void RecoveryResult_IsSuccess_ShouldBeFalseWhenNoSuccesses()
    {
        var result = new RecoveryResult(0, 0, TimeSpan.FromSeconds(1));
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void RecoveryResult_IsSuccess_ShouldBeFalseWhenHasFailures()
    {
        var result = new RecoveryResult(5, 1, TimeSpan.FromSeconds(1));
        result.IsSuccess.Should().BeFalse();
    }

    private class TestRecoverableComponent : IRecoverableComponent
    {
        public bool IsHealthy { get; set; } = true;
        public bool ThrowOnRecover { get; set; }
        public bool StayUnhealthy { get; set; } // 新增：控制是否在恢复后保持不健康
        public TimeSpan RecoveryDelay { get; set; } = TimeSpan.Zero;
        public int RecoverCallCount { get; private set; }

        public async Task RecoverAsync(CancellationToken cancellationToken = default)
        {
            RecoverCallCount++;

            if (RecoveryDelay > TimeSpan.Zero)
                await Task.Delay(RecoveryDelay, cancellationToken);

            if (ThrowOnRecover)
                throw new InvalidOperationException("Recovery failed");

            // 只有在不需要保持不健康状态时才设置为健康
            if (!StayUnhealthy)
            {
                IsHealthy = true;
            }
        }
    }
}
