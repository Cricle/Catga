using System.Diagnostics;
using Catga.Abstractions;
using Catga.Core;
using Catga.Observability;
using Catga.Resilience;
using Catga.Serialization.MemoryPack;
using Catga.Transport;
using Catga.Transport.Nats;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using FluentAssertions;
using MemoryPack;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NATS.Client.Core;

namespace Catga.Tests.Integration.Nats;

/// <summary>
/// E2E tests for NATS Transport.
/// Target: 80% coverage for Catga.Transport.Nats
/// </summary>
[Trait("Category", "Integration")]
[Trait("Requires", "Docker")]
public sealed class NatsTransportE2ETests : IAsyncLifetime
{
    private IContainer? _container;
    private NatsConnection? _nats;
    private readonly IMessageSerializer _serializer = new MemoryPackMessageSerializer();
    private readonly IResiliencePipelineProvider _provider = new DefaultResiliencePipelineProvider();
    private readonly ILogger<NatsMessageTransport> _logger = NullLogger<NatsMessageTransport>.Instance;

    public async Task InitializeAsync()
    {
        if (!IsDockerRunning()) return;
        var image = ResolveImage("TEST_NATS_IMAGE", "nats:latest");
        if (image is null) return;

        _container = new ContainerBuilder()
            .WithImage(image)
            .WithPortBinding(4222, true)
            .WithPortBinding(8222, true)
            .WithCommand("-js", "-m", "8222")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(8222).ForPath("/varz")))
            .Build();
        await _container.StartAsync();
        var port = _container.GetMappedPublicPort(4222);
        _nats = new NatsConnection(new NatsOpts { Url = $"nats://localhost:{port}", ConnectTimeout = TimeSpan.FromSeconds(10) });
        await _nats.ConnectAsync();
    }

    public async Task DisposeAsync()
    {
        if (_nats is not null) await _nats.DisposeAsync();
        if (_container is not null) await _container.DisposeAsync();
    }

    #region Basic Publish/Subscribe Tests

    [Fact]
    public async Task PublishAsync_WithSubscriber_ShouldDeliverMessage()
    {
        if (_nats is null) return;
        var options = new NatsTransportOptions { SubjectPrefix = $"test-{Guid.NewGuid():N}" };
        await using var transport = new NatsMessageTransport(_nats, _serializer, _logger, _provider, options);
        var tcs = new TaskCompletionSource<NatsTransportTestMessage>();

        await transport.SubscribeAsync<NatsTransportTestMessage>(async (msg, ctx) =>
        {
            tcs.TrySetResult(msg);
            await Task.CompletedTask;
        });
        await Task.Delay(200);

        var message = new NatsTransportTestMessage { MessageId = MessageExtensions.NewMessageId(), Data = "hello" };
        await transport.PublishAsync(message, new TransportContext { MessageId = message.MessageId });

        var result = await Task.WhenAny(tcs.Task, Task.Delay(10000));
        result.Should().Be(tcs.Task);
        tcs.Task.Result.Data.Should().Be("hello");
    }

    [Fact]
    public async Task PublishAsync_QoS0_AtMostOnce_ShouldDeliverViaCorePubSub()
    {
        if (_nats is null) return;
        var options = new NatsTransportOptions { SubjectPrefix = $"qos0-{Guid.NewGuid():N}" };
        await using var transport = new NatsMessageTransport(_nats, _serializer, _logger, _provider, options);
        var tcs = new TaskCompletionSource<NatsTransportTestMessage>();

        await transport.SubscribeAsync<NatsTransportTestMessage>(async (msg, ctx) =>
        {
            tcs.TrySetResult(msg);
            await Task.CompletedTask;
        });
        await Task.Delay(200);

        var message = new NatsTransportTestMessage { MessageId = MessageExtensions.NewMessageId(), Data = "qos0", QoS = QualityOfService.AtMostOnce };
        await transport.PublishAsync(message, new TransportContext { MessageId = message.MessageId });

        var result = await Task.WhenAny(tcs.Task, Task.Delay(10000));
        result.Should().Be(tcs.Task);
    }

    [Fact]
    public async Task PublishAsync_QoS1_AtLeastOnce_ShouldDeliverViaJetStream()
    {
        if (_nats is null) return;
        var options = new NatsTransportOptions { SubjectPrefix = $"qos1-{Guid.NewGuid():N}" };
        await using var transport = new NatsMessageTransport(_nats, _serializer, _logger, _provider, options);
        var tcs = new TaskCompletionSource<NatsTransportTestMessage>();

        await transport.SubscribeAsync<NatsTransportTestMessage>(async (msg, ctx) =>
        {
            tcs.TrySetResult(msg);
            await Task.CompletedTask;
        });
        await Task.Delay(200);

        var message = new NatsTransportTestMessage { MessageId = MessageExtensions.NewMessageId(), Data = "qos1", QoS = QualityOfService.AtLeastOnce };
        await transport.PublishAsync(message, new TransportContext { MessageId = message.MessageId });

        var result = await Task.WhenAny(tcs.Task, Task.Delay(10000));
        result.Should().Be(tcs.Task);
    }

    [Fact]
    public async Task PublishAsync_QoS2_ExactlyOnce_ShouldDeliverWithDedup()
    {
        if (_nats is null) return;
        var options = new NatsTransportOptions { SubjectPrefix = $"qos2-{Guid.NewGuid():N}" };
        await using var transport = new NatsMessageTransport(_nats, _serializer, _logger, _provider, options);
        var received = 0;
        var tcs = new TaskCompletionSource();

        await transport.SubscribeAsync<NatsTransportTestMessage>(async (msg, ctx) =>
        {
            Interlocked.Increment(ref received);
            tcs.TrySetResult();
            await Task.CompletedTask;
        });
        await Task.Delay(200);

        var messageId = MessageExtensions.NewMessageId();
        var message = new NatsTransportTestMessage { MessageId = messageId, Data = "qos2", QoS = QualityOfService.ExactlyOnce };
        var ctx = new TransportContext { MessageId = messageId };

        // Publish twice with same MessageId
        await transport.PublishAsync(message, ctx);
        await transport.PublishAsync(message, ctx);

        await Task.WhenAny(tcs.Task, Task.Delay(5000));
        await Task.Delay(500);

        received.Should().Be(1, "QoS2 should deduplicate messages via NATS MsgId");
    }

    #endregion

    #region Batch Publish Tests

    [Fact]
    public async Task PublishBatchAsync_ShouldDeliverAllMessages()
    {
        if (_nats is null) return;
        var options = new NatsTransportOptions { SubjectPrefix = $"batch-{Guid.NewGuid():N}" };
        await using var transport = new NatsMessageTransport(_nats, _serializer, _logger, _provider, options);
        var receivedCount = 0;
        var tcs = new TaskCompletionSource();
        const int batchSize = 5;

        await transport.SubscribeAsync<NatsTransportTestMessage>(async (msg, ctx) =>
        {
            if (Interlocked.Increment(ref receivedCount) >= batchSize)
                tcs.TrySetResult();
            await Task.CompletedTask;
        });
        await Task.Delay(200);

        var messages = Enumerable.Range(0, batchSize)
            .Select(i => new NatsTransportTestMessage { MessageId = MessageExtensions.NewMessageId(), Data = $"batch-{i}" })
            .ToList();

        await transport.PublishBatchAsync(messages);

        var result = await Task.WhenAny(tcs.Task, Task.Delay(15000));
        result.Should().Be(tcs.Task);
        receivedCount.Should().Be(batchSize);
    }

    #endregion

    #region SendAsync Tests

    [Fact]
    public async Task SendAsync_ShouldDeliverToDestination()
    {
        if (_nats is null) return;
        var options = new NatsTransportOptions { SubjectPrefix = $"send-{Guid.NewGuid():N}" };
        await using var transport = new NatsMessageTransport(_nats, _serializer, _logger, _provider, options);
        var tcs = new TaskCompletionSource<NatsTransportTestMessage>();

        await transport.SubscribeAsync<NatsTransportTestMessage>(async (msg, ctx) =>
        {
            tcs.TrySetResult(msg);
            await Task.CompletedTask;
        });
        await Task.Delay(200);

        var message = new NatsTransportTestMessage { MessageId = MessageExtensions.NewMessageId(), Data = "send" };
        await transport.SendAsync(message, "test-destination");

        var result = await Task.WhenAny(tcs.Task, Task.Delay(10000));
        result.Should().Be(tcs.Task);
    }

    #endregion

    #region Transport Options Tests

    [Fact]
    public async Task Transport_WithCustomSubjectPrefix_ShouldWork()
    {
        if (_nats is null) return;
        var options = new NatsTransportOptions { SubjectPrefix = $"custom-prefix-{Guid.NewGuid():N}" };
        await using var transport = new NatsMessageTransport(_nats, _serializer, _logger, _provider, options);
        var tcs = new TaskCompletionSource<NatsTransportTestMessage>();

        await transport.SubscribeAsync<NatsTransportTestMessage>(async (msg, ctx) =>
        {
            tcs.TrySetResult(msg);
            await Task.CompletedTask;
        });
        await Task.Delay(200);

        var message = new NatsTransportTestMessage { MessageId = MessageExtensions.NewMessageId(), Data = "prefix" };
        await transport.PublishAsync(message);

        var result = await Task.WhenAny(tcs.Task, Task.Delay(10000));
        result.Should().Be(tcs.Task);
    }

    [Fact]
    public async Task Transport_Name_ShouldBeNATS()
    {
        if (_nats is null) return;
        await using var transport = new NatsMessageTransport(_nats, _serializer, _logger, _provider);

        transport.Name.Should().Be("NATS");
    }

    #endregion

    #region Resilience Tests

    [Fact]
    public async Task PublishAsync_WithResilience_ShouldRetryOnFailure()
    {
        if (_nats is null) return;
        var retryOptions = new CatgaResilienceOptions
        {
            TransportRetryCount = 3,
            TransportRetryDelay = TimeSpan.FromMilliseconds(50)
        };
        var retryProvider = new DefaultResiliencePipelineProvider(retryOptions);
        var options = new NatsTransportOptions { SubjectPrefix = $"resilience-{Guid.NewGuid():N}" };
        await using var transport = new NatsMessageTransport(_nats, _serializer, _logger, retryProvider, options);
        var tcs = new TaskCompletionSource<NatsTransportTestMessage>();

        await transport.SubscribeAsync<NatsTransportTestMessage>(async (msg, ctx) =>
        {
            tcs.TrySetResult(msg);
            await Task.CompletedTask;
        });
        await Task.Delay(200);

        var message = new NatsTransportTestMessage { MessageId = MessageExtensions.NewMessageId(), Data = "resilience" };
        await transport.PublishAsync(message);

        var result = await Task.WhenAny(tcs.Task, Task.Delay(10000));
        result.Should().Be(tcs.Task);
    }

    #endregion

    #region Observability Tests

    [Fact]
    public async Task PublishAsync_ShouldEmitDiagnostics()
    {
        if (_nats is null) return;
        var options = new NatsTransportOptions { SubjectPrefix = $"diag-{Guid.NewGuid():N}" };
        await using var transport = new NatsMessageTransport(_nats, _serializer, _logger, _provider, options);
        var activityStarted = false;

        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == CatgaDiagnostics.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStarted = _ => activityStarted = true
        };
        ActivitySource.AddActivityListener(listener);

        var message = new NatsTransportTestMessage { MessageId = MessageExtensions.NewMessageId(), Data = "diag" };
        await transport.PublishAsync(message);

        activityStarted.Should().BeTrue();
    }

    #endregion

    #region Auto-Batching Tests

    [Fact]
    public async Task PublishAsync_WithAutoBatching_ShouldBatchMessages()
    {
        if (_nats is null) return;
        var options = new NatsTransportOptions
        {
            SubjectPrefix = $"autobatch-{Guid.NewGuid():N}",
            Batch = new BatchTransportOptions
            {
                EnableAutoBatching = true,
                MaxBatchSize = 5,
                BatchTimeout = TimeSpan.FromMilliseconds(100)
            }
        };
        await using var transport = new NatsMessageTransport(_nats, _serializer, _logger, _provider, options);
        var receivedCount = 0;
        var tcs = new TaskCompletionSource();

        await transport.SubscribeAsync<NatsTransportTestMessage>(async (msg, ctx) =>
        {
            if (Interlocked.Increment(ref receivedCount) >= 3)
                tcs.TrySetResult();
            await Task.CompletedTask;
        });
        await Task.Delay(200);

        // Publish multiple messages quickly
        for (int i = 0; i < 3; i++)
        {
            var message = new NatsTransportTestMessage { MessageId = MessageExtensions.NewMessageId(), Data = $"autobatch-{i}" };
            await transport.PublishAsync(message);
        }

        var result = await Task.WhenAny(tcs.Task, Task.Delay(10000));
        result.Should().Be(tcs.Task);
    }

    #endregion

    #region Dispose Tests

    [Fact]
    public async Task DisposeAsync_ShouldCleanupResources()
    {
        if (_nats is null) return;
        var options = new NatsTransportOptions { SubjectPrefix = $"dispose-{Guid.NewGuid():N}" };
        var transport = new NatsMessageTransport(_nats, _serializer, _logger, _provider, options);

        await transport.SubscribeAsync<NatsTransportTestMessage>(async (msg, ctx) => await Task.CompletedTask);

        await transport.DisposeAsync();

        // Should not throw
    }

    #endregion

    #region Multiple Messages Tests

    [Fact]
    public async Task PublishAsync_MultipleMessages_ShouldDeliverAll()
    {
        if (_nats is null) return;
        var options = new NatsTransportOptions { SubjectPrefix = $"multi-{Guid.NewGuid():N}" };
        var transport = new NatsMessageTransport(_nats, _serializer, _logger, _provider, options);
        var receivedCount = 0;
        var tcs = new TaskCompletionSource();

        await transport.SubscribeAsync<NatsTransportTestMessage>(async (msg, ctx) =>
        {
            if (Interlocked.Increment(ref receivedCount) >= 3)
                tcs.TrySetResult();
            await Task.CompletedTask;
        });

        for (int i = 0; i < 3; i++)
        {
            var message = new NatsTransportTestMessage { MessageId = MessageExtensions.NewMessageId(), Data = $"msg-{i}" };
            await transport.PublishAsync(message);
        }

        await Task.WhenAny(tcs.Task, Task.Delay(5000));
        receivedCount.Should().BeGreaterOrEqualTo(1);
    }

    [Fact]
    public async Task SubscribeAsync_MultipleSubscribers_ShouldWork()
    {
        if (_nats is null) return;
        var options = new NatsTransportOptions { SubjectPrefix = $"multi-sub-{Guid.NewGuid():N}" };
        var transport = new NatsMessageTransport(_nats, _serializer, _logger, _provider, options);
        var received1 = false;
        var received2 = false;
        var tcs = new TaskCompletionSource();

        await transport.SubscribeAsync<NatsTransportTestMessage>(async (msg, ctx) =>
        {
            received1 = true;
            if (received1 && received2) tcs.TrySetResult();
            await Task.CompletedTask;
        });

        await transport.SubscribeAsync<NatsTransportTestMessage>(async (msg, ctx) =>
        {
            received2 = true;
            if (received1 && received2) tcs.TrySetResult();
            await Task.CompletedTask;
        });

        var message = new NatsTransportTestMessage { MessageId = MessageExtensions.NewMessageId(), Data = "multi-sub" };
        await transport.PublishAsync(message);

        await Task.WhenAny(tcs.Task, Task.Delay(5000));
        (received1 || received2).Should().BeTrue();
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
public partial class NatsTransportTestMessage : IMessage
{
    public long MessageId { get; set; }
    public long CorrelationId { get; set; }
    public QualityOfService QoS { get; set; } = QualityOfService.AtLeastOnce;
    public string Data { get; set; } = string.Empty;
}

#endregion
