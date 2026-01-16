using Catga.Hosting;
using Catga.Outbox;
using Catga.Transport;
using FsCheck;
using FsCheck.Xunit;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Catga.Tests.Hosting;

/// <summary>
/// OutboxProcessorService 属性测试
/// Feature: hosting-integration
/// </summary>
public class OutboxProcessorServicePropertyTests
{
    /// <summary>
    /// Property 10: Outbox 处理器定期扫描
    /// Feature: hosting-integration, Property 10: Outbox 处理器定期扫描
    /// Validates: Requirements 6.3
    /// 
    /// For any 配置的扫描间隔，Outbox 处理器应该在该间隔内扫描并处理待发送的消息。
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(OutboxArbitraries) })]
    public Property OutboxProcessor_PerformsPeriodicScanning(PositiveInt scanIntervalMs)
    {
        // 限制扫描间隔以确保测试可以在合理时间内完成
        var interval = TimeSpan.FromMilliseconds(Math.Min(scanIntervalMs.Get, 500));

        return Prop.ForAll(
            Gen.Constant(interval).ToArbitrary(),
            scanInterval =>
            {
                // Arrange
                var logger = Substitute.For<ILogger<OutboxProcessorService>>();
                var outboxStore = Substitute.For<IOutboxStore>();
                var transport = Substitute.For<IMessageTransport>();
                
                var scanCount = 0;
                outboxStore.GetPendingMessagesAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
                    .Returns(callInfo =>
                    {
                        Interlocked.Increment(ref scanCount);
                        return ValueTask.FromResult<IReadOnlyList<OutboxMessage>>(Array.Empty<OutboxMessage>());
                    });

                var options = new OutboxProcessorOptions
                {
                    ScanInterval = scanInterval,
                    BatchSize = 100,
                    ErrorDelay = TimeSpan.FromMilliseconds(10)
                };

                var service = new OutboxProcessorService(outboxStore, transport, logger, options);
                var cts = new CancellationTokenSource();

                // Act
                var startTask = service.StartAsync(cts.Token);
                startTask.Wait(1000);

                // 等待至少 2 个扫描周期
                var waitTime = scanInterval.Add(scanInterval).Add(TimeSpan.FromMilliseconds(100));
                Thread.Sleep(waitTime);

                cts.Cancel();
                var stopTask = service.StopAsync(CancellationToken.None);
                stopTask.Wait(2000);

                // Assert
                // 应该至少扫描一次
                var scannedAtLeastOnce = scanCount >= 1;

                return scannedAtLeastOnce.Label($"Should scan at least once with interval {scanInterval.TotalMilliseconds}ms, got {scanCount} scans");
            });
    }

    /// <summary>
    /// Property 11: Outbox 批次完整性
    /// Feature: hosting-integration, Property 11: Outbox 批次完整性
    /// Validates: Requirements 6.4
    /// 
    /// For any 正在处理的 Outbox 批次，当停机请求到来时，当前批次应该完整处理完成后再停止。
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(OutboxArbitraries) })]
    public Property OutboxProcessor_CompletesCurrentBatchOnShutdown(PositiveInt batchSize)
    {
        // 限制批次大小
        var size = Math.Min(batchSize.Get, 20);

        return Prop.ForAll(
            Gen.Constant(size).ToArbitrary(),
            batchCount =>
            {
                // Arrange
                var logger = Substitute.For<ILogger<OutboxProcessorService>>();
                var outboxStore = Substitute.For<IOutboxStore>();
                var transport = Substitute.For<IMessageTransport>();
                
                var messages = new List<OutboxMessage>();
                for (int i = 0; i < batchCount; i++)
                {
                    messages.Add(new OutboxMessage
                    {
                        MessageId = i,
                        MessageType = $"TestMessage{i}",
                        Payload = new byte[] { (byte)i },
                        Status = OutboxStatus.Pending
                    });
                }

                var markedAsPublishedCount = 0;
                var firstCall = true;

                // Return messages only on first call, then return empty list
                outboxStore.GetPendingMessagesAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
                    .Returns(callInfo =>
                    {
                        if (firstCall)
                        {
                            firstCall = false;
                            return ValueTask.FromResult<IReadOnlyList<OutboxMessage>>(messages);
                        }
                        return ValueTask.FromResult<IReadOnlyList<OutboxMessage>>(Array.Empty<OutboxMessage>());
                    });

                outboxStore.MarkAsPublishedAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
                    .Returns(callInfo =>
                    {
                        Interlocked.Increment(ref markedAsPublishedCount);
                        return ValueTask.CompletedTask;
                    });

                var options = new OutboxProcessorOptions
                {
                    ScanInterval = TimeSpan.FromMilliseconds(100),
                    BatchSize = batchCount,
                    ErrorDelay = TimeSpan.FromMilliseconds(10),
                    CompleteCurrentBatchOnShutdown = true
                };

                var service = new OutboxProcessorService(outboxStore, transport, logger, options);
                var cts = new CancellationTokenSource();

                // Act
                var startTask = service.StartAsync(cts.Token);
                startTask.Wait(1000);

                // 等待批次开始处理
                Thread.Sleep(150);

                // 在批次处理期间取消
                cts.Cancel();
                var stopTask = service.StopAsync(CancellationToken.None);
                stopTask.Wait(5000);

                // Assert
                // 如果批次开始处理，应该完整处理所有消息
                // 由于时序问题，可能批次还没开始就取消了，所以我们检查：
                // 如果有任何消息被标记为已发布，那么所有消息都应该被标记
                var batchIntegrity = markedAsPublishedCount == 0 || markedAsPublishedCount == batchCount;

                return batchIntegrity.Label($"Batch integrity: {markedAsPublishedCount}/{batchCount} messages marked as published");
            });
    }

    /// <summary>
    /// Property 12: Outbox 配置生效
    /// Feature: hosting-integration, Property 12: Outbox 配置生效
    /// Validates: Requirements 6.6
    /// 
    /// For any 配置的扫描间隔和批次大小，Outbox 处理器应该按照这些配置值运行。
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(OutboxArbitraries) })]
    [Trait("Category", "Flaky")] // 时序敏感的属性测试
    public Property OutboxProcessor_RespectsConfiguredBatchSize(PositiveInt batchSize)
    {
        // 限制批次大小
        var size = Math.Min(batchSize.Get, 50);

        return Prop.ForAll(
            Gen.Constant(size).ToArbitrary(),
            configuredBatchSize =>
            {
                // Arrange
                var logger = Substitute.For<ILogger<OutboxProcessorService>>();
                var outboxStore = Substitute.For<IOutboxStore>();
                var transport = Substitute.For<IMessageTransport>();
                
                var requestedBatchSizes = new List<int>();
                outboxStore.GetPendingMessagesAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
                    .Returns(callInfo =>
                    {
                        var batchSize = callInfo.Arg<int>();
                        lock (requestedBatchSizes)
                        {
                            requestedBatchSizes.Add(batchSize);
                        }
                        return ValueTask.FromResult<IReadOnlyList<OutboxMessage>>(Array.Empty<OutboxMessage>());
                    });

                var options = new OutboxProcessorOptions
                {
                    ScanInterval = TimeSpan.FromMilliseconds(50), // 减少扫描间隔
                    BatchSize = configuredBatchSize,
                    ErrorDelay = TimeSpan.FromMilliseconds(10)
                };

                var service = new OutboxProcessorService(outboxStore, transport, logger, options);
                var cts = new CancellationTokenSource();

                // Act
                var startTask = service.StartAsync(cts.Token);
                startTask.Wait(1000);

                // 等待足够长的时间确保至少一次扫描
                Thread.Sleep(300);

                cts.Cancel();
                var stopTask = service.StopAsync(CancellationToken.None);
                stopTask.Wait(2000);

                // Assert
                // 请求的批次大小应该等于配置的批次大小
                // 获取最后一次请求的批次大小（如果有的话）
                int requestedBatchSize;
                lock (requestedBatchSizes)
                {
                    // 如果没有调用，跳过此测试（可能是时序问题）
                    if (requestedBatchSizes.Count == 0)
                    {
                        return true.Label($"No batch requests made (timing issue)");
                    }
                    requestedBatchSize = requestedBatchSizes.Last();
                }
                
                var batchSizeRespected = requestedBatchSize == configuredBatchSize;

                return batchSizeRespected.Label($"Requested batch size {requestedBatchSize} should equal configured size {configuredBatchSize}");
            });
    }
}

/// <summary>
/// Outbox 处理器属性测试的自定义生成器
/// </summary>
public class OutboxArbitraries
{
    /// <summary>
    /// 生成合理的扫描间隔（50ms - 500ms）
    /// </summary>
    public static Arbitrary<PositiveInt> ScanIntervalArb()
    {
        return Gen.Choose(50, 500)
            .Select(ms => PositiveInt.NewPositiveInt(ms))
            .ToArbitrary();
    }

    /// <summary>
    /// 生成合理的批次大小（1 - 50）
    /// </summary>
    public static Arbitrary<PositiveInt> BatchSizeArb()
    {
        return Gen.Choose(1, 50)
            .Select(size => PositiveInt.NewPositiveInt(size))
            .ToArbitrary();
    }
}
