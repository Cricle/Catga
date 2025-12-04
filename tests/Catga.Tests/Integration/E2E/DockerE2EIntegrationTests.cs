using System.Diagnostics;
using Catga.Abstractions;
using Catga.Core;
using Catga.Outbox;
using Catga.Inbox;
using Catga.Persistence.Redis.Persistence;
using Catga.Serialization.MemoryPack;
using Catga.Transport;
using Catga.Transport.Nats;
using Catga.Observability;
using FluentAssertions;
using System.Linq;
using Microsoft.Extensions.Logging;
using Moq;
using NATS.Client.Core;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using StackExchange.Redis;
using Catga.Persistence;
using Testcontainers.Redis;
using Xunit;

namespace Catga.Tests.Integration.E2E;

[Trait("Category", "Integration")]
[Trait("Requires", "Docker")]
public sealed partial class DockerE2EIntegrationTests : IAsyncLifetime
{
    private IContainer? _natsContainer;
    private NatsConnection? _natsConnection;
    private RedisContainer? _redisContainer;
    private IConnectionMultiplexer? _redis;

    private IMessageSerializer? _serializer;
    private ILogger<NatsMessageTransport>? _natsLogger;
    private ILogger<RedisOutboxPersistence>? _outboxLogger;
    private ILogger<RedisInboxPersistence>? _inboxLogger;

    public async Task InitializeAsync()
    {
        if (!IsDockerRunning()) return;

        // Resolve images (allow override via env vars) and ensure they are locally available to avoid long pulls
        var natsImage = ResolveImage("TEST_NATS_IMAGE", "nats:2.10-alpine");
        var redisImage = ResolveImage("TEST_REDIS_IMAGE", "redis:7-alpine");
        if (natsImage is null || redisImage is null) return;

        // Start NATS (JetStream + monitoring)
        // Avoid relying on nc/bash/wget in base image; wait on log message instead.
        _natsContainer = new ContainerBuilder()
            .WithImage(natsImage)
            .WithPortBinding(4222, true)
            .WithPortBinding(8222, true)
            .WithCommand("-js", "-m", "8222")
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilMessageIsLogged("Server is ready"))
            .Build();
        await _natsContainer.StartAsync();

        var natsPort = _natsContainer.GetMappedPublicPort(4222);
        var natsOpts = new NatsOpts { Url = $"nats://localhost:{natsPort}", ConnectTimeout = TimeSpan.FromSeconds(10) };
        _natsConnection = new NatsConnection(natsOpts);
        await ConnectWithRetryAsync(_natsConnection);

        // Start Redis
        _redisContainer = new RedisBuilder()
            .WithImage(redisImage)
            .Build();
        await _redisContainer.StartAsync();
        _redis = await ConnectionMultiplexer.ConnectAsync(_redisContainer.GetConnectionString());

