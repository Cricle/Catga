using System;
using System.Threading.Tasks;
using Catga.Abstractions;
using Catga.Core;
using Catga.Resilience;
using Catga.Serialization.MemoryPack;
using Catga.Transport.Nats;
using Catga.Transport;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using FluentAssertions;
using MemoryPack;
using NATS.Client.Core;
using Xunit;

namespace Catga.Tests.Integration;

[Trait("Category", "Integration")]
[Trait("Requires", "Docker")]
public partial class NatsTransportIntegrationTests : IAsyncLifetime
{
    private IContainer? _natsContainer;
    private INatsConnection? _connection;
    private NatsMessageTransport? _transport;

    public async Task InitializeAsync()
    {
        if (!IsDockerRunning()) return;

        var natsImage = ResolveImage("TEST_NATS_IMAGE", "nats:latest");
        if (natsImage is null) return;

        _natsContainer = new ContainerBuilder()
            .WithImage(natsImage)
            .WithPortBinding(4222, true)
            .WithPortBinding(8222, true)
            .WithCommand("-js", "-m", "8222")
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilHttpRequestIsSucceeded(r => r
                    .ForPort(8222)
                    .ForPath("/varz")))
            .Build();

        await _natsContainer.StartAsync();

        var port = _natsContainer.GetMappedPublicPort(4222);
        _connection = new NatsConnection(new NatsOpts { Url = $"nats://localhost:{port}", ConnectTimeout = TimeSpan.FromSeconds(10) });
        await _connection.ConnectAsync();

        _transport = new NatsMessageTransport(_connection, new MemoryPackMessageSerializer(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<NatsMessageTransport>.Instance,
            options: null, provider: new DiagnosticResiliencePipelineProvider());
    }

    public async Task DisposeAsync()
    {
        if (_transport is not null) await _transport.DisposeAsync();
        if (_connection is not null) await _connection.DisposeAsync();
        if (_natsContainer is not null) await _natsContainer.DisposeAsync();
    }

    private static bool IsDockerRunning()
    {
        try
        {
            var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "docker",
                Arguments = "info",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            p?.WaitForExit(5000);
            return p?.ExitCode == 0;
        }
        catch { return false; }
    }

    private static string? ResolveImage(string envVar, string defaultImage)
    {
        var img = Environment.GetEnvironmentVariable(envVar) ?? defaultImage;
        return IsImageAvailable(img) ? img : null;
    }

    private static bool IsImageAvailable(string image)
    {
        try
        {
            var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "docker",
                Arguments = $"image inspect {image}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            p?.WaitForExit(3000);
            return p?.ExitCode == 0;
        }
        catch { return false; }
    }

    [Fact]
    public async Task Publish_QoS0_ShouldDeliverToSubscriber()
    {
        if (_transport is null) return; // Docker not running

        var receivedTcs = new TaskCompletionSource<TestEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        await _transport.SubscribeAsync<TestEvent>((msg, ctx) => { receivedTcs.TrySetResult(msg); return Task.CompletedTask; });
        await Task.Delay(100);

        await _transport.PublishAsync(new TestEvent
        {
            MessageId = MessageExtensions.NewMessageId(),
            Id = "nats-qos0",
            Data = "hello"
        });

        var completed = await Task.WhenAny(receivedTcs.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        completed.Should().Be(receivedTcs.Task);
        (await receivedTcs.Task).Id.Should().Be("nats-qos0");
    }

    [Fact]
    public async Task Publish_QoS1_ShouldPersistAndDeliver()
    {
        if (_transport is null) return; // Docker not running

        var receivedTcs = new TaskCompletionSource<TestEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        await _transport.SubscribeAsync<TestEvent>((msg, ctx) => { receivedTcs.TrySetResult(msg); return Task.CompletedTask; });
        await Task.Delay(100);

        await _transport.PublishAsync(new TestEvent
        {
            MessageId = MessageExtensions.NewMessageId(),
            Id = "nats-qos1",
            Data = "persist"
        }, new TransportContext { MessageType = typeof(TestEvent).FullName });

        var completed = await Task.WhenAny(receivedTcs.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        completed.Should().Be(receivedTcs.Task);
        (await receivedTcs.Task).Id.Should().Be("nats-qos1");
    }

    [Fact]
    public async Task Publish_QoS2_ExactlyOnce_ShouldDeduplicate()
    {
        if (_transport is null) return; // Docker not running

        var received = 0;
        await _transport.SubscribeAsync<QoS2Message>((msg, ctx) => { Interlocked.Increment(ref received); return Task.CompletedTask; });
        await Task.Delay(100);

        var msg = new QoS2Message
        {
            MessageId = MessageExtensions.NewMessageId(),
            Id = "nats-qos2",
            Data = "dedup"
        };
        var ctx = new TransportContext { MessageId = 4242424242L, MessageType = typeof(QoS2Message).FullName };

        await _transport.PublishAsync(msg, ctx);
        await _transport.PublishAsync(msg, ctx);

        await Task.Delay(500);
        received.Should().Be(1);
    }

    [Fact]
    public async Task PublishBatchAsync_ShouldDeliverAll()
    {
        if (_transport is null) return; // Docker not running

        var count = 0;
        await _transport.SubscribeAsync<BatchEvent>((msg, ctx) => { Interlocked.Increment(ref count); return Task.CompletedTask; });
        await Task.Delay(100);

        var events = Enumerable.Range(1, 10).Select(i => new BatchEvent
        {
            MessageId = MessageExtensions.NewMessageId(),
            Id = $"batch-{i}",
            Data = $"msg-{i}"
        }).ToList();

        await _transport.PublishBatchAsync(events);
        await Task.Delay(1000);

        count.Should().Be(events.Count);
    }

    [MemoryPackable]
    private partial record TestEvent : IEvent
    {
        public required long MessageId { get; init; }
        public required string Id { get; init; }
        public required string Data { get; init; }
        public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
    }

    [MemoryPackable]
    private partial record QoS2Message : IMessage
    {
        public required long MessageId { get; init; }
        public required string Id { get; init; }
        public required string Data { get; init; }
        public QualityOfService QoS => QualityOfService.ExactlyOnce;
        public DeliveryMode DeliveryMode => DeliveryMode.WaitForResult;
    }

    [MemoryPackable]
    private partial record BatchEvent : IEvent
    {
        public required long MessageId { get; init; }
        public required string Id { get; init; }
        public required string Data { get; init; }
        public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
    }
}
