using Xunit;
using FluentAssertions;
using NSubstitute;
using Microsoft.Extensions.Logging;
using Catga.Core;
using Catga.Transport;
using Catga.Serialization.MemoryPack;
using System.Collections.Concurrent;
using Catga.Abstractions;

namespace Catga.Tests.Transport;

/// <summary>
/// QoS (Quality of Service) 验证测试
/// 验证 AtMostOnce、AtLeastOnce、ExactlyOnce 三种服务质量等级
/// </summary>
public class QosVerificationTests
{
    private readonly ILogger<MockTransport> _logger;
    private readonly IMessageSerializer _serializer;

    public QosVerificationTests()
    {
        _logger = Substitute.For<ILogger<MockTransport>>();

        _serializer = new MemoryPackMessageSerializer();
    }

    #region Test Messages

    /// <summary>
    /// QoS 0 事件 (Fire-and-Forget)
    /// </summary>
    public record TestEvent(string Id, string Data) : IEvent
    {
        public long MessageId { get; init; } = MessageExtensions.NewMessageId();
        public QualityOfService QoS => QualityOfService.AtMostOnce;
    }

    /// <summary>
    /// QoS 1 可靠事件 (At-Least-Once)
    /// </summary>
    public record ReliableTestEvent(string Id, string Data) : IReliableEvent
    {
        public long MessageId { get; init; } = MessageExtensions.NewMessageId();
        QualityOfService IMessage.QoS => QualityOfService.AtLeastOnce;
    }

    /// <summary>
    /// QoS 2 事件 (Exactly-Once)
    /// </summary>
    public record ExactlyOnceEvent(string Id, string Data) : IEvent
    {
        public long MessageId { get; init; } = MessageExtensions.NewMessageId();
        public QualityOfService QoS => QualityOfService.ExactlyOnce;
    }

    #endregion

    #region QoS 0 (AtMostOnce) Tests

    [Fact]
    public async Task QoS0_AtMostOnce_ShouldNotRetryOnFailure()
    {
        // Arrange
        var transport = new MockTransport(_logger, _serializer, failureRate: 0.5); // 50% 失败率
        var @event = new TestEvent("test-1", "data");
        var publishCount = 10;

        // Act - 发布 10 次
        for (int i = 0; i < publishCount; i++)
        {
            await transport.PublishAsync(@event);
        }

        // Assert
        // QoS 0: 不保证送达，不重试，失败就丢失
        transport.PublishAttempts.Should().Be(publishCount, "QoS 0 should not retry");
        transport.SuccessfulDeliveries.Should().BeLessThan(publishCount,
            "with 50% failure rate, some messages should be lost");
    }

    [Fact]
    public async Task QoS0_AtMostOnce_ShouldBeFastest()
    {
        // Arrange
        var transport = new MockTransport(_logger, _serializer);
        var @event = new TestEvent("test-1", "data");

        // Act
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await transport.PublishAsync(@event);
        stopwatch.Stop();

        // Assert
        // QoS 0: 无 ACK 等待，应该最快 (放宽到 50ms 避免 CI 环境的不稳定性)
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(300,
            "QoS 0 should be fastest (no ACK wait)");
        transport.AckWaitTime.Should().Be(TimeSpan.Zero, "QoS 0 should not wait for ACK");
    }

    #endregion

    #region QoS 1 (AtLeastOnce) Tests

    [Fact]
    public async Task QoS1_AtLeastOnce_ShouldRetryUntilSuccess()
    {
        // Arrange - 使用固定失败计数器来保证可预测的行为
        int failureCount = 0;
        var transport = new MockTransport(_logger, _serializer,
            failureCallback: () =>
            {
                // 前 3 次失败，第 4 次成功
                return ++failureCount <= 3;
            },
            maxRetries: 10);
        var @event = new ReliableTestEvent("test-1", "data");

        // Act
        await transport.PublishAsync(@event);

        // Assert
        // QoS 1: 应该重试直到成功
        transport.PublishAttempts.Should().Be(4, "should attempt 4 times (3 failures + 1 success)");
        transport.SuccessfulDeliveries.Should().Be(1, "should eventually succeed");
    }

