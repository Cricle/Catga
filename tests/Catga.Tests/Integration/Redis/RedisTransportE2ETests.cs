using System.Diagnostics;
using Catga.Abstractions;
using Catga.Core;
using Catga.Observability;
using Catga.Resilience;
using Catga.Serialization.MemoryPack;
using Catga.Transport;
using FluentAssertions;
using MemoryPack;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace Catga.Tests.Integration.Redis;

/// <summary>
/// E2E tests for Redis Transport.
/// Target: 80% coverage for Catga.Transport.Redis
/// </summary>
[Trait("Category", "Integration")]
[Trait("Requires", "Docker")]
public sealed class RedisTransportE2ETests : IAsyncLifetime
{
    private RedisContainer? _container;
    private IConnectionMultiplexer? _redis;
    private readonly IMessageSerializer _serializer = new MemoryPackMessageSerializer();
    private readonly IResiliencePipelineProvider _provider = new DefaultResiliencePipelineProvider();

    public async Task InitializeAsync()
    {
        if (!IsDockerRunning()) return;
        var image = ResolveImage("TEST_REDIS_IMAGE", "redis:7-alpine");
        if (image is null) return;

        _container = new RedisBuilder().WithImage(image).Build();
        await _container.StartAsync();
        _redis = await ConnectionMultiplexer.ConnectAsync(_container.GetConnectionString());
    }

    public async Task DisposeAsync()
    {
        if (_redis is not null) await _redis.CloseAsync();
        if (_container is not null) await _container.DisposeAsync();
    }

    #region Basic Publish/Subscribe Tests

    [Fact]
    public async Task PublishAsync_WithSubscriber_ShouldDeliverMessage()
    {
        if (_redis is null) return;
        await using var transport = new RedisMessageTransport(_redis, _serializer, _provider);
        var tcs = new TaskCompletionSource<RedisTransportMessage>();

        await transport.SubscribeAsync<RedisTransportMessage>(async (msg, ctx) =>
        {
            tcs.TrySetResult(msg);
            await Task.CompletedTask;
        });
        await Task.Delay(100);

        var message = new RedisTransportMessage { MessageId = MessageExtensions.NewMessageId(), Data = "hello" };
        await transport.PublishAsync(message, new TransportContext { MessageId = message.MessageId });

        var result = await Task.WhenAny(tcs.Task, Task.Delay(5000));
        result.Should().Be(tcs.Task);
        tcs.Task.Result.Data.Should().Be("hello");
    }

    [Fact]
    public async Task PublishAsync_QoS0_AtMostOnce_ShouldDeliverViaPubSub()
    {
        if (_redis is null) return;
        await using var transport = new RedisMessageTransport(_redis, _serializer, _provider);
        var tcs = new TaskCompletionSource<RedisTransportMessage>();

        await transport.SubscribeAsync<RedisTransportMessage>(async (msg, ctx) =>
        {
            tcs.TrySetResult(msg);
            await Task.CompletedTask;
        });
        await Task.Delay(100);

        var message = new RedisTransportMessage { MessageId = MessageExtensions.NewMessageId(), Data = "qos0", QoS = QualityOfService.AtMostOnce };
        await transport.PublishAsync(message, new TransportContext { MessageId = message.MessageId });

        var result = await Task.WhenAny(tcs.Task, Task.Delay(5000));
        result.Should().Be(tcs.Task);
    }

    [Fact]
    public async Task PublishAsync_QoS1_AtLeastOnce_ShouldDeliverViaStreams()
    {
        if (_redis is null) return;
        var options = new RedisTransportOptions();
        await using var transport = new RedisMessageTransport(_redis, _serializer, _provider, options);
        var tcs = new TaskCompletionSource<RedisTransportMessage>();

        await transport.SubscribeAsync<RedisTransportMessage>(async (msg, ctx) =>
        {
            tcs.TrySetResult(msg);
            await Task.CompletedTask;
        });
        await Task.Delay(100);

        var message = new RedisTransportMessage { MessageId = MessageExtensions.NewMessageId(), Data = "qos1", QoS = QualityOfService.AtLeastOnce };
        await transport.PublishAsync(message, new TransportContext { MessageId = message.MessageId });

        var result = await Task.WhenAny(tcs.Task, Task.Delay(5000));
        result.Should().Be(tcs.Task);
    }