        // Common services
        _serializer = new MemoryPackMessageSerializer();
        _natsLogger = Mock.Of<ILogger<NatsMessageTransport>>();
        _outboxLogger = Mock.Of<ILogger<RedisOutboxPersistence>>();
        _inboxLogger = Mock.Of<ILogger<RedisInboxPersistence>>();
    }

    public async Task DisposeAsync()
    {
        if (_natsConnection is not null)
            await _natsConnection.DisposeAsync();
        _redis?.Dispose();
        if (_natsContainer is not null)
            await _natsContainer.DisposeAsync();
        if (_redisContainer is not null)
            await _redisContainer.DisposeAsync();
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

    private static async Task ConnectWithRetryAsync(NatsConnection connection, int attempts = 10, int delayMs = 500)
    {
        for (int i = 0; i < attempts; i++)
        {
            try
            {
                await connection.ConnectAsync();
                return;
            }
            catch
            {
                if (i == attempts - 1) throw;
                await Task.Delay(delayMs);
            }
        }
    }

    [Fact]
    public async Task E2E_Outbox_To_NATS_To_Inbox_QoS2_ExactlyOnce()
    {
        if (_natsConnection is null || _redis is null) return; // skip if docker not running

        // Arrange
        var provider = new Catga.Resilience.DiagnosticResiliencePipelineProvider();
        await using var transport = new NatsMessageTransport(_natsConnection, _serializer!, _natsLogger!, provider, new NatsTransportOptions { SubjectPrefix = "e2e" });
        var outbox = new RedisOutboxPersistence(_redis!, _serializer!, _outboxLogger!, options: null, provider: provider);
        var inbox = new RedisInboxPersistence(_redis!, _serializer!, _inboxLogger!, options: null, provider: provider);

        var received = 0;
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await transport.SubscribeAsync<TestEvent>(async (msg, ctx) =>
        {
            // Inbox dedup
            var id = msg.MessageId;
            var locked = await inbox.TryLockMessageAsync(id, TimeSpan.FromMinutes(5));
            if (!locked) return; // duplicate

            Interlocked.Increment(ref received);

            var inboxMessage = new InboxMessage
            {
                MessageId = id,
                MessageType = typeof(TestEvent).FullName!,
                Payload = _serializer!.Serialize(msg),
                Status = InboxStatus.Processing,
                ReceivedAt = DateTime.UtcNow,
                ProcessingResult = System.Text.Encoding.UTF8.GetBytes("ok")
            };
            await inbox.MarkAsProcessedAsync(inboxMessage);

            if (Volatile.Read(ref received) == 1)
                tcs.TrySetResult();
        });

        // Create event and outbox record
        var messageId = MessageExtensions.NewMessageId();
        var ev = new TestEvent { MessageId = messageId, Id = "e2e-1", Data = "Hello" };

        var outboxMsg = new OutboxMessage
        {
            MessageId = messageId,
            MessageType = typeof(TestEvent).FullName!,
            Payload = _serializer!.Serialize(ev),
            Status = OutboxStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };
        await outbox.AddAsync(outboxMsg);

        // Act - publish twice with QoS2 (MsgId dedup) and mark published
        var ctx2 = new TransportContext { MessageId = messageId, MessageType = typeof(TestEvent).FullName };
        ev.QoS = QualityOfService.ExactlyOnce;
        await transport.PublishAsync(ev, ctx2);
        await transport.PublishAsync(ev, ctx2); // duplicate publish
        await outbox.MarkAsPublishedAsync(messageId);

        // Assert
        var done = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(20)));
        done.Should().Be(tcs.Task, "message should be processed exactly once end-to-end");
        Volatile.Read(ref received).Should().Be(1);

        // Verify inbox result exists
        var has = await inbox.HasBeenProcessedAsync(messageId);
        has.Should().BeTrue();
    }

    [Fact]
    public async Task E2E_NATS_Batch_Publish_QoS1_AtLeastOnce_InboxProcessesAll()
    {
        if (_natsConnection is null || _redis is null) return; // skip if docker not running

        // Arrange
        var provider = new Catga.Resilience.DiagnosticResiliencePipelineProvider();
        await using var transport = new NatsMessageTransport(_natsConnection, _serializer!, _natsLogger!, provider, new NatsTransportOptions { SubjectPrefix = "e2e" });
        var inbox = new RedisInboxPersistence(_redis!, _serializer!, _inboxLogger!, options: null, provider: provider);

        var total = 10;
        var received = 0;
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await transport.SubscribeAsync<TestEvent>(async (msg, ctx) =>
        {
            var id = msg.MessageId;
            if (!await inbox.TryLockMessageAsync(id, TimeSpan.FromMinutes(5))) return;
            Interlocked.Increment(ref received);
            await inbox.MarkAsProcessedAsync(new InboxMessage
            {
                MessageId = id,
                MessageType = typeof(TestEvent).FullName!,
                Payload = _serializer!.Serialize(msg),
                Status = InboxStatus.Processed,
                ReceivedAt = DateTime.UtcNow,
                ProcessedAt = DateTime.UtcNow,
                ProcessingResult = System.Text.Encoding.UTF8.GetBytes("ok")
            });
            if (Volatile.Read(ref received) == total) tcs.TrySetResult();
        });

        // Act
        var events = Enumerable.Range(0, total).Select(i => new TestEvent
        {
            MessageId = MessageExtensions.NewMessageId(),
            Id = $"item-{i}",
            Data = "batch",
            QoS = QualityOfService.AtLeastOnce
        }).ToList();

        var ctx = new TransportContext();
        await transport.PublishBatchAsync(events, ctx);

        // Assert
        var done = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(30)));
        done.Should().Be(tcs.Task, "all messages should be delivered and processed once");
        Volatile.Read(ref received).Should().Be(total);
    }

    [Fact]
    public async Task E2E_NATS_QoS0_AtMostOnce_FastPath()
    {
        if (_natsConnection is null || _redis is null) return; // skip if docker not running

        var provider = new Catga.Resilience.DiagnosticResiliencePipelineProvider();
        await using var transport = new NatsMessageTransport(_natsConnection, _serializer!, _natsLogger!, provider, new NatsTransportOptions { SubjectPrefix = "e2e" });
        var inbox = new RedisInboxPersistence(_redis!, _serializer!, _inboxLogger!, options: null, provider: provider);

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await transport.SubscribeAsync<TestEvent>(async (msg, ctx) =>
        {
            if (!await inbox.TryLockMessageAsync(msg.MessageId, TimeSpan.FromMinutes(5))) return;
            await inbox.MarkAsProcessedAsync(new InboxMessage
            {
                MessageId = msg.MessageId,
                MessageType = typeof(TestEvent).FullName!,
                Payload = _serializer!.Serialize(msg),
                Status = InboxStatus.Processed,
                ReceivedAt = DateTime.UtcNow,
                ProcessedAt = DateTime.UtcNow,
                ProcessingResult = System.Text.Encoding.UTF8.GetBytes("ok")
            });
            tcs.TrySetResult();
        });

        await Task.Delay(200);

        var ev = new TestEvent { MessageId = MessageExtensions.NewMessageId(), Id = "fast", Data = "qos0", QoS = QualityOfService.AtMostOnce };
        await transport.PublishAsync(ev, new TransportContext { MessageId = ev.MessageId, MessageType = typeof(TestEvent).FullName });

        var done = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        done.Should().Be(tcs.Task, "QoS0 should be delivered via core pub/sub");
    }

    [Fact]
    public async Task E2E_EventStore_NATS_Append_Read_Version()
    {
        if (_natsConnection is null) return;

        var provider = new Catga.Resilience.DiagnosticResiliencePipelineProvider();
        var es = new NatsJSEventStore(_natsConnection, _serializer!, streamName: $"E2E_EVENTS_{Guid.NewGuid():N}", options: null, provider: provider);
        var streamId = $"order-{Guid.NewGuid()}";

        var events = new List<IEvent>
        {
            new TestEvent { MessageId = MessageExtensions.NewMessageId(), Id = "es-1", Data = "a" },
            new TestEvent { MessageId = MessageExtensions.NewMessageId(), Id = "es-2", Data = "b" }
        };

        await es.AppendAsync(streamId, events);
        await Task.Delay(200);

        var v = await es.GetVersionAsync(streamId);
        v.Should().BeGreaterOrEqualTo(0);

        var s = await es.ReadAsync(streamId);
        s.Events.Should().HaveCountGreaterOrEqualTo(2);
    }

    [Fact]
    public async Task E2E_Transport_Publish_Retry_WithTransientFailures_Succeeds()
    {
        if (_natsConnection is null || _redis is null) return; // skip if docker not running

        // Arrange resilience with retries
        var options = new Catga.Resilience.CatgaResilienceOptions
        {
            TransportRetryCount = 3,
            TransportRetryDelay = TimeSpan.FromMilliseconds(50)
        };
        var inner = new Catga.Resilience.DefaultResiliencePipelineProvider(options);
        var fault = new FaultInjectingProvider(inner, publishFailuresBeforeSuccess: 2);

        // Activity listener to capture resilience.retry events
        var retryEvents = 0;
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == CatgaDiagnostics.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = a =>
            {
                foreach (var e in a.Events)
                {
                    if (e.Name == "resilience.retry") retryEvents++;
                }
            }
        };
        ActivitySource.AddActivityListener(listener);

        await using var transport = new NatsMessageTransport(_natsConnection, _serializer!, _natsLogger!, fault, new NatsTransportOptions { SubjectPrefix = "e2e" });
        var inbox = new RedisInboxPersistence(_redis!, _serializer!, _inboxLogger!, options: null, provider: fault);

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await transport.SubscribeAsync<TestEvent>(async (msg, ctx) =>
        {
            if (!await inbox.TryLockMessageAsync(msg.MessageId, TimeSpan.FromMinutes(5))) return;
            await inbox.MarkAsProcessedAsync(new InboxMessage
            {
                MessageId = msg.MessageId,
                MessageType = typeof(TestEvent).FullName!,
                Payload = _serializer!.Serialize(msg),
                Status = InboxStatus.Processed,
                ReceivedAt = DateTime.UtcNow,
                ProcessedAt = DateTime.UtcNow,
                ProcessingResult = System.Text.Encoding.UTF8.GetBytes("ok")
            });
            tcs.TrySetResult();
        });

        // Act
        var ev = new TestEvent { MessageId = MessageExtensions.NewMessageId(), Id = "retry", Data = "transient", QoS = QualityOfService.AtLeastOnce };
        await transport.PublishAsync(ev, new TransportContext { MessageId = ev.MessageId, MessageType = typeof(TestEvent).FullName });

        // Assert publish eventually delivered
        var done = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        done.Should().Be(tcs.Task, "publish should succeed after retries");
        fault.PublishAttemptCount.Should().BeGreaterOrEqualTo(3); // 2 failures + 1 success
    }

    private sealed class FaultInjectingProvider : Catga.Resilience.IResiliencePipelineProvider
    {
        private readonly Catga.Resilience.IResiliencePipelineProvider _inner;
        private int _publishFailuresBeforeSuccess;
        public int PublishAttemptCount { get; private set; }

        public FaultInjectingProvider(Catga.Resilience.IResiliencePipelineProvider inner, int publishFailuresBeforeSuccess)
        {
            _inner = inner;
            _publishFailuresBeforeSuccess = publishFailuresBeforeSuccess;
        }

        public ValueTask<T> ExecuteMediatorAsync<T>(Func<CancellationToken, ValueTask<T>> action, CancellationToken cancellationToken)
            => _inner.ExecuteMediatorAsync(action, cancellationToken);

        public ValueTask ExecuteMediatorAsync(Func<CancellationToken, ValueTask> action, CancellationToken cancellationToken)
            => _inner.ExecuteMediatorAsync(action, cancellationToken);

        public ValueTask<T> ExecuteTransportPublishAsync<T>(Func<CancellationToken, ValueTask<T>> action, CancellationToken cancellationToken)
            => _inner.ExecuteTransportPublishAsync<T>(ct =>
            {
                PublishAttemptCount++;
                if (_publishFailuresBeforeSuccess-- > 0)
                    throw new Exception("Injected transient publish failure");
                return action(ct);
            }, cancellationToken);

        public ValueTask ExecuteTransportPublishAsync(Func<CancellationToken, ValueTask> action, CancellationToken cancellationToken)
            => _inner.ExecuteTransportPublishAsync(ct =>
            {
                PublishAttemptCount++;
                if (_publishFailuresBeforeSuccess-- > 0)
                    throw new Exception("Injected transient publish failure");
                return action(ct);
            }, cancellationToken);

        public ValueTask<T> ExecuteTransportSendAsync<T>(Func<CancellationToken, ValueTask<T>> action, CancellationToken cancellationToken)
            => _inner.ExecuteTransportSendAsync(action, cancellationToken);

        public ValueTask ExecuteTransportSendAsync(Func<CancellationToken, ValueTask> action, CancellationToken cancellationToken)
            => _inner.ExecuteTransportSendAsync(action, cancellationToken);

        public ValueTask<T> ExecutePersistenceAsync<T>(Func<CancellationToken, ValueTask<T>> action, CancellationToken cancellationToken)
            => _inner.ExecutePersistenceAsync(action, cancellationToken);

        public ValueTask ExecutePersistenceAsync(Func<CancellationToken, ValueTask> action, CancellationToken cancellationToken)
            => _inner.ExecutePersistenceAsync(action, cancellationToken);
    }

    // Test model
    [MemoryPack.MemoryPackable]
    private partial record TestEvent : IEvent
    {
        public required long MessageId { get; init; }
        public long? CorrelationId { get; init; }
        public QualityOfService QoS { get; set; } = QualityOfService.AtLeastOnce;
        public required string Id { get; init; }
        public required string Data { get; init; }
        public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
    }
}