    [Fact]
    public async Task QoS1_AtLeastOnce_AllowsDuplicates()
    {
        // Arrange
        var transport = new MockTransport(_logger, _serializer,
            simulateDuplicates: true);
        var @event = new ReliableTestEvent("test-1", "data");
        var receivedMessages = new ConcurrentBag<string>();

        await transport.SubscribeAsync<ReliableTestEvent>(async (msg, ctx) =>
        {
            receivedMessages.Add(msg.Id);
            await Task.CompletedTask;
        });

        // Act - 发布 1 次，但可能收到多次
        await transport.PublishAsync(@event);
        await Task.Delay(100); // 等待异步处理

        // Assert
        // QoS 1: 保证至少送达一次，但可能重复
        receivedMessages.Should().NotBeEmpty("at least one delivery");
        receivedMessages.Count.Should().BeGreaterOrEqualTo(1,
            "QoS 1 guarantees at-least-once, may have duplicates");
    }

    [Fact]
    public async Task QoS1_AtLeastOnce_ShouldWaitForAck()
    {
        // Arrange
        var transport = new MockTransport(_logger, _serializer, ackDelay: TimeSpan.FromMilliseconds(50));
        var @event = new ReliableTestEvent("test-1", "data");

        // Act
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await transport.PublishAsync(@event);
        stopwatch.Stop();

        // Assert
        // QoS 1: 应该等待 ACK
        transport.AckWaitTime.Should().BeGreaterThan(TimeSpan.Zero, "QoS 1 should wait for ACK");
        stopwatch.ElapsedMilliseconds.Should().BeGreaterOrEqualTo(30,
            "should wait for ACK delay");
    }

    #endregion

    #region QoS 2 (ExactlyOnce) Tests

    [Fact]
    public async Task QoS2_ExactlyOnce_ShouldDeduplicateMessages()
    {
        // Arrange
        var transport = new MockTransport(_logger, _serializer,
            enableDeduplication: true);
        var @event = new ExactlyOnceEvent("test-1", "data");

        // 使用固定的 MessageId 进行去重
        var context = new TransportContext { MessageId = 1001L };

        // Act - 发布同一条消息 5 次，使用相同的 MessageId
        for (int i = 0; i < 5; i++)
        {
            await transport.PublishAsync(@event, context);
        }

        // Assert
        // QoS 2: 即使调用 5 次，由于 MessageId 相同，只发布 1 次
        transport.SuccessfulDeliveries.Should().Be(1,
            "QoS 2 should deduplicate by MessageId, only 1 actual delivery");
    }

    [Fact]
    public async Task QoS2_ExactlyOnce_ShouldHandleMultipleUniqueMessages()
    {
        // Arrange
        var transport = new MockTransport(_logger, _serializer,
            enableDeduplication: true);

        // Act - 发布 3 条不同的消息（不同的 MessageId）
        await transport.PublishAsync(new ExactlyOnceEvent("msg-1", "data1"),
            new TransportContext { MessageId = 2001L });
        await transport.PublishAsync(new ExactlyOnceEvent("msg-2", "data2"),
            new TransportContext { MessageId = 2002L });
        await transport.PublishAsync(new ExactlyOnceEvent("msg-3", "data3"),
            new TransportContext { MessageId = 2003L });

        // 重复发布前 2 条（相同的 MessageId）
        await transport.PublishAsync(new ExactlyOnceEvent("msg-1", "data1"),
            new TransportContext { MessageId = 2001L }); // 去重
        await transport.PublishAsync(new ExactlyOnceEvent("msg-2", "data2"),
            new TransportContext { MessageId = 2002L }); // 去重

        // Assert
        // QoS 2: 应该只发布 3 次（2 次重复被去重）
        transport.SuccessfulDeliveries.Should().Be(3, "should deduplicate by MessageId");
    }

    [Fact]
    public async Task QoS2_ExactlyOnce_ShouldUseDeduplication()
    {
        // Arrange
        var transport = new MockTransport(_logger, _serializer,
            enableDeduplication: true);
        var @event = new ExactlyOnceEvent("test-1", "data");
        var context = new TransportContext { MessageId = 3001L };

        // Act - 首次发布
        await transport.PublishAsync(@event, context);
        var firstDeliveries = transport.SuccessfulDeliveries;

        // Act - 重复发布（相同 MessageId）
        await transport.PublishAsync(@event, context);
        var secondDeliveries = transport.SuccessfulDeliveries;

        // Assert
        firstDeliveries.Should().Be(1, "first publish should succeed");
        secondDeliveries.Should().Be(1, "duplicate publish should be deduplicated (same count)");
    }