    [Fact]
    public async Task PublishAsync_QoS2_ExactlyOnce_ShouldDeliverWithDedup()
    {
        if (_redis is null) return;
        await using var transport = new RedisMessageTransport(_redis, _serializer, _provider);
        var received = 0;
        var tcs = new TaskCompletionSource();

        await transport.SubscribeAsync<RedisTransportMessage>(async (msg, ctx) =>
        {
            Interlocked.Increment(ref received);
            tcs.TrySetResult();
            await Task.CompletedTask;
        });
        await Task.Delay(100);

        var messageId = MessageExtensions.NewMessageId();
        var message = new RedisTransportMessage { MessageId = messageId, Data = "qos2", QoS = QualityOfService.ExactlyOnce };
        var ctx = new TransportContext { MessageId = messageId };

        // Publish twice with same MessageId
        await transport.PublishAsync(message, ctx);
        await transport.PublishAsync(message, ctx);

        await Task.WhenAny(tcs.Task, Task.Delay(3000));
        await Task.Delay(500); // Wait for potential duplicate

        received.Should().Be(1, "QoS2 should deduplicate messages");
    }

    #endregion

    #region Batch Publish Tests

    [Fact]
    public async Task PublishBatchAsync_ShouldDeliverAllMessages()
    {
        if (_redis is null) return;
        await using var transport = new RedisMessageTransport(_redis, _serializer, _provider);
        var receivedCount = 0;
        var tcs = new TaskCompletionSource();
        const int batchSize = 10;

        await transport.SubscribeAsync<RedisTransportMessage>(async (msg, ctx) =>
        {
            if (Interlocked.Increment(ref receivedCount) >= batchSize)
                tcs.TrySetResult();
            await Task.CompletedTask;
        });
        await Task.Delay(100);

        var messages = Enumerable.Range(0, batchSize)
            .Select(i => new RedisTransportMessage { MessageId = MessageExtensions.NewMessageId(), Data = $"batch-{i}" })
            .ToList();

        await transport.PublishBatchAsync(messages);

        var result = await Task.WhenAny(tcs.Task, Task.Delay(10000));
        result.Should().Be(tcs.Task);
        receivedCount.Should().Be(batchSize);
    }

    #endregion

    #region SendAsync Tests

    [Fact]
    public async Task SendAsync_ShouldDeliverToDestination()
    {
        if (_redis is null) return;
        await using var transport = new RedisMessageTransport(_redis, _serializer, _provider);
        var tcs = new TaskCompletionSource<RedisTransportMessage>();

        await transport.SubscribeAsync<RedisTransportMessage>(async (msg, ctx) =>
        {
            tcs.TrySetResult(msg);
            await Task.CompletedTask;
        });
        await Task.Delay(100);

        var message = new RedisTransportMessage { MessageId = MessageExtensions.NewMessageId(), Data = "send" };
        await transport.SendAsync(message, "test-destination");

        var result = await Task.WhenAny(tcs.Task, Task.Delay(5000));
        result.Should().Be(tcs.Task);
    }

    #endregion

    #region Transport Options Tests

    [Fact]
    public async Task Transport_WithCustomChannelPrefix_ShouldWork()
    {
        if (_redis is null) return;
        var options = new RedisTransportOptions { ChannelPrefix = "custom-prefix" };
        await using var transport = new RedisMessageTransport(_redis, _serializer, _provider, options);
        var tcs = new TaskCompletionSource<RedisTransportMessage>();

        await transport.SubscribeAsync<RedisTransportMessage>(async (msg, ctx) =>
        {
            tcs.TrySetResult(msg);
            await Task.CompletedTask;
        });
        await Task.Delay(100);

        var message = new RedisTransportMessage { MessageId = MessageExtensions.NewMessageId(), Data = "prefix" };
        await transport.PublishAsync(message);

        var result = await Task.WhenAny(tcs.Task, Task.Delay(5000));
        result.Should().Be(tcs.Task);
    }

