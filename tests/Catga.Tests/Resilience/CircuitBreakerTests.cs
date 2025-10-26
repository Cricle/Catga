using Catga.Resilience;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Catga.Tests.Resilience;

/// <summary>
/// 熔断器完整场景测试 (TDD方法)
/// 测试场景：
/// 1. 正常操作（Closed状态）
/// 2. 连续失败触发熔断（Open状态）
/// 3. 半开状态测试（HalfOpen状态）
/// 4. 自动恢复
/// 5. 并发安全性
/// 6. 手动重置
/// </summary>
public class CircuitBreakerTests
{
    private readonly ILogger<CircuitBreakerTests> _logger;

    public CircuitBreakerTests()
    {
        _logger = Substitute.For<ILogger<CircuitBreakerTests>>();
    }

    #region 基础功能测试

    [Fact]
    public async Task ExecuteAsync_InClosedState_ShouldExecuteSuccessfully()
    {
        // Arrange
        var circuitBreaker = new CircuitBreaker(failureThreshold: 3);
        var executionCount = 0;

        // Act
        await circuitBreaker.ExecuteAsync(async () =>
        {
            executionCount++;
            await Task.CompletedTask;
        });

        // Assert
        executionCount.Should().Be(1);
        circuitBreaker.State.Should().Be(CircuitState.Closed);
        circuitBreaker.ConsecutiveFailures.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_WithReturnValue_ShouldReturnCorrectResult()
    {
        // Arrange
        var circuitBreaker = new CircuitBreaker(failureThreshold: 3);
        var expectedResult = "Success Result";

        // Act
        var result = await circuitBreaker.ExecuteAsync(async () =>
        {
            await Task.Delay(10);
            return expectedResult;
        });

        // Assert
        result.Should().Be(expectedResult);
        circuitBreaker.State.Should().Be(CircuitState.Closed);
    }

    #endregion

    #region 失败计数和熔断触发测试

    [Fact]
    public async Task ExecuteAsync_WithConsecutiveFailures_ShouldOpenCircuit()
    {
        // Arrange
        var failureThreshold = 3;
        var circuitBreaker = new CircuitBreaker(failureThreshold, logger: _logger);

        // Act - 触发多次失败
        for (int i = 0; i < failureThreshold; i++)
        {
            try
            {
                await circuitBreaker.ExecuteAsync(() => throw new InvalidOperationException($"Failure {i + 1}"));
            }
            catch (InvalidOperationException)
            {
                // 预期异常
            }
        }

        // Assert
        circuitBreaker.State.Should().Be(CircuitState.Open);
        circuitBreaker.ConsecutiveFailures.Should().BeGreaterOrEqualTo(failureThreshold);
    }

    [Fact]
    public async Task ExecuteAsync_InOpenState_ShouldThrowCircuitBreakerOpenException()
    {
        // Arrange
        var circuitBreaker = new CircuitBreaker(failureThreshold: 2, openDuration: TimeSpan.FromSeconds(10));

        // 先触发熔断
        for (int i = 0; i < 2; i++)
        {
            try
            {
                await circuitBreaker.ExecuteAsync(() => throw new Exception("Failure"));
            }
            catch (Exception)
            {
                // 预期异常
            }
        }

        // Act & Assert - 熔断器打开后应该直接抛出异常
        var act = async () => await circuitBreaker.ExecuteAsync(async () => await Task.CompletedTask);
        await act.Should().ThrowAsync<CircuitBreakerOpenException>()
            .WithMessage("*Circuit breaker is open*");
    }

    [Fact]
    public async Task ExecuteAsync_SuccessAfterFailure_ShouldResetFailureCount()
    {
        // Arrange
        var circuitBreaker = new CircuitBreaker(failureThreshold: 3);

        // Act - 先失败一次
        try
        {
            await circuitBreaker.ExecuteAsync(() => throw new Exception("Failure"));
        }
        catch
        {
            // 预期异常
        }

        circuitBreaker.ConsecutiveFailures.Should().Be(1);

        // 然后成功执行
        await circuitBreaker.ExecuteAsync(async () => await Task.CompletedTask);

        // Assert - 失败计数应该被重置
        circuitBreaker.ConsecutiveFailures.Should().Be(0);
        circuitBreaker.State.Should().Be(CircuitState.Closed);
    }

    #endregion

    #region 半开状态和恢复测试

    [Fact]
    public async Task ExecuteAsync_AfterOpenDuration_ShouldTransitionToHalfOpen()
    {
        // Arrange
        var openDuration = TimeSpan.FromMilliseconds(200);
        var circuitBreaker = new CircuitBreaker(failureThreshold: 2, openDuration: openDuration, logger: _logger);

        // 触发熔断
        for (int i = 0; i < 2; i++)
        {
            try
            {
                await circuitBreaker.ExecuteAsync(() => throw new Exception("Failure"));
            }
            catch
            {
                // 预期异常
            }
        }

        circuitBreaker.State.Should().Be(CircuitState.Open);

        // Act - 等待超过打开持续时间
        await Task.Delay(openDuration + TimeSpan.FromMilliseconds(100));

        // 尝试执行操作
        await circuitBreaker.ExecuteAsync(async () => await Task.CompletedTask);

        // Assert - 成功后应该回到Closed状态
        circuitBreaker.State.Should().Be(CircuitState.Closed);
        circuitBreaker.ConsecutiveFailures.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_HalfOpenSuccessful_ShouldCloseCircuit()
    {
        // Arrange
        var openDuration = TimeSpan.FromMilliseconds(100);
        var circuitBreaker = new CircuitBreaker(failureThreshold: 2, openDuration: openDuration);

        // 触发熔断
        for (int i = 0; i < 2; i++)
        {
            try
            {
                await circuitBreaker.ExecuteAsync(() => throw new Exception());
            }
            catch { }
        }

        // 等待进入HalfOpen状态
        await Task.Delay(openDuration + TimeSpan.FromMilliseconds(50));

        // Act - 在HalfOpen状态成功执行
        await circuitBreaker.ExecuteAsync(async () => await Task.CompletedTask);

        // Assert
        circuitBreaker.State.Should().Be(CircuitState.Closed);
        circuitBreaker.ConsecutiveFailures.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_HalfOpenFailure_ShouldReopenCircuit()
    {
        // Arrange
        var openDuration = TimeSpan.FromMilliseconds(100);
        var circuitBreaker = new CircuitBreaker(failureThreshold: 2, openDuration: openDuration);

        // 触发熔断
        for (int i = 0; i < 2; i++)
        {
            try
            {
                await circuitBreaker.ExecuteAsync(() => throw new Exception());
            }
            catch { }
        }

        // 等待进入HalfOpen状态
        await Task.Delay(openDuration + TimeSpan.FromMilliseconds(50));

        // Act - 在HalfOpen状态失败
        try
        {
            await circuitBreaker.ExecuteAsync(() => throw new Exception("HalfOpen Failure"));
        }
        catch { }

        // Assert - 应该重新打开
        circuitBreaker.State.Should().Be(CircuitState.Open);
    }

    #endregion

    #region 并发安全性测试

    [Fact]
    public async Task ExecuteAsync_ConcurrentRequests_ShouldBeThreadSafe()
    {
        // Arrange
        var circuitBreaker = new CircuitBreaker(failureThreshold: 5);
        var successCount = 0;
        var tasks = new List<Task>();

        // Act - 并发执行50个操作
        for (int i = 0; i < 50; i++)
        {
            var task = Task.Run(async () =>
            {
                await circuitBreaker.ExecuteAsync(async () =>
                {
                    await Task.Delay(10);
                    Interlocked.Increment(ref successCount);
                });
            });
            tasks.Add(task);
        }

        await Task.WhenAll(tasks);

        // Assert
        successCount.Should().Be(50);
        circuitBreaker.State.Should().Be(CircuitState.Closed);
        circuitBreaker.ConsecutiveFailures.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_ConcurrentFailures_ShouldOpenCircuitOnce()
    {
        // Arrange
        var failureThreshold = 3;
        var circuitBreaker = new CircuitBreaker(failureThreshold, logger: _logger);
        var tasks = new List<Task>();
        var failureCount = 0;
        var circuitOpenCount = 0;

        // Act - 并发触发多个失败
        for (int i = 0; i < 10; i++)
        {
            var task = Task.Run(async () =>
            {
                try
                {
                    await circuitBreaker.ExecuteAsync(() => throw new Exception("Concurrent Failure"));
                }
                catch (CircuitBreakerOpenException)
                {
                    Interlocked.Increment(ref circuitOpenCount);
                }
                catch (Exception)
                {
                    Interlocked.Increment(ref failureCount);
                }
            });
            tasks.Add(task);
        }

        await Task.WhenAll(tasks);

        // Assert
        circuitBreaker.State.Should().Be(CircuitState.Open);
        (failureCount + circuitOpenCount).Should().Be(10);
    }

    [Fact]
    public async Task ExecuteAsync_ConcurrentTransitionToHalfOpen_ShouldBeThreadSafe()
    {
        // Arrange
        var openDuration = TimeSpan.FromMilliseconds(100);
        var circuitBreaker = new CircuitBreaker(failureThreshold: 2, openDuration: openDuration);

        // 触发熔断
        for (int i = 0; i < 2; i++)
        {
            try
            {
                await circuitBreaker.ExecuteAsync(() => throw new Exception());
            }
            catch { }
        }

        // 等待进入HalfOpen窗口
        await Task.Delay(openDuration + TimeSpan.FromMilliseconds(50));

        // Act - 多个并发请求尝试从Open转到HalfOpen
        var tasks = new List<Task>();
        var successCount = 0;

        for (int i = 0; i < 10; i++)
        {
            var task = Task.Run(async () =>
            {
                try
                {
                    await circuitBreaker.ExecuteAsync(async () => await Task.CompletedTask);
                    Interlocked.Increment(ref successCount);
                }
                catch { }
            });
            tasks.Add(task);
        }

        await Task.WhenAll(tasks);

        // Assert - 至少有一个成功，电路应该关闭
        successCount.Should().BeGreaterThan(0);
        circuitBreaker.State.Should().Be(CircuitState.Closed);
    }

    #endregion

    #region 手动控制测试

    [Fact]
    public async Task Reset_ShouldResetCircuitToClosedState()
    {
        // Arrange
        var circuitBreaker = new CircuitBreaker(failureThreshold: 2);

        // 触发熔断
        for (int i = 0; i < 2; i++)
        {
            try
            {
                await circuitBreaker.ExecuteAsync(() => throw new Exception());
            }
            catch { }
        }

        circuitBreaker.State.Should().Be(CircuitState.Open);

        // Act
        circuitBreaker.Reset();

        // Assert
        circuitBreaker.State.Should().Be(CircuitState.Closed);
        circuitBreaker.ConsecutiveFailures.Should().Be(0);
    }

    #endregion

    #region 边界条件测试

    [Fact]
    public void Constructor_WithInvalidThreshold_ShouldThrowArgumentException()
    {
        // Act & Assert
        var act = () => new CircuitBreaker(failureThreshold: 0);
        act.Should().Throw<ArgumentOutOfRangeException>();

        var act2 = () => new CircuitBreaker(failureThreshold: -1);
        act2.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task ExecuteAsync_WithExactThreshold_ShouldOpenCircuit()
    {
        // Arrange
        var failureThreshold = 5;
        var circuitBreaker = new CircuitBreaker(failureThreshold);

        // Act - 精确触发阈值次数的失败
        for (int i = 0; i < failureThreshold; i++)
        {
            try
            {
                await circuitBreaker.ExecuteAsync(() => throw new Exception());
            }
            catch { }
        }

        // Assert
        circuitBreaker.State.Should().Be(CircuitState.Open);
    }

    [Fact]
    public async Task ExecuteAsync_WithOneFailureBelowThreshold_ShouldStayClosed()
    {
        // Arrange
        var failureThreshold = 5;
        var circuitBreaker = new CircuitBreaker(failureThreshold);

        // Act - 失败次数比阈值少1
        for (int i = 0; i < failureThreshold - 1; i++)
        {
            try
            {
                await circuitBreaker.ExecuteAsync(() => throw new Exception());
            }
            catch { }
        }

        // Assert
        circuitBreaker.State.Should().Be(CircuitState.Closed);
        circuitBreaker.ConsecutiveFailures.Should().Be(failureThreshold - 1);
    }

    #endregion

    #region 复杂场景测试

    [Fact]
    public async Task ExecuteAsync_MultipleOpenCloseTransitions_ShouldWorkCorrectly()
    {
        // Arrange
        var openDuration = TimeSpan.FromMilliseconds(100);
        var circuitBreaker = new CircuitBreaker(failureThreshold: 2, openDuration: openDuration);

        // 第一次打开
        for (int i = 0; i < 2; i++)
        {
            try
            {
                await circuitBreaker.ExecuteAsync(() => throw new Exception());
            }
            catch { }
        }
        circuitBreaker.State.Should().Be(CircuitState.Open);

        // 等待并恢复
        await Task.Delay(openDuration + TimeSpan.FromMilliseconds(50));
        await circuitBreaker.ExecuteAsync(async () => await Task.CompletedTask);
        circuitBreaker.State.Should().Be(CircuitState.Closed);

        // 再次打开
        for (int i = 0; i < 2; i++)
        {
            try
            {
                await circuitBreaker.ExecuteAsync(() => throw new Exception());
            }
            catch { }
        }
        circuitBreaker.State.Should().Be(CircuitState.Open);

        // 再次恢复
        await Task.Delay(openDuration + TimeSpan.FromMilliseconds(50));
        await circuitBreaker.ExecuteAsync(async () => await Task.CompletedTask);
        circuitBreaker.State.Should().Be(CircuitState.Closed);
    }

    [Fact]
    public async Task ExecuteAsync_MixedSuccessAndFailure_ShouldResetOnSuccess()
    {
        // Arrange
        var circuitBreaker = new CircuitBreaker(failureThreshold: 3);

        // Act - 交替成功和失败
        // 失败
        try
        {
            await circuitBreaker.ExecuteAsync(() => throw new Exception());
        }
        catch { }
        circuitBreaker.ConsecutiveFailures.Should().Be(1);

        // 成功（重置计数）
        await circuitBreaker.ExecuteAsync(async () => await Task.CompletedTask);
        circuitBreaker.ConsecutiveFailures.Should().Be(0);

        // 再次失败
        try
        {
            await circuitBreaker.ExecuteAsync(() => throw new Exception());
        }
        catch { }
        circuitBreaker.ConsecutiveFailures.Should().Be(1);

        // 再次成功
        await circuitBreaker.ExecuteAsync(async () => await Task.CompletedTask);
        circuitBreaker.ConsecutiveFailures.Should().Be(0);

        // Assert - 电路应该保持关闭
        circuitBreaker.State.Should().Be(CircuitState.Closed);
    }

    #endregion

    #region 性能测试

    [Fact]
    public async Task ExecuteAsync_HighThroughput_ShouldMaintainPerformance()
    {
        // Arrange
        var circuitBreaker = new CircuitBreaker(failureThreshold: 1000);
        var operationCount = 10000;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Act
        for (int i = 0; i < operationCount; i++)
        {
            await circuitBreaker.ExecuteAsync(async () => await Task.CompletedTask);
        }

        stopwatch.Stop();

        // Assert - 10000次操作应该在100ms内完成（考虑CI环境）
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(100);
        circuitBreaker.State.Should().Be(CircuitState.Closed);
    }

    #endregion
}

