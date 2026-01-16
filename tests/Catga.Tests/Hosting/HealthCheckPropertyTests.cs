using Catga.Hosting;
using Catga.Transport;
using FsCheck;
using FsCheck.Xunit;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NSubstitute;
using Xunit;

namespace Catga.Tests.Hosting;

/// <summary>
/// 健康检查属性测试
/// Feature: hosting-integration
/// </summary>
public class HealthCheckPropertyTests
{
    /// <summary>
    /// Property 13: 健康检查反映传输状态
    /// Feature: hosting-integration, Property 13: 健康检查反映传输状态
    /// Validates: Requirements 7.2
    /// 
    /// For any 传输服务的连接状态变化，健康检查应该返回与实际连接状态一致的结果。
    /// </summary>
    [Property(MaxTest = 100)]
    public Property TransportHealthCheck_ReflectsTransportStatus(bool isHealthy)
    {
        return Prop.ForAll(
            Gen.Constant(isHealthy).ToArbitrary(),
            healthy =>
            {
                // Arrange
                var transport = Substitute.For<IMessageTransport, IHealthCheckable>();
                transport.Name.Returns("TestTransport");
                
                var healthCheckable = (IHealthCheckable)transport;
                healthCheckable.IsHealthy.Returns(healthy);
                healthCheckable.HealthStatus.Returns(healthy ? "Connected" : "Disconnected");
                healthCheckable.LastHealthCheck.Returns(DateTimeOffset.UtcNow);

                var healthCheck = new TransportHealthCheck(transport);
                var context = new HealthCheckContext();

                // Act
                var result = healthCheck.CheckHealthAsync(context, CancellationToken.None).Result;

                // Assert
                var statusMatches = healthy
                    ? result.Status == HealthStatus.Healthy
                    : result.Status == HealthStatus.Unhealthy;

                var descriptionContainsStatus = result.Description?.Contains(healthy ? "connected" : "disconnected", StringComparison.OrdinalIgnoreCase) ?? false;

                return (statusMatches && descriptionContainsStatus)
                    .Label($"Health check status ({result.Status}) should match transport health ({healthy}). Description: {result.Description}");
            });
    }

