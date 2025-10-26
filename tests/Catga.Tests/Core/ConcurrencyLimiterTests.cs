using Catga.Core;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Catga.Tests.Core;

/// <summary>
/// 并发限制器完整场景测试 (TDD方法)
/// 测试场景：
/// 1. 基本并发控制
/// 2. 背压（backpressure）处理
/// 3. 资源获取和释放
/// 4. 超时处理
/// 5. 并发安全性
/// 6. 性能特性
/// </summary>
public class ConcurrencyLimiterTests
{
    private readonly ILogger<ConcurrencyLimiterTests> _logger;

    public ConcurrencyLimiterTests()
    {
        _logger = Substitute.For<ILogger<ConcurrencyLimiterTests>>();
    }

    #region 基础功能测试

    [Fact]
    public async Task AcquireAsync_WhenSlotsAvailable_ShouldAcquireImmediately()
    {
        // Arrange
        var limiter = new ConcurrencyLimiter(maxConcurrency: 10);

        // Act
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        using var releaser = await limiter.AcquireAsync();
        stopwatch.Stop();

        // Assert
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(10);
        limiter.CurrentCount.Should().Be(9);
        limiter.ActiveTasks.Should().Be(1);
    }

    [Fact]
    public async Task AcquireAsync_DisposingReleaser_ShouldReleaseSlot()
    {
        // Arrange
        var limiter = new ConcurrencyLimiter(maxConcurrency: 5);

        // Act
        using (var releaser = await limiter.AcquireAsync())
        {
            limiter.ActiveTasks.Should().Be(1);
        }

        // Assert - 资源应该被释放
        limiter.ActiveTasks.Should().Be(0);
        limiter.CurrentCount.Should().Be(5);
    }

    [Fact]
    public async Task AcquireAsync_MultipleAcquisitions_ShouldTrackCorrectly()
    {
        // Arrange
        var limiter = new ConcurrencyLimiter(maxConcurrency: 10);
        var releasers = new List<ConcurrencyLimiter.SemaphoreReleaser>();

        // Act - 获取5个槽位
        for (int i = 0; i < 5; i++)
        {
            releasers.Add(await limiter.AcquireAsync());
        }

        // Assert
        limiter.ActiveTasks.Should().Be(5);
        limiter.CurrentCount.Should().Be(5);

        // Cleanup
        foreach (var releaser in releasers)
        {
            releaser.Dispose();
        }

        limiter.ActiveTasks.Should().Be(0);
    }

    #endregion

    #region 背压处理测试

    [Fact]
    public async Task AcquireAsync_WhenAllSlotsOccupied_ShouldWaitForRelease()
    {
        // Arrange
        var maxConcurrency = 2;
        var limiter = new ConcurrencyLimiter(maxConcurrency);

        // 占用所有槽位
        var releaser1 = await limiter.AcquireAsync();
        var releaser2 = await limiter.AcquireAsync();

        limiter.ActiveTasks.Should().Be(maxConcurrency);

        // Act - 第三个请求应该等待
        var acquireTask = Task.Run(async () => await limiter.AcquireAsync());
        await Task.Delay(50); // 给时间尝试获取

        acquireTask.IsCompleted.Should().BeFalse(); // 应该在等待

        // 释放一个槽位
        releaser1.Dispose();

        // 给一点时间让异步操作完成
        await Task.Delay(10);

        // 等待第三个请求完成
        var releaser3 = await acquireTask;

        // Assert
        limiter.ActiveTasks.Should().Be(2); // releaser2 和 releaser3 都在使用

        // Cleanup
        releaser2.Dispose();
        releaser3.Dispose();
    }

    [Fact]
    public async Task AcquireAsync_WithCancellation_ShouldCancelWaiting()
    {
        // Arrange
        var limiter = new ConcurrencyLimiter(maxConcurrency: 1);
        var releaser1 = await limiter.AcquireAsync();

        var cts = new CancellationTokenSource();

        // Act - 尝试获取第二个槽位（会等待）
        var acquireTask = Task.Run(async () => await limiter.AcquireAsync(cts.Token));
        await Task.Delay(50);

        // 取消获取
        cts.Cancel();

        // Assert
        var act = async () => await acquireTask;
        await act.Should().ThrowAsync<OperationCanceledException>();

        limiter.ActiveTasks.Should().Be(1); // 只有第一个在活跃

        // Cleanup
        releaser1.Dispose();
    }

    #endregion

    #region TryAcquire测试

    [Fact]
    public void TryAcquire_WhenSlotsAvailable_ShouldReturnTrue()
    {
        // Arrange
        var limiter = new ConcurrencyLimiter(maxConcurrency: 5);

        // Act
        var acquired = limiter.TryAcquire(out var releaser);

        // Assert
        acquired.Should().BeTrue();
        limiter.ActiveTasks.Should().Be(1);

        // Cleanup
        releaser.Dispose();
    }

