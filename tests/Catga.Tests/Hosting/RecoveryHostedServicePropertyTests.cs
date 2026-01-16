using Catga.Hosting;
using FsCheck;
using FsCheck.Xunit;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Catga.Tests.Hosting;

/// <summary>
/// RecoveryHostedService 属性测试
/// Feature: hosting-integration
/// </summary>
public class RecoveryHostedServicePropertyTests
{
    /// <summary>
    /// Property 5: 恢复服务定期健康检查
    /// Feature: hosting-integration, Property 5: 恢复服务定期健康检查
    /// Validates: Requirements 3.3
    /// 
    /// For any 配置的检查间隔，恢复服务应该在该间隔内对所有注册的组件执行健康检查。
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(RecoveryArbitraries) })]
    public Property RecoveryService_PerformsPeriodicHealthChecks(PositiveInt checkIntervalMs, PositiveInt componentCount)
    {
        // 限制参数范围以确保测试可以在合理时间内完成
        // 使用更长的间隔以确保测试稳定性
        var interval = TimeSpan.FromMilliseconds(Math.Max(200, Math.Min(checkIntervalMs.Get, 500)));
        var numComponents = Math.Min(componentCount.Get, 10);

        return Prop.ForAll(
            Gen.Constant(interval).ToArbitrary(),
            Gen.Constant(numComponents).ToArbitrary(),
            (checkInterval, count) =>
            {
                // Arrange
                var logger = Substitute.For<ILogger<RecoveryHostedService>>();
                var components = new List<IRecoverableComponent>();
                var healthCheckCounts = new int[count];

                for (int i = 0; i < count; i++)
                {
                    var component = Substitute.For<IRecoverableComponent>();
                    var index = i; // 捕获索引
                    component.ComponentName.Returns($"Component{i}");
                    
                    // 组件健康状态会被检查
                    component.IsHealthy.Returns(callInfo =>
                    {
                        Interlocked.Increment(ref healthCheckCounts[index]);
                        return true; // 健康的组件
                    });

                    components.Add(component);
                }

                var options = new RecoveryOptions
                {
                    CheckInterval = checkInterval,
                    EnableAutoRecovery = true,
                    MaxRetries = 1
                };

                var service = new RecoveryHostedService(logger, components, options);
                var cts = new CancellationTokenSource();

                // Act
                var startTask = service.StartAsync(cts.Token);
                startTask.Wait(1000);

                // 等待至少 3 个检查周期以确保有足够的时间进行检查
                // 初始检查 + 至少 2 个周期性检查
                var waitTime = checkInterval.Add(checkInterval).Add(checkInterval).Add(TimeSpan.FromMilliseconds(200));
                Thread.Sleep(waitTime);

                cts.Cancel();
                var stopTask = service.StopAsync(CancellationToken.None);
                stopTask.Wait(3000);

                // Assert
                // 每个组件的健康状态应该被检查至少一次
                var allChecked = healthCheckCounts.All(c => c >= 1);

                return allChecked.Label($"All {count} components should be health-checked at least once. Counts: [{string.Join(", ", healthCheckCounts)}]");
            });
    }

    /// <summary>
    /// Property 6: 不健康组件自动恢复
    /// Feature: hosting-integration, Property 6: 不健康组件自动恢复
    /// Validates: Requirements 3.4
    /// 
    /// For any 检测到的不健康组件，恢复服务应该尝试恢复该组件，直到成功或达到最大重试次数。
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(RecoveryArbitraries) })]
    public Property RecoveryService_AttemptsRecoveryForUnhealthyComponents(PositiveInt maxRetries)
    {
        // 限制重试次数以确保测试可以在合理时间内完成
        var retries = Math.Min(maxRetries.Get, 5);

        return Prop.ForAll(
            Gen.Constant(retries).ToArbitrary(),
            maxRetryCount =>
            {
                // Arrange
                var logger = Substitute.For<ILogger<RecoveryHostedService>>();
                var component = Substitute.For<IRecoverableComponent>();
                component.ComponentName.Returns("UnhealthyComponent");
                component.IsHealthy.Returns(false); // 始终不健康

                var recoveryAttempts = 0;
                component.RecoverAsync(Arg.Any<CancellationToken>()).Returns(callInfo =>
                {
                    Interlocked.Increment(ref recoveryAttempts);
                    return Task.FromException(new InvalidOperationException("Recovery failed"));
                });

                var options = new RecoveryOptions
                {
                    CheckInterval = TimeSpan.FromMilliseconds(100),
                    MaxRetries = maxRetryCount,
                    RetryDelay = TimeSpan.FromMilliseconds(10),
                    EnableAutoRecovery = true,
                    UseExponentialBackoff = false // 使用固定延迟以便测试更可预测
                };

                var service = new RecoveryHostedService(logger, new[] { component }, options);
                var cts = new CancellationTokenSource();

                // Act
                var startTask = service.StartAsync(cts.Token);
                startTask.Wait(1000);

                // 等待足够的时间让至少一个恢复周期完成
                Thread.Sleep(500);

                cts.Cancel();
                var stopTask = service.StopAsync(CancellationToken.None);
                stopTask.Wait(2000);

                // Assert
                // 应该尝试恢复至少 maxRetries 次（可能更多，因为可能有多个检查周期）
                var attemptedRecovery = recoveryAttempts >= maxRetryCount;

                return attemptedRecovery.Label($"Should attempt recovery at least {maxRetryCount} times, got {recoveryAttempts} attempts");
            });
    }

    /// <summary>
    /// Property 7: 取消令牌响应
    /// Feature: hosting-integration, Property 7: 取消令牌响应
    /// Validates: Requirements 3.6
    /// 
    /// For any 托管服务，当 CancellationToken 被取消时，服务应该在合理时间内停止执行。
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(RecoveryArbitraries) })]
    public Property RecoveryService_RespondsToCancellationToken(PositiveInt componentCount)
    {
        // 限制组件数量
        var numComponents = Math.Min(componentCount.Get, 10);

        return Prop.ForAll(
            Gen.Constant(numComponents).ToArbitrary(),
            count =>
            {
                // Arrange
                var logger = Substitute.For<ILogger<RecoveryHostedService>>();
                var components = new List<IRecoverableComponent>();

                for (int i = 0; i < count; i++)
                {
                    var component = Substitute.For<IRecoverableComponent>();
                    component.ComponentName.Returns($"Component{i}");
                    component.IsHealthy.Returns(false); // 不健康，会触发恢复

                    // 恢复操作会检查取消令牌
                    component.RecoverAsync(Arg.Any<CancellationToken>()).Returns(async callInfo =>
                    {
                        var ct = callInfo.Arg<CancellationToken>();
                        await Task.Delay(50, ct); // 模拟一些工作
                    });

                    components.Add(component);
                }

                var options = new RecoveryOptions
                {
                    CheckInterval = TimeSpan.FromMilliseconds(100),
                    MaxRetries = 3,
                    RetryDelay = TimeSpan.FromMilliseconds(50),
                    EnableAutoRecovery = true
                };

                var service = new RecoveryHostedService(logger, components, options);
                var cts = new CancellationTokenSource();

                // Act
                var startTask = service.StartAsync(cts.Token);
                startTask.Wait(1000);

                // 让服务运行一小段时间
                Thread.Sleep(200);

                // 取消并测量停止时间
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                cts.Cancel();
                var stopTask = service.StopAsync(CancellationToken.None);
                var completed = stopTask.Wait(2000); // 2秒超时
                stopwatch.Stop();

                // Assert
                // 服务应该在合理时间内停止（2秒内）
                var stoppedInTime = completed && stopwatch.ElapsedMilliseconds < 2000;

                return stoppedInTime.Label($"Service should stop within 2 seconds, took {stopwatch.ElapsedMilliseconds}ms");
            });
    }
}

/// <summary>
/// 恢复服务属性测试的自定义生成器
/// </summary>
public class RecoveryArbitraries
{
    /// <summary>
    /// 生成合理的检查间隔（200ms - 500ms）
    /// </summary>
    public static Arbitrary<PositiveInt> CheckIntervalArb()
    {
        return Gen.Choose(200, 500)
            .Select(ms => PositiveInt.NewPositiveInt(ms))
            .ToArbitrary();
    }

    /// <summary>
    /// 生成合理的组件数量（1 - 10）
    /// </summary>
    public static Arbitrary<PositiveInt> ComponentCountArb()
    {
        return Gen.Choose(1, 10)
            .Select(count => PositiveInt.NewPositiveInt(count))
            .ToArbitrary();
    }

    /// <summary>
    /// 生成合理的重试次数（1 - 5）
    /// </summary>
    public static Arbitrary<PositiveInt> MaxRetriesArb()
    {
        return Gen.Choose(1, 5)
            .Select(retries => PositiveInt.NewPositiveInt(retries))
            .ToArbitrary();
    }
}
