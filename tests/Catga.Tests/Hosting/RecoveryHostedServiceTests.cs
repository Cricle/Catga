using Catga.Hosting;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Catga.Tests.Hosting;

/// <summary>
/// RecoveryHostedService 单元测试
/// </summary>
public class RecoveryHostedServiceTests
{
    private readonly ILogger<RecoveryHostedService> _logger;
    private readonly RecoveryOptions _options;

    public RecoveryHostedServiceTests()
    {
        _logger = Substitute.For<ILogger<RecoveryHostedService>>();
        _options = new RecoveryOptions
        {
            CheckInterval = TimeSpan.FromMilliseconds(100),
            MaxRetries = 3,
            RetryDelay = TimeSpan.FromMilliseconds(50),
            EnableAutoRecovery = true
        };
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new RecoveryHostedService(null!, Array.Empty<IRecoverableComponent>(), _options));
    }

    [Fact]
    public void Constructor_WithNullComponents_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new RecoveryHostedService(_logger, null!, _options));
    }

    [Fact]
    public void Constructor_WithNullOptions_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new RecoveryHostedService(_logger, Array.Empty<IRecoverableComponent>(), null!));
    }

    [Fact]
    public void Constructor_WithInvalidOptions_ThrowsArgumentException()
    {
        // Arrange
        var invalidOptions = new RecoveryOptions
        {
            CheckInterval = TimeSpan.Zero
        };

        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            new RecoveryHostedService(_logger, Array.Empty<IRecoverableComponent>(), invalidOptions));
    }

    [Fact]
    public async Task StartAsync_WithDisabledAutoRecovery_DoesNotStartRecovery()
    {
        // Arrange
        var options = new RecoveryOptions
        {
            EnableAutoRecovery = false
        };
        var component = Substitute.For<IRecoverableComponent>();
        component.IsHealthy.Returns(false);
        component.ComponentName.Returns("TestComponent");

        var service = new RecoveryHostedService(_logger, new[] { component }, options);
        var cts = new CancellationTokenSource();

        // Act
        await service.StartAsync(cts.Token);
        await Task.Delay(200); // 等待一段时间
        cts.Cancel();
        await service.StopAsync(CancellationToken.None);

        // Assert
        await component.DidNotReceive().RecoverAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WithHealthyComponents_DoesNotAttemptRecovery()
    {
        // Arrange
        var component = Substitute.For<IRecoverableComponent>();
        component.IsHealthy.Returns(true);
        component.ComponentName.Returns("HealthyComponent");

        var service = new RecoveryHostedService(_logger, new[] { component }, _options);
        var cts = new CancellationTokenSource();

        // Act
        await service.StartAsync(cts.Token);
        await Task.Delay(300); // 等待几个检查周期
        cts.Cancel();
        await service.StopAsync(CancellationToken.None);

        // Assert
        await component.DidNotReceive().RecoverAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WithUnhealthyComponent_AttemptsRecovery()
    {
        // Arrange
        var component = Substitute.For<IRecoverableComponent>();
        component.IsHealthy.Returns(false);
        component.ComponentName.Returns("UnhealthyComponent");
        component.RecoverAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var service = new RecoveryHostedService(_logger, new[] { component }, _options);
        var cts = new CancellationTokenSource();

        // Act
        await service.StartAsync(cts.Token);
        await Task.Delay(500); // 增加等待时间确保至少一次检查周期完成
        cts.Cancel();
        await service.StopAsync(CancellationToken.None);

        // Assert
        // 验证至少尝试了一次恢复
        await component.Received().RecoverAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecoverComponentAsync_WithSuccessfulRecovery_LogsSuccess()
    {
        // Arrange
        var component = Substitute.For<IRecoverableComponent>();
        var isHealthy = false;
        component.IsHealthy.Returns(_ => isHealthy);
        component.ComponentName.Returns("RecoverableComponent");
        component.RecoverAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            isHealthy = true; // 恢复后变为健康
            return Task.CompletedTask;
        });

        var service = new RecoveryHostedService(_logger, new[] { component }, _options);
        var cts = new CancellationTokenSource();

        // Act
        await service.StartAsync(cts.Token);
        await Task.Delay(300);
        cts.Cancel();
        await service.StopAsync(CancellationToken.None);

        // Assert
        // 应该只尝试恢复一次，因为恢复后组件变为健康
        await component.Received(1).RecoverAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecoverComponentAsync_WithFailedRecovery_RetriesUpToMaxRetries()
    {
        // Arrange
        var component = Substitute.For<IRecoverableComponent>();
        component.IsHealthy.Returns(false);
        component.ComponentName.Returns("FailingComponent");
        component.RecoverAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("Recovery failed")));

        var service = new RecoveryHostedService(_logger, new[] { component }, _options);
        var cts = new CancellationTokenSource();

        // Act
        await service.StartAsync(cts.Token);
        await Task.Delay(500); // 等待足够的时间进行重试
        cts.Cancel();
        await service.StopAsync(CancellationToken.None);

        // Assert
        // 由于组件持续不健康，每个检查周期都会尝试恢复 MaxRetries 次
        // 在 500ms 内，大约有 5 个检查周期（每 100ms 一次）
        // 所以总调用次数应该是 MaxRetries * 检查周期数
        var callCount = component.ReceivedCalls().Count(c => c.GetMethodInfo().Name == "RecoverAsync");
        Assert.True(callCount >= _options.MaxRetries, $"Expected at least {_options.MaxRetries} calls, got {callCount}");
    }

    [Fact]
    public async Task RecoverComponentAsync_WithEventualSuccess_StopsRetrying()
    {
        // Arrange
        var component = Substitute.For<IRecoverableComponent>();
        var isHealthy = false;
        component.IsHealthy.Returns(_ => isHealthy);
        component.ComponentName.Returns("EventuallyRecoverableComponent");
        
        var callCount = 0;
        component.RecoverAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            callCount++;
            if (callCount < 2)
            {
                return Task.FromException(new InvalidOperationException("Recovery failed"));
            }
            isHealthy = true; // 第二次尝试成功，组件变为健康
            return Task.CompletedTask;
        });

        var service = new RecoveryHostedService(_logger, new[] { component }, _options);
        var cts = new CancellationTokenSource();

        // Act
        await service.StartAsync(cts.Token);
        await Task.Delay(500);
        cts.Cancel();
        await service.StopAsync(CancellationToken.None);

        // Assert
        // 应该在第二次尝试时成功，不会继续重试
        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task ExecuteAsync_WithCancellation_StopsGracefully()
    {
        // Arrange
        var component = Substitute.For<IRecoverableComponent>();
        component.IsHealthy.Returns(false);
        component.ComponentName.Returns("TestComponent");
        component.RecoverAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var service = new RecoveryHostedService(_logger, new[] { component }, _options);
        var cts = new CancellationTokenSource();

        // Act
        await service.StartAsync(cts.Token);
        await Task.Delay(100);
        cts.Cancel();
        
        // 应该能够快速停止
        var stopTask = service.StopAsync(CancellationToken.None);
        var completedInTime = await Task.WhenAny(stopTask, Task.Delay(1000)) == stopTask;

        // Assert
        Assert.True(completedInTime, "Service should stop within 1 second");
    }

    [Fact]
    public void IsRecovering_InitiallyFalse()
    {
        // Arrange
        var service = new RecoveryHostedService(_logger, Array.Empty<IRecoverableComponent>(), _options);

        // Act & Assert
        Assert.False(service.IsRecovering);
    }
}