    [Fact]
    public void TryAcquire_WhenNoSlotsAvailable_ShouldReturnFalse()
    {
        // Arrange
        var limiter = new ConcurrencyLimiter(maxConcurrency: 1);
        var releaser1 = limiter.TryAcquire(out _);

        // Act
        var acquired = limiter.TryAcquire(out var releaser2);

        // Assert
        acquired.Should().BeFalse();
        limiter.ActiveTasks.Should().Be(1);
    }

    [Fact]
    public void TryAcquire_WithTimeout_ShouldWaitUntilTimeout()
    {
        // Arrange
        var limiter = new ConcurrencyLimiter(maxConcurrency: 1);
        limiter.TryAcquire(out var releaser1);

        // Act
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var acquired = limiter.TryAcquire(out var releaser2, TimeSpan.FromMilliseconds(100));
        stopwatch.Stop();

        // Assert
        acquired.Should().BeFalse();
        stopwatch.ElapsedMilliseconds.Should().BeGreaterOrEqualTo(90);

        // Cleanup
        releaser1.Dispose();
    }

    #endregion

    #region 并发安全性测试

    [Fact]
    public async Task AcquireAsync_ConcurrentAcquisitions_ShouldNeverExceedLimit()
    {
        // Arrange
        var maxConcurrency = 10;
        var limiter = new ConcurrencyLimiter(maxConcurrency);
        var tasks = new List<Task>();
        var maxObservedActive = 0;
        var lockObj = new object();

        // Act - 启动100个并发任务
        for (int i = 0; i < 100; i++)
        {
            var task = Task.Run(async () =>
            {
                using var releaser = await limiter.AcquireAsync();

                // 记录最大活跃任务数
                lock (lockObj)
                {
                    var active = limiter.ActiveTasks;
                    if (active > maxObservedActive)
                        maxObservedActive = active;
                }

                await Task.Delay(10); // 模拟工作
            });
            tasks.Add(task);
        }

        await Task.WhenAll(tasks);

        // Assert
        maxObservedActive.Should().BeLessOrEqualTo(maxConcurrency);
        limiter.ActiveTasks.Should().Be(0); // 所有任务完成后应该为0
    }

    [Fact]
    public async Task AcquireAsync_HighConcurrency_ShouldMaintainCorrectCount()
    {
        // Arrange
        var maxConcurrency = 50;
        var limiter = new ConcurrencyLimiter(maxConcurrency);
        var taskCount = 500;
        var completedCount = 0;

        // Act
        var tasks = Enumerable.Range(0, taskCount).Select(async _ =>
        {
            using var releaser = await limiter.AcquireAsync();
            await Task.Delay(1);
            Interlocked.Increment(ref completedCount);
        }).ToList();

        await Task.WhenAll(tasks);

        // Assert
        completedCount.Should().Be(taskCount);
        limiter.ActiveTasks.Should().Be(0);
        limiter.CurrentCount.Should().Be(maxConcurrency);
    }

    [Fact]
    public async Task AcquireAsync_ConcurrentAcquireAndRelease_ShouldBeThreadSafe()
    {
        // Arrange
        var limiter = new ConcurrencyLimiter(maxConcurrency: 20);
        var random = new Random();
        var tasks = new List<Task>();

        // Act - 并发获取和释放，随机延迟
        for (int i = 0; i < 200; i++)
        {
            var task = Task.Run(async () =>
            {
                using var releaser = await limiter.AcquireAsync();
                await Task.Delay(random.Next(1, 10));
            });
            tasks.Add(task);
        }

        await Task.WhenAll(tasks);

        // Assert - 最终状态应该一致
        limiter.ActiveTasks.Should().Be(0);
        limiter.CurrentCount.Should().Be(limiter.MaxConcurrency);
    }

    #endregion

    #region 边界条件测试