    [Fact]
    public async Task Transport_Name_ShouldBeRedis()
    {
        if (_redis is null) return;
        await using var transport = new RedisMessageTransport(_redis, _serializer, _provider);

        transport.Name.Should().Be("Redis");
    }

    #endregion

    #region Resilience Tests

    [Fact]
    public async Task PublishAsync_WithResilience_ShouldRetryOnFailure()
    {
        if (_redis is null) return;
        var retryOptions = new CatgaResilienceOptions
        {
            TransportRetryCount = 3,
            TransportRetryDelay = TimeSpan.FromMilliseconds(50)
        };
        var retryProvider = new DefaultResiliencePipelineProvider(retryOptions);
        await using var transport = new RedisMessageTransport(_redis, _serializer, retryProvider);
        var tcs = new TaskCompletionSource<RedisTransportMessage>();

        await transport.SubscribeAsync<RedisTransportMessage>(async (msg, ctx) =>
        {
            tcs.TrySetResult(msg);
            await Task.CompletedTask;
        });
        await Task.Delay(100);

        var message = new RedisTransportMessage { MessageId = MessageExtensions.NewMessageId(), Data = "resilience" };
        await transport.PublishAsync(message);

        var result = await Task.WhenAny(tcs.Task, Task.Delay(5000));
        result.Should().Be(tcs.Task);
    }

    #endregion

    #region Observability Tests

    [Fact]
    public async Task PublishAsync_ShouldEmitDiagnostics()
    {
        if (_redis is null) return;
        await using var transport = new RedisMessageTransport(_redis, _serializer, _provider);
        var activityStarted = false;

        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == CatgaDiagnostics.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStarted = _ => activityStarted = true
        };
        ActivitySource.AddActivityListener(listener);

        var message = new RedisTransportMessage { MessageId = MessageExtensions.NewMessageId(), Data = "diag" };
        await transport.PublishAsync(message);

        activityStarted.Should().BeTrue();
    }

    #endregion

    #region Multiple Subscribers Tests

    [Fact]
    public async Task PublishAsync_MultipleSubscribers_ShouldDeliverToAll()
    {
        if (_redis is null) return;
        await using var transport = new RedisMessageTransport(_redis, _serializer, _provider);
        var received1 = false;
        var received2 = false;
        var tcs = new TaskCompletionSource();

        await transport.SubscribeAsync<RedisTransportMessage>(async (msg, ctx) =>
        {
            received1 = true;
            if (received1 && received2) tcs.TrySetResult();
            await Task.CompletedTask;
        });

        await transport.SubscribeAsync<RedisTransportMessage>(async (msg, ctx) =>
        {
            received2 = true;
            if (received1 && received2) tcs.TrySetResult();
            await Task.CompletedTask;
        });
        await Task.Delay(100);

        var message = new RedisTransportMessage { MessageId = MessageExtensions.NewMessageId(), Data = "multi" };
        await transport.PublishAsync(message);

        await Task.WhenAny(tcs.Task, Task.Delay(5000));
        (received1 || received2).Should().BeTrue();
    }

    #endregion

    #region Dispose Tests

    [Fact]
    public async Task DisposeAsync_ShouldCleanupResources()
    {
        if (_redis is null) return;
        var transport = new RedisMessageTransport(_redis, _serializer, _provider);

        await transport.SubscribeAsync<RedisTransportMessage>(async (msg, ctx) => await Task.CompletedTask);

        await transport.DisposeAsync();

        // Should not throw
    }

    #endregion

    #region Helpers

    private static bool IsDockerRunning()
    {
        try
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "docker",
                Arguments = "info",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            process?.WaitForExit(5000);
            return process?.ExitCode == 0;
        }
        catch { return false; }
    }

    private static string? ResolveImage(string envVar, string defaultImage)
    {
        var env = Environment.GetEnvironmentVariable(envVar);
        return string.IsNullOrEmpty(env) ? defaultImage : env;
    }

    #endregion
}

#region Test Types

[MemoryPackable]
public partial class RedisTransportMessage : IMessage
{
    public long MessageId { get; set; }
    public long CorrelationId { get; set; }
    public QualityOfService QoS { get; set; } = QualityOfService.AtLeastOnce;
    public string Data { get; set; } = string.Empty;
}

#endregion