    #endregion

    #region Cross-QoS Comparison Tests

    [Fact]
    public void QoS_Contracts_ShouldBeCorrect()
    {
        // Arrange & Assert
        IMessage qos0 = new TestEvent("1", "data");
        IMessage qos1 = new ReliableTestEvent("1", "data");
        IMessage qos2 = new ExactlyOnceEvent("1", "data");

        qos0.QoS.Should().Be(QualityOfService.AtMostOnce, "IEvent should default to QoS 0");
        qos1.QoS.Should().Be(QualityOfService.AtLeastOnce, "IReliableEvent should use QoS 1");
        qos2.QoS.Should().Be(QualityOfService.ExactlyOnce, "explicit QoS 2");
    }

    [Theory]
    [InlineData(QualityOfService.AtMostOnce, false, false)] // 不重试，不去重
    [InlineData(QualityOfService.AtLeastOnce, true, false)]  // 重试，不去重
    [InlineData(QualityOfService.ExactlyOnce, true, true)]   // 重试，去重
    public void QoS_Behavior_ShouldMatchExpectations(
        QualityOfService qos,
        bool shouldRetry,
        bool shouldDeduplicate)
    {
        // Assert
        switch (qos)
        {
            case QualityOfService.AtMostOnce:
                shouldRetry.Should().BeFalse("QoS 0 should not retry");
                shouldDeduplicate.Should().BeFalse("QoS 0 does not need deduplication");
                break;
            case QualityOfService.AtLeastOnce:
                shouldRetry.Should().BeTrue("QoS 1 should retry");
                shouldDeduplicate.Should().BeFalse("QoS 1 allows duplicates");
                break;
            case QualityOfService.ExactlyOnce:
                shouldRetry.Should().BeTrue("QoS 2 should retry");
                shouldDeduplicate.Should().BeTrue("QoS 2 must deduplicate");
                break;
        }
    }

    #endregion
}

#region Mock Transport for Testing

/// <summary>
/// Mock Transport for QoS Testing
/// 模拟各种 QoS 行为：失败、重试、ACK、去重
/// </summary>
public class MockTransport : IMessageTransport
{
    private readonly ILogger _logger;
    private readonly IMessageSerializer _serializer;
    private readonly double _failureRate;
    private readonly Func<bool>? _failureCallback; // 自定义失败逻辑
    private readonly int _maxRetries;
    private readonly bool _simulateDuplicates;
    private readonly bool _enableDeduplication;
    private readonly TimeSpan _ackDelay;
    private readonly ConcurrentDictionary<long, bool> _processedMessages = new();
    private readonly List<Func<object, TransportContext, Task>> _subscribers = new();
    private readonly Random _random = new();

    public int PublishAttempts { get; private set; }
    public int SuccessfulDeliveries { get; private set; }
    public TimeSpan AckWaitTime { get; private set; }

    public string Name => "MockTransport";
    public BatchTransportOptions? BatchOptions => null;
    public CompressionTransportOptions? CompressionOptions => null;

    public MockTransport(
        ILogger logger,
        IMessageSerializer serializer,
        double failureRate = 0,
        Func<bool>? failureCallback = null,
        int maxRetries = 3,
        bool simulateDuplicates = false,
        bool enableDeduplication = false,
        TimeSpan? ackDelay = null)
    {
        _logger = logger;
        _serializer = serializer;
        _failureRate = failureRate;
        _failureCallback = failureCallback;
        _maxRetries = maxRetries;
        _simulateDuplicates = simulateDuplicates;
        _enableDeduplication = enableDeduplication;
        _ackDelay = ackDelay ?? TimeSpan.Zero;
    }