    /// <summary>
    /// Property 14: 健康检查反映持久化状态
    /// Feature: hosting-integration, Property 14: 健康检查反映持久化状态
    /// Validates: Requirements 7.3
    /// 
    /// For any 持久化服务的存储状态变化，健康检查应该返回与实际存储状态一致的结果。
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(HealthCheckArbitraries) })]
    public Property PersistenceHealthCheck_ReflectsPersistenceStatus(PositiveInt componentCount, NonNegativeInt unhealthyCount)
    {
        // 确保不健康组件数不超过总组件数
        var totalComponents = Math.Min(componentCount.Get, 10);
        var unhealthy = Math.Min(unhealthyCount.Get, totalComponents);

        return Prop.ForAll(
            Gen.Constant(totalComponents).ToArbitrary(),
            Gen.Constant(unhealthy).ToArbitrary(),
            (total, unhealthyNum) =>
            {
                // Arrange
                var components = new List<IHealthCheckable>();
                
                for (int i = 0; i < total; i++)
                {
                    var component = Substitute.For<IHealthCheckable>();
                    var isHealthy = i >= unhealthyNum; // 前 unhealthyNum 个组件不健康
                    
                    component.IsHealthy.Returns(isHealthy);
                    component.HealthStatus.Returns(isHealthy ? "OK" : "Failed");
                    component.LastHealthCheck.Returns(DateTimeOffset.UtcNow);

                    components.Add(component);
                }

                var healthCheck = new PersistenceHealthCheck(components);
                var context = new HealthCheckContext();

                // Act
                var result = healthCheck.CheckHealthAsync(context, CancellationToken.None).Result;

                // Assert
                var expectedStatus = unhealthyNum > 0 ? HealthStatus.Unhealthy : HealthStatus.Healthy;
                var statusMatches = result.Status == expectedStatus;

                var dataContainsCount = result.Data.ContainsKey("total_components") &&
                                       (int)result.Data["total_components"] == total;

                return (statusMatches && dataContainsCount)
                    .Label($"Health check status ({result.Status}) should be {expectedStatus} with {unhealthyNum}/{total} unhealthy components");
            });
    }

    /// <summary>
    /// Property 15: 健康检查反映恢复状态
    /// Feature: hosting-integration, Property 15: 健康检查反映恢复状态
    /// Validates: Requirements 7.4
    /// 
    /// For any 恢复服务的状态变化，健康检查应该返回与实际恢复状态一致的结果。
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(HealthCheckArbitraries) })]
    public Property RecoveryHealthCheck_ReflectsRecoveryStatus(PositiveInt componentCount, NonNegativeInt unhealthyCount)
    {
        // 确保不健康组件数不超过总组件数
        var totalComponents = Math.Min(componentCount.Get, 10);
        var unhealthy = Math.Min(unhealthyCount.Get, totalComponents);

        return Prop.ForAll(
            Gen.Constant(totalComponents).ToArbitrary(),
            Gen.Constant(unhealthy).ToArbitrary(),
            (total, unhealthyNum) =>
            {
                // Arrange
                var components = new List<IRecoverableComponent>();
                
                for (int i = 0; i < total; i++)
                {
                    var component = Substitute.For<IRecoverableComponent>();
                    var isHealthy = i >= unhealthyNum; // 前 unhealthyNum 个组件不健康
                    
                    component.ComponentName.Returns($"Component{i}");
                    component.IsHealthy.Returns(isHealthy);
                    component.RecoverAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

                    components.Add(component);
                }

                var healthCheck = new RecoveryHealthCheck(components);
                var context = new HealthCheckContext();

                // Act
                var result = healthCheck.CheckHealthAsync(context, CancellationToken.None).Result;

                // Assert
                // 如果有不健康的组件，状态应该是 Degraded（因为恢复服务会尝试恢复）
                var expectedStatus = unhealthyNum > 0 ? HealthStatus.Degraded : HealthStatus.Healthy;
                var statusMatches = result.Status == expectedStatus;

                var dataContainsCount = result.Data.ContainsKey("total_components") &&
                                       (int)result.Data["total_components"] == total;

                return (statusMatches && dataContainsCount)
                    .Label($"Health check status ({result.Status}) should be {expectedStatus} with {unhealthyNum}/{total} unhealthy components");
            });
    }

    /// <summary>
    /// Property 16: 组件不健康时整体状态降级
    /// Feature: hosting-integration, Property 16: 组件不健康时整体状态降级
    /// Validates: Requirements 7.6
    /// 
    /// For any 不健康的组件，整体健康检查应该返回 Degraded 或 Unhealthy 状态，而不是 Healthy。
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(HealthCheckArbitraries) })]
    public Property HealthCheck_DegradeWhenComponentUnhealthy(PositiveInt componentCount)
    {
        var totalComponents = Math.Min(componentCount.Get, 10);

        return Prop.ForAll(
            Gen.Constant(totalComponents).ToArbitrary(),
            total =>
            {
                // Arrange - 创建至少一个不健康的组件
                var components = new List<IHealthCheckable>();
                
                for (int i = 0; i < total; i++)
                {
                    var component = Substitute.For<IHealthCheckable>();
                    // 第一个组件总是不健康的
                    var isHealthy = i > 0;
                    
                    component.IsHealthy.Returns(isHealthy);
                    component.HealthStatus.Returns(isHealthy ? "OK" : "Failed");
                    component.LastHealthCheck.Returns(DateTimeOffset.UtcNow);

                    components.Add(component);
                }

                var healthCheck = new PersistenceHealthCheck(components);
                var context = new HealthCheckContext();

                // Act
                var result = healthCheck.CheckHealthAsync(context, CancellationToken.None).Result;

                // Assert
                // 状态不应该是 Healthy
                var notHealthy = result.Status != HealthStatus.Healthy;
                
                // 应该是 Unhealthy 或 Degraded
                var isDegradedOrUnhealthy = result.Status == HealthStatus.Unhealthy || 
                                           result.Status == HealthStatus.Degraded;

                return (notHealthy && isDegradedOrUnhealthy)
                    .Label($"Health check with unhealthy component should not be Healthy, got {result.Status}");
            });
    }

    /// <summary>
    /// 额外属性测试：传输层不支持健康检查时应返回 Healthy
    /// </summary>
    [Property(MaxTest = 100)]
    public Property TransportHealthCheck_ReturnsHealthyWhenNotSupported()
    {
        return Prop.ForAll(
            Arb.Default.NonEmptyString().Generator.Select(s => s.Get).ToArbitrary(),
            transportName =>
            {
                // Arrange - 传输层不实现 IHealthCheckable
                var transport = Substitute.For<IMessageTransport>();
                transport.Name.Returns(transportName);

                var healthCheck = new TransportHealthCheck(transport);
                var context = new HealthCheckContext();

                // Act
                var result = healthCheck.CheckHealthAsync(context, CancellationToken.None).Result;

                // Assert
                var isHealthy = result.Status == HealthStatus.Healthy;
                var descriptionMentionsNoSupport = result.Description?.Contains("does not support", StringComparison.OrdinalIgnoreCase) ?? false;

                return (isHealthy && descriptionMentionsNoSupport)
                    .Label($"Transport without health check support should return Healthy. Status: {result.Status}, Description: {result.Description}");
            });
    }

    /// <summary>
    /// 额外属性测试：持久化层无组件时应返回 Healthy
    /// </summary>
    [Fact]
    public async Task PersistenceHealthCheck_ReturnsHealthyWhenNoComponents()
    {
        // Arrange
        var components = new List<IHealthCheckable>();
        var healthCheck = new PersistenceHealthCheck(components);
        var context = new HealthCheckContext();

        // Act
        var result = await healthCheck.CheckHealthAsync(context, CancellationToken.None);

        // Assert
        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Contains("No persistence components", result.Description ?? "");
    }

    /// <summary>
    /// 额外属性测试：恢复服务无组件时应返回 Healthy
    /// </summary>
    [Fact]
    public async Task RecoveryHealthCheck_ReturnsHealthyWhenNoComponents()
    {
        // Arrange
        var components = new List<IRecoverableComponent>();
        var healthCheck = new RecoveryHealthCheck(components);
        var context = new HealthCheckContext();

        // Act
        var result = await healthCheck.CheckHealthAsync(context, CancellationToken.None);

        // Assert
        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Contains("No recoverable components", result.Description ?? "");
    }
}

/// <summary>
/// 健康检查属性测试的自定义生成器
/// </summary>
public class HealthCheckArbitraries
{
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
    /// 生成合理的不健康组件数量（0 - 10）
    /// </summary>
    public static Arbitrary<NonNegativeInt> UnhealthyCountArb()
    {
        return Gen.Choose(0, 10)
            .Select(count => NonNegativeInt.NewNonNegativeInt(count))
            .ToArbitrary();
    }
}