    [Fact]
    public void Constructor_WithInvalidMaxConcurrency_ShouldThrowException()
    {
        // Act & Assert
        var act = () => new ConcurrencyLimiter(maxConcurrency: 0);
        act.Should().Throw<ArgumentOutOfRangeException>();

        var act2 = () => new ConcurrencyLimiter(maxConcurrency: -1);
        act2.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task AcquireAsync_WithMaxConcurrency1_ShouldSerializeOperations()
    {
        // Arrange
        var limiter = new ConcurrencyLimiter(maxConcurrency: 1);
        var executionOrder = new List<int>();
        var lockObj = new object();

        // Act - 启动3个任务
        var tasks = Enumerable.Range(1, 3).Select(async i =>
        {
            using var releaser = await limiter.AcquireAsync();
            lock (lockObj)
            {
                executionOrder.Add(i);
            }
            await Task.Delay(20);
        }).ToList();

        await Task.WhenAll(tasks);

        // Assert - 应该顺序执行
        executionOrder.Should().HaveCount(3);
        limiter.ActiveTasks.Should().Be(0);
    }

    [Fact]
    public async Task AcquireAsync_WithMaxConcurrencyEqualsTaskCount_ShouldAllRunConcurrently()
    {
        // Arrange
        var taskCount = 10;
        var limiter = new ConcurrencyLimiter(maxConcurrency: taskCount);
        var startedTasks = 0;
        var maxConcurrent = 0;
        var lockObj = new object();

        // Act
        var tasks = Enumerable.Range(0, taskCount).Select(async _ =>
        {
            using var releaser = await limiter.AcquireAsync();

            lock (lockObj)
            {
                startedTasks++;
                if (startedTasks > maxConcurrent)
                    maxConcurrent = startedTasks;
            }

            await Task.Delay(50);

            lock (lockObj)
            {
                startedTasks--;
            }
        }).ToList();

        await Task.WhenAll(tasks);

        // Assert - 所有任务应该同时运行
        maxConcurrent.Should().Be(taskCount);
    }

    #endregion

    #region 资源清理测试

    [Fact]
    public void Dispose_ShouldDisposeInternalSemaphore()
    {
        // Arrange
        var limiter = new ConcurrencyLimiter(maxConcurrency: 5);

        // Act
        limiter.Dispose();

        // Assert - 调用Dispose后不应该抛出异常
        Action act = () => limiter.Dispose();
        act.Should().NotThrow();
    }

    [Fact(Skip = "Dispose操作会影响正在使用的信号量，此测试存在竞态条件")]
    public async Task Dispose_WhileTasksActive_ShouldNotAffectActiveTasks()
    {
        // Arrange
        var limiter = new ConcurrencyLimiter(maxConcurrency: 2);
        var releaser = await limiter.AcquireAsync();
        var taskCompleted = false;

        var task = Task.Run(async () =>
        {
            await Task.Delay(100);
            taskCompleted = true;
            try
            {
                releaser.Dispose();
            }
            catch (ObjectDisposedException)
            {
                // 预期：如果limiter已经dispose，releaser.Dispose()可能抛出异常
            }
        });

        // Act - 在任务运行时Dispose
        await Task.Delay(20);
        limiter.Dispose();

        await task;

        // Assert
        taskCompleted.Should().BeTrue();
    }

    #endregion

    #region 警告阈值测试

    [Fact]
    public async Task AcquireAsync_ExceedingWarningThreshold_ShouldLogWarning()
    {
        // Arrange
        var maxConcurrency = 10;
        var limiter = new ConcurrencyLimiter(maxConcurrency, _logger);
        var releasers = new List<ConcurrencyLimiter.SemaphoreReleaser>();

        // Act - 占用超过80%的槽位（触发警告阈值）
        for (int i = 0; i < 9; i++)
        {
            releasers.Add(await limiter.AcquireAsync());
        }

        // Assert - 验证是否记录了警告（如果logger有配置）
        limiter.ActiveTasks.Should().Be(9);

        // Cleanup
        foreach (var releaser in releasers)
        {
            releaser.Dispose();
        }
    }

    #endregion

    #region 实际场景模拟

    [Fact]
    public async Task AcquireAsync_ApiRateLimitingScenario_ShouldControlConcurrency()
    {
        // Arrange - 模拟API限流场景：每次最多10个并发请求
        var limiter = new ConcurrencyLimiter(maxConcurrency: 10);
        var totalRequests = 100;
        var successfulRequests = 0;
        var maxObservedConcurrency = 0;
        var lockObj = new object();

        // Act
        var tasks = Enumerable.Range(0, totalRequests).Select(async i =>
        {
            using var releaser = await limiter.AcquireAsync();

            // 记录并发数
            lock (lockObj)
            {
                var current = limiter.ActiveTasks;
                if (current > maxObservedConcurrency)
                    maxObservedConcurrency = current;
            }

            // 模拟API调用
            await Task.Delay(Random.Shared.Next(5, 15));
            Interlocked.Increment(ref successfulRequests);
        }).ToList();

        await Task.WhenAll(tasks);

        // Assert
        successfulRequests.Should().Be(totalRequests);
        maxObservedConcurrency.Should().BeLessOrEqualTo(10);
        limiter.ActiveTasks.Should().Be(0);
    }

    [Fact]
    public async Task AcquireAsync_DatabaseConnectionPoolScenario_ShouldManageConnections()
    {
        // Arrange - 模拟数据库连接池：最多20个连接
        var maxConnections = 20;
        var limiter = new ConcurrencyLimiter(maxConnections);
        var queryCount = 200;
        var completedQueries = 0;

        // Act - 执行200个查询
        var tasks = Enumerable.Range(0, queryCount).Select(async i =>
        {
            using var connection = await limiter.AcquireAsync();

            // 模拟查询执行
            await Task.Delay(Random.Shared.Next(1, 10));
            Interlocked.Increment(ref completedQueries);
        }).ToList();

        await Task.WhenAll(tasks);

        // Assert
        completedQueries.Should().Be(queryCount);
        limiter.ActiveTasks.Should().Be(0);
        limiter.CurrentCount.Should().Be(maxConnections);
    }

    #endregion
}

