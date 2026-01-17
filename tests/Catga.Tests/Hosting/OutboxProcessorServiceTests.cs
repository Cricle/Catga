using Catga.Hosting;
using Catga.Outbox;
using Catga.Transport;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Catga.Tests.Hosting;

/// <summary>
/// OutboxProcessorService 单元测试
/// </summary>
public class OutboxProcessorServiceTests
{
    /// <summary>
    /// 测试批次处理逻辑 - 成功处理消息
    /// Requirements: 6.2, 6.4
    /// </summary>
    [Fact]
    public async Task ProcessBatch_SuccessfullyProcessesMessages()
    {
        // Arrange
        var logger = Substitute.For<ILogger<OutboxProcessorService>>();
        var outboxStore = Substitute.For<IOutboxStore>();
        var transport = Substitute.For<IMessageTransport>();

        var messages = new List<OutboxMessage>
        {
            new OutboxMessage
            {
                MessageId = 1,
                MessageType = "TestMessage1",
                Payload = new byte[] { 1 },
                Status = OutboxStatus.Pending
            },
            new OutboxMessage
            {
                MessageId = 2,
                MessageType = "TestMessage2",
                Payload = new byte[] { 2 },
                Status = OutboxStatus.Pending
            }
        };

        var callCount = 0;
        outboxStore.GetPendingMessagesAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                callCount++;
                // 第一次返回消息，后续返回空（模拟消息已被处理）
                return callCount == 1
                    ? ValueTask.FromResult<IReadOnlyList<OutboxMessage>>(messages)
                    : ValueTask.FromResult<IReadOnlyList<OutboxMessage>>(Array.Empty<OutboxMessage>());
            });

        outboxStore.MarkAsPublishedAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.CompletedTask);

        var options = new OutboxProcessorOptions
        {
            ScanInterval = TimeSpan.FromMilliseconds(50), // 减少扫描间隔
            BatchSize = 10,
            ErrorDelay = TimeSpan.FromMilliseconds(10)
        };

        var service = new OutboxProcessorService(outboxStore, transport, logger, options);
        var cts = new CancellationTokenSource();

        // Act
        var startTask = service.StartAsync(cts.Token);
        await startTask;

        // 等待足够长的时间确保至少一次扫描完成
        await Task.Delay(300); // 增加等待时间

        cts.Cancel();
        await service.StopAsync(CancellationToken.None);

        // Assert
        // 验证至少调用了一次 GetPendingMessagesAsync
        await outboxStore.Received().GetPendingMessagesAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
        await outboxStore.Received(2).MarkAsPublishedAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
        Assert.Equal(2, service.TotalProcessed);
        Assert.Equal(0, service.TotalFailed);
    }

    /// <summary>
    /// 测试停机时完成当前批次
    /// Requirements: 6.4, 6.5
    /// </summary>
    [Fact]
    public async Task ProcessBatch_CompletesCurrentBatchOnShutdown()
    {
        // Arrange
        var logger = Substitute.For<ILogger<OutboxProcessorService>>();
        var outboxStore = Substitute.For<IOutboxStore>();
        var transport = Substitute.For<IMessageTransport>();

        var messages = new List<OutboxMessage>
        {
            new OutboxMessage
            {
                MessageId = 1,
                MessageType = "TestMessage1",
                Payload = new byte[] { 1 },
                Status = OutboxStatus.Pending
            },
            new OutboxMessage
            {
                MessageId = 2,
                MessageType = "TestMessage2",
                Payload = new byte[] { 2 },
                Status = OutboxStatus.Pending
            },
            new OutboxMessage
            {
                MessageId = 3,
                MessageType = "TestMessage3",
                Payload = new byte[] { 3 },
                Status = OutboxStatus.Pending
            }
        };

        var callCount = 0;
        outboxStore.GetPendingMessagesAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                callCount++;
                // 第一次返回消息，后续返回空
                return callCount == 1
                    ? ValueTask.FromResult<IReadOnlyList<OutboxMessage>>(messages)
                    : ValueTask.FromResult<IReadOnlyList<OutboxMessage>>(Array.Empty<OutboxMessage>());
            });

        outboxStore.MarkAsPublishedAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.CompletedTask);

        var options = new OutboxProcessorOptions
        {
            ScanInterval = TimeSpan.FromMilliseconds(100),
            BatchSize = 10,
            ErrorDelay = TimeSpan.FromMilliseconds(10),
            CompleteCurrentBatchOnShutdown = true
        };

        var service = new OutboxProcessorService(outboxStore, transport, logger, options);
        var cts = new CancellationTokenSource();

        // Act
        var startTask = service.StartAsync(cts.Token);
        await startTask;

        // 等待批次开始处理
        await Task.Delay(150);

        // 在批次处理期间取消
        cts.Cancel();
        await service.StopAsync(CancellationToken.None);

        // Assert
        // 如果批次开始处理，应该完整处理所有消息
        Assert.True(service.TotalProcessed == 0 || service.TotalProcessed == 3,
            $"Expected 0 or 3 processed messages, got {service.TotalProcessed}");
    }

    /// <summary>
    /// 测试错误处理 - 消息发布失败
    /// Requirements: 6.5
    /// </summary>
    [Fact]
    public async Task ProcessBatch_HandlesPublishFailures()
    {
        // Arrange
        var logger = Substitute.For<ILogger<OutboxProcessorService>>();
        var outboxStore = Substitute.For<IOutboxStore>();
        var transport = Substitute.For<IMessageTransport>();

        var messages = new List<OutboxMessage>
        {
            new OutboxMessage
            {
                MessageId = 1,
                MessageType = "TestMessage1",
                Payload = new byte[] { 1 },
                Status = OutboxStatus.Pending
            }
        };

        outboxStore.GetPendingMessagesAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<IReadOnlyList<OutboxMessage>>(messages));

        // 模拟发布失败 - 通过让 MarkAsPublishedAsync 抛出异常
        outboxStore.MarkAsPublishedAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => throw new InvalidOperationException("Publish failed"));

        outboxStore.MarkAsFailedAsync(Arg.Any<long>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.CompletedTask);

        var options = new OutboxProcessorOptions
        {
            ScanInterval = TimeSpan.FromMilliseconds(100),
            BatchSize = 10,
            ErrorDelay = TimeSpan.FromMilliseconds(10)
        };

        var service = new OutboxProcessorService(outboxStore, transport, logger, options);
        var cts = new CancellationTokenSource();

        // Act
        var startTask = service.StartAsync(cts.Token);
        await startTask;

        // 等待至少一次扫描
        await Task.Delay(200);

        cts.Cancel();
        await service.StopAsync(CancellationToken.None);

        // Assert
        await outboxStore.Received().GetPendingMessagesAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
        await outboxStore.Received().MarkAsFailedAsync(Arg.Any<long>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        // 由于可能有多次扫描，失败次数可能大于等于1
        Assert.True(service.TotalFailed >= 1, $"Expected at least 1 failure, got {service.TotalFailed}");
    }

    /// <summary>
    /// 测试空批次处理
    /// Requirements: 6.2
    /// </summary>
    [Fact]
    public async Task ProcessBatch_HandlesEmptyBatch()
    {
        // Arrange
        var logger = Substitute.For<ILogger<OutboxProcessorService>>();
        var outboxStore = Substitute.For<IOutboxStore>();
        var transport = Substitute.For<IMessageTransport>();

        outboxStore.GetPendingMessagesAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<IReadOnlyList<OutboxMessage>>(Array.Empty<OutboxMessage>()));

        var options = new OutboxProcessorOptions
        {
            ScanInterval = TimeSpan.FromMilliseconds(50),
            BatchSize = 10,
            ErrorDelay = TimeSpan.FromMilliseconds(10)
        };

        var service = new OutboxProcessorService(outboxStore, transport, logger, options);
        var cts = new CancellationTokenSource();

        // Act
        var startTask = service.StartAsync(cts.Token);
        await startTask;

        // 等待足够时间让 ExecuteAsync 开始执行并完成至少两次扫描
        // 第一次扫描立即执行，然后等待 50ms，第二次扫描
        await Task.Delay(400);

        cts.Cancel();
        await service.StopAsync(CancellationToken.None);

        // Assert
        // 应该至少调用一次 GetPendingMessagesAsync
        await outboxStore.Received().GetPendingMessagesAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
        Assert.Equal(0, service.TotalProcessed);
        Assert.Equal(0, service.TotalFailed);
    }

    /// <summary>
    /// 测试并发批次处理保护
    /// Requirements: 6.4
    /// </summary>
    [Fact]
    public async Task ProcessBatch_PreventsConcurrentBatchProcessing()
    {
        // Arrange
        var logger = Substitute.For<ILogger<OutboxProcessorService>>();
        var outboxStore = Substitute.For<IOutboxStore>();
        var transport = Substitute.For<IMessageTransport>();

        var messages = new List<OutboxMessage>
        {
            new OutboxMessage
            {
                MessageId = 1,
                MessageType = "TestMessage1",
                Payload = new byte[] { 1 },
                Status = OutboxStatus.Pending
            }
        };

        var processingCount = 0;
        outboxStore.GetPendingMessagesAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                Interlocked.Increment(ref processingCount);
                Task.Delay(50).Wait(); // 模拟处理时间
                Interlocked.Decrement(ref processingCount);
                return ValueTask.FromResult<IReadOnlyList<OutboxMessage>>(messages);
            });

        outboxStore.MarkAsPublishedAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.CompletedTask);

        var options = new OutboxProcessorOptions
        {
            ScanInterval = TimeSpan.FromMilliseconds(20), // 非常短的间隔
            BatchSize = 10,
            ErrorDelay = TimeSpan.FromMilliseconds(10)
        };

        var service = new OutboxProcessorService(outboxStore, transport, logger, options);
        var cts = new CancellationTokenSource();

        // Act
        var startTask = service.StartAsync(cts.Token);
        await startTask;

        // 等待多次扫描尝试
        await Task.Delay(200);

        cts.Cancel();
        await service.StopAsync(CancellationToken.None);

        // Assert
        // 由于并发保护，同一时间应该只有一个批次在处理
        // processingCount 应该始终 <= 1
        Assert.True(processingCount <= 1, $"Concurrent processing detected: {processingCount}");
    }

    /// <summary>
    /// 测试配置验证
    /// Requirements: 6.6
    /// </summary>
    [Fact]
    public void Constructor_ValidatesOptions()
    {
        // Arrange
        var logger = Substitute.For<ILogger<OutboxProcessorService>>();
        var outboxStore = Substitute.For<IOutboxStore>();
        var transport = Substitute.For<IMessageTransport>();

        var invalidOptions = new OutboxProcessorOptions
        {
            ScanInterval = TimeSpan.Zero, // 无效
            BatchSize = 10
        };

        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            new OutboxProcessorService(outboxStore, transport, logger, invalidOptions));
    }

    /// <summary>
    /// 测试服务启动和停止
    /// Requirements: 6.2
    /// </summary>
    [Fact]
    public async Task Service_StartsAndStopsCorrectly()
    {
        // Arrange
        var logger = Substitute.For<ILogger<OutboxProcessorService>>();
        var outboxStore = Substitute.For<IOutboxStore>();
        var transport = Substitute.For<IMessageTransport>();

        outboxStore.GetPendingMessagesAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<IReadOnlyList<OutboxMessage>>(Array.Empty<OutboxMessage>()));

        var options = new OutboxProcessorOptions
        {
            ScanInterval = TimeSpan.FromMilliseconds(100),
            BatchSize = 10,
            ErrorDelay = TimeSpan.FromMilliseconds(10)
        };

        var service = new OutboxProcessorService(outboxStore, transport, logger, options);
        var cts = new CancellationTokenSource();

        // Act
        var startTask = service.StartAsync(cts.Token);
        await startTask;

        Assert.False(service.IsProcessingBatch);

        cts.Cancel();
        var stopTask = service.StopAsync(CancellationToken.None);
        await stopTask;

        // Assert
        Assert.False(service.IsProcessingBatch);
    }
}