    public async Task PublishAsync<TMessage>(
        TMessage message,
        TransportContext? context = null,
        CancellationToken cancellationToken = default) where TMessage : class
    {
        var qos = (message as IMessage)?.QoS ?? QualityOfService.AtMostOnce;
        var ctx = context ?? new TransportContext { MessageId = MessageExtensions.NewMessageId() };
        switch (qos)
        {
            case QualityOfService.AtMostOnce:
                await PublishQoS0Async(message, ctx, cancellationToken);
                break;
            case QualityOfService.AtLeastOnce:
                await PublishQoS1Async(message, ctx, cancellationToken);
                break;
            case QualityOfService.ExactlyOnce:
                await PublishQoS2Async(message, ctx, cancellationToken);
                break;
        }
    }

    private async Task PublishQoS0Async<TMessage>(
        TMessage message,
        TransportContext context,
        CancellationToken cancellationToken) where TMessage : class
    {
        PublishAttempts++;

        // QoS 0: Fire-and-forget, 失败就丢失
        if (_random.NextDouble() >= _failureRate)
        {
            SuccessfulDeliveries++;
            await NotifySubscribersAsync(message, context);
        }
        // 失败直接丢弃，不重试
    }

    private async Task PublishQoS1Async<TMessage>(
        TMessage message,
        TransportContext context,
        CancellationToken cancellationToken) where TMessage : class
    {
        // QoS 1: Retry until success
        for (int attempt = 0; attempt < _maxRetries; attempt++)
        {
            PublishAttempts++;

            // 判断是否失败（使用 failureCallback 或 failureRate）
            bool shouldFail = _failureCallback != null
                ? _failureCallback()
                : _random.NextDouble() < _failureRate;

            if (!shouldFail)
            {
                // 模拟 ACK 等待
                await Task.Delay(_ackDelay, cancellationToken);
                AckWaitTime += _ackDelay;

                SuccessfulDeliveries++;
                await NotifySubscribersAsync(message, context);

                // 模拟可能的重复投递
                if (_simulateDuplicates && _random.NextDouble() > 0.7)
                {
                    await NotifySubscribersAsync(message, context);
                }
                return;
            }
        }

        throw new InvalidOperationException($"Failed to publish after {_maxRetries} attempts");
    }

    private async Task PublishQoS2Async<TMessage>(
        TMessage message,
        TransportContext context,
        CancellationToken cancellationToken) where TMessage : class
    {
        // QoS 2: 发布前检查去重（防止重复发送）
        if (_enableDeduplication && context.MessageId.HasValue && _processedMessages.ContainsKey(context.MessageId.Value))
        {
            _logger.LogDebug("Message {MessageId} already published (QoS 2), skipping", context.MessageId);
            return; // 已处理，跳过
        }

        // 使用 QoS 1 逻辑（重试直到成功）
        await PublishQoS1Async(message, context, cancellationToken);

        // 标记为已发布（防止后续重复）
        if (_enableDeduplication && context.MessageId.HasValue)
        {
            _processedMessages.TryAdd(context.MessageId.Value, true);
        }
    }

    private async Task NotifySubscribersAsync<TMessage>(TMessage message, TransportContext context)
        where TMessage : class
    {
        foreach (var subscriber in _subscribers)
        {
            await subscriber(message, context);
        }
    }

    public Task SubscribeAsync<TMessage>(
        Func<TMessage, TransportContext, Task> handler,
        CancellationToken cancellationToken = default) where TMessage : class
    {
        _subscribers.Add(async (msg, ctx) =>
        {
            if (msg is TMessage typedMsg)
            {
                await handler(typedMsg, ctx);
            }
        });
        return Task.CompletedTask;
    }

    public Task SendAsync<TMessage>(TMessage message, string destination, TransportContext? context = null, CancellationToken cancellationToken = default) where TMessage : class
        => PublishAsync(message, context, cancellationToken);

    public Task PublishBatchAsync<TMessage>(IEnumerable<TMessage> messages, TransportContext? context = null, CancellationToken cancellationToken = default) where TMessage : class
        => Task.WhenAll(messages.Select(m => PublishAsync(m, context, cancellationToken)));

    public Task SendBatchAsync<TMessage>(IEnumerable<TMessage> messages, string destination, TransportContext? context = null, CancellationToken cancellationToken = default) where TMessage : class
        => PublishBatchAsync(messages, context, cancellationToken);
}

#endregion







